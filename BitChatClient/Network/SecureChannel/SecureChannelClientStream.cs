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
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient.Network.SecureChannel
{
    class SecureChannelClientStream : SecureChannelStream
    {
        #region variables

        int _version;

        CertificateStore _clientCredentials;
        Certificate[] _trustedRootCertificates;
        ISecureChannelSecurityManager _manager;
        SecureChannelCryptoOptionFlags _supportedOptions;
        string _preSharedKey;

        #endregion

        #region constructor

        public SecureChannelClientStream(Stream stream, IPEndPoint remotePeerEP, CertificateStore clientCredentials, Certificate[] trustedRootCertificates, ISecureChannelSecurityManager manager, SecureChannelCryptoOptionFlags supportedOptions, string preSharedKey = null)
            : base(remotePeerEP)
        {
            _clientCredentials = clientCredentials;
            _trustedRootCertificates = trustedRootCertificates;
            _manager = manager;
            _supportedOptions = supportedOptions;
            _preSharedKey = preSharedKey;

            try
            {
                //read server protocol version
                _version = stream.ReadByte();

                switch (_version)
                {
                    case 3:
                        //send supported client version
                        stream.WriteByte(3);

                        ProtocolV3(stream, clientCredentials, trustedRootCertificates, manager, preSharedKey, supportedOptions);
                        break;

                    default:
                        throw new SecureChannelException(SecureChannelCode.ProtocolVersionNotSupported, "SecureChannel protocol version '" + _version + "' not supported.");
                }
            }
            catch (SecureChannelException ex)
            {
                try
                {
                    if (_baseStream == null)
                        SecureChannelPacket.WritePacket(stream, ex.Code);
                    else
                        SecureChannelPacket.WritePacket(this, ex.Code);
                }
                catch
                { }

                throw;
            }
        }

        #endregion

        #region private

        private void ProtocolV3(Stream stream, CertificateStore clientCredentials, Certificate[] trustedRootCertificates, ISecureChannelSecurityManager manager, string preSharedKey, SecureChannelCryptoOptionFlags supportedOptions)
        {
            #region 1. hello handshake

            //send client hello
            SecureChannelPacket.Hello clientHello = new SecureChannelPacket.Hello(BinaryID.GenerateRandomID256(), supportedOptions);
            SecureChannelPacket.WritePacket(stream, clientHello);

            //read server hello
            SecureChannelPacket.Hello serverHello = (new SecureChannelPacket(stream)).GetHello();

            //read selected crypto option
            _selectedCryptoOption = supportedOptions & serverHello.CryptoOptions;

            if (_selectedCryptoOption == SecureChannelCryptoOptionFlags.None)
                throw new SecureChannelException(SecureChannelCode.NoMatchingCryptoAvailable);

            #endregion

            #region 2. key exchange

            SymmetricEncryptionAlgorithm encAlgo;
            string hashAlgo;
            KeyAgreement keyAgreement;

            switch (_selectedCryptoOption)
            {
                case SecureChannelCryptoOptionFlags.DHE2048_RSA_WITH_AES256_CBC_HMAC_SHA256:
                    encAlgo = SymmetricEncryptionAlgorithm.Rijndael;
                    hashAlgo = "SHA256";
                    keyAgreement = KeyAgreement.Create(KeyAgreementAlgorithm.DiffieHellman, 2048, KeyDerivationFunction.Hmac, KeyDerivationHashAlgorithm.SHA256);
                    break;

                case SecureChannelCryptoOptionFlags.ECDHE256_RSA_WITH_AES256_CBC_HMAC_SHA256:
                    encAlgo = SymmetricEncryptionAlgorithm.Rijndael;
                    hashAlgo = "SHA256";
                    keyAgreement = KeyAgreement.Create(KeyAgreementAlgorithm.ECDiffieHellman, 256, KeyDerivationFunction.Hmac, KeyDerivationHashAlgorithm.SHA256);
                    break;

                default:
                    throw new SecureChannelException(SecureChannelCode.NoMatchingCryptoAvailable);
            }

            //send client key exchange data
            SecureChannelPacket.KeyExchange clientKeyExchange = new SecureChannelPacket.KeyExchange(keyAgreement.GetPublicKeyXML(), clientCredentials.PrivateKey, hashAlgo);
            SecureChannelPacket.WritePacket(stream, clientKeyExchange);

            //read server key exchange data
            SecureChannelPacket.KeyExchange serverKeyExchange = (new SecureChannelPacket(stream)).GetKeyExchange();

            //generate: master key = HMAC(client hello + server hello + psk, derived key)
            using (MemoryStream mS = new MemoryStream(128))
            {
                clientHello.WriteTo(mS);
                serverHello.WriteTo(mS);

                if (!string.IsNullOrEmpty(preSharedKey))
                {
                    byte[] psk = System.Text.Encoding.UTF8.GetBytes(preSharedKey);
                    mS.Write(psk, 0, psk.Length);
                }

                keyAgreement.HmacMessage = mS.ToArray();
            }

            byte[] masterKey = keyAgreement.DeriveKeyMaterial(serverKeyExchange.PublicKeyXML);

            //enable channel encryption
            switch (encAlgo)
            {
                case SymmetricEncryptionAlgorithm.Rijndael:
                    //using MD5 for generating AES IV of 128bit block size
                    HashAlgorithm md5Hash = HashAlgorithm.Create("MD5");
                    byte[] eIV = md5Hash.ComputeHash(clientHello.Nonce.ID);
                    byte[] dIV = md5Hash.ComputeHash(serverHello.Nonce.ID);

                    //create encryption and decryption objects
                    SymmetricCryptoKey encryptionKey = new SymmetricCryptoKey(SymmetricEncryptionAlgorithm.Rijndael, masterKey, eIV, PaddingMode.None);
                    SymmetricCryptoKey decryptionKey = new SymmetricCryptoKey(SymmetricEncryptionAlgorithm.Rijndael, masterKey, dIV, PaddingMode.None);

                    //enable encryption
                    EnableEncryption(stream, encryptionKey, decryptionKey, new HMACSHA256(masterKey));
                    break;

                default:
                    throw new SecureChannelException(SecureChannelCode.NoMatchingCryptoAvailable);
            }

            //channel encryption is ON!

            #endregion

            #region 3. exchange & verify certificates & signatures

            if (!_reNegotiate)
            {
                //send client certificate
                SecureChannelPacket.WritePacket(this, clientCredentials.Certificate);

                //read server certificate
                _remotePeerCert = (new SecureChannelPacket(this)).GetCertificate();

                //verify server certificate
                try
                {
                    _remotePeerCert.Verify(trustedRootCertificates);
                }
                catch (Exception ex)
                {
                    throw new SecureChannelException(SecureChannelCode.InvalidRemoteCertificate, "Invalid remote certificate.", ex);
                }
            }

            //verify key exchange signature
            switch (_selectedCryptoOption)
            {
                case SecureChannelCryptoOptionFlags.DHE2048_RSA_WITH_AES256_CBC_HMAC_SHA256:
                case SecureChannelCryptoOptionFlags.ECDHE256_RSA_WITH_AES256_CBC_HMAC_SHA256:
                    if (_remotePeerCert.PublicKeyEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.RSA)
                        throw new SecureChannelException(SecureChannelCode.InvalidRemoteCertificateAlgorithm);

                    if (!serverKeyExchange.IsSignatureValid(_remotePeerCert, "SHA256"))
                        throw new SecureChannelException(SecureChannelCode.InvalidRemoteKeyExchangeSignature);

                    break;

                default:
                    throw new SecureChannelException(SecureChannelCode.NoMatchingCryptoAvailable);
            }

            if ((manager != null) && !manager.ProceedConnection(_remotePeerCert))
                throw new SecureChannelException(SecureChannelCode.SecurityManagerDeclinedAccess, "Security manager declined access.");

            #endregion
        }

        #endregion

        #region overrides

        protected override void StartReNegotiation()
        {
            try
            {
                switch (_version)
                {
                    case 3:
                        ProtocolV3(_baseStream, _clientCredentials, _trustedRootCertificates, _manager, _preSharedKey, _supportedOptions);
                        break;

                    default:
                        throw new SecureChannelException(SecureChannelCode.ProtocolVersionNotSupported, "SecureChannel protocol version '" + _version + "' not supported.");
                }
            }
            catch (SecureChannelException ex)
            {
                try
                {
                    SecureChannelPacket.WritePacket(_baseStream, ex.Code);
                }
                catch
                { }

                throw;
            }
        }

        #endregion
    }
}
