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
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient.Network.SecureChannel
{
    class SecureChannelClientStream : SecureChannelStream
    {
        #region constructor

        public SecureChannelClientStream(Stream stream, IPEndPoint remotePeerEP, CertificateStore clientCredentials, string sharedSecret, Certificate[] trustedRootCertificates, ISecureChannelSecurityManager manager)
            : base(remotePeerEP)
        {
            try
            {
                //0. check protocol version
                int version = stream.ReadByte();

                switch (version)
                {
                    case 3:
                        SecureChannelPacket.PublicKey serverPublicKey;
                        SymmetricCryptoKey encryptionKey;

                        #region 1. challenge handshake
                        {
                            //1.1. read server challenge and respond
                            {
                                //read challenge
                                SecureChannelPacket serverChallenge = new SecureChannelPacket(stream);

                                //generate response
                                byte[] clientResponse = GenerateChallengeResponse(serverChallenge.Data, sharedSecret);

                                //send response
                                SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, clientResponse, 0, clientResponse.Length);
                            }

                            //1.2. challenge server and verify response
                            {
                                //generate random challenge
                                byte[] clientChallenge = GenerateRandomChallenge(clientCredentials.Certificate.PublicKeyXML);

                                //send challenge
                                SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, clientChallenge, 0, clientChallenge.Length);

                                //read server response
                                SecureChannelPacket serverResponse = new SecureChannelPacket(stream);

                                //verify server response
                                if (!IsChallengeResponseValid(clientChallenge, serverResponse.Data, sharedSecret))
                                    throw new SecureChannelException(SecureChannelErrorCode.InvalidChallengeResponse, "SecureChannel invalid challenge response.");
                            }
                        }
                        #endregion

                        #region 2. exchange public key
                        {
                            //send client public key & options
                            byte[] publicKey = (new SecureChannelPacket.PublicKey(_supportedOptions, clientCredentials.PrivateKey.Algorithm, clientCredentials.PrivateKey.GetPublicKey())).ToArray();
                            SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, publicKey, 0, publicKey.Length);

                            //read server public key & options
                            serverPublicKey = new SecureChannelPacket(stream).GetPublicKey();

                            //match crypto options & generate encryption key
                            encryptionKey = MatchOptionsAndGenerateEncryptionKey(serverPublicKey.CryptoOptions, clientCredentials.Certificate);

                            //enable encryption layer
                            _cryptoWriter = encryptionKey.GetEncryptor();
                        }
                        #endregion

                        #region 3. exchange channel key
                        {
                            //send client channel key
                            byte[] encryptedChannelKey = AsymmetricCryptoKey.Encrypt(encryptionKey.ToArray(), serverPublicKey.PublicKeyEncryptionAlgorithm, serverPublicKey.PublicKeyXML);
                            SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, encryptedChannelKey, 0, encryptedChannelKey.Length);

                            //read server channel key
                            SecureChannelPacket serverChannelKey = new SecureChannelPacket(stream);

                            //save channel decryption key
                            SymmetricCryptoKey decryptionKey;

                            using (MemoryStream data = new MemoryStream(clientCredentials.PrivateKey.Decrypt(serverChannelKey.Data)))
                            {
                                decryptionKey = new SymmetricCryptoKey(data);
                            }

                            //enable encryption layer
                            _cryptoReader = decryptionKey.GetDecryptor();
                        }
                        #endregion

                        #region 4. exchange & verify certificates
                        {
                            //init variables
                            _baseStream = stream;
                            _blockSizeBytes = encryptionKey.BlockSize / 8;

                            //send client certificate
                            byte[] clientCert = clientCredentials.Certificate.ToArray();
                            SecureChannelPacket.WritePacket(this, SecureChannelErrorCode.NoError, clientCert, 0, clientCert.Length);

                            //read server certificate
                            _remotePeerCert = new Certificate(new SecureChannelPacket(this).GetDataStream());

                            //verify server certificate
                            if ((serverPublicKey.PublicKeyEncryptionAlgorithm != _remotePeerCert.PublicKeyEncryptionAlgorithm) || (serverPublicKey.PublicKeyXML != _remotePeerCert.PublicKeyXML))
                                throw new SecureChannelException(SecureChannelErrorCode.InvalidRemoteCertificate, "Certificate public key does not match with handshake public key.");

                            try
                            {
                                _remotePeerCert.Verify(trustedRootCertificates);
                            }
                            catch (Exception ex)
                            {
                                throw new SecureChannelException(SecureChannelErrorCode.InvalidRemoteCertificate, "Invalid remote certificate.", ex);
                            }

                            if ((manager != null) && !manager.ProceedConnection(_remotePeerCert))
                                throw new SecureChannelException(SecureChannelErrorCode.SecurityManagerDeclinedAccess, "Security manager declined access.");

                            //send ok
                            SecureChannelPacket.WritePacket(this, SecureChannelErrorCode.NoError, null, 0, 0);
                        }
                        #endregion
                        break;

                    default:
                        throw new SecureChannelException(SecureChannelErrorCode.ProtocolVersionNotSupported, "SecureChannel protocol version '" + version + "' not supported.");
                }
            }
            catch (SecureChannelException ex)
            {
                if (_baseStream == null)
                    SecureChannelPacket.WritePacket(stream, ex.ErrorCode, null, 0, 0);
                else
                    SecureChannelPacket.WritePacket(this, ex.ErrorCode, null, 0, 0);

                throw;
            }
        }

        #endregion
    }
}
