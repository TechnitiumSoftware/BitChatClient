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

using System;
using System.IO;
using System.Security.Cryptography;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore.Network.SecureChannel
{
    class SecureChannelPacket
    {
        #region variables

        SecureChannelCode _code;
        byte[] _data;

        #endregion

        #region constructor

        public SecureChannelPacket(Stream s)
        {
            _code = (SecureChannelCode)s.ReadByte();

            if (_code == SecureChannelCode.None)
            {
                byte[] buffer = new byte[2];
                OffsetStream.StreamRead(s, buffer, 0, 2);
                ushort dataLen = BitConverter.ToUInt16(buffer, 0);

                if (dataLen > 0)
                {
                    _data = new byte[dataLen];
                    OffsetStream.StreamRead(s, _data, 0, _data.Length);
                }
                else
                {
                    _data = new byte[] { };
                }
            }
            else
            {
                throw new SecureChannelException(SecureChannelCode.RemoteError, null, null, "Error packet received from remote.", new SecureChannelException(_code, null, null));
            }
        }

        #endregion

        #region static

        public static void WritePacket(Stream s, SecureChannelCode code)
        {
            s.WriteByte(Convert.ToByte(code));
            s.Flush();
        }

        public static void WritePacket(Stream s, IWriteStream obj)
        {
            byte[] data = obj.ToArray();
            WritePacket(s, data, 0, data.Length);
        }

        private static void WritePacket(Stream s, byte[] data, int offset, int count)
        {
            byte[] buffer = new byte[1 + 2 + count];

            buffer[0] = Convert.ToByte(SecureChannelCode.None);

            if (count > 0)
            {
                byte[] bufferCount = BitConverter.GetBytes(Convert.ToUInt16(count));
                buffer[1] = bufferCount[0];
                buffer[2] = bufferCount[1];

                Buffer.BlockCopy(data, offset, buffer, 3, count);
            }

            s.Write(buffer, 0, buffer.Length);
            s.Flush();
        }

        #endregion

        #region public

        public Hello GetHello()
        {
            using (MemoryStream mS = new MemoryStream(_data, false))
            {
                return new Hello(mS);
            }
        }

        public KeyExchange GetKeyExchange()
        {
            using (MemoryStream mS = new MemoryStream(_data, false))
            {
                return new KeyExchange(mS);
            }
        }

        public Authentication GetAuthentication()
        {
            using (MemoryStream mS = new MemoryStream(_data, false))
            {
                return new Authentication(mS);
            }
        }

        public Certificate GetCertificate()
        {
            using (MemoryStream mS = new MemoryStream(_data, false))
            {
                return new Certificate(mS);
            }
        }

        #endregion

        #region properties

        public byte[] Data
        { get { return _data; } }

        public SecureChannelCode Code
        { get { return _code; } }

        #endregion

        public class Hello : IWriteStream
        {
            #region variables

            BinaryID _nonce;
            SecureChannelCryptoOptionFlags _cryptoOptions;

            #endregion

            #region constructor

            public Hello(BinaryID nonce, SecureChannelCryptoOptionFlags cryptoOptions)
            {
                _nonce = nonce;
                _cryptoOptions = cryptoOptions;
            }

            public Hello(Stream s)
            {
                _nonce = new BinaryID(s);
                _cryptoOptions = (SecureChannelCryptoOptionFlags)s.ReadByte();
            }

            #endregion

            #region public

            public void WriteTo(Stream s)
            {
                _nonce.WriteTo(s);
                s.WriteByte((byte)_cryptoOptions);
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

            public BinaryID Nonce
            { get { return _nonce; } }

            public SecureChannelCryptoOptionFlags CryptoOptions
            { get { return _cryptoOptions; } }

            #endregion
        }

        public class KeyExchange : IWriteStream
        {
            #region variables

            byte[] _publicKey;
            byte[] _signature;

            #endregion

            #region constructor

            public KeyExchange(byte[] publicKey, AsymmetricCryptoKey privateKey, string hashAlgo)
            {
                _publicKey = publicKey;
                _signature = privateKey.Sign(new MemoryStream(_publicKey, false), hashAlgo);
            }

            public KeyExchange(Stream s)
            {
                byte[] buffer = new byte[2];
                ushort length;

                OffsetStream.StreamRead(s, buffer, 0, 2);
                length = BitConverter.ToUInt16(buffer, 0);
                _publicKey = new byte[length];
                OffsetStream.StreamRead(s, _publicKey, 0, length);

                OffsetStream.StreamRead(s, buffer, 0, 2);
                length = BitConverter.ToUInt16(buffer, 0);
                _signature = new byte[length];
                OffsetStream.StreamRead(s, _signature, 0, length);
            }

            #endregion

            #region public

            public bool IsSignatureValid(Certificate signingCert, string hashAlgo)
            {
                return AsymmetricCryptoKey.Verify(new MemoryStream(_publicKey, false), _signature, hashAlgo, signingCert);
            }

            public void WriteTo(Stream s)
            {
                s.Write(BitConverter.GetBytes(Convert.ToUInt16(_publicKey.Length)), 0, 2);
                s.Write(_publicKey, 0, _publicKey.Length);

                s.Write(BitConverter.GetBytes(Convert.ToUInt16(_signature.Length)), 0, 2);
                s.Write(_signature, 0, _signature.Length);
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

            public byte[] PublicKey
            { get { return _publicKey; } }

            public byte[] Signature
            { get { return _signature; } }

            #endregion
        }

        public class Authentication : IWriteStream
        {
            #region variables

            BinaryID _hmac;

            #endregion

            #region constructor

            public Authentication(Hello hello, byte[] masterKey)
            {
                _hmac = new BinaryID((new HMACSHA256(masterKey)).ComputeHash(hello.ToStream()));
            }

            public Authentication(Stream s)
            {
                _hmac = new BinaryID(s);
            }

            #endregion

            #region public

            public bool IsValid(Hello hello, byte[] masterKey)
            {
                BinaryID computedHmac = new BinaryID((new HMACSHA256(masterKey)).ComputeHash(hello.ToStream()));
                return _hmac.Equals(computedHmac);
            }

            public void WriteTo(Stream s)
            {
                _hmac.WriteTo(s);
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
        }
    }
}
