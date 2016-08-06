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

using BitChatClient.Network.Connections;
using BitChatClient.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient.Network
{
    delegate void VirtualPeerAdded(BitChatNetwork sender, BitChatNetwork.VirtualPeer virtualPeer);
    delegate void VirtualPeerHasRevokedCertificate(BitChatNetwork sender, InvalidCertificateException ex);
    delegate void VirtualPeerMessageReceived(BitChatNetwork.VirtualPeer sender, Stream messageDataStream, IPEndPoint remotePeerEP);
    delegate void VirtualPeerSecureChannelException(BitChatNetwork sender, SecureChannelException ex);
    delegate void VirtualPeerHasChangedCertificate(BitChatNetwork sender, Certificate cert);

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

        public event VirtualPeerAdded VirtualPeerAdded;
        public event VirtualPeerHasRevokedCertificate VirtualPeerHasRevokedCertificate;
        public event VirtualPeerSecureChannelException VirtualPeerSecureChannelException;
        public event VirtualPeerHasChangedCertificate VirtualPeerHasChangedCertificate;
        public event EventHandler Disposed;

        #endregion

        #region variables

        public const int MAX_MESSAGE_SIZE = 65220; //max payload size of secure channel packet
        const int BUFFER_SIZE = 65535;

        const int RE_NEGOTIATE_AFTER_BYTES_SENT = 104857600; //100mb
        const int RE_NEGOTIATE_AFTER_SECONDS = 3600; //1hr

        static readonly byte[] EMAIL_ADDRESS_HASH_SALT = new byte[] { 0x49, 0x42, 0x0C, 0x52, 0xC9, 0x3C, 0x5E, 0xB6, 0xAD, 0x83, 0x3F, 0x08, 0xBA, 0xD9, 0xB9, 0x6E, 0x23, 0x8F, 0xDC, 0xF3 };
        static readonly byte[] EMAIL_ADDRESS_MASK_SALT = new byte[] { 0x55, 0xBB, 0xB8, 0x5C, 0x21, 0xB3, 0xC3, 0x34, 0xBE, 0xF4, 0x4D, 0x9D, 0xD0, 0xAC, 0x8E, 0x8A, 0xE8, 0xED, 0x28, 0x3E };

        BitChatNetworkType _type;
        MailAddress _peerEmailAddress;
        string _networkName;
        string _sharedSecret;
        BinaryID _networkID;
        IBitChatNetworkManager _networkManager;
        ISecureChannelSecurityManager _securityManager;
        BitChatNetworkStatus _status;
        string _invitationSender;
        string _invitationMessage;

        string _peerName;
        BinaryID _maskedPeerEmailAddress;

        VirtualPeer _selfPeer;
        Dictionary<string, VirtualPeer> _virtualPeers = new Dictionary<string, VirtualPeer>();

        #endregion

        #region constructor

        public BitChatNetwork(MailAddress peerEmailAddress, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, IBitChatNetworkManager networkManager, ISecureChannelSecurityManager securityManager, BitChatNetworkStatus status, string invitationSender, string invitationMessage)
        {
            _type = BitChatNetworkType.PrivateChat;
            _peerEmailAddress = peerEmailAddress;
            _sharedSecret = sharedSecret;
            _networkID = networkID;
            _networkManager = networkManager;
            _securityManager = securityManager;
            _status = status;
            _invitationSender = invitationSender;
            _invitationMessage = invitationMessage;

            if (_networkID == null)
                _networkID = GetNetworkID(networkManager.GetLocalCredentials().Certificate.IssuedTo.EmailAddress, _peerEmailAddress, _sharedSecret);

            if (knownPeerCerts.Length > 0)
                _peerName = knownPeerCerts[0].IssuedTo.Name;

            LoadKnownPeers(knownPeerCerts);
        }

        public BitChatNetwork(string networkName, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, IBitChatNetworkManager networkManager, ISecureChannelSecurityManager securityManager, BitChatNetworkStatus status)
        {
            _type = BitChatNetworkType.GroupChat;
            _networkName = networkName;
            _sharedSecret = sharedSecret;
            _networkID = networkID;
            _networkManager = networkManager;
            _securityManager = securityManager;
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
                    lock (_virtualPeers)
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

                    _disposed = true;

                    Disposed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        #endregion

        #region static

        private static BinaryID GetNetworkID(string networkName, string sharedSecret)
        {
            return new BinaryID(PBKDF2.CreateHMACSHA256((sharedSecret == null ? "" : sharedSecret), Encoding.UTF8.GetBytes(networkName.ToLower()), 10000).GetBytes(20));
        }

        private static BinaryID GetNetworkID(MailAddress emailAddress1, MailAddress emailAddress2, string sharedSecret)
        {
            byte[] hashedEmailAddress1 = GetHashedEmailAddress(emailAddress1);
            byte[] hashedEmailAddress2 = GetHashedEmailAddress(emailAddress2);
            byte[] salt = new byte[20];

            for (int i = 0; i < 20; i++)
            {
                salt[i] = (byte)(hashedEmailAddress1[i] ^ hashedEmailAddress2[i]);
            }

            return new BinaryID(PBKDF2.CreateHMACSHA256((sharedSecret == null ? "" : sharedSecret), salt, 10000).GetBytes(20));
        }

        private static byte[] GetHashedEmailAddress(MailAddress emailAddress)
        {
            using (PBKDF2 hash = PBKDF2.CreateHMACSHA256(emailAddress.Address.ToLower(), EMAIL_ADDRESS_HASH_SALT, 10000))
            {
                return hash.GetBytes(20);
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

        private void LoadKnownPeers(Certificate[] knownPeerCerts)
        {
            //load self as virtual peer
            Certificate selfCert = _networkManager.GetLocalCredentials().Certificate;
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
                Connection connection = _networkManager.MakeConnection(peerEP);

                connection.SendBitChatNetworkInvitation(_networkID, _invitationMessage);
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
                Connection connection = _networkManager.MakeConnection(peerEP);

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
                Connection virtualConnection = _networkManager.MakeVirtualConnection(viaConnection, peerEP);

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
                SecureChannelStream secureChannel = new SecureChannelClientStream(channel, connection.RemotePeerEP, _networkManager.GetLocalCredentials(), _networkManager.GetTrustedRootCertificates(), _securityManager, _networkManager.GetSupportedCryptoOptions(), RE_NEGOTIATE_AFTER_BYTES_SENT, RE_NEGOTIATE_AFTER_SECONDS, _sharedSecret);

                //join network
                JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel, _networkManager.CheckCertificateRevocationList());
            }
            catch (SecureChannelException ex)
            {
                if (ex.Code != SecureChannelCode.EndOfStream)
                    VirtualPeerSecureChannelException?.Invoke(this, ex);

                channel.Dispose();
            }
            catch
            {
                channel.Dispose();
            }
        }

        private void JoinNetwork(string peerID, SecureChannelStream peerStream, bool checkCertificateRevocationList)
        {
            if (_status == BitChatNetworkStatus.Offline)
                throw new BitChatException("BitChat network is offline.");

            if (_type == BitChatNetworkType.PrivateChat)
            {
                if (_peerEmailAddress == null)
                {
                    BinaryID computedNetworkID = GetNetworkID(_networkManager.GetLocalCredentials().Certificate.IssuedTo.EmailAddress, peerStream.RemotePeerCertificate.IssuedTo.EmailAddress, _sharedSecret);

                    if (!computedNetworkID.Equals(_networkID))
                        throw new BitChatException("User with email address '" + peerStream.RemotePeerCertificate.IssuedTo.EmailAddress.Address + " [" + peerStream.RemotePeerEP.Address.ToString() + "]' is trying to join this private chat.");

                    _peerEmailAddress = peerStream.RemotePeerCertificate.IssuedTo.EmailAddress;
                }
                else
                {
                    if (!peerStream.RemotePeerCertificate.IssuedTo.EmailAddress.Equals(_peerEmailAddress))
                        throw new BitChatException("User with email address '" + peerStream.RemotePeerCertificate.IssuedTo.EmailAddress.Address + " [" + peerStream.RemotePeerEP.Address.ToString() + "]' is trying to join this private chat.");
                }

                _peerName = peerStream.RemotePeerCertificate.IssuedTo.Name;
            }

            peerID = peerID.ToLower();

            VirtualPeer vPeer;
            bool peerAdded = false;

            lock (_virtualPeers)
            {
                if (_virtualPeers.ContainsKey(peerID))
                {
                    vPeer = _virtualPeers[peerID];
                }
                else
                {
                    vPeer = new VirtualPeer(peerStream.RemotePeerCertificate, this);
                    _virtualPeers.Add(peerID, vPeer);

                    peerAdded = true;
                }
            }

            if (peerAdded)
                VirtualPeerAdded(this, vPeer);

            vPeer.AddStream(peerStream);

            if (checkCertificateRevocationList)
            {
                //start async revocation list check process
                ThreadPool.QueueUserWorkItem(CheckCertificateRevocationListAsync, peerStream);
            }
        }

        private bool IsPeerConnected(IPEndPoint peerEP)
        {
            //check if peer already connected
            lock (_virtualPeers)
            {
                foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                {
                    if (vPeer.Value.IsOnline && vPeer.Value.isConnectedVia(peerEP))
                        return true;
                }
            }

            return false;
        }

        private void CheckCertificateRevocationListAsync(object state)
        {
            SecureChannelStream stream = null;

            try
            {
                stream = state as SecureChannelStream;

                stream.RemotePeerCertificate.VerifyRevocationList(_networkManager.GetProxy());
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
            List<VirtualPeer> peerList = new List<VirtualPeer>(_virtualPeers.Count);

            lock (_virtualPeers)
            {
                foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                    peerList.Add(vPeer.Value);
            }

            return peerList.ToArray();
        }

        public void SendInvitation(IEnumerable<IPEndPoint> peerEPs)
        {
            if (_status == BitChatNetworkStatus.Offline)
                throw new BitChatException("Bit Chat network is offline.");

            foreach (IPEndPoint peerEP in peerEPs)
            {
                if (!IsPeerConnected(peerEP))
                    ThreadPool.QueueUserWorkItem(SendInvitationAsync, peerEP);
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
                SecureChannelStream secureChannel = new SecureChannelServerStream(channel, connection.RemotePeerEP, _networkManager.GetLocalCredentials(), _networkManager.GetTrustedRootCertificates(), _securityManager, _networkManager.GetSupportedCryptoOptions(), RE_NEGOTIATE_AFTER_BYTES_SENT, RE_NEGOTIATE_AFTER_SECONDS, _sharedSecret);

                //join network
                JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel, _networkManager.CheckCertificateRevocationList());
            }
            catch (SecureChannelException ex)
            {
                if (ex.Code != SecureChannelCode.EndOfStream)
                    VirtualPeerSecureChannelException?.Invoke(this, ex);

                channel.Dispose();
            }
            catch
            {
                channel.Dispose();
            }
        }

        public PeerInfo GetSelfPeerInfo()
        {
            return _selfPeer.GetPeerInfo();
        }

        public List<PeerInfo> GetConnectedPeerList()
        {
            List<PeerInfo> connectedPeers = new List<PeerInfo>();

            lock (_virtualPeers)
            {
                foreach (KeyValuePair<string, VirtualPeer> item in _virtualPeers)
                {
                    VirtualPeer vPeer = item.Value;

                    if (vPeer.IsOnline)
                        connectedPeers.Add(vPeer.GetPeerInfo());
                }
            }

            return connectedPeers;
        }

        public void WriteMessageBroadcast(byte[] data, int offset, int count)
        {
            lock (_virtualPeers)
            {
                foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                {
                    try
                    {
                        if (vPeer.Value.IsOnline)
                            vPeer.Value.WriteMessage(data, offset, count);
                    }
                    catch
                    { }
                }
            }
        }

        #endregion

        #region properties

        public BitChatNetworkType Type
        { get { return _type; } }

        public BinaryID MaskedPeerEmailAddress
        {
            get
            {
                if ((_maskedPeerEmailAddress == null) && (_peerEmailAddress != null))
                    _maskedPeerEmailAddress = GetMaskedEmailAddress(_peerEmailAddress);

                return _maskedPeerEmailAddress;
            }
        }

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
        { get { return _sharedSecret; } }

        public BinaryID NetworkID
        { get { return _networkID; } }

        public BitChatNetworkStatus Status
        {
            get { return _status; }
            set
            {
                _status = value;

                if (value == BitChatNetworkStatus.Offline)
                {
                    lock (_virtualPeers)
                    {
                        foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                        {
                            vPeer.Value.Disconnect();
                        }
                    }
                }
            }
        }

        public string InvitationSender
        { get { return _invitationSender; } }

        public string InvitationMessage
        { get { return _invitationMessage; } }

        #endregion

        public class VirtualPeer : IDisposable
        {
            #region events

            public event EventHandler StreamStateChanged;
            public event VirtualPeerMessageReceived MessageReceived;

            #endregion

            #region variables

            Certificate _peerCert;
            BitChatNetwork _network;
            bool _isOnline;

            List<SecureChannelStream> _streamList = new List<SecureChannelStream>();
            List<Thread> _readThreadList = new List<Thread>();

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

            bool _disposing = false;
            bool _disposed = false;

            protected void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    _disposing = true;

                    Disconnect();

                    _disposed = true;
                }
            }

            #endregion

            #region private

            private void ReadMessageAsync(object state)
            {
                SecureChannelStream stream = state as SecureChannelStream;
                bool doReconnect = false;

                try
                {
                    FixMemoryStream mS = new FixMemoryStream(BUFFER_SIZE);
                    byte[] buffer = mS.Buffer;
                    int dataLength;

                    while (true)
                    {
                        OffsetStream.StreamRead(stream, buffer, 0, 2);
                        dataLength = BitConverter.ToUInt16(buffer, 0);

                        mS.SetLength(dataLength);
                        mS.Position = 0;

                        OffsetStream.StreamRead(stream, buffer, 0, dataLength);

                        try
                        {
                            MessageReceived(this, mS, stream.RemotePeerEP);
                        }
                        catch
                        { }
                    }
                }
                catch (SecureChannelException ex)
                {
                    if (ex.Code == SecureChannelCode.EndOfStream)
                    {
                        //gracefull secure channel disconnection done; do nothing
                    }
                    else
                    {
                        if (_network.VirtualPeerSecureChannelException != null)
                            _network.VirtualPeerSecureChannelException(_network, ex);
                    }
                }
                catch (ThreadAbortException)
                {
                    //thread abort via Dispose()
                }
                catch
                {
                    //try reconnection due to unexpected channel closure
                    doReconnect = true;
                }
                finally
                {
                    int totalStreamsAvailable;

                    lock (_streamList)
                    {
                        _streamList.Remove(stream);
                        totalStreamsAvailable = _streamList.Count;
                    }

                    lock (_readThreadList)
                    {
                        _readThreadList.Remove(Thread.CurrentThread);
                    }

                    if (!_disposing)
                    {
                        lock (this)
                        {
                            if (totalStreamsAvailable == 0)
                                _isOnline = false;

                            if (StreamStateChanged != null)
                                StreamStateChanged(this, EventArgs.Empty);
                        }
                    }

                    try
                    {
                        stream.Dispose();
                    }
                    catch
                    { }

                    if (doReconnect)
                        _network.MakeConnection(stream.RemotePeerEP);
                }
            }

            #endregion

            #region public

            public void WriteMessage(byte[] data, int offset, int count)
            {
                if (count > (MAX_MESSAGE_SIZE - 2))
                    throw new IOException("BitChatNetwork message data size cannot exceed " + MAX_MESSAGE_SIZE + " bytes.");

                byte[] len = BitConverter.GetBytes(Convert.ToUInt16(count));

                lock (_streamList)
                {
                    foreach (SecureChannelStream stream in _streamList)
                    {
                        try
                        {
                            stream.Write(len, 0, 2);
                            stream.Write(data, offset, count);
                            stream.Flush();
                        }
                        catch
                        { }
                    }
                }
            }

            public void AddStream(SecureChannelStream stream)
            {
                if (!_peerCert.IssuedTo.EmailAddress.Address.Equals(stream.RemotePeerCertificate.IssuedTo.EmailAddress.Address, StringComparison.CurrentCultureIgnoreCase))
                    throw new BitChatException("Secure stream certificate email address doesn't match with existing peer email address.");

                lock (_streamList)
                {
                    if (_streamList.Count > 0)
                    {
                        if (!_peerCert.Equals(stream.RemotePeerCertificate))
                            throw new BitChatException("Secure stream certificates doesn't match with existing peer secure stream certificate.");
                    }
                    else
                    {
                        if (!_peerCert.Equals(stream.RemotePeerCertificate))
                        {
                            if (_network.VirtualPeerHasChangedCertificate != null)
                                _network.VirtualPeerHasChangedCertificate(_network, stream.RemotePeerCertificate);
                        }

                        _peerCert = stream.RemotePeerCertificate;
                    }

                    _streamList.Add(stream);
                }

                lock (_readThreadList)
                {
                    Thread readThread = new Thread(ReadMessageAsync);
                    readThread.IsBackground = true;

                    _readThreadList.Add(readThread);

                    readThread.Start(stream);
                }

                lock (this)
                {
                    _isOnline = true;

                    if (StreamStateChanged != null)
                        StreamStateChanged(this, EventArgs.Empty);
                }
            }

            public bool isConnectedVia(IPEndPoint peerEP)
            {
                lock (_streamList)
                {
                    foreach (SecureChannelStream stream in _streamList)
                    {
                        if (stream.RemotePeerEP.Equals(peerEP))
                            return true;
                    }
                }

                return false;
            }

            public PeerInfo GetPeerInfo()
            {
                List<IPEndPoint> peerEPList = new List<IPEndPoint>();

                lock (_streamList)
                {
                    foreach (SecureChannelStream stream in _streamList)
                    {
                        peerEPList.Add(stream.RemotePeerEP);
                    }
                }

                return new PeerInfo(_peerCert.IssuedTo.EmailAddress.Address, peerEPList);
            }

            public void Disconnect()
            {
                lock (_readThreadList)
                {
                    foreach (Thread readThread in _readThreadList)
                    {
                        try
                        {
                            readThread.Abort();
                        }
                        catch
                        { }
                    }
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
                    lock (_streamList)
                    {
                        if (_streamList.Count > 0)
                            return _streamList[0].SelectedCryptoOption;
                        else
                            return SecureChannelCryptoOptionFlags.None;
                    }
                }
            }

            #endregion
        }
    }
}
