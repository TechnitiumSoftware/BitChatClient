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

using BitChatClient.FileSharing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BitChatClient
{
    enum BitChatMessageType : byte
    {
        NOOP = 0,
        PeerExchange = 1,
        Text = 2,
        TypingNotification = 3,
        FileAdvertisement = 4,
        FileShareParticipate = 5,
        FileShareUnparticipate = 6,
        FileBlockWanted = 7,
        FileBlockAvailable = 8,
        FileBlockRequest = 9,
        FileBlockResponse = 10
    }

    class BitChatMessage
    {
        #region static create

        public static byte[] CreateNOOPMessage()
        {
            return new byte[] { (byte)BitChatMessageType.NOOP };
        }

        public static byte[] CreateTypingNotification()
        {
            return new byte[] { (byte)BitChatMessageType.TypingNotification };
        }

        public static byte[] CreatePeerExchange(List<PeerInfo> peerInfoList)
        {
            using (MemoryStream mS = new MemoryStream())
            {
                mS.WriteByte((byte)BitChatMessageType.PeerExchange); //1 byte

                mS.WriteByte(Convert.ToByte(peerInfoList.Count));

                foreach (PeerInfo peerInfo in peerInfoList)
                    peerInfo.WriteTo(mS);

                return mS.ToArray();
            }
        }

        public static byte[] CreateTextMessage(string message)
        {
            using (MemoryStream mS = new MemoryStream(message.Length + 1))
            {
                mS.WriteByte((byte)BitChatMessageType.Text); //1 byte

                byte[] buffer = Encoding.UTF8.GetBytes(message);
                mS.Write(buffer, 0, buffer.Length);

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileAdvertisement(SharedFileMetaData fileMetaData)
        {
            using (MemoryStream mS = new MemoryStream(64 * 1024))
            {
                mS.WriteByte((byte)BitChatMessageType.FileAdvertisement); //1 byte

                fileMetaData.WriteTo(mS);

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileParticipate(BinaryID fileID)
        {
            using (MemoryStream mS = new MemoryStream(fileID.ID.Length + 1))
            {
                mS.WriteByte((byte)BitChatMessageType.FileShareParticipate); //1 byte
                mS.Write(fileID.ID, 0, fileID.ID.Length);
                return mS.ToArray();
            }
        }

        public static byte[] CreateFileUnparticipate(BinaryID fileID)
        {
            using (MemoryStream mS = new MemoryStream(fileID.ID.Length + 1))
            {
                mS.WriteByte((byte)BitChatMessageType.FileShareUnparticipate); //1 byte
                mS.Write(fileID.ID, 0, fileID.ID.Length);
                return mS.ToArray();
            }
        }

        public static byte[] CreateFileBlockWanted(FileBlockWanted blockWanted)
        {
            using (MemoryStream mS = new MemoryStream(1 + 4))
            {
                mS.WriteByte((byte)BitChatMessageType.FileBlockWanted); //1 byte

                blockWanted.WriteTo(mS);

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileBlockAvailable(FileBlockWanted blockWanted)
        {
            using (MemoryStream mS = new MemoryStream(1 + 4))
            {
                mS.WriteByte((byte)BitChatMessageType.FileBlockAvailable); //1 byte

                blockWanted.WriteTo(mS);

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileBlockRequest(FileBlockRequest blockRequest)
        {
            using (MemoryStream mS = new MemoryStream(64 * 1024))
            {
                mS.WriteByte((byte)BitChatMessageType.FileBlockRequest); //1 byte

                blockRequest.WriteTo(mS);

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileBlockResponse(FileBlockDataPart blockData)
        {
            using (MemoryStream mS = new MemoryStream(64 * 1024))
            {
                mS.WriteByte((byte)BitChatMessageType.FileBlockResponse); //1 byte

                blockData.WriteTo(mS);

                return mS.ToArray();
            }
        }

        #endregion

        #region static read

        public static BitChatMessageType ReadType(Stream s)
        {
            return (BitChatMessageType)s.ReadByte();
        }

        public static string ReadTextMessage(Stream s)
        {
            int dataLen = Convert.ToInt32(s.Length - s.Position);
            byte[] buffer = new byte[dataLen];
            s.Read(buffer, 0, dataLen);

            return Encoding.UTF8.GetString(buffer);
        }

        public static List<PeerInfo> ReadPeerExchange(Stream s)
        {
            int count = s.ReadByte();
            List<PeerInfo> peerEPs = new List<PeerInfo>(count);

            for (int i = 0; i < count; i++)
                peerEPs.Add(new PeerInfo(s));

            return peerEPs;
        }

        public static SharedFileMetaData ReadFileAdvertisement(Stream s)
        {
            return new SharedFileMetaData(s);
        }

        public static BinaryID ReadFileID(Stream s)
        {
            int dataLen = Convert.ToInt32(s.Length - s.Position);
            byte[] buffer = new byte[dataLen];
            s.Read(buffer, 0, dataLen);

            return new BinaryID(buffer);
        }

        public static FileBlockWanted ReadFileBlockWanted(Stream s)
        {
            return new FileBlockWanted(s);
        }

        public static FileBlockRequest ReadFileBlockRequest(Stream s)
        {
            return new FileBlockRequest(s);
        }

        public static FileBlockDataPart ReadFileBlockData(Stream s)
        {
            return new FileBlockDataPart(s);
        }

        #endregion
    }
}
