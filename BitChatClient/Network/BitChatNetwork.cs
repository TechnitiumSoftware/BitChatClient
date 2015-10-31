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

using BitChatClient.Network.Connections;
using BitChatClient.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient.Network
{
    delegate void VirtualPeerAdded(BitChatNetwork sender, BitChatNetwork.VirtualPeer virtualPeer);
    delegate void VirtualPeerHasRevokedCertificate(BitChatNetwork sender, InvalidCertificateException ex);
    delegate void VirtualPeerPacketReceived(BitChatNetwork.VirtualPeer sender, Stream packetDataStream, IPEndPoint remotePeerEP);
    delegate void VirtualPeerSecureChannelException(BitChatNetwork sender, SecureChannelException ex);

    public enum BitChatNetworkType : byte
    {
        PrivateChat = 1,
        GroupChat = 2
    }

    class BitChatNetwork : IDisposable
    {
        #region events

        public event VirtualPeerAdded VirtualPeerAdded;
        public event VirtualPeerHasRevokedCertificate VirtualPeerHasRevokedCertificate;
        public event VirtualPeerSecureChannelException VirtualPeerSecureChannelException;

        #endregion

        #region variables

        BitChatNetworkType _type;
        MailAddress _peerEmailAddress;
        string _networkName;
        string _sharedSecret;

        string _peerName;
        BinaryID _networkID;

        IBitChatNetworkManager _networkManager;
        ISecureChannelSecurityManager _securityManager;
        VirtualPeer _selfPeer;
        Dictionary<string, VirtualPeer> _virtualPeers = new Dictionary<string, VirtualPeer>();

        #endregion

        #region constructor

        public BitChatNetwork(MailAddress peerEmailAddress, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, IBitChatNetworkManager networkManager, ISecureChannelSecurityManager securityManager)
        {
            _type = BitChatNetworkType.PrivateChat;
            _peerEmailAddress = peerEmailAddress;
            _sharedSecret = sharedSecret;
            _networkManager = networkManager;
            _securityManager = securityManager;

            LoadPeers(knownPeerCerts);

            if (knownPeerCerts.Length > 0)
                _peerName = knownPeerCerts[0].IssuedTo.Name;

            if (networkID == null)
            {
                //compute network id
                HashAlgorithm hash = HashAlgorithm.Create("SHA1");

                byte[] peerEmailAddressHash = hash.ComputeHash(Encoding.UTF8.GetBytes(_peerEmailAddress.Address.ToLower()));
                byte[] selfEmailAddressHash = hash.ComputeHash(Encoding.UTF8.GetBytes(networkManager.GetLocalCredentials().Certificate.IssuedTo.EmailAddress.Address.ToLower()));
                byte[] salt = new byte[20];

                for (int i = 0; i < 20; i++)
                {
                    salt[i] = (byte)(peerEmailAddressHash[i] ^ selfEmailAddressHash[i]);
                }

                _networkID = new BinaryID(PBKDF2.CreateHMACSHA1(_sharedSecret, salt, 200000).GetBytes(20));
            }
            else
            {
                _networkID = networkID;
            }
        }

        public BitChatNetwork(string networkName, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, IBitChatNetworkManager networkManager, ISecureChannelSecurityManager securityManager)
        {
            _type = BitChatNetworkType.GroupChat;
            _networkName = networkName;
            _sharedSecret = sharedSecret;
            _networkManager = networkManager;
            _securityManager = securityManager;

            LoadPeers(knownPeerCerts);

            if (networkID == null)
            {
                //compute network id
                _networkID = new BinaryID(PBKDF2.CreateHMACSHA1(_sharedSecret, Encoding.UTF8.GetBytes(_networkName.ToLower()), 200000).GetBytes(20));
            }
            else
            {
                _networkID = networkID;
            }
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
                }
            }
        }

        #endregion

        #region private

        private void LoadPeers(Certificate[] knownPeerCerts)
        {
            //load self as virtual peer
            Certificate selfCert = _networkManager.GetLocalCredentials().Certificate;
            _selfPeer = new VirtualPeer(selfCert, this);
            _virtualPeers.Add(selfCert.IssuedTo.EmailAddress.Address.ToLower(), _selfPeer);

            //load known peers
            foreach (Certificate knownPeerCert in knownPeerCerts)
                _virtualPeers.Add(knownPeerCert.IssuedTo.EmailAddress.Address.ToLower(), new VirtualPeer(knownPeerCert, this));
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

        private void MakeVirtualConnection(object state)
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
                SecureChannelStream secureChannel = new SecureChannelClientStream(channel, connection.RemotePeerEP, _networkManager.GetLocalCredentials(), _networkManager.GetTrustedRootCertificates(), _securityManager, _networkManager.GetSupportedCryptoOptions(), _networkManager.GetReNegotiateOnBytesSent(), _networkManager.GetReNegotiateAfterSeconds(), _sharedSecret);

                //join network
                JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel, _networkManager.CheckCertificateRevocationList());
            }
            catch (SecureChannelException ex)
            {
                if (VirtualPeerSecureChannelException != null)
                    VirtualPeerSecureChannelException(this, ex);

                channel.Dispose();
            }
            catch
            {
                channel.Dispose();
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

                stream.RemotePeerCertificate.VerifyRevocationList(_networkManager.GetSocksProxy());
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

        public void MakeConnection(IPEndPoint peerEP)
        {
            if (!IsPeerConnected(peerEP))
                ThreadPool.QueueUserWorkItem(new WaitCallback(MakeConnectionAsync), peerEP);
        }

        public void MakeConnection(IEnumerable<IPEndPoint> peerEPs)
        {
            foreach (IPEndPoint peerEP in peerEPs)
            {
                if (!IsPeerConnected(peerEP))
                    ThreadPool.QueueUserWorkItem(new WaitCallback(MakeConnectionAsync), peerEP);
            }
        }

        public void MakeConnection(IEnumerable<PeerInfo> peerList)
        {
            foreach (PeerInfo peerInfo in peerList)
            {
                foreach (IPEndPoint peerEP in peerInfo.PeerEPList)
                {
                    if (!IsPeerConnected(peerEP))
                        ThreadPool.QueueUserWorkItem(new WaitCallback(MakeConnectionAsync), peerEP);
                }
            }
        }

        public void MakeConnection(Connection viaConnection, IEnumerable<IPEndPoint> peerEPs)
        {
            foreach (IPEndPoint peerEP in peerEPs)
            {
                if (!IsPeerConnected(peerEP))
                    ThreadPool.QueueUserWorkItem(new WaitCallback(MakeVirtualConnection), new object[] { viaConnection, peerEP });
            }
        }

        public void JoinNetwork(string peerID, SecureChannelStream peerStream, bool checkCertificateRevocationList)
        {
            if (_type == BitChatNetworkType.PrivateChat)
            {
                if (!peerStream.RemotePeerCertificate.IssuedTo.EmailAddress.Equals(_peerEmailAddress))
                    throw new BitChatException("User with another email address '" + peerStream.RemotePeerCertificate.IssuedTo.EmailAddress.Address + " [" + peerStream.RemotePeerEP.Address.ToString() + "]' trying to join private chat.");

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

        public bool RemovePeer(string peerID)
        {
            lock (_virtualPeers)
            {
                _virtualPeers[peerID].Dispose();
                return _virtualPeers.Remove(peerID);
            }
        }

        public void WritePacketTo(string peerID, byte[] data, int offset, int count)
        {
            lock (_virtualPeers)
            {
                _virtualPeers[peerID].WritePacket(data, offset, count);
            }
        }

        public void WritePacketBroadcast(byte[] data, int offset, int count)
        {
            lock (_virtualPeers)
            {
                foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                {
                    try
                    {
                        if (vPeer.Value.IsOnline)
                            vPeer.Value.WritePacket(data, offset, count);
                    }
                    catch
                    { }
                }
            }
        }

        public void WritePacketExcept(string peerID, byte[] data, int offset, int count)
        {
            lock (_virtualPeers)
            {
                foreach (KeyValuePair<string, VirtualPeer> vPeer in _virtualPeers)
                {
                    if (vPeer.Key != peerID)
                        vPeer.Value.WritePacket(data, offset, count);
                }
            }
        }

        public void RemoveNetwork()
        {
            _networkManager.RemoveNetwork(this);
        }

        #endregion

        #region properties

        public BitChatNetworkType Type
        { get { return _type; } }

        public MailAddress PeerEmailAddress
        { get { return _peerEmailAddress; } }

        public string NetworkName
        {
            get
            {
                if (_type == BitChatNetworkType.PrivateChat)
                {
                    return _peerName;
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

        #endregion

        public class VirtualPeer : IDisposable
        {
            #region events

            public event EventHandler StreamStateChanged;
            public event VirtualPeerPacketReceived PacketReceived;

            #endregion

            #region variables

            const int MAX_BUFFER_SIZE = 65532;

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

                    _disposed = true;
                }
            }

            #endregion

            #region private

            private void ReadPacketAsync(object state)
            {
                SecureChannelStream stream = state as SecureChannelStream;

                try
                {
                    FixMemoryStream mS = new FixMemoryStream(MAX_BUFFER_SIZE);
                    byte[] buffer = mS.Buffer;
                    int dataLength;

                    while (true)
                    {
                        OffsetStream.StreamRead(stream, buffer, 0, 2);
                        dataLength = BitConverter.ToUInt16(buffer, 0) + 1;

                        mS.SetLength(dataLength);
                        mS.Position = 0;

                        OffsetStream.StreamRead(stream, buffer, 0, dataLength);

                        try
                        {
                            PacketReceived(this, mS, stream.RemotePeerEP);
                        }
                        catch
                        { }
                    }
                }
                catch (SecureChannelException ex)
                {
                    if (_network.VirtualPeerSecureChannelException != null)
                        _network.VirtualPeerSecureChannelException(_network, ex);
                }
                catch (Exception ex)
                {
                    Debug.Write("VirtualPeer.ReadPacketAsync", ex);
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

                    stream.Dispose();
                }
            }

            #endregion

            #region public

            public void WritePacket(byte[] data, int offset, int count)
            {
                if (count > MAX_BUFFER_SIZE)
                    throw new IOException("BitChatNetwork packet data size cannot exceed 65532 bytes.");

                byte[] len = BitConverter.GetBytes(Convert.ToUInt16(count - 1));

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
                        catch (Exception ex)
                        {
                            Debug.Write("VirtualPeer.WritePacket", ex);
                        }
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
                        _peerCert = stream.RemotePeerCertificate;
                    }

                    _streamList.Add(stream);
                }

                lock (_readThreadList)
                {
                    Thread readThread = new Thread(ReadPacketAsync);
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

            #region properties

            public Certificate PeerCertificate
            { get { return _peerCert; } }

            public bool IsOnline
            { get { return _isOnline; } }

            #endregion
        }
    }
}
