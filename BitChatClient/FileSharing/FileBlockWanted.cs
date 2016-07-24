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
    class FileBlockWanted : IWriteStream
    {
        #region variables

        BinaryID _fileID;
        int _blockNumber;

        #endregion

        #region constructor

        public FileBlockWanted(BinaryID fileID, int blockNumber)
        {
            _fileID = fileID;
            _blockNumber = blockNumber;
        }

        public FileBlockWanted(Stream s)
        {
            BinaryReader bR = new BinaryReader(s);

            _fileID = new BinaryID(bR.ReadBytes(bR.ReadByte()));
            _blockNumber = bR.ReadInt32();
        }

        #endregion

        #region public

        public void WriteTo(Stream s)
        {
            s.WriteByte(Convert.ToByte(_fileID.ID.Length));
            s.Write(_fileID.ID, 0, _fileID.ID.Length);
            s.Write(BitConverter.GetBytes(_blockNumber), 0, 4);
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

        #endregion
    }
}
