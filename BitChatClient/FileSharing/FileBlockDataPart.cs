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
using TechnitiumLibrary.IO;

namespace BitChatClient.FileSharing
{
    class FileBlockDataPart : WriteStream
    {
        #region variables

        BinaryID _fileID;
        int _blockNumber;
        int _blockOffset;
        ushort _length;
        byte[] _blockDataPart;

        #endregion

        #region constructor

        public FileBlockDataPart(BinaryID fileID, int blockNumber, int blockOffset, ushort length, byte[] blockDataPart)
        {
            _fileID = fileID;
            _blockNumber = blockNumber;
            _blockOffset = blockOffset;
            _length = length;
            _blockDataPart = blockDataPart;
        }

        public FileBlockDataPart(Stream s)
        {
            BinaryReader bR = new BinaryReader(s);

            _fileID = new BinaryID(bR.ReadBytes(bR.ReadByte()));
            _blockNumber = bR.ReadInt32();
            _blockOffset = bR.ReadInt32();
            _length = bR.ReadUInt16();
            _blockDataPart = bR.ReadBytes(_length);
        }

        #endregion

        #region public

        public override void WriteTo(BinaryWriter bW)
        {
            bW.Write(Convert.ToByte(_fileID.ID.Length));
            bW.Write(_fileID.ID);
            bW.Write(_blockNumber);
            bW.Write(_blockOffset);
            bW.Write(_length);
            bW.Write(_blockDataPart, 0, _length);
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

        public byte[] BlockDataPart
        { get { return _blockDataPart; } }

        #endregion
    }
}
