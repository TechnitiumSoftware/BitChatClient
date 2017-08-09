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

using BitChatCore.Network;
using System;
using System.IO;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using TechnitiumLibrary.IO;

namespace BitChatCore.FileSharing
{
    public class SharedFileMetaData : IWriteStream
    {
        #region variables

        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        string _fileName;
        ContentType _contentType;
        DateTime _lastModified;

        long _fileSize;
        int _blockSize;

        string _hashAlgo;
        byte[][] _blockHash;

        BinaryID _fileID;

        #endregion

        #region constructor

        public SharedFileMetaData(string fileName, ContentType contentType, DateTime lastModified, long fileSize, int blockSize, string hashAlgo, byte[][] blockHash)
        {
            _fileName = fileName;
            _contentType = contentType;
            _lastModified = lastModified;

            _fileSize = fileSize;
            _blockSize = blockSize;

            _hashAlgo = hashAlgo;
            _blockHash = blockHash;

            _fileID = ComputeFileID();
        }

        public SharedFileMetaData(Stream s)
        {
            switch (s.ReadByte()) //version
            {
                case 1:
                    int length;
                    byte[] buffer = new byte[255];

                    length = s.ReadByte();
                    OffsetStream.StreamRead(s, buffer, 0, length);
                    _fileName = Encoding.UTF8.GetString(buffer, 0, length);

                    length = s.ReadByte();
                    OffsetStream.StreamRead(s, buffer, 0, length);
                    _contentType = new ContentType(Encoding.UTF8.GetString(buffer, 0, length));

                    OffsetStream.StreamRead(s, buffer, 0, 8);
                    _lastModified = _epoch.AddSeconds(BitConverter.ToInt64(buffer, 0));

                    OffsetStream.StreamRead(s, buffer, 0, 8);
                    _fileSize = BitConverter.ToInt64(buffer, 0);

                    OffsetStream.StreamRead(s, buffer, 0, 4);
                    _blockSize = BitConverter.ToInt32(buffer, 0);

                    length = s.ReadByte();
                    OffsetStream.StreamRead(s, buffer, 0, length);
                    _hashAlgo = Encoding.ASCII.GetString(buffer, 0, length);

                    int totalBlocks = Convert.ToInt32(Math.Ceiling(Convert.ToDouble((double)_fileSize / _blockSize)));

                    _blockHash = new byte[totalBlocks][];

                    int hashLength = s.ReadByte();

                    for (int i = 0; i < totalBlocks; i++)
                    {
                        _blockHash[i] = new byte[hashLength];
                        OffsetStream.StreamRead(s, _blockHash[i], 0, hashLength);
                    }

                    _fileID = ComputeFileID();
                    break;

                default:
                    throw new BitChatException("FileMetaData format version not supported.");
            }
        }

        #endregion

        #region private

        private BinaryID ComputeFileID()
        {
            using (MemoryStream mS = new MemoryStream(_blockHash[0].Length * _blockHash.Length))
            {
                for (int i = 0; i < _blockHash.Length; i++)
                    mS.Write(_blockHash[i], 0, _blockHash[i].Length);

                mS.Position = 0;

                using (HashAlgorithm hash = HashAlgorithm.Create(_hashAlgo))
                {
                    return new BinaryID(hash.ComputeHash(mS));
                }
            }
        }

        #endregion

        #region public

        public byte[] ComputeBlockHash(byte[] blockData)
        {
            using (HashAlgorithm hash = HashAlgorithm.Create(_hashAlgo))
            {
                return hash.ComputeHash(blockData);
            }
        }

        public void WriteTo(Stream s)
        {
            byte[] buffer;

            s.WriteByte(1);

            buffer = Encoding.UTF8.GetBytes(_fileName);
            s.WriteByte(Convert.ToByte(buffer.Length));
            s.Write(buffer, 0, buffer.Length);

            buffer = Encoding.UTF8.GetBytes(_contentType.MediaType);
            s.WriteByte(Convert.ToByte(buffer.Length));
            s.Write(buffer, 0, buffer.Length);

            s.Write(BitConverter.GetBytes(Convert.ToInt64((_lastModified - _epoch).TotalSeconds)), 0, 8);

            s.Write(BitConverter.GetBytes(_fileSize), 0, 8);
            s.Write(BitConverter.GetBytes(_blockSize), 0, 4);

            buffer = Encoding.ASCII.GetBytes(_hashAlgo);
            s.WriteByte(Convert.ToByte(buffer.Length));
            s.Write(buffer, 0, buffer.Length);

            s.WriteByte(Convert.ToByte(_blockHash[0].Length));

            for (int i = 0; i < _blockHash.Length; i++)
                s.Write(_blockHash[i], 0, _blockHash[i].Length);
        }

        #endregion

        #region properties

        public string FileName
        { get { return _fileName; } }

        public ContentType ContentType
        { get { return _contentType; } }

        public DateTime LastModified
        { get { return _lastModified; } }

        public long FileSize
        { get { return _fileSize; } }

        public int BlockSize
        { get { return _blockSize; } }

        public string HashAlgo
        { get { return _hashAlgo; } }

        public byte[][] BlockHash
        { get { return _blockHash; } }

        public BinaryID FileID
        { get { return _fileID; } }

        #endregion
    }
}
