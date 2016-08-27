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

using BitChatCore.Network;
using System;
using System.IO;
using TechnitiumLibrary.IO;

namespace BitChatCore.FileSharing
{
    class FileBlockRequest : IWriteStream
    {
        #region variables

        BinaryID _fileID;
        int _blockNumber;
        int _blockOffset;
        ushort _length;

        #endregion

        #region constructor

        public FileBlockRequest(BinaryID fileID, int blockNumber, int blockOffset, ushort length)
        {
            _fileID = fileID;
            _blockNumber = blockNumber;
            _blockOffset = blockOffset;
            _length = length;
        }

        public FileBlockRequest(Stream s)
        {
            BinaryReader bR = new BinaryReader(s);

            _fileID = new BinaryID(bR.ReadBytes(bR.ReadByte()));
            _blockNumber = bR.ReadInt32();
            _blockOffset = bR.ReadInt32();
            _length = bR.ReadUInt16();
        }

        #endregion

        #region public

        public void WriteTo(Stream s)
        {
            s.WriteByte(Convert.ToByte(_fileID.ID.Length));
            s.Write(_fileID.ID, 0, _fileID.ID.Length);
            s.Write(BitConverter.GetBytes(_blockNumber), 0, 4);
            s.Write(BitConverter.GetBytes(_blockOffset), 0, 4);
            s.Write(BitConverter.GetBytes(_length), 0, 2);
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

        public BinaryID FileID
        { get { return _fileID; } }

        public int BlockNumber
        { get { return _blockNumber; } }

        public int BlockOffset
        { get { return _blockOffset; } }

        public ushort Length
        { get { return _length; } }

        #endregion
    }
}
