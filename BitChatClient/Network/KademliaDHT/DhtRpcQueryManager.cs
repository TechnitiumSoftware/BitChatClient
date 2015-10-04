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

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TechnitiumLibrary.IO;

namespace BitChatClient.Network.KademliaDHT
{
    class DhtRpcQueryManager
    {
        #region variables

        const int BUFFER_MAX_SIZE = 1024;
        public const int QUERY_TIMEOUT = 5000;
        const int KADEMLIA_ALPHA = 3;

        int _k;
        NodeContact _currentNode;

        Socket _udpClient;
        Thread _readThread;

        FixMemoryStream _sendBufferStream = new FixMemoryStream(BUFFER_MAX_SIZE);

        Dictionary<int, Transaction> _transactions = new Dictionary<int, Transaction>(10);

        #endregion

        #region constructor

        public DhtRpcQueryManager(int k, NodeContact currentNode)
        {
            _k = k;
            _currentNode = currentNode;

            //bind udp socket to random port
            _udpClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpClient.Bind(new IPEndPoint(IPAddress.Any, 0));

            //start reading udp packets
            _readThread = new Thread(ReadPacketsAsync);
            _readThread.IsBackground = true;
            _readThread.Start();
        }

        #endregion

        #region private

        private void ReadPacketsAsync(object state)
        {
            EndPoint remoteNodeEP = new IPEndPoint(IPAddress.Any, 0);
            FixMemoryStream recvStream = new FixMemoryStream(BUFFER_MAX_SIZE);
            byte[] bufferRecv = recvStream.Buffer;
            int bytesRecv;

            try
            {
                while (true)
                {
                    bytesRecv = _udpClient.ReceiveFrom(bufferRecv, ref remoteNodeEP);

                    if (bytesRecv > 0)
                    {
                        recvStream.SetLength(bytesRecv);
                        recvStream.Position = 0;

                        try
                        {
                            DhtRpcPacket response = new DhtRpcPacket(recvStream, (remoteNodeEP as IPEndPoint).Address);

                            //only incoming response packets handled here
                            if (response.PacketType == RpcPacketType.Response)
                            {
                                Transaction transaction = null;

                                lock (_transactions)
                                {
                                    try
                                    {
                                        transaction = _transactions[response.TransactionID];
                                    }
                                    catch (KeyNotFoundException)
                                    { }
                                }

                                if ((transaction != null) && transaction.RemoteNodeEP.Equals(remoteNodeEP))
                                {
                                    lock (transaction)
                                    {
                                        transaction.ResponsePacket = response;

                                        Monitor.Pulse(transaction);
                                    }
                                }
                            }
                        }
                        catch
                        { }
                    }
                }
            }
            catch
            { }
        }

        private DhtRpcPacket Query(DhtRpcPacket packet, IPEndPoint remoteNodeEP)
        {
            Transaction transaction = new Transaction(remoteNodeEP);

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
                        _udpClient.SendTo(_sendBufferStream.Buffer, 0, (int)_sendBufferStream.Position, SocketFlags.None, remoteNodeEP);
                    }

                    if (!Monitor.Wait(transaction, QUERY_TIMEOUT))
                        return null;

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

        private void QueryFindNodeAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                NodeContact contact = parameters[0] as NodeContact;
                BinaryID nodeID = parameters[1] as BinaryID;
                List<NodeContact> availableContacts = parameters[2] as List<NodeContact>;
                List<NodeContact> respondedContacts = parameters[3] as List<NodeContact>;
                List<NodeContact> failedContacts = parameters[4] as List<NodeContact>;
                List<NodeContact> receivedContacts = parameters[5] as List<NodeContact>;

                DhtRpcPacket responsePacket = Query(DhtRpcPacket.CreateFindNodePacketQuery(_currentNode, nodeID), contact.NodeEP);

