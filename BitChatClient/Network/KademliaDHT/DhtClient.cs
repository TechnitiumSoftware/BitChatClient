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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;

namespace BitChatClient.Network.KademliaDHT
{
    class DhtClient
    {
        #region variables

        const int BUFFER_MAX_SIZE = 1024;
        const int SECRET_EXPIRY_SECONDS = 300;

        Socket _udpListener;
        Thread _readThread;

        DhtRpcQueryManager _queryManager;

        CurrentNode _currentNode;

        //routing table
        int _maxDhtNodes;
        KBucket _routingTable;

        //secret
        HashAlgorithm _secretHmac;
        DateTime _secretUpdatedOn;
        HashAlgorithm _previousSecretHmac;

        #endregion

        #region constructor

        public DhtClient(int udpDhtPort, int maxDhtNodes = 300, int k = 8)
            : base()
        {
            //bind udp dht port
            _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpListener.Bind(new IPEndPoint(IPAddress.Any, udpDhtPort));

            //setup routing table
            _currentNode = new CurrentNode();
            _routingTable = new KBucket(k, _currentNode);
            _maxDhtNodes = maxDhtNodes;

            //start reading udp packets
            _readThread = new Thread(ReadPackets);
            _readThread.Start();

            //init rpc query manager
            _queryManager = new DhtRpcQueryManager(_currentNode.NodeID);
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

        private void ReadPackets(object state)
        {
            EndPoint remoteNodeEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] recvBuffer = new byte[BUFFER_MAX_SIZE];
            MemoryStream recvBufferStream = new MemoryStream(recvBuffer, false);
            int bytesRecv;
            byte[] sendBuffer = new byte[BUFFER_MAX_SIZE];
            MemoryStream sendBufferStream = new MemoryStream(sendBuffer);

            try
            {
                while (true)
                {
                    bytesRecv = _udpListener.ReceiveFrom(recvBuffer, ref remoteNodeEP);

                    if (bytesRecv > 0)
                    {
                        recvBufferStream.Position = 0;
                        recvBufferStream.SetLength(bytesRecv);

                        DhtRpcPacket packet = new DhtRpcPacket(recvBufferStream, _queryManager);

                        //only incoming query packets handled on this port
                        switch (packet.PacketType)
                        {
                            case RpcPacketType.Query:
                                switch (packet.QueryType)
                                {
                                    case RpcQueryType.PING:
                                        #region PING

                                        sendBufferStream.Position = 0;
                                        DhtRpcPacket.CreatePingPacketResponse(packet.TransactionID, _currentNode.NodeID).WriteTo(sendBufferStream);
                                        _udpListener.SendTo(sendBuffer, 0, (int)sendBufferStream.Position, SocketFlags.None, remoteNodeEP);

                                        break;

                                        #endregion

                                    case RpcQueryType.FIND_NODE:
                                        #region FIND_NODE

                                        KBucket closestBucket;

                                        lock (_routingTable)
                                        {
                                            closestBucket = _routingTable.FindClosestBucket(packet.NetworkID);
                                        }

                                        NodeContact[] contacts = closestBucket.GetContacts();

                                        sendBufferStream.Position = 0;
                                        DhtRpcPacket.CreateFindNodePacketResponse(packet.TransactionID, _currentNode.NodeID, packet.NetworkID, contacts).WriteTo(sendBufferStream);
                                        _udpListener.SendTo(sendBuffer, 0, (int)sendBufferStream.Position, SocketFlags.None, remoteNodeEP);

                                        break;

                                        #endregion

                                    case RpcQueryType.GET_PEERS:
                                        #region GET_PEERS

                                        PeerEndPoint[] peers = _currentNode.GetPeers(packet.NetworkID);

                                        sendBufferStream.Position = 0;
                                        DhtRpcPacket.CreateGetPeersPacketResponse(packet.TransactionID, _currentNode.NodeID, packet.NetworkID, peers, GetToken((remoteNodeEP as IPEndPoint).Address)).WriteTo(sendBufferStream);
                                        _udpListener.SendTo(sendBuffer, 0, (int)sendBufferStream.Position, SocketFlags.None, remoteNodeEP);

                                        break;

                                        #endregion

                                    case RpcQueryType.ANNOUNCE_PEER:
                                        #region ANNOUNCE_PEER

                                        IPAddress remoteNodeIP = (remoteNodeEP as IPEndPoint).Address;

                                        if (IsTokenValid(packet.Token, remoteNodeIP))
                                            _currentNode.StorePeer(packet.NetworkID, new IPEndPoint(remoteNodeIP, packet.ServicePort));

                                        break;

                                        #endregion
                                }
                                break;
                        }
                    }
                }
            }
            catch
            { }
        }

        private void AddContactAsync(object state)
        {
            try
            {
                IPEndPoint contactEP = state as IPEndPoint;
                NodeContact contact = new NodeContact(contactEP, _queryManager);

                if (contact.Ping())
                {
                    //ping success; add contact to routing table
                    lock (_routingTable)
                    {
                        _routingTable.AddContact(contact);
                    }
                }
            }
            catch
            { }
        }

        #endregion

        #region public

        public void AddContact(List<IPEndPoint> contactEPs)
        {
            foreach (IPEndPoint contactEP in contactEPs)
            {
                AddContact(contactEP);
            }
        }

        public void AddContact(IPEndPoint contactEP)
        {
            ThreadPool.QueueUserWorkItem(AddContactAsync, contactEP);
        }

        public void AnnouncePeer(BinaryID networkID, ushort servicePort)
        {

        }

        public PeerEndPoint[] GetPeers(BinaryID networkID)
        {
            return null;
        }

        #endregion

        class CurrentNode : NodeContact
        {
            #region variables

            Dictionary<BinaryID, List<PeerEndPoint>> _data = new Dictionary<BinaryID, List<PeerEndPoint>>();

            #endregion

            #region public

            public void StorePeer(BinaryID networkID, IPEndPoint peerEP)
            {
                List<PeerEndPoint> peerList;

                lock (_data)
                {
                    try
                    {
                        peerList = _data[networkID];
                    }
                    catch (KeyNotFoundException)
                    {
                        peerList = new List<PeerEndPoint>();
                        _data.Add(networkID, peerList);
                    }
                }

                lock (peerList)
                {
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

            public override NodeContactStatus GetStatus()
            {
                return NodeContactStatus.Fresh;
            }

            public override bool Ping()
            {
                return true;
            }

            public override void AnnouncePeer(BinaryID networkID, ushort servicePort, BinaryID token)
            {
                return;
            }

            public override NodeContact[] FindNode(BinaryID networkID)
            {
                return new NodeContact[] { };
            }

            public override PeerEndPoint[] GetPeers(BinaryID networkID)
            {
                lock (_data)
                {
                    try
                    {
                        return _data[networkID].ToArray();
                    }
                    catch (KeyNotFoundException)
                    {
                        return new PeerEndPoint[] { };
                    }
                }
            }

            #endregion
        }
    }
}
