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
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

/*
 =============
 = VERSION 2 =
 =============
 
    SERVER               CLIENT
 <==================================>
    version     --->
    challenge   ---> 
                <---    response
                <---    challenge
    response    --->
 <----------------------------------> challenge handshake complete
                <---    public key
    public key  --->
 <----------------------------------> encrypted key exchange
                <---    channel key
  channel key   --->
 <----------------------------------> encryption layer ON
                <---    certificate
  certificate   --->
                <---    ok
 <----------------------------------> data exchange ON
    data        <-->    data
 
 */

namespace BitChatClient.Network.SecureChannel
{
    enum SecureChannelCryptoOptionFlags : byte
    {
        None = 0,
        RSA_AES256 = 1
    }

    abstract class SecureChannelStream : Stream
    {
        #region variables

        static HashAlgorithm _hashAlgoSHA256 = HashAlgorithm.Create("SHA256");
        static RandomNumberGenerator _rnd = new RNGCryptoServiceProvider();
        static RandomNumberGenerator _rndPadding = new RNGCryptoServiceProvider();

        protected const SecureChannelCryptoOptionFlags _supportedOptions = SecureChannelCryptoOptionFlags.RSA_AES256;

        IPEndPoint _remotePeerEP;

        protected Certificate _remotePeerCert;
        protected SecureChannelCryptoOptionFlags _selectedCryptoOption;

        //io & crypto related
        protected Stream _baseStream;
        protected int _blockSizeBytes;

        protected ICryptoTransform _cryptoWriter;
        protected ICryptoTransform _cryptoReader;

        //buffering
        const int BUFFER_SIZE = 65536;

        byte[] _writeBufferData = new byte[BUFFER_SIZE];
        int _writeBufferPosition = 2;
        byte[] _writeEncryptedData = new byte[BUFFER_SIZE];

        byte[] _readBufferData = new byte[BUFFER_SIZE];
        int _readBufferPosition;
        int _readBufferLength;
        byte[] _readEncryptedData = new byte[BUFFER_SIZE];

        #endregion

        #region constructor

