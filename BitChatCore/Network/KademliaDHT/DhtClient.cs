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
 * 2. RPC: TCP based protocol with PING & FIND_NODE implemented. FIND_PEER & ANNOUNCE_PEER implemented similar to BitTorrent DHT implementation.
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

namespace BitChatCore.Network.KademliaDHT
{
    class DhtClient : IDisposable
    {
        #region variables

        const int BUFFER_MAX_SIZE = 1024;
        const int SECRET_EXPIRY_SECONDS = 300; //5min
        public const int KADEMLIA_K = 8;
        const int HEALTH_CHECK_TIMER_INTERVAL = 5 * 60 * 1000; //5 min
        public const int QUERY_TIMEOUT = 5000;
        const int KADEMLIA_ALPHA = 3;

        IDhtClientManager _manager;

        CurrentNode _currentNode;
        KBucket _routingTable;

        Timer _healthTimer;

        Dictionary<int, Transaction> _transactions = new Dictionary<int, Transaction>(10);

        #endregion

        #region constructor

        public DhtClient(int localDhtPort, IDhtClientManager manager)
        {
            _manager = manager;

            //init routing table
            _currentNode = new CurrentNode(localDhtPort);
            _routingTable = new KBucket(_currentNode);

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
                if (_routingTable != null)
                    _routingTable.Dispose();

                if (_healthTimer != null)
                {
                    _healthTimer.Dispose();
                    _healthTimer = null;
                }

                _disposed = true;
            }
        }

        #endregion

        #region private

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

        private DhtRpcPacket ProcessQueryPacket(DhtRpcPacket packet)
        {
            if (packet.PacketType == RpcPacketType.Query)
            {
                DhtRpcPacket response = null;

                switch (packet.QueryType)
                {
                    case RpcQueryType.PING:
                        #region PING
                        {
                            response = DhtRpcPacket.CreatePingPacketResponse(packet.TransactionID, _currentNode);
                        }
                        #endregion
                        break;

                    case RpcQueryType.FIND_NODE:
                        #region FIND_NODE
                        {
                            NodeContact[] contacts = _routingTable.GetKClosestContacts(packet.NetworkID);

                            response = DhtRpcPacket.CreateFindNodePacketResponse(packet.TransactionID, _currentNode, packet.NetworkID, contacts);
                        }
                        #endregion
                        break;

                    case RpcQueryType.FIND_PEERS:
                        #region GET_PEERS
                        {
                            PeerEndPoint[] peers = _currentNode.GetPeers(packet.NetworkID);
                            NodeContact[] contacts;

                            if (peers.Length < 1)
                                contacts = _routingTable.GetKClosestContacts(packet.NetworkID);
                            else
                                contacts = new NodeContact[] { };

                            response = DhtRpcPacket.CreateFindPeersPacketResponse(packet.TransactionID, _currentNode, packet.NetworkID, contacts, peers);
                        }
                        #endregion
                        break;

                    case RpcQueryType.ANNOUNCE_PEER:
                        #region ANNOUNCE_PEER
                        {
                            _currentNode.StorePeer(packet.NetworkID, new PeerEndPoint(packet.SourceNode.NodeEP.Address, packet.ServicePort));

                            response = DhtRpcPacket.CreateAnnouncePeerPacketResponse(packet.TransactionID, _currentNode, packet.NetworkID);
                        }
                        #endregion
                        break;
                }

                return response;
            }

            return null;
        }

        private void ProcessResponsePacket(DhtRpcPacket packet)
        {
            if (packet.PacketType == RpcPacketType.Response)
            {
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
                    //send packet async
                    ThreadPool.QueueUserWorkItem(SendDhtPacketAsync, new object[] { contact.NodeEP, packet });

                    //wait for response
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

        private void SendDhtPacketAsync(object state)
        {
            object[] parameters = state as object[];

            IPEndPoint remoteNodeEP = parameters[0] as IPEndPoint;
            DhtRpcPacket packet = parameters[1] as DhtRpcPacket;

            using (FixMemoryStream mS = new FixMemoryStream(BUFFER_MAX_SIZE))
            {
                mS.WriteByte(0); //version=0 switch
                packet.WriteTo(mS);

                try
                {
                    _manager.SendDhtPacket(remoteNodeEP, mS.Buffer, 0, (int)mS.Length);
                }
                catch
                { }
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
                switch (queryType)
                {
                    case RpcQueryType.FIND_NODE:
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

                        if (responsePacket.Contacts.Length > 0)
                        {
                            //pulse only if the contact has sent next level contacts list
                            lock (lockObj)
                            {
                                Monitor.Pulse(lockObj);
                            }
                        }
                        break;

                    case RpcQueryType.FIND_PEERS:
                        if (responsePacket.Peers.Length > 0)
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
                        break;
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

                    Query(DhtRpcPacket.CreateAnnouncePeerPacketQuery(_currentNode, networkID, servicePort), contact);
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

        public byte[] ProcessQueryPacket(Stream s, IPAddress remoteNodeIP)
        {
            DhtRpcPacket response = ProcessQueryPacket(new DhtRpcPacket(s, remoteNodeIP));

            if (response == null)
                return null;

            return response.ToArray();
        }

        public void ProcessResponsePacket(Stream s, IPAddress remoteNodeIP)
        {
            ProcessResponsePacket(new DhtRpcPacket(s, remoteNodeIP));
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

        #endregion

        class CurrentNode : NodeContact
        {
            #region variables

            const int MAX_PEERS_TO_RETURN = 30;

            Dictionary<BinaryID, List<PeerEndPoint>> _data = new Dictionary<BinaryID, List<PeerEndPoint>>();

            #endregion

            #region constructor

            public CurrentNode(int localDhtPort)
                : base(localDhtPort)
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
