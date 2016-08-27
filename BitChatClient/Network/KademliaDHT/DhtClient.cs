/*
Technitium Bit Chat
Copyright (C) 2016  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;

/*
 * Kademlia based Distributed Hash Table (DHT) Implementation For Bit Chat
 * =======================================================================
 *
 * FEATURES IMPLEMENTED
 * --------------------
 * 1. Routing table with K-Bucket: K=8, bucket refresh, contact health check, additional "replacement" list of upto K contacts.
 * 2. RPC: UDP based protocol with PING & FIND_NODE implemented. FIND_PEER & ANNOUNCE_PEER implemented similar to BitTorrent DHT implementation.
 * 3. Peer data eviction after 15mins of receiving announcement.
 * 4. Parallel lookup: FIND_NODE lookup with alpha=3 implemented.
 * 
 * FEATURES NOT IMPLEMENTED
 * ------------------------
 * 1. Node data republishing. Each peer MUST announce itself within 15mins to all nodes closer to bit chat networkID.
 * 
 * REFERENCE
 * ---------
 * 1. https://pdos.csail.mit.edu/~petar/papers/maymounkov-kademlia-lncs.pdf
 * 2. http://www.bittorrent.org/beps/bep_0005.html
*/

namespace BitChatClient.Network.KademliaDHT
{
    class DhtClient : IDisposable
    {
        #region variables

        const int BUFFER_MAX_SIZE = 1024;
        const int SECRET_EXPIRY_SECONDS = 300; //5min
        public const int KADEMLIA_K = 8;
        const int HEALTH_CHECK_TIMER_INTERVAL = 5 * 60 * 1000; //5 min
        const int QUERY_TIMEOUT = 5000;
        const int KADEMLIA_ALPHA = 3;

        IDhtClientManager _manager;
        bool _proxyEnabled = false;

        Socket _udpListener;
        Thread _udpListenerThread;

        int _localPort;
        CurrentNode _currentNode;
        KBucket _routingTable;

        Timer _healthTimer;

        //secret
        HashAlgorithm _secretHmac;
        DateTime _secretUpdatedOn;
        HashAlgorithm _previousSecretHmac;

        //query manager
        Socket _udpClient;
        Thread _udpClientThread;

        readonly FixMemoryStream _sendBufferStream = new FixMemoryStream(BUFFER_MAX_SIZE);

        Dictionary<int, Transaction> _transactions = new Dictionary<int, Transaction>(10);

        #endregion

        #region constructor

        public DhtClient(int udpDhtPort, IDhtClientManager manager)
        {
            _manager = manager;

            IPEndPoint localEP;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    if (Environment.OSVersion.Version.Major < 6)
                    {
                        //below vista
                        _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        _udpClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                        localEP = new IPEndPoint(IPAddress.Any, udpDhtPort);
                    }
                    else
                    {
                        //vista & above
                        _udpListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                        _udpListener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);

                        _udpClient = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                        _udpClient.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);

                        localEP = new IPEndPoint(IPAddress.IPv6Any, udpDhtPort);
                    }
                    break;

                case PlatformID.Unix: //mono framework
                    if (Socket.OSSupportsIPv6)
                    {
                        _udpListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                        _udpClient = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

                        localEP = new IPEndPoint(IPAddress.IPv6Any, udpDhtPort);
                    }
                    else
                    {
                        _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        _udpClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                        localEP = new IPEndPoint(IPAddress.Any, udpDhtPort);
                    }

                    break;

