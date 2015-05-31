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
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

/*
=============
= VERSION 3 =
=============

FEATURES-
---------
 - random challenge on both ends to prevent replay attacks.
 - ephemeral keys used for key exchange to provide perfect forward secrecy.
 - pre-shared key based auth to prevent direct certificate disclosure, preventing identity disclosure to active attacker.
 - encrypted digital certificate exchange to prevent certificate disclosure while exchange, preventing identity disclosure to passive attacker.
 - digital certificate based authentication for ensuring identity and prevent MiTM.
 - secure channel data packet authenticated by HMAC(cipher-text) to provide authenticated encryption.
 - key re-negotation feature for allowing the secure channel to remain always on.
 
<============================================================================>
                            SERVER        CLIENT
<============================================================================>
                           version  --->
                                    <---  version supported
<----------------------------------------------------------------------------> version selection done
                                          client nonce +
                                    <---  crypto options  
                    server nonce +  
            selected crypto option  ---> 
<----------------------------------------------------------------------------> hello handshake done
                                    <---  ephemeral public key +
                                          signature
            ephemeral public key +
                         signature  --->
<----------------------------------------------------------------------------> key exchange done
    master key = HMAC(client hello + server hello + optional psk, derived key)
<----------------------------------------------------------------------------> master key generated on both sides with optional pre-shared key; encryption layer ON
                                    <---  certificate
                       certificate  --->
<----------------------------------------------------------------------------> cert exchange done
    verify certificate and ephemeral public key signature
<----------------------------------------------------------------------------> final authentication done; data exchange ON
                              data  <-->  data
<============================================================================>
 */

namespace BitChatClient.Network.SecureChannel
{
    enum SecureChannelCryptoOptionFlags : byte
    {
        None = 0,
        DHE2048_RSA_WITH_AES256_CBC_HMAC_SHA256 = 1,
        ECDHE256_RSA_WITH_AES256_CBC_HMAC_SHA256 = 2
    }

    abstract class SecureChannelStream : Stream
    {
        #region variables

        protected static RandomNumberGenerator _rnd = new RNGCryptoServiceProvider();
        static RandomNumberGenerator _rndPadding = new RNGCryptoServiceProvider();

        IPEndPoint _remotePeerEP;
        protected Certificate _remotePeerCert;

        //io & crypto related
        protected Stream _baseStream;
        protected bool _reNegotiate = false;
        protected SecureChannelCryptoOptionFlags _selectedCryptoOption;
        int _blockSizeBytes;
        ICryptoTransform _cryptoEncryptor;
        ICryptoTransform _cryptoDecryptor;
        HMAC _authHMAC;

        //buffering
        const int BUFFER_SIZE = 65536;
        int _authHMACSize;

