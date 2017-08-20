/*
Technitium Bit Chat
Copyright (C) 2017  Shreyas Zare (shreyas@technitium.com)

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
using TechnitiumLibrary.IO;

namespace BitChatCore.Network
{
    public class BinaryID : IWriteStream, IEquatable<BinaryID>, IComparable<BinaryID>
    {
        #region variables

        byte[] _id;

        #endregion

        #region constructor

        public BinaryID(byte[] id)
        {
            _id = id;
        }

        public BinaryID(Stream s)
        {
            int length = s.ReadByte();
            if (length < 0)
                throw new EndOfStreamException();

            _id = new byte[length];
            OffsetStream.StreamRead(s, _id, 0, length);
        }

        #endregion

        #region static

        static RandomNumberGenerator _rnd = new RNGCryptoServiceProvider();

        public static BinaryID GenerateRandomID160()
        {
            byte[] buffer = new byte[20];

            _rnd.GetBytes(buffer);

            return new BinaryID(buffer);
        }

        public static BinaryID GenerateRandomID256()
        {
            byte[] buffer = new byte[32];

            _rnd.GetBytes(buffer);

            return new BinaryID(buffer);
        }

        public static BinaryID MaxValueID160()
        {
            return new BinaryID(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        }

        public static BinaryID Clone(byte[] buffer, int offset, int count)
        {
            byte[] id = new byte[count];
            Buffer.BlockCopy(buffer, offset, id, 0, count);

            return new BinaryID(id);
        }

        #endregion

        #region public

        public BinaryID Clone()
        {
            byte[] id = new byte[_id.Length];
            Buffer.BlockCopy(_id, 0, id, 0, _id.Length);
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
            if (_id.Length < 4)
                return 0;
            else
                return BitConverter.ToInt32(_id, 0);
        }

        public int CompareTo(BinaryID other)
        {
            if (this._id.Length != other._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            for (int i = 0; i < this._id.Length; i++)
            {
                if (this._id[i] > other._id[i])
                    return 1;

                if (this._id[i] < other._id[i])
                    return -1;
            }

            return 0;
        }

        public override string ToString()
        {
            return BitConverter.ToString(_id).Replace("-", "").ToLower();
        }

        public void WriteTo(Stream s)
        {
            s.WriteByte(Convert.ToByte(_id.Length));
            s.Write(_id, 0, _id.Length);
        }

        #endregion

        #region operators

        public static bool operator ==(BinaryID b1, BinaryID b2)
        {
            if (ReferenceEquals(b1, b2))
                return true;

            return b1.Equals(b2);
        }

        public static bool operator !=(BinaryID b1, BinaryID b2)
        {
            if (ReferenceEquals(b1, b2))
                return false;

            return !b1.Equals(b2);
        }

        public static BinaryID operator |(BinaryID b1, BinaryID b2)
        {
            if (b1._id.Length != b2._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            byte[] id = new byte[b1._id.Length];

            for (int i = 0; i < id.Length; i++)
                id[i] = (byte)(b1._id[i] | b2._id[i]);

            return new BinaryID(id);
        }

        public static BinaryID operator &(BinaryID b1, BinaryID b2)
        {
            if (b1._id.Length != b2._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            byte[] id = new byte[b1._id.Length];

            for (int i = 0; i < id.Length; i++)
                id[i] = (byte)(b1._id[i] & b2._id[i]);

            return new BinaryID(id);
        }

        public static BinaryID operator ^(BinaryID b1, BinaryID b2)
        {
            if (b1._id.Length != b2._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            byte[] id = new byte[b1._id.Length];

            for (int i = 0; i < id.Length; i++)
                id[i] = (byte)(b1._id[i] ^ b2._id[i]);

            return new BinaryID(id);
        }

        public static BinaryID operator >>(BinaryID b1, int bitcount)
        {
            byte[] id = new byte[b1._id.Length];

            if (bitcount >= 8)
                Buffer.BlockCopy(b1._id, 0, id, bitcount / 8, id.Length - (bitcount / 8));
            else
                Buffer.BlockCopy(b1._id, 0, id, 0, id.Length);

            bitcount = bitcount % 8;

            if (bitcount > 0)
            {
                for (int i = id.Length - 1; i >= 0; i--)
                {
                    id[i] >>= bitcount;

                    if (i > 0)
                        id[i] |= (byte)(id[i - 1] << (8 - bitcount));
                }
            }

            return new BinaryID(id);
        }

        public static BinaryID operator <<(BinaryID b1, int bitcount)
        {
            byte[] id = new byte[b1._id.Length];

            if (bitcount >= 8)
                Buffer.BlockCopy(b1._id, bitcount / 8, id, 0, id.Length - (bitcount / 8));
            else
                Buffer.BlockCopy(b1._id, 0, id, 0, id.Length);

            bitcount = bitcount % 8;

            if (bitcount > 0)
            {
                for (int i = 0; i < id.Length; i++)
                {
                    id[i] <<= bitcount;

                    if (i < (id.Length - 1))
                        id[i] |= (byte)(id[i + 1] >> (8 - bitcount));
                }
            }

            return new BinaryID(id);
        }

        public static bool operator <(BinaryID b1, BinaryID b2)
        {
            if (b1._id.Length != b2._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            bool eq = true;

            for (int i = 0; i < b1._id.Length; i++)
            {
                if (b1._id[i] > b2._id[i])
                    return false;

                if (b1._id[i] != b2._id[i])
                    eq = false;
            }

            if (eq)
                return false;

            return true;
        }

        public static bool operator >(BinaryID b1, BinaryID b2)
        {
            if (b1._id.Length != b2._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            bool eq = true;

            for (int i = 0; i < b1._id.Length; i++)
            {
                if (b1._id[i] < b2._id[i])
                    return false;

                if (b1._id[i] != b2._id[i])
                    eq = false;
            }

            if (eq)
                return false;

            return true;
        }

        public static bool operator <=(BinaryID b1, BinaryID b2)
        {
            if (b1._id.Length != b2._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            for (int i = 0; i < b1._id.Length; i++)
            {
                if (b1._id[i] > b2._id[i])
                    return false;
            }

            return true;
        }

        public static bool operator >=(BinaryID b1, BinaryID b2)
        {
            if (b1._id.Length != b2._id.Length)
                throw new ArgumentException("Operand id length not equal.");

            for (int i = 0; i < b1._id.Length; i++)
            {
                if (b1._id[i] < b2._id[i])
                    return false;
            }

            return true;
        }

        public static BinaryID operator ~(BinaryID b1)
        {
            BinaryID obj = b1.Clone();

            for (int i = 0; i < obj._id.Length; i++)
            {
                obj._id[i] = (byte)~obj._id[i];
            }

            return obj;
        }

        #endregion

        #region properties

        public byte[] ID
        { get { return _id; } }

        #endregion
    }
}