        public SecureChannelStream(IPEndPoint remotePeerEP)
        {
            _remotePeerEP = remotePeerEP;
        }

        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            try
            {
                _baseStream.Dispose();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        #endregion

        #region stream support

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override bool CanTimeout
        {
            get { return _baseStream.CanTimeout; }
        }

        public override int ReadTimeout
        {
            get { return _baseStream.ReadTimeout; }
            set { _baseStream.ReadTimeout = value; }
        }

        public override int WriteTimeout
        {
            get { return _baseStream.WriteTimeout; }
            set { _baseStream.WriteTimeout = value; }
        }

        public override long Length
        {
            get { throw new IOException("SecureChannel stream is not seekable."); }
        }

        public override long Position
        {
            get
            {
                throw new IOException("SecureChannel stream is not seekable.");
            }
            set
            {
                throw new IOException("SecureChannel stream is not seekable.");
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new IOException("SecureChannel stream is not seekable.");
        }

        public override void SetLength(long value)
        {
            throw new IOException("SecureChannel stream is not seekable.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count < 1)
                return;

            do
            {
                int bytesAvailable = BUFFER_SIZE - _writeBufferPosition;
                if (bytesAvailable < count)
                {
                    if (bytesAvailable > 0)
                    {
                        Buffer.BlockCopy(buffer, offset, _writeBufferData, _writeBufferPosition, count);
                        _writeBufferPosition += count;
                        offset += bytesAvailable;
                        count -= bytesAvailable;
                    }

                    Flush();
                }
                else
                {
                    Buffer.BlockCopy(buffer, offset, _writeBufferData, _writeBufferPosition, count);
                    _writeBufferPosition += count;
                    break;
                }
            }
            while (true);
        }

        public override void Flush()
        {
            if (_writeBufferPosition == 2)
                return;

            //calc header and padding
            ushort dataLength = Convert.ToUInt16(_writeBufferPosition); //2bytes header length

            int bytesPadding = 0;
            int pendingBytes = dataLength % _blockSizeBytes;
            if (pendingBytes > 0)
                bytesPadding = _blockSizeBytes - pendingBytes;

            //write padding
            if (bytesPadding > 0)
            {
                byte[] padding = new byte[bytesPadding];
                _rndPadding.GetBytes(padding);

                Buffer.BlockCopy(padding, 0, _writeBufferData, _writeBufferPosition, padding.Length);
                _writeBufferPosition += padding.Length;
            }

            //write header
            byte[] header = BitConverter.GetBytes(dataLength);
            _writeBufferData[0] = header[0];
            _writeBufferData[1] = header[1];

            //encrypt buffered data
            if (_cryptoWriter.CanTransformMultipleBlocks)
            {
                _cryptoWriter.TransformBlock(_writeBufferData, 0, _writeBufferPosition, _writeEncryptedData, 0);
            }
            else
            {
                for (int offset = 0; offset < _writeBufferPosition; offset += _blockSizeBytes)
                    _cryptoWriter.TransformBlock(_writeBufferData, offset, _blockSizeBytes, _writeEncryptedData, offset);
            }

            //write encrypted data
            _baseStream.Write(_writeEncryptedData, 0, _writeBufferPosition);

            //reset buffer
            _writeBufferPosition = 2;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count < 1)
                throw new IOException("Count must be atleast 1 byte.");

            int bytesAvailableForRead = _readBufferLength - _readBufferPosition;

            if (bytesAvailableForRead < 1)
            {
                //read first block to read the encrypted frame size
                OffsetStream.StreamRead(_baseStream, _readEncryptedData, 0, _blockSizeBytes);

                //decrypt first block
                _cryptoReader.TransformBlock(_readEncryptedData, 0, _blockSizeBytes, _readBufferData, 0);

                //read frame header
                int dataLength = BitConverter.ToUInt16(_readBufferData, 0);
                _readBufferPosition = _blockSizeBytes;
                _readBufferLength = dataLength;

                dataLength -= _blockSizeBytes;

                if (_cryptoReader.CanTransformMultipleBlocks)
                {
                    if (dataLength > 0)
                    {
                        int pendingBlocks = dataLength / _blockSizeBytes;

                        if (dataLength % _blockSizeBytes > 0)
                            pendingBlocks++;

                        int pendingBytes = pendingBlocks * _blockSizeBytes;

                        //read pending blocks
                        OffsetStream.StreamRead(_baseStream, _readEncryptedData, 0, pendingBytes);

                        //decrypt blocks
                        _cryptoReader.TransformBlock(_readEncryptedData, 0, pendingBytes, _readBufferData, _readBufferPosition);
                    }
                }
                else
                {
                    while (dataLength > 0)
                    {
                        //read next block
                        OffsetStream.StreamRead(_baseStream, _readEncryptedData, 0, _blockSizeBytes);

                        //decrypt block
                        _cryptoReader.TransformBlock(_readEncryptedData, 0, _blockSizeBytes, _readBufferData, _readBufferPosition);
                        _readBufferPosition += _blockSizeBytes;

                        dataLength -= _blockSizeBytes;
                    }
                }

                _readBufferPosition = 2;
                bytesAvailableForRead = _readBufferLength - _readBufferPosition;
            }

            {
                int bytesToRead = count;

                if (bytesToRead > bytesAvailableForRead)
                    bytesToRead = bytesAvailableForRead;

                Buffer.BlockCopy(_readBufferData, _readBufferPosition, buffer, offset, bytesToRead);
                _readBufferPosition += bytesToRead;

                return bytesToRead;
            }
        }

        #endregion

        #region protected

        protected byte[] GenerateRandomChallenge(string seed)
        {
            using (MemoryStream mS = new MemoryStream(1024))
            {
                byte[] buffer = new byte[255];
                _rnd.GetBytes(buffer);
                mS.Write(buffer, 0, buffer.Length);

                byte[] buffer2 = BitConverter.GetBytes(Thread.CurrentThread.ManagedThreadId);
                mS.Write(buffer2, 0, buffer2.Length);

                buffer = Encoding.UTF8.GetBytes(seed);
                mS.Write(buffer, 0, buffer.Length);

                byte[] buffer3 = BitConverter.GetBytes(System.Diagnostics.Process.GetCurrentProcess().Id);
                mS.Write(buffer2, 0, buffer2.Length);

                buffer = BitConverter.GetBytes(DateTime.UtcNow.ToBinary());
                mS.Write(buffer, 0, buffer.Length);

                buffer = new byte[255];
                _rnd.GetBytes(buffer);
                mS.Write(buffer, 0, buffer.Length);

                //get challenge
                return _hashAlgoSHA256.ComputeHash(mS.ToArray());
            }
        }

        protected byte[] GenerateChallengeResponse(byte[] challenge, string sharedSecret)
        {
            using (MemoryStream mS = new MemoryStream(1024))
            {
                //challenge + sharedSecret + challenge
                mS.Write(challenge, 0, challenge.Length);

                byte[] buffer = Encoding.UTF8.GetBytes(sharedSecret);
                mS.Write(buffer, 0, buffer.Length);

                mS.Write(challenge, 0, challenge.Length);

                //get response
                return _hashAlgoSHA256.ComputeHash(mS.ToArray());
            }
        }

        protected bool IsChallengeResponseValid(byte[] challenge, byte[] response, string sharedSecret)
        {
            byte[] computedResponse = GenerateChallengeResponse(challenge, sharedSecret);

            //verify

            for (int i = 0; i < computedResponse.Length; i++)
            {
                if (computedResponse[i] != response[i])
                    return false;
            }

            return true;
        }

        protected SymmetricCryptoKey MatchOptionsAndGenerateEncryptionKey(SecureChannelCryptoOptionFlags options, Certificate localCert)
        {
            //match crypto options
            _selectedCryptoOption = _supportedOptions & options;

            if ((_selectedCryptoOption & SecureChannelCryptoOptionFlags.RSA_AES256) > 0)
            {
                if (localCert.PublicKeyEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.RSA)
                    throw new SecureChannelException(SecureChannelErrorCode.NoMatchingCryptoAvailable, "SecureChannel handshake failed: no matching crypto option available");

                _selectedCryptoOption = SecureChannelCryptoOptionFlags.RSA_AES256;
            }
            else
            {
                throw new SecureChannelException(SecureChannelErrorCode.NoMatchingCryptoAvailable, "SecureChannel handshake failed: no matching crypto option available");
            }

            //generate encryption key
            switch (_selectedCryptoOption)
            {
                case SecureChannelCryptoOptionFlags.RSA_AES256:
                    return new SymmetricCryptoKey(SymmetricEncryptionAlgorithm.Rijndael, 256, PaddingMode.None);

                default:
                    throw new IOException();
            }
        }

        #endregion

        #region properties

        public IPEndPoint RemotePeerEP
        { get { return _remotePeerEP; } }

        public Certificate RemotePeerCertificate
        { get { return _remotePeerCert; } }

        public SecureChannelCryptoOptionFlags SelectedCryptoOption
        { get { return _selectedCryptoOption; } }

        #endregion
    }
}
