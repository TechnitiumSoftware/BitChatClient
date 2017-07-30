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
using System.IO;
using TechnitiumLibrary.IO;

namespace BitChatCore.Network.KademliaDHT
{
    enum DhtRpcType : byte
    {
        PING = 0,
        FIND_NODE = 1,
        FIND_PEERS = 2,
        ANNOUNCE_PEER = 3
    }

    class DhtRpcPacket : IWriteStream
    {
        #region variables

        readonly BinaryID _sourceNodeID;
        readonly ushort _sourceNodePort;
        readonly DhtRpcType _type;

        readonly BinaryID _networkID;
        readonly NodeContact[] _contacts;
        readonly PeerEndPoint[] _peers;
        readonly ushort _servicePort;

        #endregion

        #region constructor

        public DhtRpcPacket(Stream s)
        {
            int version = s.ReadByte();

            switch (version)
            {
                case -1:
                    throw new EndOfStreamException();

                case 2:
                    byte[] buffer = new byte[20];

                    OffsetStream.StreamRead(s, buffer, 0, 20);
                    _sourceNodeID = BinaryID.Clone(buffer, 0, 20);

                    OffsetStream.StreamRead(s, buffer, 0, 2);
                    _sourceNodePort = BitConverter.ToUInt16(buffer, 0);

                    _type = (DhtRpcType)s.ReadByte();

                    switch (_type)
                    {
                        case DhtRpcType.PING:
                            break;

                        case DhtRpcType.FIND_NODE:
                            {
                                OffsetStream.StreamRead(s, buffer, 0, 20);
                                _networkID = BinaryID.Clone(buffer, 0, 20);

                                int count = s.ReadByte();
                                _contacts = new NodeContact[count];

                                for (int i = 0; i < count; i++)
                                    _contacts[i] = new NodeContact(s);
                            }
                            break;

                        case DhtRpcType.FIND_PEERS:
                            {
                                OffsetStream.StreamRead(s, buffer, 0, 20);
                                _networkID = BinaryID.Clone(buffer, 0, 20);

                                int count = s.ReadByte();
                                _contacts = new NodeContact[count];

                                for (int i = 0; i < count; i++)
                                    _contacts[i] = new NodeContact(s);

                                count = s.ReadByte();
                                _peers = new PeerEndPoint[count];

                                for (int i = 0; i < count; i++)
                                    _peers[i] = new PeerEndPoint(s);
                            }
                            break;

                        case DhtRpcType.ANNOUNCE_PEER:
                            {
                                OffsetStream.StreamRead(s, buffer, 0, 20);
                                _networkID = BinaryID.Clone(buffer, 0, 20);

                                OffsetStream.StreamRead(s, buffer, 0, 2);
                                _servicePort = BitConverter.ToUInt16(buffer, 0);

                                int count = s.ReadByte();
                                _peers = new PeerEndPoint[count];

                                for (int i = 0; i < count; i++)
                                    _peers[i] = new PeerEndPoint(s);
                            }
                            break;

                        default:
                            throw new IOException("Invalid DHT-RPC type.");
                    }

                    break;

                default:
                    throw new IOException("DHT-RPC packet version not supported: " + version);
            }
        }

        private DhtRpcPacket(BinaryID sourceNodeID, ushort sourceNodePort, DhtRpcType type, BinaryID networkID, NodeContact[] contacts, PeerEndPoint[] peers, ushort servicePort)
        {
            _sourceNodeID = sourceNodeID;
            _sourceNodePort = sourceNodePort;
            _type = type;

            _networkID = networkID;
            _contacts = contacts;
            _peers = peers;
            _servicePort = servicePort;
        }

        #endregion

        #region static create

        public static DhtRpcPacket CreatePingPacket(NodeContact sourceNode)
        {
            return new DhtRpcPacket(sourceNode.NodeID, Convert.ToUInt16(sourceNode.NodeEP.Port), DhtRpcType.PING, null, null, null, 0);
        }

        public static DhtRpcPacket CreateFindNodePacketQuery(NodeContact sourceNode, BinaryID networkID)
        {
            return new DhtRpcPacket(sourceNode.NodeID, Convert.ToUInt16(sourceNode.NodeEP.Port), DhtRpcType.FIND_NODE, networkID, null, null, 0);
        }

