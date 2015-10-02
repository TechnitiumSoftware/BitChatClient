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
    public class PeerEndPoint : WriteStream
    {
        #region variables

        const int PEER_EXPIRY_TIME_SECONDS = 900; //15 min expiry

        DateTime _dateAdded;
        IPEndPoint _peerEP;

        #endregion

        #region constructor

        public PeerEndPoint(Stream s)
        {
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

            _peerEP = new IPEndPoint(new IPAddress(address), BitConverter.ToUInt16(port, 0));
        }

        public PeerEndPoint(IPEndPoint peerEP)
        {
            _dateAdded = DateTime.UtcNow;
            _peerEP = peerEP;
        }

        #endregion

        #region public

        public bool HasExpired()
        {
            return (DateTime.UtcNow - _dateAdded).TotalSeconds > PEER_EXPIRY_TIME_SECONDS;
        }

        public void UpdateDateAdded()
        {
            _dateAdded = DateTime.UtcNow;
        }

        public override void WriteTo(Stream s)
        {
            byte[] address = _peerEP.Address.GetAddressBytes();
            byte[] port = BitConverter.GetBytes(Convert.ToUInt16(_peerEP.Port));

            switch (_peerEP.AddressFamily)
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

        public override bool Equals(object obj)
        {
            PeerEndPoint peer = obj as PeerEndPoint;

            if (peer == null)
                return false;

            return _peerEP.Equals(peer._peerEP);
        }

        public override int GetHashCode()
        {
            return _peerEP.GetHashCode();
        }

        public override string ToString()
        {
            return _peerEP.ToString();
        }

        #endregion

        #region properties

        public DateTime DateAdded
        { get { return _dateAdded; } }

        public IPEndPoint PeerEP
        { get { return _peerEP; } }

        #endregion
    }
}
