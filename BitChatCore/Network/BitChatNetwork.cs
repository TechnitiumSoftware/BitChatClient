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

using BitChatCore.Network.Connections;
using BitChatCore.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore.Network
{
    delegate void NetworkChanged(BitChatNetwork network, BinaryID newNetworkID);
    delegate void VirtualPeerAdded(BitChatNetwork network, BitChatNetwork.VirtualPeer virtualPeer);
    delegate void VirtualPeerHasRevokedCertificate(BitChatNetwork network, InvalidCertificateException ex);
    delegate void VirtualPeerSecureChannelException(BitChatNetwork network, SecureChannelException ex);
    delegate void VirtualPeerHasChangedCertificate(BitChatNetwork network, Certificate cert);
    delegate void VirtualPeerStateChanged(BitChatNetwork.VirtualPeer.VirtualSession peerSession);
    delegate void VirtualPeerMessageReceived(BitChatNetwork.VirtualPeer.VirtualSession peerSession, Stream messageDataStream);

    public enum BitChatNetworkType : byte
    {
        PrivateChat = 1,
        GroupChat = 2
    }

    public enum BitChatNetworkStatus : byte
    {
        Offline = 0,
        Online = 1
    }

    class BitChatNetwork : IDisposable
    {
        #region events

        public event NetworkChanged NetworkChanged;
        public event VirtualPeerAdded VirtualPeerAdded;
        public event VirtualPeerHasRevokedCertificate VirtualPeerHasRevokedCertificate;
        public event VirtualPeerSecureChannelException VirtualPeerSecureChannelException;
        public event VirtualPeerHasChangedCertificate VirtualPeerHasChangedCertificate;

        #endregion

        #region variables

        public const int MAX_MESSAGE_SIZE = 65216; //max payload size of secure channel packet - 4 bytes header
        const int BUFFER_SIZE = 65535;

        const int RE_NEGOTIATE_AFTER_BYTES_SENT = 104857600; //100mb
        const int RE_NEGOTIATE_AFTER_SECONDS = 3600; //1hr

        static readonly byte[] NETWORK_SECRET_HASH_SALT = new byte[] { 0xBB, 0x47, 0x0E, 0xE3, 0x0E, 0xAD, 0xFC, 0x36, 0xF2, 0x18, 0xF4, 0x0A, 0xDA, 0xA4, 0x2A, 0xF2, 0xB7, 0x20, 0xF6, 0xD2 };
        static readonly byte[] EMAIL_ADDRESS_HASH_SALT = new byte[] { 0x49, 0x42, 0x0C, 0x52, 0xC9, 0x3C, 0x5E, 0xB6, 0xAD, 0x83, 0x3F, 0x08, 0xBA, 0xD9, 0xB9, 0x6E, 0x23, 0x8F, 0xDC, 0xF3 };
        static readonly byte[] EMAIL_ADDRESS_MASK_SALT = new byte[] { 0x55, 0xBB, 0xB8, 0x5C, 0x21, 0xB3, 0xC3, 0x34, 0xBE, 0xF4, 0x4D, 0x9D, 0xD0, 0xAC, 0x8E, 0x8A, 0xE8, 0xED, 0x28, 0x3E };

        readonly BitChatNetworkType _type;
        readonly ConnectionManager _connectionManager;
        readonly Certificate[] _trustedRootCertificates;
        readonly SecureChannelCryptoOptionFlags _supportedCryptoOptions;
        MailAddress _peerEmailAddress;
        BinaryID _hashedPeerEmailAddress;
        readonly string _networkName;
        string _sharedSecret;
        BinaryID _networkID;
        BitChatNetworkStatus _status;
        readonly string _invitationSender;
        readonly string _invitationMessage;

        string _peerName;
        BinaryID _maskedPeerEmailAddress;
        byte[] _networkSecret;

        VirtualPeer _selfPeer;
        readonly Dictionary<string, VirtualPeer> _virtualPeers = new Dictionary<string, VirtualPeer>();
        readonly ReaderWriterLockSlim _virtualPeersLock = new ReaderWriterLockSlim();

        #endregion

        #region constructor

        public BitChatNetwork(ConnectionManager connectionManager, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions, BinaryID hashedPeerEmailAddress, BinaryID networkID, BitChatNetworkStatus status, string invitationSender, string invitationMessage)
        {
            _type = BitChatNetworkType.PrivateChat;
            _connectionManager = connectionManager;
            _trustedRootCertificates = trustedRootCertificates;
            _supportedCryptoOptions = supportedCryptoOptions;
            _hashedPeerEmailAddress = hashedPeerEmailAddress;
            _sharedSecret = "";
            _networkID = networkID;
            _status = status;
            _invitationSender = invitationSender;
            _invitationMessage = invitationMessage;

            LoadKnownPeers(new Certificate[] { });
        }

        public BitChatNetwork(ConnectionManager connectionManager, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions, MailAddress peerEmailAddress, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, BitChatNetworkStatus status, string invitationSender, string invitationMessage)
        {
            _type = BitChatNetworkType.PrivateChat;
            _connectionManager = connectionManager;
            _trustedRootCertificates = trustedRootCertificates;
            _supportedCryptoOptions = supportedCryptoOptions;
            _peerEmailAddress = peerEmailAddress;
            _sharedSecret = sharedSecret;
            _networkID = networkID;
            _status = status;
            _invitationSender = invitationSender;
            _invitationMessage = invitationMessage;

            if (_networkID == null)
                _networkID = GetNetworkID(_connectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress, _peerEmailAddress, _sharedSecret);

            if (knownPeerCerts.Length > 0)
                _peerName = knownPeerCerts[0].IssuedTo.Name;

            LoadKnownPeers(knownPeerCerts);
        }

        public BitChatNetwork(ConnectionManager connectionManager, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions, string networkName, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, BitChatNetworkStatus status)
        {
            _type = BitChatNetworkType.GroupChat;
            _connectionManager = connectionManager;
            _trustedRootCertificates = trustedRootCertificates;
            _supportedCryptoOptions = supportedCryptoOptions;
            _networkName = networkName;
            _sharedSecret = sharedSecret;
            _networkID = networkID;
            _status = status;

            if (_networkID == null)
                _networkID = GetNetworkID(_networkName, _sharedSecret);

            LoadKnownPeers(knownPeerCerts);
        }

        #endregion

        #region IDisposable support

        ~BitChatNetwork()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool _disposed = false;

        protected void Dispose(bool disposing)
        {
            lock (this)
            {
                if (!_disposed)
                {
                    _virtualPeersLock.EnterWriteLock();
                    try
                    {
                        foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                        {
                            try
                            {
                                vPeer.Value.Dispose();
                            }
                            catch
                            { }
                        }

                        _virtualPeers.Clear();

                        if (_selfPeer != null)
                        {
                            _selfPeer.Dispose();
                            _selfPeer = null;
                        }
                    }
                    finally
                    {
                        _virtualPeersLock.ExitWriteLock();
                    }

                    _virtualPeersLock.Dispose();

                    _disposed = true;
                }
            }
        }

        #endregion

        #region static

        private static BinaryID GetNetworkID(string networkName, string sharedSecret)
        {
            using (PBKDF2 hash = PBKDF2.CreateHMACSHA256((sharedSecret == null ? "" : sharedSecret), Encoding.UTF8.GetBytes(networkName.ToLower()), 10000))
            {
                return new BinaryID(hash.GetBytes(20));
            }
        }

        private static BinaryID GetNetworkID(MailAddress emailAddress1, MailAddress emailAddress2, string sharedSecret)
        {
            BinaryID salt = GetHashedEmailAddress(emailAddress1) ^ GetHashedEmailAddress(emailAddress2);

            using (PBKDF2 hash = PBKDF2.CreateHMACSHA256((sharedSecret == null ? "" : sharedSecret), salt.ID, 10000))
            {
                return new BinaryID(hash.GetBytes(20));
            }
        }

        public static BinaryID GetNetworkID(MailAddress emailAddress1, BinaryID hashedEmailAddress2)
        {
            BinaryID salt = GetHashedEmailAddress(emailAddress1) ^ hashedEmailAddress2;

            using (PBKDF2 hash = PBKDF2.CreateHMACSHA256("", salt.ID, 10000))
            {
                return new BinaryID(hash.GetBytes(20));
            }
        }

        private static byte[] GetNetworkSecret(string networkName, string sharedSecret)
        {
            using (PBKDF2 hash1 = PBKDF2.CreateHMACSHA256(networkName.ToLower(), NETWORK_SECRET_HASH_SALT, 10000))
            {
                using (PBKDF2 hash2 = PBKDF2.CreateHMACSHA256((sharedSecret == null ? "" : sharedSecret), hash1.GetBytes(20), 10000))
                {
                    return hash2.GetBytes(20);
                }
            }
        }

        private static byte[] GetNetworkSecret(MailAddress emailAddress1, MailAddress emailAddress2, string sharedSecret)
        {
            BinaryID salt = GetHashedEmailAddress(emailAddress1) ^ GetHashedEmailAddress(emailAddress2);

            using (PBKDF2 hash1 = PBKDF2.CreateHMACSHA256(salt.ID, NETWORK_SECRET_HASH_SALT, 10000))
            {
                using (PBKDF2 hash2 = PBKDF2.CreateHMACSHA256((sharedSecret == null ? "" : sharedSecret), hash1.GetBytes(20), 10000))
                {
                    return hash2.GetBytes(20);
                }
            }
        }

        private static byte[] GetNetworkSecret(MailAddress emailAddress1, BinaryID hashedEmailAddress2)
        {
            BinaryID salt = GetHashedEmailAddress(emailAddress1) ^ hashedEmailAddress2;

            using (PBKDF2 hash1 = PBKDF2.CreateHMACSHA256(salt.ID, NETWORK_SECRET_HASH_SALT, 10000))
            {
                using (PBKDF2 hash2 = PBKDF2.CreateHMACSHA256("", hash1.GetBytes(20), 10000))
                {
                    return hash2.GetBytes(20);
                }
            }
        }

        public static BinaryID GetHashedEmailAddress(MailAddress emailAddress)
        {
            using (PBKDF2 hash = PBKDF2.CreateHMACSHA256(emailAddress.Address.ToLower(), EMAIL_ADDRESS_HASH_SALT, 10000))
            {
                return new BinaryID(hash.GetBytes(20));
            }
        }

        public static BinaryID GetMaskedEmailAddress(MailAddress emailAddress)
        {
            using (PBKDF2 hash = PBKDF2.CreateHMACSHA256(emailAddress.Address.ToLower(), EMAIL_ADDRESS_MASK_SALT, 10000))
            {
                return new BinaryID(hash.GetBytes(20));
            }
        }

        #endregion

        #region private

        private byte[] GetNetworkSecret()
        {
            if (_networkSecret == null)
            {
                switch (_type)
                {
                    case BitChatNetworkType.PrivateChat:
                        if (_peerEmailAddress == null)
                            _networkSecret = GetNetworkSecret(_connectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress, _hashedPeerEmailAddress);
                        else
                            _networkSecret = GetNetworkSecret(_connectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress, _peerEmailAddress, _sharedSecret);

                        break;

                    default:
                        _networkSecret = GetNetworkSecret(_networkName, _sharedSecret);
                        break;
                }
            }

            return _networkSecret;
        }

        private void LoadKnownPeers(Certificate[] knownPeerCerts)
        {
            //load self as virtual peer
            Certificate selfCert = _connectionManager.Profile.LocalCertificateStore.Certificate;
            _selfPeer = new VirtualPeer(selfCert, this);
            _virtualPeers.Add(selfCert.IssuedTo.EmailAddress.Address.ToLower(), _selfPeer);

            //load known peers
            foreach (Certificate knownPeerCert in knownPeerCerts)
                _virtualPeers.Add(knownPeerCert.IssuedTo.EmailAddress.Address.ToLower(), new VirtualPeer(knownPeerCert, this));
        }

        private void SendInvitationAsync(object state)
        {
            try
            {
                IPEndPoint peerEP = state as IPEndPoint;

                //make connection
                Connection connection = _connectionManager.MakeConnection(peerEP);

                //send invitation
                connection.SendBitChatNetworkInvitation(_invitationMessage);
            }
            catch
            { }
        }

        private void MakeConnectionAsync(object state)
        {
            try
            {
                IPEndPoint peerEP = state as IPEndPoint;

                //make connection
                Connection connection = _connectionManager.MakeConnection(peerEP);

                EstablishSecureChannelAndJoinNetwork(connection);
            }
            catch
            { }
        }

        private void MakeVirtualConnectionAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];
                Connection viaConnection = parameters[0] as Connection;
                IPEndPoint peerEP = parameters[1] as IPEndPoint;

                //make virtual connection
                Connection virtualConnection = _connectionManager.MakeVirtualConnection(viaConnection, peerEP);

                EstablishSecureChannelAndJoinNetwork(virtualConnection);
            }
            catch
            { }
        }

        private void EstablishSecureChannelAndJoinNetwork(Connection connection)
        {
            BinaryID channelName = GetChannelName(connection.LocalPeerID, connection.RemotePeerID);

            //check if channel exists
            if (connection.BitChatNetworkChannelExists(channelName))
                return;

            //request channel
            Stream channel = connection.RequestBitChatNetworkChannel(channelName);

            try
            {
                //get secure channel
                SecureChannelStream secureChannel = new SecureChannelClientStream(channel, connection.RemotePeerEP, _connectionManager.Profile.LocalCertificateStore, _trustedRootCertificates, _connectionManager.Profile, _supportedCryptoOptions, RE_NEGOTIATE_AFTER_BYTES_SENT, RE_NEGOTIATE_AFTER_SECONDS, GetNetworkSecret());

                //join network
                JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel);
            }
            catch (SecureChannelException ex)
            {
                channel.Dispose();

                VirtualPeerSecureChannelException(this, ex);
            }
            catch (Exception ex)
            {
                channel.Dispose();

                Debug.Write("BitChatNetwork.EstablishSecureChannelAndJoinNetwork", ex);
            }
        }

        private void JoinNetwork(string peerID, SecureChannelStream channel)
        {
            if (_status == BitChatNetworkStatus.Offline)
                throw new BitChatException("Bit Chat network is offline.");

            if (_type == BitChatNetworkType.PrivateChat)
            {
                MailAddress remotePeerEmail = channel.RemotePeerCertificate.IssuedTo.EmailAddress;
                MailAddress selfEmail = _connectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress;

                if (!remotePeerEmail.Equals(selfEmail))
                {
                    if (_peerEmailAddress == null)
                    {
                        BinaryID computedNetworkID = GetNetworkID(selfEmail, remotePeerEmail, _sharedSecret);

                        if (!computedNetworkID.Equals(_networkID))
                            throw new BitChatException("User with email address '" + remotePeerEmail.Address + " [" + channel.RemotePeerEP.Address.ToString() + "]' is trying to join this private chat.");

                        _peerEmailAddress = remotePeerEmail;
                    }
                    else
                    {
                        if (!remotePeerEmail.Equals(_peerEmailAddress))
                            throw new BitChatException("User with email address '" + remotePeerEmail.Address + " [" + channel.RemotePeerEP.Address.ToString() + "]' is trying to join this private chat.");
                    }

                    _peerName = channel.RemotePeerCertificate.IssuedTo.Name;
                }
            }

            peerID = peerID.ToLower();

            VirtualPeer vPeer;
            bool peerAdded = false;

            _virtualPeersLock.EnterWriteLock();
            try
            {
                if (_virtualPeers.ContainsKey(peerID))
                {
                    vPeer = _virtualPeers[peerID];
                }
                else
                {
                    vPeer = new VirtualPeer(channel.RemotePeerCertificate, this);
                    _virtualPeers.Add(peerID, vPeer);

                    peerAdded = true;
                }
            }
            finally
            {
                _virtualPeersLock.ExitWriteLock();
            }

            if (peerAdded)
                VirtualPeerAdded(this, vPeer);

            vPeer.AddSession(channel);

            if (_connectionManager.Profile.CheckCertificateRevocationList)
            {
                //start async revocation list check process
                ThreadPool.QueueUserWorkItem(CheckCertificateRevocationListAsync, channel);
            }
        }

        private bool IsPeerConnected(IPEndPoint peerEP)
        {
            //check if peer already connected
            _virtualPeersLock.EnterReadLock();
            try
            {
                foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                {
                    if (vPeer.Value.IsOnline && vPeer.Value.isConnectedVia(peerEP))
                        return true;
                }
            }
            finally
            {
                _virtualPeersLock.ExitReadLock();
            }

            return false;
        }

        private void CheckCertificateRevocationListAsync(object state)
        {
            SecureChannelStream stream = null;

            try
            {
                stream = state as SecureChannelStream;

                stream.RemotePeerCertificate.VerifyRevocationList(_connectionManager.Profile.Proxy);
            }
            catch (InvalidCertificateException ex)
            {
                if (stream != null)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch
                    { }
                }

                VirtualPeerHasRevokedCertificate(this, ex);
            }
            catch
            { }
        }

        #endregion

        #region public

        public BinaryID GetChannelName(BinaryID localPeerID, BinaryID remotePeerID)
        {
            return Connection.GetChannelName(localPeerID, remotePeerID, _networkID);
        }

        public VirtualPeer[] GetVirtualPeerList()
        {
            VirtualPeer[] peerList;

            _virtualPeersLock.EnterReadLock();
            try
            {
                peerList = new VirtualPeer[_virtualPeers.Count];
                _virtualPeers.Values.CopyTo(peerList, 0);
            }
            finally
            {
                _virtualPeersLock.ExitReadLock();
            }

            return peerList;
        }

        public void SendInvitation(IEnumerable<IPEndPoint> peerEPs)
        {
            if (_status == BitChatNetworkStatus.Offline)
                throw new BitChatException("Bit Chat network is offline.");

            if (!string.IsNullOrEmpty(_invitationMessage))
            {
                foreach (IPEndPoint peerEP in peerEPs)
                {
                    if (!IsPeerConnected(peerEP))
                        ThreadPool.QueueUserWorkItem(SendInvitationAsync, peerEP);
                }
            }
        }

        public void MakeConnection(IPEndPoint peerEP)
        {
            if (_status == BitChatNetworkStatus.Offline)
                throw new BitChatException("Bit Chat network is offline.");

            if (!IsPeerConnected(peerEP))
                ThreadPool.QueueUserWorkItem(MakeConnectionAsync, peerEP);
        }

        public void MakeConnection(IEnumerable<IPEndPoint> peerEPs)
        {
            if (_status == BitChatNetworkStatus.Offline)
                throw new BitChatException("Bit Chat network is offline.");

            foreach (IPEndPoint peerEP in peerEPs)
            {
                if (!IsPeerConnected(peerEP))
                    ThreadPool.QueueUserWorkItem(MakeConnectionAsync, peerEP);
            }
        }

        public void MakeConnection(Connection viaConnection, IEnumerable<IPEndPoint> peerEPs)
        {
            if (_status == BitChatNetworkStatus.Offline)
                throw new BitChatException("Bit Chat network is offline.");

            foreach (IPEndPoint peerEP in peerEPs)
            {
                if (!IsPeerConnected(peerEP))
                    ThreadPool.QueueUserWorkItem(MakeVirtualConnectionAsync, new object[] { viaConnection, peerEP });
            }
        }

        public void AcceptConnectionAndJoinNetwork(Connection connection, Stream channel)
        {
            try
            {
                //get secure channel
                SecureChannelStream secureChannel = new SecureChannelServerStream(channel, connection.RemotePeerEP, _connectionManager.Profile.LocalCertificateStore, _trustedRootCertificates, _connectionManager.Profile, _supportedCryptoOptions, RE_NEGOTIATE_AFTER_BYTES_SENT, RE_NEGOTIATE_AFTER_SECONDS, GetNetworkSecret());

                //join network
                JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel);
            }
            catch (SecureChannelException ex)
            {
                channel.Dispose();

                VirtualPeerSecureChannelException(this, ex);
            }
            catch (Exception ex)
            {
                channel.Dispose();

                Debug.Write("BitChatNetwork.AcceptConnectionAndJoinNetwork", ex);
            }
        }

        public PeerInfo GetSelfPeerInfo()
        {
            return _selfPeer.GetPeerInfo();
        }

        public List<PeerInfo> GetConnectedPeerList()
        {
            List<PeerInfo> connectedPeers;

            _virtualPeersLock.EnterReadLock();
            try
            {
                connectedPeers = new List<PeerInfo>(_virtualPeers.Count);

                foreach (KeyValuePair<string, VirtualPeer> item in _virtualPeers)
                {
                    VirtualPeer vPeer = item.Value;

                    if (vPeer.IsOnline)
                        connectedPeers.Add(vPeer.GetPeerInfo());
                }
            }
            finally
            {
                _virtualPeersLock.ExitReadLock();
            }

            return connectedPeers;
        }

        public void WriteMessageBroadcast(byte[] data, int offset, int count)
        {
            _virtualPeersLock.EnterReadLock();
            try
            {
                foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                {
                    if (vPeer.Value.IsOnline)
                        vPeer.Value.WriteMessage(data, offset, count);
                }
            }
            finally
            {
                _virtualPeersLock.ExitReadLock();
            }
        }

        public void GoOffline()
        {
            if (_status == BitChatNetworkStatus.Online)
            {
                _virtualPeersLock.EnterReadLock();
                try
                {
                    foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                    {
                        vPeer.Value.Disconnect();
                    }
                }
                finally
                {
                    _virtualPeersLock.ExitReadLock();
                }

                _status = BitChatNetworkStatus.Offline;
            }
        }

        public void GoOnline()
        {
            _status = BitChatNetworkStatus.Online;
        }

        #endregion

        #region properties

        public BitChatNetworkType Type
        { get { return _type; } }

        public ConnectionManager ConnectionManager
        { get { return _connectionManager; } }

        public BinaryID MaskedPeerEmailAddress
        {
            get
            {
                if ((_maskedPeerEmailAddress == null) && (_peerEmailAddress != null))
                    _maskedPeerEmailAddress = GetMaskedEmailAddress(_peerEmailAddress);

                return _maskedPeerEmailAddress;
            }
        }

        public BinaryID HashedPeerEmailAddress
        { get { return _hashedPeerEmailAddress; } }

        public string NetworkName
        {
            get
            {
                if (_type == BitChatNetworkType.PrivateChat)
                {
                    if (_peerEmailAddress == null)
                        return null;
                    else
                        return _peerEmailAddress.Address;
                }
                else
                {
                    return _networkName;
                }
            }
        }

        public string NetworkDisplayName
        {
            get
            {
                if (_type == BitChatNetworkType.PrivateChat)
                {
                    if (_peerEmailAddress == null)
                        return _invitationSender;
                    else if (_peerName == null)
                        return _peerEmailAddress.Address;
                    else
                        return _peerName;
                }
                else
                {
                    return _networkName;
                }
            }
        }

        public string NetworkDisplayTitle
        {
            get
            {
                if (_type == BitChatNetworkType.PrivateChat)
                {
                    if (_peerEmailAddress == null)
                        return _invitationSender;
                    else if (_peerName == null)
                        return _peerEmailAddress.Address;
                    else
                        return _peerName + " <" + _peerEmailAddress.Address + ">";
                }
                else
                {
                    return _networkName;
                }
            }
        }

        public string SharedSecret
        {
            get { return _sharedSecret; }
            set
            {
                BinaryID newNetworkID;

                switch (_type)
                {
                    case BitChatNetworkType.PrivateChat:
                        if (_peerEmailAddress == null)
                            throw new BitChatException("Cannot change shared secret for this Bit Chat network.");

                        newNetworkID = GetNetworkID(_connectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress, _peerEmailAddress, value);
                        break;

                    default:
                        newNetworkID = GetNetworkID(_networkName, value);
                        break;
                }

                try
                {
                    NetworkChanged(this, newNetworkID);
                    _sharedSecret = value;
                    _networkID = newNetworkID;
                    _networkSecret = null;
                }
                catch (ArgumentException)
                {
                    throw new BitChatException("Unable to change shared secret/password. Bit Chat network with same Id already exists.");
                }
            }
        }

        public BinaryID NetworkID
        { get { return _networkID; } }

        public BitChatNetworkStatus Status
        { get { return _status; } }

        public string InvitationSender
        { get { return _invitationSender; } }

        public string InvitationMessage
        { get { return _invitationMessage; } }

        #endregion

        public class VirtualPeer : IDisposable
        {
            #region events

            public event VirtualPeerStateChanged StateChanged;
            public event VirtualPeerMessageReceived MessageReceived;

            #endregion

            #region variables

            Certificate _peerCert;
            readonly BitChatNetwork _network;

            bool _isOnline = false;

            readonly List<VirtualSession> _sessions = new List<VirtualSession>(1);
            readonly ReaderWriterLockSlim _sessionsLock = new ReaderWriterLockSlim();

            #endregion

            #region constructor

            internal VirtualPeer(Certificate peerCert, BitChatNetwork network)
            {
                _peerCert = peerCert;
                _network = network;
            }

            #endregion

            #region IDisposable

            ~VirtualPeer()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            bool _isDisposing = false;
            bool _disposed = false;

            protected void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    _isDisposing = true;

                    _sessionsLock.EnterWriteLock();
                    try
                    {
                        foreach (VirtualSession session in _sessions)
                            session.Dispose();

                        _sessions.Clear();
                    }
                    finally
                    {
                        _sessionsLock.ExitWriteLock();
                    }

                    _sessionsLock.Dispose();

                    _disposed = true;
                }
            }

            #endregion

            #region public

            public void WriteMessage(byte[] data, int offset, int count)
            {
                if (count > MAX_MESSAGE_SIZE)
                    throw new IOException("BitChatNetwork message data size cannot exceed " + MAX_MESSAGE_SIZE + " bytes.");

                _sessionsLock.EnterReadLock();
                try
                {
                    foreach (VirtualSession session in _sessions)
                    {
                        session.WriteMessage(data, offset, count);
                    }
                }
                finally
                {
                    _sessionsLock.ExitReadLock();
                }
            }

            public void AddSession(SecureChannelStream channel)
            {
                if (!_peerCert.IssuedTo.EmailAddress.Address.Equals(channel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, StringComparison.CurrentCultureIgnoreCase))
                    throw new BitChatException("Secure stream certificate email address doesn't match with existing peer email address.");

                VirtualSession peerSession;

                _sessionsLock.EnterWriteLock();
                try
                {
                    if (_sessions.Count > 0)
                    {
                        if (!_peerCert.Equals(channel.RemotePeerCertificate))
                            throw new BitChatException("Secure stream certificates doesn't match with existing peer secure stream certificate.");
                    }
                    else
                    {
                        if (!_peerCert.Equals(channel.RemotePeerCertificate))
                            _network.VirtualPeerHasChangedCertificate(_network, channel.RemotePeerCertificate);

                        _peerCert = channel.RemotePeerCertificate;
                    }

                    peerSession = new VirtualSession(this, channel);
                    _sessions.Add(peerSession);
                }
                finally
                {
                    _sessionsLock.ExitWriteLock();
                }

                _isOnline = true;
                StateChanged(peerSession);
            }

            public bool isConnectedVia(IPEndPoint peerEP)
            {
                _sessionsLock.EnterReadLock();
                try
                {
                    foreach (VirtualSession session in _sessions)
                    {
                        if (session.RemotePeerEP.Equals(peerEP))
                            return true;
                    }
                }
                finally
                {
                    _sessionsLock.ExitReadLock();
                }

                return false;
            }

            public PeerInfo GetPeerInfo()
            {
                List<IPEndPoint> peerEPList = new List<IPEndPoint>();

                _sessionsLock.EnterReadLock();
                try
                {
                    foreach (VirtualSession session in _sessions)
                    {
                        peerEPList.Add(session.RemotePeerEP);
                    }
                }
                finally
                {
                    _sessionsLock.ExitReadLock();
                }

                return new PeerInfo(_peerCert.IssuedTo.EmailAddress.Address, peerEPList);
            }

            public void Disconnect()
            {
                _sessionsLock.EnterReadLock();
                try
                {
                    foreach (VirtualSession session in _sessions)
                    {
                        session.Disconnect();
                    }
                }
                finally
                {
                    _sessionsLock.ExitReadLock();
                }
            }

            #endregion

            #region properties

            public Certificate PeerCertificate
            { get { return _peerCert; } }

            public bool IsOnline
            { get { return _isOnline; } }

            public SecureChannelCryptoOptionFlags CipherSuite
            {
                get
                {
                    _sessionsLock.EnterReadLock();
                    try
                    {
                        if (_sessions.Count > 0)
                            return _sessions[0].CipherSuite;
                        else
                            return SecureChannelCryptoOptionFlags.None;
                    }
                    finally
                    {
                        _sessionsLock.ExitReadLock();
                    }
                }
            }

            #endregion

            public class VirtualSession : IDisposable
            {
                #region variables

                readonly VirtualPeer _peer;
                readonly SecureChannelStream _channel;

                readonly Thread _readThread;

                readonly Dictionary<ushort, DataStream> _dataStreams = new Dictionary<ushort, DataStream>();
                ushort _lastPort = 0;

                #endregion

                #region constructor

                public VirtualSession(VirtualPeer peer, SecureChannelStream channel)
                {
                    _peer = peer;
                    _channel = channel;

                    //client will use odd port & server will use even port to avoid conflicts
                    if (_channel is SecureChannelClientStream)
                        _lastPort = 1;

                    //start read thread
                    _readThread = new Thread(ReadMessageAsync);
                    _readThread.IsBackground = true;
                    _readThread.Start();
                }

                #endregion

                #region IDisposable

                ~VirtualSession()
                {
                    Dispose(false);
                }

                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                bool _isDisposing = false;
                bool _disposed = false;

                protected void Dispose(bool disposing)
                {
                    lock (this)
                    {
                        if (!_disposed)
                        {
                            _isDisposing = true;

                            //close all data streams
                            lock (_dataStreams)
                            {
                                foreach (KeyValuePair<ushort, DataStream> dataStream in _dataStreams)
                                    dataStream.Value.Dispose();

                                _dataStreams.Clear();
                            }

                            //close base secure channel
                            try
                            {
                                _channel.Dispose();
                            }
                            catch
                            { }

                            if (!_peer._isDisposing)
                            {
                                //remove this session from peer
                                _peer._sessionsLock.EnterWriteLock();
                                try
                                {
                                    _peer._sessions.Remove(this);
                                    _peer._isOnline = (_peer._sessions.Count > 0);
                                }
                                finally
                                {
                                    _peer._sessionsLock.ExitWriteLock();
                                }

                                _peer.StateChanged(this);
                            }

                            _disposed = true;
                        }
                    }
                }

                #endregion

                #region private

                private void WriteDataPacket(ushort port, byte[] data, int offset, int count)
                {
                    Monitor.Enter(_channel);
                    try
                    {
                        _channel.Write(BitConverter.GetBytes(port), 0, 2); //port
                        _channel.Write(BitConverter.GetBytes(Convert.ToUInt16(count)), 0, 2); //data length
                        _channel.Write(data, offset, count);
                        _channel.Flush();
                    }
                    catch (Exception ex)
                    {
                        Debug.Write("VirtualSession.WriteDataPacket", ex);
                    }
                    finally
                    {
                        Monitor.Exit(_channel);
                    }
                }

                private void ReadMessageAsync(object state)
                {
                    bool doReconnect = false;

                    try
                    {
                        FixMemoryStream mS = new FixMemoryStream(BUFFER_SIZE);
                        byte[] buffer = mS.Buffer;
                        ushort port;
                        int dataLength;

                        while (true)
                        {
                            OffsetStream.StreamRead(_channel, buffer, 0, 2);
                            port = BitConverter.ToUInt16(buffer, 0);

                            OffsetStream.StreamRead(_channel, buffer, 0, 2);
                            dataLength = BitConverter.ToUInt16(buffer, 0);

                            OffsetStream.StreamRead(_channel, buffer, 0, dataLength);

                            if (port == 0)
                            {
                                mS.SetLength(dataLength);
                                mS.Position = 0;

                                try
                                {
                                    _peer.MessageReceived(this, mS);
                                }
                                catch
                                { }
                            }
                            else
                            {
                                DataStream stream = null;

                                try
                                {
                                    lock (_dataStreams)
                                    {
                                        stream = _dataStreams[port];
                                    }

                                    stream.WriteBuffer(buffer, 0, dataLength, 30000);
                                }
                                catch
                                {
                                    if (stream != null)
                                        stream.Dispose();
                                }
                            }
                        }
                    }
                    catch (SecureChannelException ex)
                    {
                        _peer._network.VirtualPeerSecureChannelException(_peer._network, ex);
                    }
                    catch (EndOfStreamException)
                    {
                        //gracefull secure channel disconnection done; do nothing
                    }
                    catch
                    {
                        //try reconnection due to unexpected channel closure (mostly read timed out exception)
                        doReconnect = true;
                    }
                    finally
                    {
                        Dispose();

                        if (doReconnect)
                            _peer._network.MakeConnection(_channel.RemotePeerEP);
                    }
                }

                #endregion

                #region public

                public void WriteMessage(byte[] data, int offset, int count)
                {
                    WriteDataPacket(0, data, offset, count);
                }

                public DataStream OpenDataStream(ushort port = 0)
                {
                    lock (_dataStreams)
                    {
                        if (port == 0)
                        {
                            do
                            {
                                _lastPort += 2;

                                if (_lastPort > (ushort.MaxValue - 3))
                                {
                                    if (_channel is SecureChannelClientStream)
                                        _lastPort = 1;
                                    else
                                        _lastPort = 0;

                                    continue;
                                }
                            }
                            while (_dataStreams.ContainsKey(_lastPort));

                            port = _lastPort;
                        }
                        else if (_dataStreams.ContainsKey(port))
                        {
                            throw new ArgumentException("Data port already in use.");
                        }

                        DataStream stream = new DataStream(this, port);
                        _dataStreams.Add(port, stream);

                        return stream;
                    }
                }

                public void Disconnect()
                {
                    try
                    {
                        _channel.Dispose();
                    }
                    catch
                    { }
                }

                #endregion

                #region properties

                public VirtualPeer VirtualPeer
                { get { return _peer; } }

                public SecureChannelCryptoOptionFlags CipherSuite
                { get { return _channel.SelectedCryptoOption; } }

                public IPEndPoint RemotePeerEP
                { get { return _channel.RemotePeerEP; } }

                #endregion

                public class DataStream : Stream
                {
                    #region variables

                    const int DATA_READ_TIMEOUT = 60000;
                    const int DATA_WRITE_TIMEOUT = 30000; //dummy

                    readonly VirtualSession _session;
                    readonly ushort _port;

                    readonly byte[] _buffer = new byte[BUFFER_SIZE];
                    int _offset;
                    int _count;

                    int _readTimeout = DATA_READ_TIMEOUT;
                    int _writeTimeout = DATA_WRITE_TIMEOUT;

                    #endregion

                    #region constructor

                    public DataStream(VirtualSession session, ushort port)
                    {
                        _session = session;
                        _port = port;
                    }

                    #endregion

                    #region IDisposable

                    ~DataStream()
                    {
                        Dispose(false);
                    }

                    bool _disposed = false;

                    protected override void Dispose(bool disposing)
                    {
                        lock (this)
                        {
                            if (!_disposed)
                            {
                                try
                                {
                                    _session.WriteDataPacket(_port, new byte[] { }, 0, 0);
                                }
                                catch
                                { }

                                if (!_session._isDisposing)
                                {
                                    lock (_session._dataStreams)
                                    {
                                        _session._dataStreams.Remove(_port);
                                    }
                                }

                                Monitor.PulseAll(this);

                                _disposed = true;
                            }
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
                        get { return true; }
                    }

                    public override int ReadTimeout
                    {
                        get { return _readTimeout; }
                        set { _readTimeout = value; }
                    }

                    public override int WriteTimeout
                    {
                        get { return _writeTimeout; }
                        set { _writeTimeout = value; }
                    }

                    public override void Flush()
                    {
                        //do nothing
                    }

                    public override long Length
                    {
                        get { throw new NotSupportedException("DataStream stream does not support seeking."); }
                    }

                    public override long Position
                    {
                        get
                        {
                            throw new NotSupportedException("DataStream stream does not support seeking.");
                        }
                        set
                        {
                            throw new NotSupportedException("DataStream stream does not support seeking.");
                        }
                    }

                    public override long Seek(long offset, SeekOrigin origin)
                    {
                        throw new NotSupportedException("DataStream stream does not support seeking.");
                    }

                    public override void SetLength(long value)
                    {
                        throw new NotSupportedException("DataStream stream does not support seeking.");
                    }

                    public override int Read(byte[] buffer, int offset, int count)
                    {
                        if (count < 1)
                            throw new ArgumentOutOfRangeException("Count must be atleast 1 byte.");

                        lock (this)
                        {
                            if (_count < 1)
                            {
                                if (_disposed)
                                    return 0;

                                if (!Monitor.Wait(this, _readTimeout))
                                    throw new IOException("Read timed out.");

                                if (_count < 1)
                                    return 0;
                            }

                            int bytesToCopy = count;

                            if (bytesToCopy > _count)
                                bytesToCopy = _count;

                            Buffer.BlockCopy(_buffer, _offset, buffer, offset, bytesToCopy);

                            _offset += bytesToCopy;
                            _count -= bytesToCopy;

                            if (_count < 1)
                                Monitor.Pulse(this);

                            return bytesToCopy;
                        }
                    }

                    public override void Write(byte[] buffer, int offset, int count)
                    {
                        if (_disposed)
                            throw new ObjectDisposedException("DataStream");

                        _session.WriteDataPacket(_port, buffer, offset, count);
                    }

                    #endregion

                    #region private

                    internal void WriteBuffer(byte[] buffer, int offset, int count, int timeout)
                    {
                        if (count < 1)
                        {
                            Dispose();
                            return;
                        }

                        lock (this)
                        {
                            if (_disposed)
                                throw new ObjectDisposedException("DataStream");

                            if (_count > 0)
                            {
                                if (!Monitor.Wait(this, timeout))
                                    throw new IOException("DataStream WriteBuffer timed out.");

                                if (_count > 0)
                                    throw new IOException("DataStream WriteBuffer failed. Buffer not empty.");
                            }

                            Buffer.BlockCopy(buffer, offset, _buffer, 0, count);
                            _offset = 0;
                            _count = count;

                            Monitor.Pulse(this);
                        }
                    }

                    #endregion

                    #region properties

                    public ushort Port
                    { get { return _port; } }

                    #endregion
                }
            }
        }
    }
}