                if (responsePacket == null)
                {
                    //time out
                    contact.IncrementRpcFailCount();

                    //add contact to failed contacts
                    lock (failedContacts)
                    {
                        if (!failedContacts.Contains(contact))
                            failedContacts.Add(contact);
                    }
                }
                else
                {
                    //got reply!
                    contact.UpdateLastSeenTime();

                    if ((responsePacket.QueryType == RpcQueryType.FIND_NODE) && (responsePacket.Contacts.Length > 0))
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

                            Monitor.Pulse(receivedContacts);
                        }
                    }
                    else
                    {
                        //invalid response received
                        //add contact to failed contacts
                        lock (failedContacts)
                        {
                            if (!failedContacts.Contains(contact))
                                failedContacts.Add(contact);
                        }
                    }
                }
            }
            catch
            { }
        }

        private void QueryFindPeersAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                NodeContact contact = parameters[0] as NodeContact;
                BinaryID networkID = parameters[1] as BinaryID;
                List<PeerEndPoint> peers = parameters[2] as List<PeerEndPoint>;

                DhtRpcPacket responsePacket = Query(DhtRpcPacket.CreateFindPeersPacketQuery(_currentNode, networkID), contact.NodeEP);

                if (responsePacket == null)
                {
                    contact.IncrementRpcFailCount();
                }
                else
                {
                    contact.UpdateLastSeenTime();

                    if ((responsePacket.QueryType == RpcQueryType.FIND_PEERS) && (responsePacket.Peers.Length > 0))
                    {
                        lock (peers)
                        {
                            foreach (PeerEndPoint peer in responsePacket.Peers)
                            {
                                if (!peers.Contains(peer))
                                    peers.Add(peer);
                            }

                            Monitor.Pulse(peers);
                        }
                    }
                }
            }
            catch
            { }
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

                DhtRpcPacket responsePacket1 = Query(DhtRpcPacket.CreateFindPeersPacketQuery(_currentNode, networkID), contact.NodeEP);

                if (responsePacket1 == null)
                {
                    contact.IncrementRpcFailCount();
                }
                else
                {
                    contact.UpdateLastSeenTime();

                    if (responsePacket1.QueryType == RpcQueryType.FIND_PEERS)
                    {
                        if (responsePacket1.Peers.Length > 0)
                        {
                            lock (peers)
                            {
                                foreach (PeerEndPoint peer in responsePacket1.Peers)
                                {
                                    if (!peers.Contains(peer))
                                        peers.Add(peer);
                                }

                                Monitor.Pulse(peers);
                            }
                        }

                        DhtRpcPacket responsePacket2 = Query(DhtRpcPacket.CreateAnnouncePeerPacketQuery(_currentNode, networkID, servicePort, responsePacket1.Token), contact.NodeEP);

                        if (responsePacket2 == null)
                            contact.IncrementRpcFailCount();
                        else
                            contact.UpdateLastSeenTime();
                    }
                }
            }
            catch
            { }
        }

        #endregion

        #region public

        public bool Ping(NodeContact contact)
        {
            NodeContact recvContact = Ping(contact.NodeEP);

            if (recvContact == null)
            {
                contact.IncrementRpcFailCount();
                return false;
            }
            else
            {
                if (contact.Equals(recvContact))
                {
                    contact.UpdateLastSeenTime();
                    return true;
                }
                else
                {
                    contact.IncrementRpcFailCount();
                    return false;
                }
            }
        }

        public NodeContact Ping(IPEndPoint nodeEP)
        {
            DhtRpcPacket response = Query(DhtRpcPacket.CreatePingPacketQuery(_currentNode), nodeEP);

            if (response == null)
                return null;
            else
                return response.SourceNode;
        }

        public NodeContact[] QueryFindNode(NodeContact[] initialContacts, BinaryID nodeID)
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
                        if (respondedContacts.Count > _k)
                            return KBucket.GetClosestContacts(respondedContacts, nodeID, _k);
                        else if (respondedContacts.Count > 0)
                            return respondedContacts.ToArray();
                    }

                    return null;
                }

                List<NodeContact> receivedContacts = new List<NodeContact>();

                lock (receivedContacts)
                {
                    //query each alpha contact async
                    foreach (NodeContact alphaContact in alphaContacts)
                    {
                        ThreadPool.QueueUserWorkItem(QueryFindNodeAsync, new object[] { alphaContact, nodeID, availableContacts, respondedContacts, failedContacts, receivedContacts });
                    }

                    //wait for any of the contact to return new contacts
                    if (Monitor.Wait(receivedContacts, QUERY_TIMEOUT))
                    {
                        //got reply!

                        if (receivedContacts.Count < 1)
                        {
                            //current round failed to return any new closer nodes

                            if (finalRound)
                            {
                                lock (respondedContacts)
                                {
                                    if (respondedContacts.Count > _k)
                                        return KBucket.GetClosestContacts(respondedContacts, nodeID, _k);
                                    else
                                        return respondedContacts.ToArray();
                                }
                            }
                            else
                            {
                                //resend query to k closest node not already queried
                                finalRound = true;
                                alpha = _k;
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

        public PeerEndPoint[] QueryFindPeers(NodeContact[] initialContacts, BinaryID networkID)
        {
            NodeContact[] contacts = QueryFindNode(initialContacts, networkID);

            if (contacts == null)
                return new PeerEndPoint[] { };

            List<NodeContact> finalContacts = new List<NodeContact>(contacts);
            NodeContact[] alphaContacts;

            while (true)
            {
                alphaContacts = PickClosestContacts(finalContacts, KADEMLIA_ALPHA);

                if (alphaContacts.Length < 1)
                {
                    //no contacts available to query
                    return null;
                }

                List<PeerEndPoint> peers = new List<PeerEndPoint>();

                lock (peers)
                {
                    foreach (NodeContact contact in alphaContacts)
                    {
                        ThreadPool.QueueUserWorkItem(QueryFindPeersAsync, new object[] { contact, networkID, peers });
                    }

                    Monitor.Wait(peers, QUERY_TIMEOUT);

                    if (peers.Count > 0)
                        return peers.ToArray();
                }
            }
        }

        public PeerEndPoint[] QueryAnnounce(NodeContact[] initialContacts, BinaryID networkID, ushort servicePort)
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

        class Transaction
        {
            #region variables

            public IPEndPoint RemoteNodeEP;
            public DhtRpcPacket ResponsePacket;

            #endregion

            #region constructor

            public Transaction(IPEndPoint remoteNodeEP)
            {
                this.RemoteNodeEP = remoteNodeEP;
            }

            #endregion
        }
    }
}
