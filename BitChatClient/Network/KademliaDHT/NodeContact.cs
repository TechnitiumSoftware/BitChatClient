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
    class NodeContact : IWriteStream
    {
        #region variables

        const int NODE_RPC_FAIL_LIMIT = 5; //max failed RPC count before declaring node stale
        const int NODE_STALE_TIMEOUT_SECONDS = 900; //15mins timeout before declaring node stale

        BinaryID _nodeID;
        IPEndPoint _nodeEP;

        bool _currentNode;
        DateTime _lastSeen = DateTime.UtcNow;
        int _failRpcCount = 0;

        #endregion

        #region constructor

        public NodeContact(Stream s)
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
        }

        public NodeContact(BinaryID nodeID, IPEndPoint nodeEP)
        {
            _nodeID = nodeID;
            _nodeEP = nodeEP;
        }

        protected NodeContact(int udpDhtPort)
        {
            _nodeID = BinaryID.GenerateRandomID160();
            _nodeEP = new IPEndPoint(IPAddress.Loopback, udpDhtPort);
            _currentNode = true;
        }

        #endregion

        #region public

        public bool IsStale()
        {
            if (_currentNode)
                return false;
            else
                return ((_failRpcCount > NODE_RPC_FAIL_LIMIT) || ((DateTime.UtcNow - _lastSeen).TotalSeconds > NODE_STALE_TIMEOUT_SECONDS));
        }

        public void UpdateLastSeenTime()
        {
            _lastSeen = DateTime.UtcNow;
            _failRpcCount = 0;
        }

        public void IncrementRpcFailCount()
        {
            _failRpcCount++;
        }

        public void WriteTo(Stream s)
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

        public override bool Equals(object obj)
        {
            NodeContact contact = obj as NodeContact;

            if (contact == null)
                return false;

            return _nodeID.Equals(contact._nodeID);
        }

        public override int GetHashCode()
        {
            return _nodeID.GetHashCode();
        }

        public override string ToString()
        {
            return _nodeID.ToString();
        }

        #endregion

        #region properties

        public BinaryID NodeID
        { get { return _nodeID; } }

        public IPEndPoint NodeEP
        { get { return _nodeEP; } }

        public bool IsCurrentNode
        { get { return _currentNode; } }

        public DateTime LastSeen
        { get { return _lastSeen; } }

        #endregion
    }
}
