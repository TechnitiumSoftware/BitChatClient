/*
Technitium Bit Chat
Copyright (C) 2017  Shreyas Zare (shreyas@technitium.com)

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

        public const int KADEMLIA_K = 8;
        const int QUERY_TIMEOUT = 5000;
        const int BUFFER_MAX_SIZE = 1024;
        const int HEALTH_CHECK_TIMER_INTERVAL = 5 * 60 * 1000; //5 min
        const int KADEMLIA_ALPHA = 3;

        readonly IDhtClientManager _manager;

        readonly CurrentNode _currentNode;
        readonly KBucket _routingTable;

        readonly Timer _healthTimer;

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
                if (_healthTimer != null)
                    _healthTimer.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region private

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

        private DhtRpcPacket ProcessQuery(DhtRpcPacket query, IPAddress remoteNodeIP)
        {
            if (query.SourceNodeID.Equals(_currentNode.NodeID))
                return null; //decline self node query

            switch (query.Type)
            {
                case DhtRpcType.PING:
                    return DhtRpcPacket.CreatePingPacket(_currentNode);

                case DhtRpcType.FIND_NODE:
                    AddNode(new IPEndPoint(remoteNodeIP, query.SourceNodePort)); //add node if endpoint doesnt exists
                    return DhtRpcPacket.CreateFindNodePacketResponse(_currentNode, query.NetworkID, _routingTable.GetKClosestContacts(query.NetworkID));

                case DhtRpcType.FIND_PEERS:
                    PeerEndPoint[] peers = _currentNode.GetPeers(query.NetworkID);
                    if (peers.Length == 0)
                        return DhtRpcPacket.CreateFindPeersPacketResponse(_currentNode, query.NetworkID, _routingTable.GetKClosestContacts(query.NetworkID), peers);
                    else
                        return DhtRpcPacket.CreateFindPeersPacketResponse(_currentNode, query.NetworkID, new NodeContact[] { }, peers);

                case DhtRpcType.ANNOUNCE_PEER:
                    _currentNode.StorePeer(query.NetworkID, new PeerEndPoint(remoteNodeIP, query.ServicePort));
                    return DhtRpcPacket.CreateAnnouncePeerPacketResponse(_currentNode, query.NetworkID, _currentNode.GetPeers(query.NetworkID));

                default:
                    throw new Exception("Invalid DHT-RPC type.");
            }
        }

        private DhtRpcPacket Query(DhtRpcPacket query, NodeContact contact)
        {
            Stream s = null;

            try
            {
                Stream connection = _manager.CreateConnection(contact.NodeEP);

                //set timeout
                connection.WriteTimeout = QUERY_TIMEOUT;
                connection.ReadTimeout = QUERY_TIMEOUT;

                //enable buffering
                s = new BufferedStream(connection, 512);

                //send DHT TCP switch
                s.WriteByte(0);

                //send query
                query.WriteTo(s);
                s.Flush();

                //read response
                DhtRpcPacket response = new DhtRpcPacket(s);

                //auto add contact or update last seen time
                {
                    bool contactFailed = false;

                    if (contact.NodeID == null)
                    {
                        //must be new contact
                        contact = new NodeContact(response.SourceNodeID, contact.NodeEP);
                    }
                    else if (contact.NodeID != response.SourceNodeID)
                    {
                        //contact nodeID changed! fail old node contact
                        contact.IncrementRpcFailCount();
                        contactFailed = true;

                        //add new node contact
                        contact = new NodeContact(response.SourceNodeID, contact.NodeEP);
                    }

                    KBucket closestBucket = _routingTable.FindClosestBucket(contact.NodeID);
                    NodeContact bucketContact = closestBucket.FindContactInCurrentBucket(contact.NodeID);

                    if (bucketContact == null)
                        closestBucket.AddContactInCurrentBucket(contact);
                    else
                        bucketContact.UpdateLastSeenTime(contact.NodeEP);

                    if (contactFailed)
                        return null;
                }

                return response;
            }
            catch
            {
                contact.IncrementRpcFailCount();
                return null;
            }
            finally
            {
                if (s != null)
                    s.Dispose();
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
            object[] parameters = state as object[];

            object lockObj = parameters[0] as object;
            DhtRpcType queryType = (DhtRpcType)parameters[1];
            NodeContact contact = parameters[2] as NodeContact;
            BinaryID nodeID = parameters[3] as BinaryID;
            List<NodeContact> availableContacts = parameters[4] as List<NodeContact>;
            List<NodeContact> respondedContacts = parameters[5] as List<NodeContact>;
            List<NodeContact> failedContacts = parameters[6] as List<NodeContact>;
            List<NodeContact> receivedContacts = parameters[7] as List<NodeContact>;

            DhtRpcPacket response;

            if (queryType == DhtRpcType.FIND_NODE)
                response = Query(DhtRpcPacket.CreateFindNodePacketQuery(_currentNode, nodeID), contact);
            else
                response = Query(DhtRpcPacket.CreateFindPeersPacketQuery(_currentNode, nodeID), contact);

            if ((response == null) || (response.Type != queryType))
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
                case DhtRpcType.FIND_NODE:
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
                                foreach (NodeContact receivedContact in response.Contacts)
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

                    if (response.Contacts.Length > 0)
                    {
                        //pulse only if the contact has sent next level contacts list
                        lock (lockObj)
                        {
                            Monitor.Pulse(lockObj);
                        }
                    }
                    break;

                case DhtRpcType.FIND_PEERS:
                    if (response.Peers.Length > 0)
                    {
                        List<PeerEndPoint> receivedPeers = parameters[8] as List<PeerEndPoint>;

                        lock (receivedPeers)
                        {
                            foreach (PeerEndPoint peer in response.Peers)
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

        private object QueryFind(NodeContact[] initialContacts, BinaryID nodeID, DhtRpcType queryType)
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

                if (queryType == DhtRpcType.FIND_PEERS)
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

                        if (queryType == DhtRpcType.FIND_PEERS)
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
                                    if (queryType == DhtRpcType.FIND_PEERS)
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
            object[] parameters = state as object[];

            NodeContact contact = parameters[0] as NodeContact;
            BinaryID networkID = parameters[1] as BinaryID;
            List<PeerEndPoint> peers = parameters[2] as List<PeerEndPoint>;
            ushort servicePort = (ushort)parameters[3];

            DhtRpcPacket response = Query(DhtRpcPacket.CreateAnnouncePeerPacketQuery(_currentNode, networkID, servicePort), contact);
            if ((response != null) && (response.Type == DhtRpcType.ANNOUNCE_PEER) && (response.Peers.Length > 0))
            {
                lock (peers)
                {
                    foreach (PeerEndPoint peer in response.Peers)
                    {
                        if (!peers.Contains(peer))
                            peers.Add(peer);
                    }

                    //Monitor.Pulse(peers); //removed so that response from multiple nodes is collected till query times out
                }
            }
        }

        internal bool Ping(NodeContact contact)
        {
            DhtRpcPacket response = Query(DhtRpcPacket.CreatePingPacket(_currentNode), contact);
            return (response != null);
        }

        private void PingAsync(object state)
        {
            Query(DhtRpcPacket.CreatePingPacket(_currentNode), new NodeContact(null, state as IPEndPoint));
        }

        internal NodeContact[] QueryFindNode(NodeContact[] initialContacts, BinaryID nodeID)
        {
            object contacts = QueryFind(initialContacts, nodeID, DhtRpcType.FIND_NODE);

            if (contacts == null)
                return null;

            return contacts as NodeContact[];
        }

        private PeerEndPoint[] QueryFindPeers(NodeContact[] initialContacts, BinaryID networkID)
        {
            object peers = QueryFind(initialContacts, networkID, DhtRpcType.FIND_PEERS);

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

        public void AcceptConnection(Stream s, IPAddress remoteNodeIP)
        {
            while (true)
            {
                DhtRpcPacket response = ProcessQuery(new DhtRpcPacket(s), remoteNodeIP);
                if (response == null)
                    break;

                response.WriteTo(s);
                s.Flush();
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
                    ThreadPool.QueueUserWorkItem(PingAsync, nodeEP);
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
    }
}