        public static DhtRpcPacket CreateFindNodePacketResponse(NodeContact sourceNode, BinaryID networkID, NodeContact[] contacts)
        {
            return new DhtRpcPacket(sourceNode.NodeID, Convert.ToUInt16(sourceNode.NodeEP.Port), DhtRpcType.FIND_NODE, networkID, contacts, null, 0);
        }

        public static DhtRpcPacket CreateFindPeersPacketQuery(NodeContact sourceNode, BinaryID networkID)
        {
            return new DhtRpcPacket(sourceNode.NodeID, Convert.ToUInt16(sourceNode.NodeEP.Port), DhtRpcType.FIND_PEERS, networkID, null, null, 0);
        }

        public static DhtRpcPacket CreateFindPeersPacketResponse(NodeContact sourceNode, BinaryID networkID, NodeContact[] contacts, PeerEndPoint[] peers)
        {
            return new DhtRpcPacket(sourceNode.NodeID, Convert.ToUInt16(sourceNode.NodeEP.Port), DhtRpcType.FIND_PEERS, networkID, contacts, peers, 0);
        }

        public static DhtRpcPacket CreateAnnouncePeerPacketQuery(NodeContact sourceNode, BinaryID networkID, ushort servicePort)
        {
            return new DhtRpcPacket(sourceNode.NodeID, Convert.ToUInt16(sourceNode.NodeEP.Port), DhtRpcType.ANNOUNCE_PEER, networkID, null, null, servicePort);
        }

        public static DhtRpcPacket CreateAnnouncePeerPacketResponse(NodeContact sourceNode, BinaryID networkID, PeerEndPoint[] peers)
        {
            return new DhtRpcPacket(sourceNode.NodeID, Convert.ToUInt16(sourceNode.NodeEP.Port), DhtRpcType.ANNOUNCE_PEER, networkID, null, peers, 0);
        }

        #endregion

        #region public

        public void WriteTo(Stream s)
        {
            s.WriteByte(2); //version
            s.Write(_sourceNodeID.ID, 0, 20); //source node id
            s.Write(BitConverter.GetBytes(_sourceNodePort), 0, 2); //source node port
            s.WriteByte((byte)_type); //type

            switch (_type)
            {
                case DhtRpcType.FIND_NODE:
                    s.Write(_networkID.ID, 0, 20);

                    if (_contacts == null)
                    {
                        s.WriteByte(0);
                    }
                    else
                    {
                        s.WriteByte(Convert.ToByte(_contacts.Length));

                        foreach (NodeContact contact in _contacts)
                            contact.WriteTo(s);
                    }

                    break;

                case DhtRpcType.FIND_PEERS:
                    s.Write(_networkID.ID, 0, 20);

                    if (_contacts == null)
                    {
                        s.WriteByte(0);
                    }
                    else
                    {
                        s.WriteByte(Convert.ToByte(_contacts.Length));

                        foreach (NodeContact contact in _contacts)
                            contact.WriteTo(s);
                    }

                    if (_peers == null)
                    {
                        s.WriteByte(0);
                    }
                    else
                    {
                        s.WriteByte(Convert.ToByte(_peers.Length));

                        foreach (PeerEndPoint peer in _peers)
                            peer.WriteTo(s);
                    }
                    break;

                case DhtRpcType.ANNOUNCE_PEER:
                    s.Write(_networkID.ID, 0, 20);

                    s.Write(BitConverter.GetBytes(_servicePort), 0, 2);

                    if (_peers == null)
                    {
                        s.WriteByte(0);
                    }
                    else
                    {
                        s.WriteByte(Convert.ToByte(_peers.Length));

                        foreach (PeerEndPoint peer in _peers)
                            peer.WriteTo(s);
                    }
                    break;
            }
        }

        public byte[] ToArray()
        {
            using (MemoryStream mS = new MemoryStream(128))
            {
                WriteTo(mS);
                return mS.ToArray();
            }
        }

        public Stream ToStream()
        {
            MemoryStream mS = new MemoryStream(128);
            WriteTo(mS);
            mS.Position = 0;
            return mS;
        }

        #endregion

        #region properties

        public BinaryID SourceNodeID
        { get { return _sourceNodeID; } }

        public ushort SourceNodePort
        { get { return _sourceNodePort; } }

        public DhtRpcType Type
        { get { return _type; } }

        public BinaryID NetworkID
        { get { return _networkID; } }

        public NodeContact[] Contacts
        { get { return _contacts; } }

        public PeerEndPoint[] Peers
        { get { return _peers; } }

        public ushort ServicePort
        { get { return _servicePort; } }

        #endregion
    }
}
