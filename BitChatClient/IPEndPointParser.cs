﻿/*
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

namespace BitChatClient
{
    public static class IPEndPointParser
    {
        #region static

        public static IPEndPoint Parse(Stream s)
        {
            return Parse(new BinaryReader(s));
        }

        public static IPEndPoint Parse(BinaryReader bR)
        {
            byte[] address;
            byte[] port;

            switch (bR.ReadByte())
            {
                case 0:
                    address = bR.ReadBytes(4);
                    port = bR.ReadBytes(2);
                    break;

                case 1:
                    address = bR.ReadBytes(16);
                    port = bR.ReadBytes(2);
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            return new IPEndPoint(new IPAddress(address), BitConverter.ToUInt16(port, 0));
        }

        public static void WriteTo(IPEndPoint ep, Stream s)
        {
            BinaryWriter bW = new BinaryWriter(s);
            WriteTo(ep, bW);
            bW.Flush();
        }

        public static void WriteTo(IPEndPoint ep, BinaryWriter bW)
        {
            switch (ep.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    bW.Write((byte)0);
                    break;

                case AddressFamily.InterNetworkV6:
                    bW.Write((byte)1);
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            bW.Write(ep.Address.GetAddressBytes());
            bW.Write(BitConverter.GetBytes(Convert.ToUInt16(ep.Port)));
        }

        #endregion
    }
}