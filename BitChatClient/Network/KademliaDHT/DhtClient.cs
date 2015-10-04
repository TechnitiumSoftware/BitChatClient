/*
Technitium Bit Chat
Copyright (C) 2015  Shreyas Zare (shreyas@technitium.com)

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
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using TechnitiumLibrary.IO;

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
    public class DhtClient
    {
        #region variables

        const int BUFFER_MAX_SIZE = 1024;
        const int SECRET_EXPIRY_SECONDS = 300; //5min
        const int KADEMLIA_K = 8;
        const int KADEMLIA_HEALTH_CHECK_TIMER_INTERVAL = 5 * 60 * 1000; //5min

        Socket _udpListener;
        Thread _readThread;

        CurrentNode _currentNode;
        DhtRpcQueryManager _queryManager;

        Timer _healthTimer;

        //routing table
        KBucket _routingTable;

        //secret
        HashAlgorithm _secretHmac;
        DateTime _secretUpdatedOn;
        HashAlgorithm _previousSecretHmac;

        #endregion

        #region constructor

        public DhtClient(int udpDhtPort, IPEndPoint[] bootstrapNodeEPs)
            : base()
        {
            //bind udp dht port
            _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpListener.Bind(new IPEndPoint(IPAddress.Any, udpDhtPort));

            //init rpc query manager
            _currentNode = new CurrentNode((_udpListener.LocalEndPoint as IPEndPoint).Port);
            _queryManager = new DhtRpcQueryManager(KADEMLIA_K, _currentNode);

            //setup routing table
            _routingTable = new KBucket(KADEMLIA_K, _currentNode, _queryManager);

            //start reading udp packets
            _readThread = new Thread(ReadPacketsAsync);
            _readThread.IsBackground = true;
            _readThread.Start();

            if ((bootstrapNodeEPs != null) && (bootstrapNodeEPs.Length > 0))
            {
                foreach (IPEndPoint nodeEP in bootstrapNodeEPs)
                    ThreadPool.QueueUserWorkItem(AddNodeAfterPingAsync, nodeEP);
            }

            //start health timer
            _healthTimer = new Timer(HealthTimerCallback, null, DhtRpcQueryManager.QUERY_TIMEOUT, Timeout.Infinite);
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

            return new BinaryID(_secretHmac.ComputeHash(nodeIP.GetAddressBytes()));
        }

        private BinaryID GetOldToken(IPAddress nodeIP)
        {
            if (_previousSecretHmac == null)
                return null;

            return new BinaryID(_previousSecretHmac.ComputeHash(nodeIP.GetAddressBytes()));
        }

        private bool IsTokenValid(BinaryID token, IPAddress nodeIP)
        {
            if (token.Equals(GetToken(nodeIP)))
                return true;
            else
                return token.Equals(GetOldToken(nodeIP));
        }

        private void ReadPacketsAsync(object state)
        {
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            FixMemoryStream recvBufferStream = new FixMemoryStream(BUFFER_MAX_SIZE);
            FixMemoryStream sendBufferStream = new FixMemoryStream(BUFFER_MAX_SIZE);
            int bytesRecv;

            try
            {
                while (true)
                {
                    bytesRecv = _udpListener.ReceiveFrom(recvBufferStream.Buffer, ref remoteEP);

                    if (bytesRecv > 0)
                    {
                        recvBufferStream.Position = 0;
                        recvBufferStream.SetLength(bytesRecv);

                        IPEndPoint remoteNodeEP = remoteEP as IPEndPoint;

                        DhtRpcPacket request = new DhtRpcPacket(recvBufferStream, remoteNodeEP.Address);
                        DhtRpcPacket response = null;

                        //only incoming query packets handled on this port
                        switch (request.PacketType)
                        {
                            case RpcPacketType.Query:
                                switch (request.QueryType)
                                {
                                    case RpcQueryType.PING:
                                        #region PING
                                        {
                                            response = DhtRpcPacket.CreatePingPacketResponse(request.TransactionID, _currentNode);
                                            break;
                                        }
                                        #endregion

                                    case RpcQueryType.FIND_NODE:
                                        #region FIND_NODE
                                        {
                                            NodeContact[] contacts = _routingTable.GetKClosestContacts(request.NetworkID);

                                            response = DhtRpcPacket.CreateFindNodePacketResponse(request.TransactionID, _currentNode, request.NetworkID, contacts);
                                            break;
                                        }
                                        #endregion

                                    case RpcQueryType.FIND_PEERS:
                                        #region GET_PEERS
                                        {
                                            PeerEndPoint[] peers = _currentNode.GetPeers(request.NetworkID);
                                            NodeContact[] contacts;

                                            if (peers.Length < 1)
                                                contacts = _routingTable.GetKClosestContacts(request.NetworkID);
                                            else
                                                contacts = new NodeContact[] { };

                                            response = DhtRpcPacket.CreateFindPeersPacketResponse(request.TransactionID, _currentNode, request.NetworkID, contacts, peers, GetToken(remoteNodeEP.Address));
                                            break;
                                        }
                                        #endregion

                                    case RpcQueryType.ANNOUNCE_PEER:
                                        #region ANNOUNCE_PEER
                                        {
                                            IPAddress remoteNodeIP = (remoteEP as IPEndPoint).Address;

                                            if (IsTokenValid(request.Token, remoteNodeIP))
                                                _currentNode.StorePeer(request.NetworkID, new IPEndPoint(remoteNodeIP, request.ServicePort));

                                            response = DhtRpcPacket.CreateAnnouncePeerPacketResponse(request.TransactionID, _currentNode, request.NetworkID);
                                            break;
                                        }
                                        #endregion
                                }

                                //send response
                                if (response != null)
                                {
                                    sendBufferStream.Position = 0;
                                    response.WriteTo(sendBufferStream);
                                    _udpListener.SendTo(sendBufferStream.Buffer, 0, (int)sendBufferStream.Position, SocketFlags.None, remoteEP);
                                }

                                //if contact doesnt exists then add contact else update last seen time
                                NodeContact contact = _routingTable.FindContact(request.SourceNode.NodeID);

                                if (contact == null)
                                    ThreadPool.QueueUserWorkItem(AddNodeAfterPingAsync, request.SourceNode.NodeEP);
                                else
                                    contact.UpdateLastSeenTime();

                                break;
                        }
                    }
                }
            }
            catch
            { }
        }

        private void AddNodeAfterPingAsync(object state)
        {
            try
            {
                IPEndPoint nodeEP = state as IPEndPoint;
                NodeContact contact = _queryManager.Ping(nodeEP);

                if (contact != null)
                {
                    //ping success; add contact to routing table
                    _routingTable.AddContact(contact);
                }
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

                //find closest contacts for current node id
                NodeContact[] initialContacts = _routingTable.GetKClosestContacts(_currentNode.NodeID);

                if (initialContacts.Length < 1)
                    return;

                NodeContact[] closestContacts = _queryManager.QueryFindNode(initialContacts, _currentNode.NodeID);

                if (closestContacts != null)
                {
                    foreach (NodeContact contact in closestContacts)
                        _routingTable.AddContact(contact);
                }

                //check contact health
                _routingTable.CheckContactHealth();

                //refresh buckets
                _routingTable.RefreshBucket();
            }
            catch
            { }
            finally
            {
                if (_healthTimer != null)
                    _healthTimer.Change(KADEMLIA_HEALTH_CHECK_TIMER_INTERVAL, Timeout.Infinite);
            }
        }

        #endregion

        #region public

        public void AddNode(IPEndPoint[] nodeEPs)
        {
            foreach (IPEndPoint nodeEP in nodeEPs)
            {
                if (!_routingTable.ContactExists(nodeEP))
                    ThreadPool.QueueUserWorkItem(AddNodeAfterPingAsync, nodeEP);
            }
        }

        public void AddNode(IPEndPoint nodeEP)
        {
            if (!_routingTable.ContactExists(nodeEP))
                ThreadPool.QueueUserWorkItem(AddNodeAfterPingAsync, nodeEP);
        }

        public PeerEndPoint[] FindPeers(BinaryID networkID)
        {
            NodeContact[] initialContacts = _routingTable.GetKClosestContacts(networkID);

            if (initialContacts.Length < 1)
                return null;

            return _queryManager.QueryFindPeers(initialContacts, networkID);
        }

        public PeerEndPoint[] Announce(BinaryID networkID, ushort servicePort)
        {
            NodeContact[] initialContacts = _routingTable.GetKClosestContacts(networkID);

            if (initialContacts.Length < 1)
                return null;

            return _queryManager.QueryAnnounce(initialContacts, networkID, servicePort);
        }

        #endregion

        class CurrentNode : NodeContact
        {
            #region variables

            const int MAX_PEERS_TO_RETURN = 50;

            Dictionary<BinaryID, List<PeerEndPoint>> _data = new Dictionary<BinaryID, List<PeerEndPoint>>();

            #endregion

            #region constructor

            public CurrentNode(int udpDhtPort)
                : base(udpDhtPort)
            { }

            #endregion

            #region public

            public void StorePeer(BinaryID networkID, IPEndPoint peerEP)
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
                        if (peer.PeerEP.Equals(peerEP))
                        {
                            peer.UpdateDateAdded();
                            return;
                        }
                    }

                    peerList.Add(new PeerEndPoint(peerEP));
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
    }
}
