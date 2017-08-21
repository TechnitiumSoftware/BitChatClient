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
using System.Security.Cryptography;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore
{
    public class MessageStore : IDisposable
    {
        #region variables

        readonly Stream _index;
        readonly Stream _data;
        readonly byte[] _key;

        readonly SymmetricCryptoKey _crypto;

        readonly object _lock = new object();

        #endregion

        #region constructor

        public MessageStore(Stream index, Stream data, byte[] key)
        {
            _index = index;
            _data = data;
            _key = key;

            _crypto = new SymmetricCryptoKey(SymmetricEncryptionAlgorithm.Rijndael, key, null, PaddingMode.ISO10126);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~MessageStore()
        {
            Dispose(false);
        }

        bool _disposed = false;

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                lock (_lock)
                {
                    _index.Dispose();
                    _data.Dispose();

                    for (int i = 0; i < _key.Length; i++)
                        _key[i] = 255;

                    _crypto.Dispose();
                }

                _disposed = true;
            }
        }

        #endregion

        #region public

        public int WriteMessage(byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                //encrypt message data
                byte[] encryptedData;
                using (MemoryStream mS = new MemoryStream(count))
                {
                    using (MemoryStream src = new MemoryStream(data, offset, count))
                    {
                        _crypto.GenerateIV();
                        _crypto.Encrypt(src, mS);
                    }

                    encryptedData = mS.ToArray();
                }

                byte[] aeHmac;
                using (HMAC hmac = new HMACSHA256(_key))
                {
                    aeHmac = hmac.ComputeHash(encryptedData);
                }

                //write encrypted message data

                //seek to end of stream
                _index.Position = _index.Length;
                _data.Position = _data.Length;

                //get message offset
                uint messageOffset = Convert.ToUInt32(_data.Position);

                //write data
                BincodingEncoder encoder = new BincodingEncoder(_data);

                encoder.Encode((byte)1); //version
                encoder.Encode(_crypto.IV);
                encoder.Encode(encryptedData);
                encoder.Encode(aeHmac);

                //write message offset to index stream
                _index.Write(BitConverter.GetBytes(messageOffset), 0, 4);

                //return message number
                return Convert.ToInt32(_index.Position / 4) - 1;
            }
        }

        public void UpdateMessage(int number, byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                //seek to index location
                int indexPosition = number * 4;

                if (indexPosition >= _index.Length)
                    throw new IOException("Cannot read message from message store: message number out of range.");

                _index.Position = indexPosition;

                //read message offset
                byte[] buffer = new byte[4];
                OffsetStream.StreamRead(_index, buffer, 0, 4);
                uint messageOffset = BitConverter.ToUInt32(buffer, 0);

                //seek to message offset
                _data.Position = messageOffset;

                //read data
                BincodingDecoder decoder = new BincodingDecoder(_data);
                byte[] existingEncryptedData;

                switch (decoder.DecodeNext().GetByteValue()) //version
                {
                    case 1:
                        decoder.DecodeNext();
                        existingEncryptedData = decoder.DecodeNext().Value;
                        break;

                    default:
                        throw new IOException("Cannot read message from message store: message version not supported.");
                }

                //encrypt message data
                byte[] newEncryptedData;
                using (MemoryStream mS = new MemoryStream(count))
                {
                    using (MemoryStream src = new MemoryStream(data, offset, count))
                    {
                        _crypto.GenerateIV();
                        _crypto.Encrypt(src, mS);
                    }

                    newEncryptedData = mS.ToArray();
                }

                byte[] aeHmac;
                using (HMAC hmac = new HMACSHA256(_key))
                {
                    aeHmac = hmac.ComputeHash(newEncryptedData);
                }

                bool lengthIsInLimit = (newEncryptedData.Length <= existingEncryptedData.Length);

                if (lengthIsInLimit)
                {
                    //seek to message offset
                    _data.Position = messageOffset;

                    //overwrite new data
                    BincodingEncoder encoder = new BincodingEncoder(_data);

                    encoder.Encode((byte)1); //version
                    encoder.Encode(_crypto.IV);
                    encoder.Encode(newEncryptedData);
                    encoder.Encode(aeHmac);
                }
                else
                {
                    //seek to index location
                    _index.Position = number * 4;

                    //seek to end of data stream
                    _data.Position = _data.Length;

                    //get message offset
                    messageOffset = Convert.ToUInt32(_data.Position);

                    //write new data
                    BincodingEncoder encoder = new BincodingEncoder(_data);

                    encoder.Encode((byte)1); //version
                    encoder.Encode(_crypto.IV);
                    encoder.Encode(newEncryptedData);
                    encoder.Encode(aeHmac);

                    //overwrite message offset to index stream
                    _index.Write(BitConverter.GetBytes(messageOffset), 0, 4);
                }
            }
        }

        public byte[] ReadMessage(int number)
        {
            lock (_lock)
            {
                //seek to index location
                int indexPosition = number * 4;

                if (indexPosition >= _index.Length)
                    throw new IOException("Cannot read message from message store: message number out of range.");

                _index.Position = indexPosition;

                //read message offset
                byte[] buffer = new byte[4];
                OffsetStream.StreamRead(_index, buffer, 0, 4);
                uint messageOffset = BitConverter.ToUInt32(buffer, 0);

                //seek to message offset
                _data.Position = messageOffset;

                //read data
                BincodingDecoder decoder = new BincodingDecoder(_data);
                byte[] IV;
                byte[] encryptedData;

                switch (decoder.DecodeNext().GetByteValue()) //version
                {
                    case 1:
                        IV = decoder.DecodeNext().Value;
                        encryptedData = decoder.DecodeNext().Value;
                        byte[] aeHmac = decoder.DecodeNext().Value;

                        //verify hmac
                        BinaryNumber computedAeHmac;

                        using (HMAC hmac = new HMACSHA256(_key))
                        {
                            computedAeHmac = new BinaryNumber(hmac.ComputeHash(encryptedData));
                        }

                        if (!computedAeHmac.Equals(new BinaryNumber(aeHmac)))
                            throw new CryptoException("Cannot read message from message store: message is corrupt or tampered.");

                        break;

                    default:
                        throw new IOException("Cannot read message from message store: message version not supported.");
                }

                using (MemoryStream mS = new MemoryStream(encryptedData.Length))
                {
                    using (MemoryStream src = new MemoryStream(encryptedData, 0, encryptedData.Length))
                    {
                        _crypto.IV = IV;
                        _crypto.Decrypt(src, mS);
                    }

                    return mS.ToArray();
                }
            }
        }

        public int GetMessageCount()
        {
            lock (_lock)
            {
                return Convert.ToInt32(_index.Length / 4);
            }
        }

        #endregion
    }
}
