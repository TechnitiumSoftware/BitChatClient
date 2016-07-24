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
using System.IO;
using System.Net;
using System.Security.Cryptography;
using TechnitiumLibrary.IO;

namespace BitChatClient.Network.KademliaDHT
{
    enum RpcPacketType : byte
    {
        Query = 0,
        Response = 1
    }

    enum RpcQueryType : byte
    {
        PING = 0,
        FIND_NODE = 1,
        FIND_PEERS = 2,
        ANNOUNCE_PEER = 3
    }

    class DhtRpcPacket : IWriteStream
    {
        #region variables

        static RandomNumberGenerator _rnd = new RNGCryptoServiceProvider();

        int _transactionID;
        NodeContact _sourceNode;
        RpcPacketType _type;
        RpcQueryType _queryType;

        BinaryID _networkID;
        NodeContact[] _contacts;
        PeerEndPoint[] _peers;
        BinaryID _token;
        ushort _servicePort;

        #endregion

        #region constructor

        public DhtRpcPacket(Stream s, IPAddress nodeIP)
        {
            int version = s.ReadByte();

            switch (version)
            {
                case 1:
                    byte[] buffer = new byte[20];

                    OffsetStream.StreamRead(s, buffer, 0, 4);
                    _transactionID = BitConverter.ToInt32(buffer, 0);

                    OffsetStream.StreamRead(s, buffer, 0, 20);
                    BinaryID nodeID = BinaryID.Clone(buffer, 0, 20);

                    OffsetStream.StreamRead(s, buffer, 0, 2);
                    _sourceNode = new NodeContact(nodeID, new IPEndPoint(nodeIP, BitConverter.ToUInt16(buffer, 0)));

                    _type = (RpcPacketType)s.ReadByte();
                    _queryType = (RpcQueryType)s.ReadByte();

                    switch (_queryType)
                    {
                        case RpcQueryType.FIND_NODE:
                            OffsetStream.StreamRead(s, buffer, 0, 20);
                            _networkID = BinaryID.Clone(buffer, 0, 20);

                            if (_type == RpcPacketType.Response)
                            {
                                int count = s.ReadByte();
                                _contacts = new NodeContact[count];

                                for (int i = 0; i < count; i++)
                                {
                                    _contacts[i] = new NodeContact(s);
                                }
                            }
                            break;

                        case RpcQueryType.FIND_PEERS:
                            OffsetStream.StreamRead(s, buffer, 0, 20);
                            _networkID = BinaryID.Clone(buffer, 0, 20);

                            if (_type == RpcPacketType.Response)
                            {
                                int count = s.ReadByte();
                                _contacts = new NodeContact[count];

                                for (int i = 0; i < count; i++)
                                {
                                    _contacts[i] = new NodeContact(s);
                                }

                                count = s.ReadByte();
                                _peers = new PeerEndPoint[count];

                                for (int i = 0; i < count; i++)
                                {
                                    _peers[i] = new PeerEndPoint(s);
                                }

                                OffsetStream.StreamRead(s, buffer, 0, 20);
                                _token = new BinaryID(buffer);
                            }
                            break;

                        case RpcQueryType.ANNOUNCE_PEER:
                            OffsetStream.StreamRead(s, buffer, 0, 20);
                            _networkID = BinaryID.Clone(buffer, 0, 20);

                            if (_type == RpcPacketType.Query)
                            {
                                OffsetStream.StreamRead(s, buffer, 0, 2);
                                _servicePort = BitConverter.ToUInt16(buffer, 0);

                                OffsetStream.StreamRead(s, buffer, 0, 20);
                                _token = new BinaryID(buffer);
                            }
                            break;
                    }

                    break;

                default:
                    throw new IOException("DHT-RPC packet version not supported: " + version);
            }
        }

        private DhtRpcPacket(int transactionID, NodeContact sourceNode, RpcPacketType type, RpcQueryType queryType)
        {
            _transactionID = transactionID;
            _sourceNode = sourceNode;
            _type = type;
            _queryType = queryType;
        }

        #endregion

        #region private

        private static int GetRandomTransactionID()
        {
            byte[] rndBytes = new byte[4];
            _rnd.GetBytes(rndBytes);
            return BitConverter.ToInt32(rndBytes, 0);
        }

        #endregion

        #region static create

        public static DhtRpcPacket CreatePingPacketQuery(NodeContact sourceNode)
        {
            return new DhtRpcPacket(GetRandomTransactionID(), sourceNode, RpcPacketType.Query, RpcQueryType.PING);
        }

        public static DhtRpcPacket CreatePingPacketResponse(int transactionID, NodeContact sourceNode)
        {
            return new DhtRpcPacket(transactionID, sourceNode, RpcPacketType.Response, RpcQueryType.PING);
        }

        public static DhtRpcPacket CreateFindNodePacketQuery(NodeContact sourceNode, BinaryID networkID)
        {
            DhtRpcPacket packet = new DhtRpcPacket(GetRandomTransactionID(), sourceNode, RpcPacketType.Query, RpcQueryType.FIND_NODE);
            packet._networkID = networkID;

            return packet;
        }

