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
using System.Text;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient.Network.SecureChannel
{
    public class SecureChannelPacket
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
                throw new SecureChannelException(SecureChannelCode.RemoteError, "Error packet received from remote.", new SecureChannelException(_code));
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
            if (count > 65534)
                throw new IOException("SecureChannelPacket data size cannot exceed 65534 bytes.");

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

        public class Hello : WriteStream
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

            #region override

            public override void WriteTo(Stream s)
            {
                _nonce.WriteTo(s);
                s.WriteByte((byte)_cryptoOptions);
            }

            public override void WriteTo(BinaryWriter bW)
            {
                throw new NotImplementedException();
            }

            #endregion

            #region properties

            public BinaryID Nonce
            { get { return _nonce; } }

            public SecureChannelCryptoOptionFlags CryptoOptions
            { get { return _cryptoOptions; } }

            #endregion
        }

        public class KeyExchange : WriteStream
        {
            #region variables

            string _publicKeyXML;
            byte[] _signature;

            #endregion

            #region constructor

            public KeyExchange(string publicKeyXML, AsymmetricCryptoKey privateKey, string hashAlgo)
            {
                _publicKeyXML = publicKeyXML;
                _signature = privateKey.Sign(new MemoryStream(Encoding.UTF8.GetBytes(_publicKeyXML), false), hashAlgo);
            }

            public KeyExchange(Stream s)
            {
                byte[] buffer = new byte[2];
                ushort length;

                OffsetStream.StreamRead(s, buffer, 0, 2);
                length = BitConverter.ToUInt16(buffer, 0);
                byte[] publicKey = new byte[length];
                OffsetStream.StreamRead(s, publicKey, 0, length);
                _publicKeyXML = Encoding.UTF8.GetString(publicKey);

                OffsetStream.StreamRead(s, buffer, 0, 2);
                length = BitConverter.ToUInt16(buffer, 0);
                _signature = new byte[length];
                OffsetStream.StreamRead(s, _signature, 0, length);
            }

            #endregion

            #region public

            public bool IsSignatureValid(Certificate signingCert, string hashAlgo)
            {
                return AsymmetricCryptoKey.Verify(new MemoryStream(Encoding.UTF8.GetBytes(_publicKeyXML), false), _signature, hashAlgo, signingCert);
            }

            #endregion

            #region override

            public override void WriteTo(Stream s)
            {
                byte[] publicKey = Encoding.UTF8.GetBytes(_publicKeyXML);
                s.Write(BitConverter.GetBytes(Convert.ToUInt16(publicKey.Length)), 0, 2);
                s.Write(publicKey, 0, publicKey.Length);

                s.Write(BitConverter.GetBytes(Convert.ToUInt16(_signature.Length)), 0, 2);
                s.Write(_signature, 0, _signature.Length);
            }

            public override void WriteTo(BinaryWriter bW)
            {
                throw new NotImplementedException();
            }

            #endregion

            #region properties

            public string PublicKeyXML
            { get { return _publicKeyXML; } }

            public byte[] Signature
            { get { return _signature; } }

            #endregion
        }
    }
}
