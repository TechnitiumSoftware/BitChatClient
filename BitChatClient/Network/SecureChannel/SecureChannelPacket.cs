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
using System.Text;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient.Network.SecureChannel
{
    class SecureChannelPacket
    {
        #region variables

        SecureChannelErrorCode _errorCode;
        byte[] _data;

        #endregion

        #region constructor

        public SecureChannelPacket(Stream s)
        {
            _errorCode = (SecureChannelErrorCode)s.ReadByte();

            if (_errorCode == SecureChannelErrorCode.NoError)
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
                throw new SecureChannelException(SecureChannelErrorCode.RemoteError, "Error packet received from remote.", new SecureChannelException(_errorCode));
            }
        }

        #endregion

        #region static

        public static void WritePacket(Stream s, SecureChannelErrorCode errorCode, byte[] data, int offset, int count)
        {
            if (count > 65534)
                throw new IOException("SecureChannelPacket data size cannot exceed 65534 bytes.");

            if (errorCode == SecureChannelErrorCode.NoError)
            {
                byte[] buffer = new byte[1 + 2 + count];

                buffer[0] = Convert.ToByte(errorCode);

                if (count > 0)
                {
                    byte[] bufferCount = BitConverter.GetBytes(Convert.ToUInt16(count));
                    buffer[1] = bufferCount[0];
                    buffer[2] = bufferCount[1];

                    Buffer.BlockCopy(data, offset, buffer, 3, count);
                }

                s.Write(buffer, 0, buffer.Length);
            }
            else
            {
                s.WriteByte(Convert.ToByte(errorCode));
            }

            s.Flush();
        }

        #endregion

        #region public

        public PublicKey GetPublicKey()
        {
            using (MemoryStream mS = new MemoryStream(_data, false))
            {
                return new PublicKey(mS);
            }
        }

        public Stream GetDataStream()
        {
            return new MemoryStream(_data, false);
        }

        #endregion

        #region properties

        public byte[] Data
        { get { return _data; } }

        public SecureChannelErrorCode ErrorCode
        { get { return _errorCode; } }

        #endregion

        public class PublicKey : WriteStream
        {
            #region variables

            SecureChannelCryptoOptionFlags _cryptoOptions;
            AsymmetricEncryptionAlgorithm _publicKeyEncryptionAlgorithm;
            string _publicKeyXML;

            #endregion

            #region constructor

            public PublicKey(SecureChannelCryptoOptionFlags cryptoOptions, AsymmetricEncryptionAlgorithm publicKeyEncryptionAlgorithm, string publicKeyXML)
            {
                _cryptoOptions = cryptoOptions;
                _publicKeyEncryptionAlgorithm = publicKeyEncryptionAlgorithm;
                _publicKeyXML = publicKeyXML;
            }

            internal PublicKey(Stream s)
            {
                BinaryReader bR = new BinaryReader(s);

                _cryptoOptions = (SecureChannelCryptoOptionFlags)bR.ReadByte();
                _publicKeyEncryptionAlgorithm = (AsymmetricEncryptionAlgorithm)bR.ReadByte();
                _publicKeyXML = Encoding.ASCII.GetString(bR.ReadBytes(bR.ReadUInt16()));
            }

            #endregion

            #region override

            public override void WriteTo(BinaryWriter bW)
            {
                bW.Write((byte)_cryptoOptions);
                bW.Write((byte)_publicKeyEncryptionAlgorithm);

                bW.Write(Convert.ToUInt16(_publicKeyXML.Length));
                bW.Write(Encoding.ASCII.GetBytes(_publicKeyXML));
            }

            #endregion

            #region properties

            public SecureChannelCryptoOptionFlags CryptoOptions
            { get { return _cryptoOptions; } }

            public AsymmetricEncryptionAlgorithm PublicKeyEncryptionAlgorithm
            { get { return _publicKeyEncryptionAlgorithm; } }

            public string PublicKeyXML
            { get { return _publicKeyXML; } }

            #endregion
        }
    }
}
