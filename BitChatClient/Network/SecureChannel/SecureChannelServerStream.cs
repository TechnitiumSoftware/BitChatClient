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
    class SecureChannelServerStream : SecureChannelStream
    {
        #region constructor

        public SecureChannelServerStream(Stream stream, IPEndPoint remotePeerEP, CertificateStore serverCredentials, string sharedSecret, Certificate[] trustedRootCertificates, ISecureChannelSecurityManager manager)
            : base(remotePeerEP)
        {
            try
            {
                //0. send protocol version
                stream.WriteByte(3);

                SecureChannelPacket.PublicKey clientPublicKey;
                SymmetricCryptoKey encryptionKey;

                #region 1. challenge handshake
                {
                    //1.1 challenge client and verify response
                    {
                        //generate random challenge
                        byte[] serverChallenge = GenerateRandomChallenge(serverCredentials.Certificate.PublicKeyXML);

                        //send challenge
                        SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, serverChallenge, 0, serverChallenge.Length);

                        //read response
                        SecureChannelPacket clientResponse = new SecureChannelPacket(stream);

                        //verify response
                        if (!IsChallengeResponseValid(serverChallenge, clientResponse.Data, sharedSecret))
                            throw new SecureChannelException(SecureChannelErrorCode.InvalidChallengeResponse, "SecureChannel invalid challenge response.");
                    }

                    //1.2. read client challenge and respond
                    {
                        //read challenge
                        SecureChannelPacket clientChallenge = new SecureChannelPacket(stream);

                        //generate response
                        byte[] serverResponse = GenerateChallengeResponse(clientChallenge.Data, sharedSecret);

                        //send response
                        SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, serverResponse, 0, serverResponse.Length);
                    }
                }
                #endregion

                #region 2. exchange public key
                {
                    //read client public key & options
                    clientPublicKey = new SecureChannelPacket(stream).GetPublicKey();

                    //send server public key & options
                    byte[] publicKey = (new SecureChannelPacket.PublicKey(_supportedOptions, serverCredentials.PrivateKey.Algorithm, serverCredentials.PrivateKey.GetPublicKey())).ToArray();
                    SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, publicKey, 0, publicKey.Length);

                    //match crypto options & generate encryption key
                    encryptionKey = MatchOptionsAndGenerateEncryptionKey(clientPublicKey.CryptoOptions, serverCredentials.Certificate);

                    //enable encryption layer
                    _cryptoWriter = encryptionKey.GetEncryptor();
                }
                #endregion

                #region 3. exchange channel key
                {
                    //read client channel key
                    SecureChannelPacket clientChannelKey = new SecureChannelPacket(stream);

                    //send server channel key
                    byte[] encryptedChannelKey = AsymmetricCryptoKey.Encrypt(encryptionKey.ToArray(), clientPublicKey.PublicKeyEncryptionAlgorithm, clientPublicKey.PublicKeyXML);
                    SecureChannelPacket.WritePacket(stream, SecureChannelErrorCode.NoError, encryptedChannelKey, 0, encryptedChannelKey.Length);

                    //save channel decryption key
                    SymmetricCryptoKey decryptionKey;

                    using (MemoryStream data = new MemoryStream(serverCredentials.PrivateKey.Decrypt(clientChannelKey.Data)))
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

                    //read client certificate
                    _remotePeerCert = new Certificate(new SecureChannelPacket(this).GetDataStream());

                    //verify client certificate
                    if ((clientPublicKey.PublicKeyEncryptionAlgorithm != _remotePeerCert.PublicKeyEncryptionAlgorithm) || (clientPublicKey.PublicKeyXML != _remotePeerCert.PublicKeyXML))
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

                    //send server certificate
                    byte[] serverCert = serverCredentials.Certificate.ToArray();
                    SecureChannelPacket.WritePacket(this, SecureChannelErrorCode.NoError, serverCert, 0, serverCert.Length);

                    //read ok
                    SecureChannelPacket ok = new SecureChannelPacket(this);
                }
                #endregion
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
