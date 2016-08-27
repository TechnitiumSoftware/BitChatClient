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

namespace BitChatClient.Network
{
    static class IPEndPointParser
    {
        #region static

        public static IPEndPoint Parse(Stream s)
        {
            byte[] address;
            byte[] port = new byte[2];

            switch (s.ReadByte())
            {
                case 0:
                    address = new byte[4];
                    break;

                case 1:
                    address = new byte[16];
                    break;

                case -1:
                    throw new EndOfStreamException();

                default:
                    throw new NotSupportedException("AddressFamily not supported.");
            }

            OffsetStream.StreamRead(s, address, 0, address.Length);
            OffsetStream.StreamRead(s, port, 0, 2);

            return new IPEndPoint(new IPAddress(address), BitConverter.ToUInt16(port, 0));
        }

        public static void WriteTo(IPEndPoint ep, Stream s)
        {
            byte[] endpoint = ToArray(ep);

            s.Write(endpoint, 0, endpoint.Length);
        }

        public static byte[] ToArray(IPEndPoint ep)
        {
            byte[] address = ep.Address.GetAddressBytes();
            byte[] port = BitConverter.GetBytes(Convert.ToUInt16(ep.Port));

            byte[] endpoint = new byte[1 + address.Length + 2];

            switch (ep.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    break;

                case AddressFamily.InterNetworkV6:
                    endpoint[0] = 1;
                    break;

                default:
                    throw new NotSupportedException("AddressFamily not supported.");
            }

            Buffer.BlockCopy(address, 0, endpoint, 1, address.Length);
            Buffer.BlockCopy(port, 0, endpoint, 1 + address.Length, 2);

            return endpoint;
        }

        #endregion
    }
}