                default: //unknown
                    _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _udpClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                    localEP = new IPEndPoint(IPAddress.Any, udpDhtPort);
                    break;
            }

            try
            {
                _udpListener.Bind(localEP);
            }
            catch
            {
                localEP.Port = 0;

                _udpListener.Bind(localEP);
            }

            _localPort = (_udpListener.LocalEndPoint as IPEndPoint).Port;

            //init routing table
            _currentNode = new CurrentNode(udpDhtPort);
            _routingTable = new KBucket(_currentNode);

            //bind udp client socket to random port
            if (_udpClient.AddressFamily == AddressFamily.InterNetwork)
                _udpClient.Bind(new IPEndPoint(IPAddress.Any, 0));
            else
                _udpClient.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));

            //start reading udp response packets
            _udpClientThread = new Thread(ReadResponsePacketsAsync);
            _udpClientThread.IsBackground = true;
            _udpClientThread.Start(_udpClient);

            //start reading udp query packets
            _udpListenerThread = new Thread(ReadQueryPacketsAsync);
            _udpListenerThread.IsBackground = true;
            _udpListenerThread.Start(_udpListener);

            //start health timer
            _healthTimer = new Timer(HealthTimerCallback, null, QUERY_TIMEOUT, Timeout.Infinite);
        }

        #endregion

        #region IDisposable

        ~DhtClient()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool _disposed = false;

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_udpListener != null)
                    _udpListener.Dispose();

                if (_udpListenerThread != null)
                    _udpListenerThread.Abort();

                if (_udpClient != null)
                    _udpClient.Dispose();

                if (_udpClientThread != null)
                    _udpClientThread.Abort();

                if (_sendBufferStream != null)
                    _sendBufferStream.Dispose();

                if (_routingTable != null)
                    _routingTable.Dispose();

                if (_healthTimer != null)
                {
                    _healthTimer.Dispose();
                    _healthTimer = null;
                }

                if (_secretHmac != null)
                    _secretHmac.Dispose();

                if (_previousSecretHmac != null)
                    _previousSecretHmac.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region private

        private BinaryID GetToken(IPAddress nodeIP)
        {
            if ((_secretHmac == null) || ((DateTime.UtcNow - _secretUpdatedOn).TotalSeconds > SECRET_EXPIRY_SECONDS))
            {
                if (_previousSecretHmac != null)
                    _previousSecretHmac.Dispose();

                _previousSecretHmac = _secretHmac;
                _secretHmac = new HMACSHA1();
                _secretUpdatedOn = DateTime.UtcNow;
            }

            lock (_secretHmac)
            {
                return new BinaryID(_secretHmac.ComputeHash(nodeIP.GetAddressBytes()));
            }
        }

        private BinaryID GetOldToken(IPAddress nodeIP)
        {
            if (_previousSecretHmac == null)
                return null;

            lock (_previousSecretHmac)
            {
                return new BinaryID(_previousSecretHmac.ComputeHash(nodeIP.GetAddressBytes()));
            }
        }

        private bool IsTokenValid(BinaryID token, IPAddress nodeIP)
        {
            if (token.Equals(GetToken(nodeIP)))
                return true;
            else
                return token.Equals(GetOldToken(nodeIP));
        }

        private void AddContactAfterPingAsync(object state)
        {
            try
            {
                Ping(state as NodeContact); //query manager auto add contacts that respond
            }
            catch
            { }
        }

        private void AddNodeAfterPingAsync(object state)
        {
            try
            {
                Ping(state as IPEndPoint); //query manager auto add contacts that respond
            }
            catch
            { }
        }

        private void HealthTimerCallback(object state)
        {
            try
            {
                //remove expired data
                _currentNode.RemoveExpiredPeers();

                //check contact health
                _routingTable.CheckContactHealth(this);

                //refresh buckets
                _routingTable.RefreshBucket(this);

                //find closest contacts for current node id
                NodeContact[] initialContacts = _routingTable.GetKClosestContacts(_currentNode.NodeID);

                if (initialContacts.Length > 0)
                    QueryFindNode(initialContacts, _currentNode.NodeID); //query manager auto add contacts that respond
            }
            catch
            { }
            finally
            {
                if (_healthTimer != null)
                    _healthTimer.Change(HEALTH_CHECK_TIMER_INTERVAL, Timeout.Infinite);
            }
        }

        private DhtRpcPacket ProcessPacket(DhtRpcPacket packet)
        {
            switch (packet.PacketType)
            {
                case RpcPacketType.Query:
                    #region Query

                    DhtRpcPacket response = null;

                    switch (packet.QueryType)
                    {
                        case RpcQueryType.PING:
                            #region PING
                            {
                                response = DhtRpcPacket.CreatePingPacketResponse(packet.TransactionID, _currentNode);
                                break;
                            }
                        #endregion

                        case RpcQueryType.FIND_NODE:
                            #region FIND_NODE
                            {
                                NodeContact[] contacts = _routingTable.GetKClosestContacts(packet.NetworkID);

                                response = DhtRpcPacket.CreateFindNodePacketResponse(packet.TransactionID, _currentNode, packet.NetworkID, contacts);
                                break;
                            }
                        #endregion

                        case RpcQueryType.FIND_PEERS:
                            #region GET_PEERS
                            {
                                PeerEndPoint[] peers = _currentNode.GetPeers(packet.NetworkID);
                                NodeContact[] contacts;

                                if (peers.Length < 1)
                                    contacts = _routingTable.GetKClosestContacts(packet.NetworkID);
                                else
                                    contacts = new NodeContact[] { };

                                response = DhtRpcPacket.CreateFindPeersPacketResponse(packet.TransactionID, _currentNode, packet.NetworkID, contacts, peers, GetToken(packet.SourceNode.NodeEP.Address));
                                break;
                            }
                        #endregion

                        case RpcQueryType.ANNOUNCE_PEER:
                            #region ANNOUNCE_PEER
                            {
                                IPAddress remoteNodeIP = packet.SourceNode.NodeEP.Address;

                                if (IsTokenValid(packet.Token, remoteNodeIP))
                                    _currentNode.StorePeer(packet.NetworkID, new PeerEndPoint(remoteNodeIP, packet.ServicePort));

                                response = DhtRpcPacket.CreateAnnouncePeerPacketResponse(packet.TransactionID, _currentNode, packet.NetworkID);
                                break;
                            }
                            #endregion
                    }

                    return response;

                #endregion

                case RpcPacketType.Response:
                    #region Response

                    Transaction transaction = null;

                    lock (_transactions)
                    {
                        if (_transactions.ContainsKey(packet.TransactionID))
                        {
                            transaction = _transactions[packet.TransactionID];
                        }
                    }

                    if ((transaction != null) && ((transaction.RemoteNodeID == null) || transaction.RemoteNodeID.Equals(packet.SourceNode.NodeID)) && transaction.RemoteNodeEP.Equals(packet.SourceNode.NodeEP))
                    {
                        lock (transaction)
                        {
                            transaction.ResponsePacket = packet;

                            Monitor.Pulse(transaction);
                        }
                    }

                    return null;

                    #endregion
            }

            return null;
        }

        private void ReadQueryPacketsAsync(object parameter)
        {
            Socket udpListener = parameter as Socket;

            EndPoint remoteEP;
            FixMemoryStream recvBufferStream = new FixMemoryStream(BUFFER_MAX_SIZE);
            FixMemoryStream sendBufferStream = new FixMemoryStream(BUFFER_MAX_SIZE);
            int bytesRecv;

            if (udpListener.AddressFamily == AddressFamily.InterNetwork)
                remoteEP = new IPEndPoint(IPAddress.Any, 0);
            else
                remoteEP = new IPEndPoint(IPAddress.IPv6Any, 0);

            try
            {
                while (true)
                {
                    bytesRecv = udpListener.ReceiveFrom(recvBufferStream.Buffer, ref remoteEP);

                    if (bytesRecv > 0)
                    {
                        recvBufferStream.Position = 0;
                        recvBufferStream.SetLength(bytesRecv);

                        IPEndPoint remoteNodeEP = remoteEP as IPEndPoint;

                        if (NetUtilities.IsIPv4MappedIPv6Address(remoteNodeEP.Address))
                            remoteNodeEP = new IPEndPoint(NetUtilities.ConvertFromIPv4MappedIPv6Address(remoteNodeEP.Address), remoteNodeEP.Port);

                        DhtRpcPacket request = new DhtRpcPacket(recvBufferStream, remoteNodeEP.Address);
                        DhtRpcPacket response = ProcessPacket(request);

                        //send response
                        if (response != null)
                        {
                            sendBufferStream.Position = 0;
                            response.WriteTo(sendBufferStream);
                            udpListener.SendTo(sendBufferStream.Buffer, 0, (int)sendBufferStream.Position, SocketFlags.None, remoteEP);
                        }

                        //if contact doesnt exists then add contact else update last seen time
                        KBucket closestBucket = _routingTable.FindClosestBucket(request.SourceNode.NodeID);
                        NodeContact contact = closestBucket.FindContactInCurrentBucket(request.SourceNode.NodeID);

                        if (contact == null)
                        {
                            //check if the closest bucket can accomodate another contact
                            if (!closestBucket.IsCurrentBucketFull(true))
                                ThreadPool.QueueUserWorkItem(AddContactAfterPingAsync, request.SourceNode);
                        }
                        else
                        {
                            contact.UpdateLastSeenTime();
                        }
                    }
                }
            }
            catch
            { }
        }

        private void ReadResponsePacketsAsync(object parameter)
        {
            Socket udpClient = parameter as Socket;

            EndPoint remoteEP;
            FixMemoryStream recvStream = new FixMemoryStream(BUFFER_MAX_SIZE);
            byte[] bufferRecv = recvStream.Buffer;
            int bytesRecv;

            if (udpClient.AddressFamily == AddressFamily.InterNetwork)
                remoteEP = new IPEndPoint(IPAddress.Any, 0);
            else
                remoteEP = new IPEndPoint(IPAddress.IPv6Any, 0);

            while (true)
            {
                try
                {
                    while (true)
                    {
                        bytesRecv = udpClient.ReceiveFrom(bufferRecv, ref remoteEP);

                        if (bytesRecv > 0)
                        {
                            recvStream.SetLength(bytesRecv);
                            recvStream.Position = 0;

                            IPEndPoint remoteNodeEP = remoteEP as IPEndPoint;

                            if (NetUtilities.IsIPv4MappedIPv6Address(remoteNodeEP.Address))
                                remoteNodeEP = new IPEndPoint(NetUtilities.ConvertFromIPv4MappedIPv6Address(remoteNodeEP.Address), remoteNodeEP.Port);

                            try
                            {
                                DhtRpcPacket response = new DhtRpcPacket(recvStream, remoteNodeEP.Address);

                                ProcessPacket(response);
                            }
                            catch
                            { }
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    return;
                }
                catch
                {
                    Thread.Sleep(5000);
                }
            }
        }

        private DhtRpcPacket Query(DhtRpcPacket packet, NodeContact contact)
        {
            Transaction transaction = new Transaction(contact.NodeID, contact.NodeEP);

            try
            {
                lock (_transactions)
                {
                    _transactions.Add(packet.TransactionID, transaction);
                }

                lock (transaction)
                {
                    lock (_sendBufferStream)
                    {
                        _sendBufferStream.Position = 0;
                        packet.WriteTo(_sendBufferStream);

                        if (_proxyEnabled)
                            _manager.SendDhtPacket(contact.NodeEP, _sendBufferStream.Buffer, 0, (int)_sendBufferStream.Position);
                        else
                            _udpClient.SendTo(_sendBufferStream.Buffer, 0, (int)_sendBufferStream.Position, SocketFlags.None, contact.NodeEP);
                    }

                    if (!Monitor.Wait(transaction, QUERY_TIMEOUT))
                    {
                        contact.IncrementRpcFailCount();
                        return null;
                    }

                    //auto add contact or update last seen time
                    if (contact.NodeID == null)
                        contact = transaction.ResponsePacket.SourceNode;

                    KBucket closestBucket = _routingTable.FindClosestBucket(contact.NodeID);
                    NodeContact bucketContact = closestBucket.FindContactInCurrentBucket(contact.NodeID);

                    if (bucketContact == null)
                        closestBucket.AddContactInCurrentBucket(contact);
                    else
                        bucketContact.UpdateLastSeenTime();

                    return transaction.ResponsePacket;
                }
            }
            finally
            {
                lock (_transactions)
                {
                    _transactions.Remove(packet.TransactionID);
                }
            }
        }

        private NodeContact[] PickClosestContacts(List<NodeContact> availableContacts, int count)
        {
            if (availableContacts.Count < count)
                count = availableContacts.Count;

            NodeContact[] closestContacts = KBucket.GetClosestContacts(availableContacts, _currentNode.NodeID, count);

            //remove selected contacts from available contacts 
            foreach (NodeContact closestContact in closestContacts)
            {
                availableContacts.Remove(closestContact);
            }

            return closestContacts;
        }

        private void QueryFindAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                object lockObj = parameters[0] as object;
                RpcQueryType queryType = (RpcQueryType)parameters[1];
                NodeContact contact = parameters[2] as NodeContact;
                BinaryID nodeID = parameters[3] as BinaryID;
                List<NodeContact> availableContacts = parameters[4] as List<NodeContact>;
                List<NodeContact> respondedContacts = parameters[5] as List<NodeContact>;
                List<NodeContact> failedContacts = parameters[6] as List<NodeContact>;
                List<NodeContact> receivedContacts = parameters[7] as List<NodeContact>;

                DhtRpcPacket responsePacket;

                if (queryType == RpcQueryType.FIND_NODE)
                    responsePacket = Query(DhtRpcPacket.CreateFindNodePacketQuery(_currentNode, nodeID), contact);
                else
                    responsePacket = Query(DhtRpcPacket.CreateFindPeersPacketQuery(_currentNode, nodeID), contact);

                if ((responsePacket == null) || (responsePacket.QueryType != queryType))
                {
                    //time out
                    //add contact to failed contacts
                    lock (failedContacts)
                    {
                        if (!failedContacts.Contains(contact))
                            failedContacts.Add(contact);
                    }

                    return;
                }

                //got reply!
                if (responsePacket.Contacts.Length > 0)
                {
                    lock (receivedContacts)
                    {
                        lock (respondedContacts)
                        {
                            //add contact to responded contacts list
                            if (!respondedContacts.Contains(contact))
                                respondedContacts.Add(contact);

                            lock (failedContacts)
                            {
                                //add received contacts to received contacts list
                                foreach (NodeContact receivedContact in responsePacket.Contacts)
                                {
                                    if (!respondedContacts.Contains(receivedContact) && !failedContacts.Contains(receivedContact))
                                        receivedContacts.Add(receivedContact);
                                }
                            }
                        }

                        //add received contacts to available contacts list
                        lock (availableContacts)
                        {
                            foreach (NodeContact receivedContact in receivedContacts)
                            {
                                if (!availableContacts.Contains(receivedContact))
                                    availableContacts.Add(receivedContact);
                            }
                        }
                    }

                    lock (lockObj)
                    {
                        Monitor.Pulse(lockObj);
                    }
                }
                else if ((queryType == RpcQueryType.FIND_PEERS) && (responsePacket.Peers.Length > 0))
                {
                    List<PeerEndPoint> receivedPeers = parameters[8] as List<PeerEndPoint>;

                    lock (receivedPeers)
                    {
                        foreach (PeerEndPoint peer in responsePacket.Peers)
                        {
                            if (!receivedPeers.Contains(peer))
                                receivedPeers.Add(peer);
                        }
                    }

                    lock (lockObj)
                    {
                        Monitor.Pulse(lockObj);
                    }
                }
            }
            catch
            { }
        }

        private object QueryFind(NodeContact[] initialContacts, BinaryID nodeID, RpcQueryType queryType)
        {
            List<NodeContact> availableContacts = new List<NodeContact>(initialContacts);
            List<NodeContact> respondedContacts = new List<NodeContact>();
            List<NodeContact> failedContacts = new List<NodeContact>();
            NodeContact[] alphaContacts;
            int alpha = KADEMLIA_ALPHA;
            bool finalRound = false;

            while (true)
            {
                //pick alpha contacts to query from available contacts
                lock (availableContacts)
                {
                    alphaContacts = PickClosestContacts(availableContacts, alpha);
                }

                if (alphaContacts.Length < 1)
                {
                    //no contacts available to query further

                    lock (respondedContacts)
                    {
                        if (respondedContacts.Count > KADEMLIA_K)
                            return KBucket.GetClosestContacts(respondedContacts, nodeID, KADEMLIA_K);
                        else if (respondedContacts.Count > 0)
                            return respondedContacts.ToArray();
                    }

                    return null;
                }

                object lockObj = new object();
                List<NodeContact> receivedContacts = new List<NodeContact>();
                List<PeerEndPoint> receivedPeers = null;

                if (queryType == RpcQueryType.FIND_PEERS)
                    receivedPeers = new List<PeerEndPoint>();

                lock (lockObj)
                {
                    //query each alpha contact async
                    foreach (NodeContact alphaContact in alphaContacts)
                    {
                        ThreadPool.QueueUserWorkItem(QueryFindAsync, new object[] { lockObj, queryType, alphaContact, nodeID, availableContacts, respondedContacts, failedContacts, receivedContacts, receivedPeers });
                    }

                    //wait for any of the contact to return new contacts
                    if (Monitor.Wait(lockObj, QUERY_TIMEOUT))
                    {
                        //got reply!

                        if (queryType == RpcQueryType.FIND_PEERS)
                        {
                            lock (receivedPeers)
                            {
                                if (receivedPeers.Count > 0)
                                    return receivedPeers.ToArray();
                            }
                        }

                        lock (receivedContacts)
                        {
                            if (receivedContacts.Count < 1)
                            {
                                //current round failed to return any new closer nodes

                                if (finalRound)
                                {
                                    if (queryType == RpcQueryType.FIND_PEERS)
                                        return null;

                                    lock (respondedContacts)
                                    {
                                        if (respondedContacts.Count > KADEMLIA_K)
                                            return KBucket.GetClosestContacts(respondedContacts, nodeID, KADEMLIA_K);
                                        else
                                            return respondedContacts.ToArray();
                                    }
                                }
                                else
                                {
                                    //resend query to k closest node not already queried
                                    finalRound = true;
                                    alpha = KADEMLIA_K;
                                }
                            }
                            else
                            {
                                finalRound = false;
                                alpha = KADEMLIA_ALPHA;
                            }
                        }
                    }
                }
            }
        }

        private void QueryAnnounceAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                NodeContact contact = parameters[0] as NodeContact;
                BinaryID networkID = parameters[1] as BinaryID;
                List<PeerEndPoint> peers = parameters[2] as List<PeerEndPoint>;
                ushort servicePort = (ushort)parameters[3];

                DhtRpcPacket responsePacket = Query(DhtRpcPacket.CreateFindPeersPacketQuery(_currentNode, networkID), contact);

                if ((responsePacket != null) && (responsePacket.QueryType == RpcQueryType.FIND_PEERS))
                {
                    if (responsePacket.Peers.Length > 0)
                    {
                        lock (peers)
                        {
                            foreach (PeerEndPoint peer in responsePacket.Peers)
                            {
                                if (!peers.Contains(peer))
                                    peers.Add(peer);
                            }

                            //Monitor.Pulse(peers); //removed so that response from multiple nodes is collected till query times out
                        }
                    }

                    Query(DhtRpcPacket.CreateAnnouncePeerPacketQuery(_currentNode, networkID, servicePort, responsePacket.Token), contact);
                }
            }
            catch
            { }
        }

        internal bool Ping(NodeContact contact)
        {
            DhtRpcPacket response = Query(DhtRpcPacket.CreatePingPacketQuery(_currentNode), contact);

            if (response == null)
            {
                return false;
            }
            else
            {
                if (contact.Equals(response.SourceNode))
                {
                    return true;
                }
                else
                {
                    contact.IncrementRpcFailCount();
                    return false;
                }
            }
        }

        private NodeContact Ping(IPEndPoint nodeEP)
        {
            DhtRpcPacket response = Query(DhtRpcPacket.CreatePingPacketQuery(_currentNode), new NodeContact(null, nodeEP));

            if (response == null)
                return null;
            else
                return response.SourceNode;
        }

        internal NodeContact[] QueryFindNode(NodeContact[] initialContacts, BinaryID nodeID)
        {
            object contacts = QueryFind(initialContacts, nodeID, RpcQueryType.FIND_NODE);

            if (contacts == null)
                return null;

            return contacts as NodeContact[];
        }

        private PeerEndPoint[] QueryFindPeers(NodeContact[] initialContacts, BinaryID networkID)
        {
            object peers = QueryFind(initialContacts, networkID, RpcQueryType.FIND_PEERS);

            if (peers == null)
                return null;

            return peers as PeerEndPoint[];
        }

        private PeerEndPoint[] QueryAnnounce(NodeContact[] initialContacts, BinaryID networkID, ushort servicePort)
        {
            NodeContact[] contacts = QueryFindNode(initialContacts, networkID);

            if (contacts == null)
                return null;

            List<PeerEndPoint> peers = new List<PeerEndPoint>();

            lock (peers)
            {
                foreach (NodeContact contact in contacts)
                {
                    ThreadPool.QueueUserWorkItem(QueryAnnounceAsync, new object[] { contact, networkID, peers, servicePort });
                }

                Monitor.Wait(peers, QUERY_TIMEOUT);

                return peers.ToArray();
            }
        }

        #endregion

        #region public

        public byte[] ProcessPacket(byte[] dhtPacket, int offset, int count, IPAddress remoteNodeIP)
        {
            using (MemoryStream mS = new MemoryStream(dhtPacket, offset, count, false))
            {
                DhtRpcPacket response = ProcessPacket(new DhtRpcPacket(mS, remoteNodeIP));

                if (response == null)
                    return null;

                return response.ToArray();
            }
        }

        public void AddNode(IEnumerable<IPEndPoint> nodeEPs)
        {
            foreach (IPEndPoint nodeEP in nodeEPs)
                AddNode(nodeEP);
        }

        public void AddNode(IPEndPoint nodeEP)
        {
            if (!NetUtilities.IsPrivateIP(nodeEP.Address))
            {
                if (!_routingTable.ContactExists(nodeEP))
                    ThreadPool.QueueUserWorkItem(AddNodeAfterPingAsync, nodeEP);
            }
        }

        public IPEndPoint[] FindPeers(BinaryID networkID)
        {
            NodeContact[] initialContacts = _routingTable.GetKClosestContacts(networkID);

            if (initialContacts.Length < 1)
                return null;

            return QueryFindPeers(initialContacts, networkID);
        }

        public IPEndPoint[] Announce(BinaryID networkID, int servicePort)
        {
            NodeContact[] initialContacts = _routingTable.GetKClosestContacts(networkID);

            if (initialContacts.Length < 1)
                return null;

            return QueryAnnounce(initialContacts, networkID, Convert.ToUInt16(servicePort));
        }

        public int GetTotalNodes()
        {
            return _routingTable.TotalContacts + _routingTable.TotalReplacementContacts;
        }

        public IPEndPoint[] GetAllNodes()
        {
            NodeContact[] contacts = _routingTable.GetAllContacts(true);
            IPEndPoint[] nodeEPs = new IPEndPoint[contacts.Length];

            for (int i = 0; i < contacts.Length; i++)
                nodeEPs[i] = contacts[i].NodeEP;

            return nodeEPs;
        }

        #endregion

        #region properties

        public BinaryID LocalNodeID
        { get { return _currentNode.NodeID; } }

        public int LocalPort
        { get { return _localPort; } }

        public bool ProxyEnabled
        {
            get { return _proxyEnabled; }
            set { _proxyEnabled = value; }
        }

        #endregion

        class CurrentNode : NodeContact
        {
            #region variables

            const int MAX_PEERS_TO_RETURN = 30;

            Dictionary<BinaryID, List<PeerEndPoint>> _data = new Dictionary<BinaryID, List<PeerEndPoint>>();

            #endregion

            #region constructor

            public CurrentNode(int udpDhtPort)
                : base(udpDhtPort)
            { }

            #endregion

            #region public

            public void StorePeer(BinaryID networkID, PeerEndPoint peerEP)
            {
                lock (_data)
                {
                    List<PeerEndPoint> peerList;

                    if (_data.ContainsKey(networkID))
                    {
                        peerList = _data[networkID];
                    }
                    else
                    {
                        peerList = new List<PeerEndPoint>();
                        _data.Add(networkID, peerList);
                    }

                    foreach (PeerEndPoint peer in peerList)
                    {
                        if (peer.Equals(peerEP))
                        {
                            peer.UpdateDateAdded();
                            return;
                        }
                    }

                    peerList.Add(peerEP);
                }
            }

            public PeerEndPoint[] GetPeers(BinaryID networkID)
            {
                lock (_data)
                {
                    if (_data.ContainsKey(networkID))
                    {
                        List<PeerEndPoint> peers = _data[networkID];

                        if (peers.Count > MAX_PEERS_TO_RETURN)
                        {
                            List<PeerEndPoint> finalPeers = new List<PeerEndPoint>(peers);
                            Random rnd = new Random(DateTime.UtcNow.Millisecond);

                            while (finalPeers.Count > MAX_PEERS_TO_RETURN)
                            {
                                finalPeers.RemoveAt(rnd.Next(finalPeers.Count - 1));
                            }

                            return finalPeers.ToArray();
                        }
                        else
                        {
                            return _data[networkID].ToArray();
                        }
                    }
                    else
                    {
                        return new PeerEndPoint[] { };
                    }
                }
            }

            public void RemoveExpiredPeers()
            {
                lock (_data)
                {
                    List<PeerEndPoint> expiredPeers = new List<PeerEndPoint>();

                    foreach (List<PeerEndPoint> peerList in _data.Values)
                    {
                        foreach (PeerEndPoint peer in peerList)
                        {
                            if (peer.HasExpired())
                                expiredPeers.Add(peer);
                        }

                        foreach (PeerEndPoint expiredPeer in expiredPeers)
                        {
                            peerList.Remove(expiredPeer);
                        }

                        expiredPeers.Clear();
                    }
                }
            }

            #endregion
        }

        class Transaction
        {
            #region variables

            public BinaryID RemoteNodeID;
            public IPEndPoint RemoteNodeEP;
            public DhtRpcPacket ResponsePacket;

            #endregion

            #region constructor

            public Transaction(BinaryID remoteNodeID, IPEndPoint remoteNodeEP)
            {
                this.RemoteNodeID = remoteNodeID;
                this.RemoteNodeEP = remoteNodeEP;
            }

            #endregion
        }
    }
}
