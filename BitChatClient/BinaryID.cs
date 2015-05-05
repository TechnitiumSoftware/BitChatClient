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
using System.Security.Cryptography;
using System.Threading;

namespace BitChatClient
{
    public class BinaryID : IEquatable<BinaryID>
    {
        #region variables

        byte[] _id;

        #endregion

        #region constructor

        public BinaryID(byte[] id)
        {
            if (id.Length != 20)
                throw new BitChatException("BinaryID must be of 20 bytes.");

            _id = id;
        }

        #endregion

        #region static

        static Random _rnd = new Random(DateTime.UtcNow.Millisecond);
        static HashAlgorithm _hash = HashAlgorithm.Create("SHA1");

        public static BinaryID GenerateRandomID()
        {
            using (MemoryStream mS = new MemoryStream())
            {
                byte[] buffer = new byte[20];

                BinaryWriter bW = new BinaryWriter(mS);

                bW.Write(_rnd.Next());
                bW.Write(DateTime.UtcNow.ToBinary());

                _rnd.NextBytes(buffer);
                bW.Write(buffer);

                bW.Write(Thread.CurrentThread.ManagedThreadId);

                _rnd.NextBytes(buffer);
                bW.Write(buffer);

                bW.Write(System.Diagnostics.Process.GetCurrentProcess().Id);

                _rnd.NextBytes(buffer);
                bW.Write(buffer);

                bW.Write(DateTime.UtcNow.ToBinary());
                bW.Write(_rnd.Next());

                bW.Flush();

                mS.Position = 0;
                return new BinaryID(_hash.ComputeHash(mS.ToArray()));
            }
        }

        #endregion

        #region public

        public BinaryID Clone()
        {
            byte[] id = new byte[20];
            Buffer.BlockCopy(_id, 0, id, 0, 20);
            return new BinaryID(id);
        }

        public bool Equals(BinaryID obj)
        {
            if (ReferenceEquals(null, obj))
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            byte[] objID = obj._id;

            if (_id.Length != objID.Length)
                return false;

            for (int i = 0; i < _id.Length; i++)
            {
                if (_id[i] != objID[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as BinaryID);
        }

        public override int GetHashCode()
        {
            return BitConverter.ToInt32(_id, 0);
        }

        public override string ToString()
        {
            return BitConverter.ToString(_id).Replace("-", "").ToLower();
        }

        #endregion

        #region properties

        public byte[] ID
        { get { return _id; } }

        #endregion
    }
}