        byte[] _writeBufferData = new byte[BUFFER_SIZE];
        int _writeBufferPosition;
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
                int bytesAvailable = BUFFER_SIZE - _writeBufferPosition - _authHMACSize;
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
            lock (this) //flush lock
            {
                if ((_writeBufferPosition == 3) && !_reNegotiate)
                    return;

                //calc header and padding
                ushort dataLength = Convert.ToUInt16(_writeBufferPosition); //includes 3 bytes header length
                int bytesPadding = 0;
                int pendingBytes = dataLength % _blockSizeBytes;
                if (pendingBytes > 0)
                    bytesPadding = _blockSizeBytes - pendingBytes;

                //write header
                byte[] header = BitConverter.GetBytes(dataLength);
                _writeBufferData[0] = header[0];
                _writeBufferData[1] = header[1];

                if (_reNegotiate)
                    _writeBufferData[2] = 1; //re-negotiate
                else
                    _writeBufferData[2] = 0; //no flags

                //write padding
                if (bytesPadding > 0)
                {
                    byte[] padding = new byte[bytesPadding];
                    _rndPadding.GetBytes(padding);

                    Buffer.BlockCopy(padding, 0, _writeBufferData, _writeBufferPosition, padding.Length);
                    _writeBufferPosition += padding.Length;
                }

                //encrypt buffered data
                if (_cryptoEncryptor.CanTransformMultipleBlocks)
                {
                    _cryptoEncryptor.TransformBlock(_writeBufferData, 0, _writeBufferPosition, _writeEncryptedData, 0);
                }
                else
                {
                    for (int offset = 0; offset < _writeBufferPosition; offset += _blockSizeBytes)
                        _cryptoEncryptor.TransformBlock(_writeBufferData, offset, _blockSizeBytes, _writeEncryptedData, offset);
                }

                //append auth hmac to encrypted data
                byte[] authHMAC = _authHMAC.ComputeHash(_writeEncryptedData, 0, _writeBufferPosition);
                Buffer.BlockCopy(authHMAC, 0, _writeEncryptedData, _writeBufferPosition, authHMAC.Length);
                _writeBufferPosition += authHMAC.Length;

                //write encrypted data + auth hmac
                _baseStream.Write(_writeEncryptedData, 0, _writeBufferPosition);

                //reset buffer
                _writeBufferPosition = 3;

                //check for re-negotiation
                if (_reNegotiate)
                {
                    Monitor.Pulse(this); //signal waiting read() thread
                    Monitor.Wait(this); //wait till re-negotiation completes
                    _reNegotiate = false;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count < 1)
                throw new IOException("Count must be atleast 1 byte.");

            int bytesAvailableForRead = _readBufferLength - _readBufferPosition;

            while (bytesAvailableForRead < 1)
            {
                //read first block to read the encrypted frame size
                OffsetStream.StreamRead(_baseStream, _readEncryptedData, 0, _blockSizeBytes);
                _cryptoDecryptor.TransformBlock(_readEncryptedData, 0, _blockSizeBytes, _readBufferData, 0);
                _readBufferPosition = _blockSizeBytes;

                //read frame header 2 byte length
                int dataLength = BitConverter.ToUInt16(_readBufferData, 0);
                _readBufferLength = dataLength;

                dataLength -= _blockSizeBytes;

                if (_cryptoDecryptor.CanTransformMultipleBlocks)
                {
                    if (dataLength > 0)
                    {
                        int pendingBlocks = dataLength / _blockSizeBytes;

                        if (dataLength % _blockSizeBytes > 0)
                            pendingBlocks++;

                        int pendingBytes = pendingBlocks * _blockSizeBytes;

                        //read pending blocks
                        OffsetStream.StreamRead(_baseStream, _readEncryptedData, _readBufferPosition, pendingBytes);
                        _cryptoDecryptor.TransformBlock(_readEncryptedData, _readBufferPosition, pendingBytes, _readBufferData, _readBufferPosition);
                        _readBufferPosition += pendingBytes;
                    }
                }
                else
                {
                    while (dataLength > 0)
                    {
                        //read next block
                        OffsetStream.StreamRead(_baseStream, _readEncryptedData, _readBufferPosition, _blockSizeBytes);
                        _cryptoDecryptor.TransformBlock(_readEncryptedData, 0, _blockSizeBytes, _readBufferData, _readBufferPosition);
                        _readBufferPosition += _blockSizeBytes;

                        dataLength -= _blockSizeBytes;
                    }
                }

                //read auth hmac
                BinaryID authHMAC = new BinaryID(new byte[_authHMACSize]);
                OffsetStream.StreamRead(_baseStream, authHMAC.ID, 0, _authHMACSize);

                //verify auth hmac with computed hmac
                BinaryID computedAuthHMAC = new BinaryID(_authHMAC.ComputeHash(_readEncryptedData, 0, _readBufferPosition));

                if (!computedAuthHMAC.Equals(authHMAC))
                    throw new SecureChannelException(SecureChannelCode.InvalidMessageHMACReceived);

                _readBufferPosition = 3;
                bytesAvailableForRead = _readBufferLength - _readBufferPosition;

                //check header flags
                if (_readBufferData[2] == 1)
                {
                    //re-negotiate
                    lock (this) //take flush lock
                    {
                        if (!_reNegotiate)
                        {
                            ThreadPool.QueueUserWorkItem(AsyncCallReNegotiateNow, null);
                            Monitor.Wait(this); //wait till flush() thread blocks
                        }

                        StartReNegotiation();
                        Monitor.Pulse(this); //signal flush() thread complete
                    }
                }
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

        private void AsyncCallReNegotiateNow(object state)
        {
            ReNegotiateNow();
        }

        #endregion

        #region public

        public void ReNegotiateNow()
        {
            _reNegotiate = true;
            this.Flush();
        }

        #endregion

        #region protected

        protected abstract void StartReNegotiation();

        protected void EnableEncryption(Stream inputStream, SymmetricCryptoKey encryptionKey, SymmetricCryptoKey decryptionKey, HMAC authHMAC)
        {
            //create reader and writer objects
            _cryptoEncryptor = encryptionKey.GetEncryptor();
            _cryptoDecryptor = decryptionKey.GetDecryptor();

            //init variables
            _baseStream = inputStream;
            _blockSizeBytes = encryptionKey.BlockSize / 8;
            _authHMAC = authHMAC;

            _authHMACSize = _authHMAC.HashSize / 8;
            _writeBufferPosition = 3;
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
