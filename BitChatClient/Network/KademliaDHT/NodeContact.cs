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
using System.Net.Sockets;
using TechnitiumLibrary.IO;

namespace BitChatClient.Network.KademliaDHT
{
    enum NodeContactStatus
    {
        Fresh = 0,
        Stale = 1
    }

    class NodeContact : WriteStream
    {
        #region variables

        const int NODE_RPC_FAIL_LIMIT = 5; //max failed RPC count before declaring node stale
        const int NODE_STALE_TIMEOUT_SECONDS = 900; //15mins timeout before declaring node stale

        BinaryID _nodeID;
        IPEndPoint _nodeEP;

        DhtRpcQueryManager _queryManager;

        bool _currentNode;
        DateTime _lastSeen;
        int _failRpcCount = 0;

        #endregion

        #region constructor

        public NodeContact(Stream s, DhtRpcQueryManager queryManager)
        {
            byte[] nodeID = new byte[20];

            OffsetStream.StreamRead(s, nodeID, 0, 20);
            _nodeID = new BinaryID(nodeID);

            byte[] address;
            byte[] port;

            switch (s.ReadByte())
            {
                case 0:
                    address = new byte[4];
                    port = new byte[2];

                    OffsetStream.StreamRead(s, address, 0, 4);
                    OffsetStream.StreamRead(s, port, 0, 2);
                    break;

                case 1:
                    address = new byte[16];
                    port = new byte[2];

                    OffsetStream.StreamRead(s, address, 0, 16);
                    OffsetStream.StreamRead(s, port, 0, 2);
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            _nodeEP = new IPEndPoint(new IPAddress(address), BitConverter.ToUInt16(port, 0));
            _queryManager = queryManager;
        }

        public NodeContact(IPEndPoint nodeEP, DhtRpcQueryManager queryManager)
        {
            _nodeEP = nodeEP;
            _queryManager = queryManager;
        }

        protected NodeContact()
        {
            _nodeID = BinaryID.GenerateRandomID160();
            _currentNode = true;
        }

        #endregion

        #region public

        public virtual NodeContactStatus GetStatus()
        {
            if ((_failRpcCount > NODE_RPC_FAIL_LIMIT) || ((DateTime.UtcNow - _lastSeen).TotalSeconds > NODE_STALE_TIMEOUT_SECONDS))
                return NodeContactStatus.Stale;
            else
                return NodeContactStatus.Fresh;
        }

        public virtual bool Ping()
        {
            DhtRpcPacket response = _queryManager.Query(DhtRpcPacket.CreatePingPacketQuery(_queryManager.CurrentNodeID), _nodeEP);

            if (response == null)
            {
                _failRpcCount++;

                return false;
            }
            else
            {
                if (_nodeID == null)
                {
                    _nodeID = response.SourceNodeID;
                }
                else if (!response.SourceNodeID.Equals(_nodeID))
                {
                    _failRpcCount++;

                    return false;
                }

                _lastSeen = DateTime.UtcNow;
                _failRpcCount = 0;

                return true;
            }
        }

        public virtual void AnnouncePeer(BinaryID networkID, ushort servicePort, BinaryID token)
        {
            DhtRpcPacket response = _queryManager.Query(DhtRpcPacket.CreateAnnouncePeerPacketQuery(_queryManager.CurrentNodeID, networkID, servicePort, token), _nodeEP);

            if (response == null)
            {
                _failRpcCount++;
            }
            else
            {
                _lastSeen = DateTime.UtcNow;
                _failRpcCount = 0;
            }
        }

        public virtual NodeContact[] FindNode(BinaryID networkID)
        {
            DhtRpcPacket response = _queryManager.Query(DhtRpcPacket.CreateFindNodePacketQuery(_queryManager.CurrentNodeID, networkID), _nodeEP);

            if (response == null)
            {
                _failRpcCount++;

                return null;
            }
            else
            {
                _lastSeen = DateTime.UtcNow;
                _failRpcCount = 0;

                return response.Contacts;
            }
        }

        public virtual PeerEndPoint[] GetPeers(BinaryID key)
        {
            return null;
        }

        public override void WriteTo(Stream s)
        {
            s.Write(_nodeID.ID, 0, 20);

            byte[] address = _nodeEP.Address.GetAddressBytes();
            byte[] port = BitConverter.GetBytes(Convert.ToUInt16(_nodeEP.Port));

            switch (_nodeEP.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    s.WriteByte(0);
                    break;

                case AddressFamily.InterNetworkV6:
                    s.WriteByte(1);
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            s.Write(address, 0, address.Length);
            s.Write(port, 0, 2);
        }

        public override void WriteTo(BinaryWriter bW)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region properties

        public BinaryID NodeID
        { get { return _nodeID; } }

        public bool IsCurrentNode
        { get { return _currentNode; } }

        #endregion
    }
}
