/*
Technitium Bit Chat
Copyright (C) 2016  Shreyas Zare (shreyas@technitium.com)

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

using BitChatCore.FileSharing;
using BitChatCore.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BitChatCore
{
    enum BitChatMessageType : byte
    {
        NOOP = 0,
        PeerExchange = 1,
        Text = 2,
        TextDeliveryNotification = 3,
        TypingNotification = 4,
        ProfileImage = 5,
        GroupImage = 6,
        FileAdvertisement = 10,
        FileShareParticipate = 11,
        FileShareUnparticipate = 12,
        FileBlockWanted = 13,
        FileBlockAvailable = 14,
        FileBlockRequest = 15
    }

    class BitChatMessage
    {
        #region variables

        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #endregion

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

        public static byte[] CreateTextMessage(MessageItem message)
        {
            using (MemoryStream mS = new MemoryStream(message.Message.Length + 1 + 4))
            {
                mS.WriteByte((byte)BitChatMessageType.Text); //1 byte
                mS.Write(BitConverter.GetBytes(message.MessageNumber), 0, 4); //4 bytes
                mS.Write(BitConverter.GetBytes(Convert.ToInt64((message.MessageDate - _epoch).TotalMilliseconds)), 0, 8); //8 bytes

                byte[] buffer = Encoding.UTF8.GetBytes(message.Message);
                mS.Write(buffer, 0, buffer.Length);

                return mS.ToArray();
            }
        }

        public static byte[] CreateTextDeliveryNotification(int messageNumber)
        {
            byte[] buffer = new byte[5];

            buffer[0] = (byte)BitChatMessageType.TextDeliveryNotification;
            Buffer.BlockCopy(BitConverter.GetBytes(messageNumber), 0, buffer, 1, 4);

            return buffer;
        }

        public static byte[] CreateProfileImage(byte[] image, long dateModified)
        {
            using (MemoryStream mS = new MemoryStream(4096))
            {
                mS.WriteByte((byte)BitChatMessageType.ProfileImage); //1 byte
                mS.Write(BitConverter.GetBytes(dateModified), 0, 8); //8 bytes date modified

                if (image != null)
                    mS.Write(image, 0, image.Length);

                return mS.ToArray();
            }
        }

        public static byte[] CreateGroupImage(byte[] image, long dateModified)
        {
            using (MemoryStream mS = new MemoryStream(4096))
            {
                mS.WriteByte((byte)BitChatMessageType.GroupImage); //1 byte
                mS.Write(BitConverter.GetBytes(dateModified), 0, 8); //8 bytes date modified

                if (image != null)
                    mS.Write(image, 0, image.Length);

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
            using (MemoryStream mS = new MemoryStream(fileID.ID.Length + 1 + 1))
            {
                mS.WriteByte((byte)BitChatMessageType.FileShareParticipate); //1 byte
                fileID.WriteTo(mS);

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileUnparticipate(BinaryID fileID)
        {
            using (MemoryStream mS = new MemoryStream(fileID.ID.Length + 1 + 1))
            {
                mS.WriteByte((byte)BitChatMessageType.FileShareUnparticipate); //1 byte
                fileID.WriteTo(mS);

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileBlockWanted(BinaryID fileID, int blockNumber)
        {
            using (MemoryStream mS = new MemoryStream(fileID.ID.Length + 1 + 1 + 4))
            {
                mS.WriteByte((byte)BitChatMessageType.FileBlockWanted); //1 byte
                fileID.WriteTo(mS);
                mS.Write(BitConverter.GetBytes(blockNumber), 0, 4); //4 bytes

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileBlockAvailable(BinaryID fileID, int blockNumber)
        {
            using (MemoryStream mS = new MemoryStream(fileID.ID.Length + 1 + 1 + 4))
            {
                mS.WriteByte((byte)BitChatMessageType.FileBlockAvailable); //1 byte
                fileID.WriteTo(mS);
                mS.Write(BitConverter.GetBytes(blockNumber), 0, 4); //4 bytes

                return mS.ToArray();
            }
        }

        public static byte[] CreateFileBlockRequest(BinaryID fileID, int blockNumber, ushort dataPort)
        {
            using (MemoryStream mS = new MemoryStream(fileID.ID.Length + 1 + 1 + 4 + 2))
            {
                mS.WriteByte((byte)BitChatMessageType.FileBlockRequest); //1 byte
                fileID.WriteTo(mS);
                mS.Write(BitConverter.GetBytes(blockNumber), 0, 4); //4 bytes
                mS.Write(BitConverter.GetBytes(dataPort), 0, 2); //2 bytes

                return mS.ToArray();
            }
        }

        #endregion

        #region static read
        
        public static int ReadInt32(Stream s)
        {
            byte[] buffer = new byte[4];
            s.Read(buffer, 0, 4);

            return BitConverter.ToInt32(buffer, 0);
        }

        public static long ReadInt64(Stream s)
        {
            byte[] buffer = new byte[8];
            s.Read(buffer, 0, 8);

            return BitConverter.ToInt64(buffer, 0);
        }

        public static ushort ReadUInt16(Stream s)
        {
            byte[] buffer = new byte[2];
            s.Read(buffer, 0, 2);

            return BitConverter.ToUInt16(buffer, 0);
        }

        public static DateTime ReadDateTime(Stream s)
        {
            byte[] buffer = new byte[8];
            s.Read(buffer, 0, 8);

            return _epoch.AddMilliseconds(BitConverter.ToInt64(buffer, 0));
        }

        public static byte[] ReadData(Stream s)
        {
            int dataLen = Convert.ToInt32(s.Length - s.Position);

            if (dataLen > 0)
            {
                byte[] buffer = new byte[dataLen];
                s.Read(buffer, 0, dataLen);

                return buffer;
            }
            else
            {
                return new byte[] { };
            }
        }

        public static List<PeerInfo> ReadPeerExchange(Stream s)
        {
            int count = s.ReadByte();
            List<PeerInfo> peerEPs = new List<PeerInfo>(count);

            for (int i = 0; i < count; i++)
                peerEPs.Add(new PeerInfo(s));

            return peerEPs;
        }

        #endregion
    }
}