        public static DhtRpcPacket CreateFindNodePacketResponse(int transactionID, NodeContact sourceNode, BinaryID networkID, NodeContact[] contacts)
        {
            DhtRpcPacket packet = new DhtRpcPacket(transactionID, sourceNode, RpcPacketType.Response, RpcQueryType.FIND_NODE);
            packet._networkID = networkID;
            packet._contacts = contacts;

            return packet;
        }

        public static DhtRpcPacket CreateFindPeersPacketQuery(NodeContact sourceNode, BinaryID networkID)
        {
            DhtRpcPacket packet = new DhtRpcPacket(GetRandomTransactionID(), sourceNode, RpcPacketType.Query, RpcQueryType.FIND_PEERS);
            packet._networkID = networkID;

            return packet;
        }

        public static DhtRpcPacket CreateFindPeersPacketResponse(int transactionID, NodeContact sourceNode, BinaryID networkID, NodeContact[] contacts, PeerEndPoint[] peers, BinaryID token)
        {
            DhtRpcPacket packet = new DhtRpcPacket(transactionID, sourceNode, RpcPacketType.Response, RpcQueryType.FIND_PEERS);
            packet._networkID = networkID;
            packet._contacts = contacts;
            packet._peers = peers;
            packet._token = token;

            return packet;
        }

        public static DhtRpcPacket CreateAnnouncePeerPacketQuery(NodeContact sourceNode, BinaryID networkID, ushort servicePort, BinaryID token)
        {
            DhtRpcPacket packet = new DhtRpcPacket(GetRandomTransactionID(), sourceNode, RpcPacketType.Query, RpcQueryType.ANNOUNCE_PEER);
            packet._networkID = networkID;
            packet._servicePort = servicePort;
            packet._token = token;

            return packet;
        }

        public static DhtRpcPacket CreateAnnouncePeerPacketResponse(int transactionID, NodeContact sourceNode, BinaryID networkID)
        {
            DhtRpcPacket packet = new DhtRpcPacket(transactionID, sourceNode, RpcPacketType.Response, RpcQueryType.ANNOUNCE_PEER);
            packet._networkID = networkID;

            return packet;
        }

        #endregion

        #region public

        public void WriteTo(Stream s)
        {
            s.WriteByte((byte)1); //version
            s.Write(BitConverter.GetBytes(_transactionID), 0, 4); //transaction id
            s.Write(_sourceNode.NodeID.ID, 0, 20); //source node id
            s.Write(BitConverter.GetBytes(Convert.ToUInt16(_sourceNode.NodeEP.Port)), 0, 2); //source node port
            s.WriteByte((byte)_type); //message type
            s.WriteByte((byte)_queryType); //query type

            switch (_queryType)
            {
                case RpcQueryType.FIND_NODE:
                    s.Write(_networkID.ID, 0, 20);

                    if (_type == RpcPacketType.Response)
                    {
                        s.WriteByte(Convert.ToByte(_contacts.Length));

                        foreach (NodeContact contact in _contacts)
                        {
                            contact.WriteTo(s);
                        }
                    }

                    break;

                case RpcQueryType.FIND_PEERS:
                    s.Write(_networkID.ID, 0, 20);

                    if (_type == RpcPacketType.Response)
                    {
                        s.WriteByte(Convert.ToByte(_contacts.Length));

                        foreach (NodeContact contact in _contacts)
                        {
                            contact.WriteTo(s);
                        }

                        s.WriteByte(Convert.ToByte(_peers.Length));

                        foreach (PeerEndPoint peer in _peers)
                        {
                            peer.WriteTo(s);
                        }

                        s.Write(_token.ID, 0, 20);
                    }

                    break;

                case RpcQueryType.ANNOUNCE_PEER:
                    s.Write(_networkID.ID, 0, 20);

                    if (_type == RpcPacketType.Query)
                    {
                        s.Write(BitConverter.GetBytes(_servicePort), 0, 2);
                        s.Write(_token.ID, 0, 20);
                    }
                    break;
            }
        }

        public byte[] ToArray()
        {
            using (MemoryStream mS = new MemoryStream())
            {
                WriteTo(mS);
                return mS.ToArray();
            }
        }

        public Stream ToStream()
        {
            MemoryStream mS = new MemoryStream();
            WriteTo(mS);
            mS.Position = 0;
            return mS;
        }

        #endregion

        #region properties

        public int TransactionID
        { get { return _transactionID; } }

        public NodeContact SourceNode
        { get { return _sourceNode; } }

        public RpcPacketType PacketType
        { get { return _type; } }

        public RpcQueryType QueryType
        { get { return _queryType; } }

        public BinaryID NetworkID
        { get { return _networkID; } }

        public NodeContact[] Contacts
        { get { return _contacts; } }

        public PeerEndPoint[] Peers
        { get { return _peers; } }

        public BinaryID Token
        { get { return _token; } }

        public ushort ServicePort
        { get { return _servicePort; } }

        #endregion
    }
}
