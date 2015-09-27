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

using BitChatClient.Network;
using BitChatClient.Network.Connections;
using BitChatClient.Network.PeerDiscovery;
using BitChatClient.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient
{
    public delegate void InvalidCertificateEvent(BitChatService sender, InvalidCertificateException e);

    public class BitChatService : IDisposable
    {
        #region variables

        SynchronizationContext _syncCxt = SynchronizationContext.Current;
        InvalidCertificateEvent _invalidCertEventHandler;

        InternalBitChatService _manager;
        List<BitChat> _bitChats = new List<BitChat>();

        #endregion

        #region constructor

        public BitChatService(BitChatProfile profile, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions, InvalidCertificateEvent invalidCertEventHandler)
        {
            //verify root certs
            foreach (Certificate trustedCert in trustedRootCertificates)
                trustedCert.Verify(trustedRootCertificates);

            //verify profile cert
            profile.LocalCertificateStore.Certificate.Verify(trustedRootCertificates);

            _invalidCertEventHandler = invalidCertEventHandler;

            _manager = new InternalBitChatService(this, profile, trustedRootCertificates, supportedCryptoOptions);

            foreach (BitChatProfile.BitChatInfo bitChatInfo in profile.BitChatInfoList)
            {
                if (bitChatInfo.Type == BitChatNetworkType.PrivateChat)
                    _bitChats.Add(_manager.CreateBitChat(new MailAddress(bitChatInfo.NetworkNameOrPeerEmailAddress), bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs));
                else
                    _bitChats.Add(_manager.CreateBitChat(bitChatInfo.NetworkNameOrPeerEmailAddress, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs));
            }

            //check profile cert revocation
            ThreadPool.QueueUserWorkItem(CheckCertificateRevocationAsync, new Certificate[] { profile.LocalCertificateStore.Certificate });

            //check trusted root cert revocation
            ThreadPool.QueueUserWorkItem(CheckCertificateRevocationAsync, trustedRootCertificates);
        }

        #endregion

        #region IDisposable

        ~BitChatService()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool _disposed = false;

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                foreach (BitChat chat in _bitChats)
                    chat.Dispose();

                if (_manager != null)
                    _manager.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region private

        private void CheckCertificateRevocationAsync(object state)
        {
            try
            {
                Certificate[] certificates = state as Certificate[];

                foreach (Certificate cert in certificates)
                {
                    cert.VerifyRevocationList();
                }
            }
            catch (InvalidCertificateException ex)
            {
                this.Dispose();

                _syncCxt.Send(InvalidCertCallBack, ex);
            }
            catch
            { }
        }

        private void InvalidCertCallBack(object state)
        {
            try
            {
                _invalidCertEventHandler(this, state as InvalidCertificateException);
            }
            catch
            { }
        }

        #endregion

        #region public

        public BitChat CreateBitChat(MailAddress peerEmailAddress, string sharedSecret, bool useTrackers)
        {
            Uri[] trackerURIs = null;

            if (!useTrackers)
                trackerURIs = new Uri[] { };

            BitChat bitChat = _manager.CreateBitChat(peerEmailAddress, sharedSecret, null, new Certificate[] { }, new BitChatProfile.SharedFileInfo[] { }, trackerURIs);

            lock (_bitChats)
            {
                _bitChats.Add(bitChat);
            }

            return bitChat;
        }

        public BitChat CreateBitChat(string networkName, string sharedSecret, bool useTrackers)
        {
            Uri[] trackerURIs = null;

            if (!useTrackers)
                trackerURIs = new Uri[] { };

            BitChat bitChat = _manager.CreateBitChat(networkName, sharedSecret, null, new Certificate[] { }, new BitChatProfile.SharedFileInfo[] { }, trackerURIs);

            lock (_bitChats)
            {
                _bitChats.Add(bitChat);
            }

            return bitChat;
        }

        public BitChat[] GetBitChatList()
        {
            lock (_bitChats)
            {
                return _bitChats.ToArray();
            }
        }

        public void UpdateProfile()
        {
            List<BitChatProfile.BitChatInfo> bitChatInfoList = new List<BitChatProfile.BitChatInfo>();

            lock (_bitChats)
            {
                foreach (BitChat chat in _bitChats)
                {
                    bitChatInfoList.Add(chat.GetBitChatInfo());
                }
            }

            _manager.UpdateProfile(bitChatInfoList.ToArray());
        }

        #endregion

        #region properties

        public BitChatProfile Profile
        { get { return _manager.Profile; } }

        public int LocalPort
        { get { return _manager.LocalPort; } }

        public UPnPDeviceStatus UPnPStatus
        { get { return _manager.UPnPStatus; } }

        public IPEndPoint UPnPExternalEP
        { get { return _manager.UPnPExternalEP; } }

        #endregion

        private class InternalBitChatService : IBitChatManager, IBitChatNetworkManager, ISecureChannelSecurityManager, IDisposable
        {
            #region variables

            BitChatService _service;
            BitChatProfile _profile;
            Certificate[] _trustedRootCertificates;
            SecureChannelCryptoOptionFlags _supportedCryptoOptions;

            ConnectionManager _connectionManager;
            LocalPeerDiscoveryIPv4 _localDiscovery;
            Dictionary<BinaryID, BitChatNetwork> _networks = new Dictionary<BinaryID, BitChatNetwork>();

            //proxy connection
            const int PROXY_CONNECTION_MAX_NUMBER = 3;
            const int PROXY_CONNECTION_CHECK_INTERVAL = 30000; //30sec
            TrackerManager _proxyConnectionDiscoveryTracker = null;
            BinaryID _proxyConnectionDiscoveryNetworkID = new BinaryID(new byte[] { 0xfa, 0x20, 0xf3, 0x45, 0xe6, 0xbe, 0x43, 0x68, 0xcb, 0x1e, 0x2a, 0xfb, 0xc0, 0x08, 0x0d, 0x95, 0xf1, 0xd1, 0xe6, 0x5b });
            Dictionary<IPEndPoint, Connection> _proxyConnections = new Dictionary<IPEndPoint, Connection>();
            Timer _proxyConnectionCheckTimer;
            bool _ignoreTrackerPeerDiscovery = false;

            int _reNegotiateOnBytesSent = 104857600; //100mb
            int _reNegotiateAfterSeconds = 3600; //1hr

            #endregion

            #region constructor

            public InternalBitChatService(BitChatService service, BitChatProfile profile, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions)
            {
                _service = service;
                _profile = profile;
                _trustedRootCertificates = trustedRootCertificates;
                _supportedCryptoOptions = supportedCryptoOptions;

                _connectionManager = new ConnectionManager(_profile.LocalPort, BitChatNetworkChannelRequest, ProxyNetworkPeersAvailable);
                _connectionManager.InternetConnectivityStatusChanged += ConnectionManager_InternetConnectivityStatusChanged;

                LocalPeerDiscoveryIPv4.StartListener(41733);
                _localDiscovery = new LocalPeerDiscoveryIPv4(_connectionManager.LocalPort);
                _localDiscovery.PeerDiscovered += _localDiscovery_PeerDiscovered;

                _proxyConnectionDiscoveryTracker = new TrackerManager(_proxyConnectionDiscoveryNetworkID, _connectionManager);
                _proxyConnectionDiscoveryTracker.DiscoveredPeers += ProxyConnectionDiscoveryTracker_DiscoveredPeers;
            }

            #endregion

            #region IDisposable

            ~InternalBitChatService()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            bool _disposed = false;

            private void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (_connectionManager != null)
                        _connectionManager.Dispose();

                    if (_localDiscovery != null)
                        _localDiscovery.Dispose();

                    _disposed = true;
                }
            }

            #endregion

            #region Proxy Connection

            private void ConnectionManager_InternetConnectivityStatusChanged(object sender, EventArgs e)
            {
                IPEndPoint externalEP = _connectionManager.GetExternalEP();

                if (externalEP == null)
                {
                    //no incoming connection possible; setup proxy network
                    _ignoreTrackerPeerDiscovery = false;

                    if (_proxyConnectionCheckTimer == null)
                    {
                        _proxyConnectionCheckTimer = new Timer(ProxyConnectionCheckTimerCallback, null, PROXY_CONNECTION_CHECK_INTERVAL, Timeout.Infinite);
                        _proxyConnectionDiscoveryTracker.LookupOnly = true;

                        if (!_proxyConnectionDiscoveryTracker.IsTrackerRunning)
                            _proxyConnectionDiscoveryTracker.StartTracking(_profile.TrackerURIs);
                    }
                    else
                    {
                        bool forceUpdate;

                        lock (_proxyConnections)
                        {
                            forceUpdate = (_proxyConnections.Count < PROXY_CONNECTION_MAX_NUMBER);
                        }

                        if (forceUpdate)
                            _proxyConnectionDiscoveryTracker.ForceUpdate();
                    }
                }
                else
                {
                    //can receive incoming connection; no need for setting up proxy network;
                    _ignoreTrackerPeerDiscovery = true;

                    if (_proxyConnectionCheckTimer != null)
                    {
                        _proxyConnectionCheckTimer.Dispose();
                        _proxyConnectionCheckTimer = null;

                        BinaryID[] networkIDs = new BinaryID[_networks.Keys.Count];
                        _networks.Keys.CopyTo(networkIDs, 0);

                        lock (_proxyConnections)
                        {
                            foreach (Connection connection in _proxyConnections.Values)
                            {
                                ThreadPool.QueueUserWorkItem(RemoveProxyNetworksFromConnectionAsync, new object[] { connection, networkIDs });
                            }

                            _proxyConnections.Clear();
                        }
                    }

                    //setup tracker to advertise self as proxy host
                    _proxyConnectionDiscoveryTracker.LookupOnly = false;

                    if (!_proxyConnectionDiscoveryTracker.IsTrackerRunning)
                        _proxyConnectionDiscoveryTracker.StartTracking(_profile.TrackerURIs);

                    _proxyConnectionDiscoveryTracker.ForceUpdate();
                }
            }

            private void ProxyConnectionCheckTimerCallback(object state)
            {
                try
                {
                    //if less number of proxy connections, try to find new connections
                    bool forceUpdate;

                    lock (_proxyConnections)
                    {
                        forceUpdate = (_proxyConnections.Count < PROXY_CONNECTION_MAX_NUMBER);
                    }

                    if (forceUpdate)
                        _proxyConnectionDiscoveryTracker.ForceUpdate();

                    //send noop to all connections
                    foreach (Connection connection in _proxyConnections.Values)
                    {
                        connection.SendNOOP();
                    }
                }
                catch
                { }
                finally
                {
                    if (_proxyConnectionCheckTimer != null)
                        _proxyConnectionCheckTimer.Change(PROXY_CONNECTION_CHECK_INTERVAL, Timeout.Infinite);
                }
            }

            private void RemoveProxyNetworksFromConnectionAsync(object state)
            {
                try
                {
                    object[] parameters = state as object[];

                    Connection viaConnection = parameters[0] as Connection;
                    BinaryID[] networkIDs = parameters[1] as BinaryID[];

                    viaConnection.RequestStopProxyNetwork(networkIDs);
                }
                catch
                { }
            }

            private void SetupProxyNetworkOnConnectionAsync(object state)
            {
                try
                {
                    object[] parameters = state as object[];

                    Connection viaConnection = parameters[0] as Connection;
                    BinaryID networkID = parameters[1] as BinaryID;
                    Uri[] trackerURIs = parameters[2] as Uri[];

                    viaConnection.RequestStartProxyNetwork(new BinaryID[] { networkID }, trackerURIs);
                }
                catch
                { }
            }

            private void ProxyConnectionDiscoveryTracker_DiscoveredPeers(TrackerManager sender, List<IPEndPoint> peerEPs)
            {
                if (!_ignoreTrackerPeerDiscovery)
                {
                    foreach (IPEndPoint peerEP in peerEPs)
                    {
                        lock (_proxyConnections)
                        {
                            if (_proxyConnections.Count >= PROXY_CONNECTION_MAX_NUMBER)
                                return;
                        }

                        ThreadPool.QueueUserWorkItem(SetupProxyConnectionAsync, peerEP);
                    }
                }
            }

            private void SetupProxyConnectionAsync(object state)
            {
                IPEndPoint proxyPeerEP = state as IPEndPoint;

                try
                {
                    Connection viaConnection = _connectionManager.MakeConnection(proxyPeerEP);

                    lock (_proxyConnections)
                    {
                        if (_proxyConnections.Count < PROXY_CONNECTION_MAX_NUMBER)
                        {
                            viaConnection.Disposed += ProxyConnection_Disposed;
                            _proxyConnections.Add(proxyPeerEP, viaConnection);
                        }
                        else
                        {
                            return;
                        }
                    }

                    BinaryID[] networkIDs = new BinaryID[_networks.Keys.Count];
                    _networks.Keys.CopyTo(networkIDs, 0);

                    if (!viaConnection.RequestStartProxyNetwork(networkIDs, _profile.TrackerURIs))
                    {
                        lock (_proxyConnections)
                        {
                            _proxyConnections.Remove(proxyPeerEP);
                        }
                    }
                }
                catch
                {
                    lock (_proxyConnections)
                    {
                        _proxyConnections.Remove(proxyPeerEP);
                    }
                }
            }

            private void ProxyConnection_Disposed(object sender, EventArgs e)
            {
                Connection proxyConnection = sender as Connection;

                lock (_proxyConnections)
                {
                    _proxyConnections.Remove(proxyConnection.RemotePeerEP);
                }
            }

            #endregion

            #region public

            public BitChat CreateBitChat(MailAddress peerEmailAddress, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs)
            {
                BitChatNetwork network = new BitChatNetwork(peerEmailAddress, sharedSecret, networkID, knownPeerCerts, this, this);

                lock (_networks)
                {
                    _networks.Add(network.NetworkID, network);
                }

                if (trackerURIs == null)
                    trackerURIs = _profile.TrackerURIs;

                if (trackerURIs.Length > 0)
                {
                    lock (_proxyConnections)
                    {
                        foreach (Connection connection in _proxyConnections.Values)
                        {
                            ThreadPool.QueueUserWorkItem(SetupProxyNetworkOnConnectionAsync, new object[] { connection, networkID, trackerURIs });
                        }
                    }
                }

                return new BitChat(this, _connectionManager, _profile, network, sharedFileInfoList, trackerURIs);
            }

            public BitChat CreateBitChat(string networkName, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs)
            {
                BitChatNetwork network = new BitChatNetwork(networkName, sharedSecret, networkID, knownPeerCerts, this, this);

                lock (_networks)
                {
                    _networks.Add(network.NetworkID, network);
                }

                if (trackerURIs == null)
                    trackerURIs = _profile.TrackerURIs;

                if (trackerURIs.Length > 0)
                {
                    lock (_proxyConnections)
                    {
                        foreach (Connection connection in _proxyConnections.Values)
                        {
                            ThreadPool.QueueUserWorkItem(SetupProxyNetworkOnConnectionAsync, new object[] { connection, networkID, trackerURIs });
                        }
                    }
                }

                return new BitChat(this, _connectionManager, _profile, network, sharedFileInfoList, trackerURIs);
            }

            public void UpdateProfile(BitChatProfile.BitChatInfo[] bitChatInfoList)
            {
                _profile.UpdateBitChatInfo(bitChatInfoList);
            }

            #endregion

            #region LocalDiscovery support

            private void _localDiscovery_PeerDiscovered(LocalPeerDiscoveryIPv4 sender, IPEndPoint peerEP, BinaryID networkID)
            {
                lock (_networks)
                {
                    if (_networks.ContainsKey(networkID))
                        _networks[networkID].MakeConnection(peerEP);
                }
            }

            #endregion

            #region IBitChatManager support

            public void RemoveBitChat(BitChat chat)
            {
                lock (_service._bitChats)
                {
                    _service._bitChats.Remove(chat);
                }
            }

            public void StartLocalTracking(BinaryID networkID)
            {
                _localDiscovery.StartTracking(networkID);
            }

            public void StopLocalTracking(BinaryID networkID)
            {
                _localDiscovery.StopTracking(networkID);
            }

            public void PauseLocalAnnouncement(BinaryID networkID)
            {
                _localDiscovery.PauseAnnouncement(networkID);
            }

            public void ResumeLocalAnnouncement(BinaryID networkID)
            {
                _localDiscovery.ResumeAnnouncement(networkID);
            }

            #endregion

            #region IBitChatNetworkManager support

            public Connection MakeConnection(IPEndPoint peerEP)
            {
                return _connectionManager.MakeConnection(peerEP);
            }

            public Connection MakeVirtualConnection(Connection viaConnection, IPEndPoint remotePeerEP)
            {
                return _connectionManager.MakeVirtualConnection(viaConnection, remotePeerEP);
            }

            public CertificateStore GetLocalCredentials()
            {
                return _profile.LocalCertificateStore;
            }

            public Certificate[] GetTrustedRootCertificates()
            {
                return _trustedRootCertificates;
            }

            public SecureChannelCryptoOptionFlags GetSupportedCryptoOptions()
            {
                return _supportedCryptoOptions;
            }

            public int GetReNegotiateOnBytesSent()
            {
                return _reNegotiateOnBytesSent;
            }

            public int GetReNegotiateAfterSeconds()
            {
                return _reNegotiateAfterSeconds + 60;
            }

            public bool CheckCertificateRevocationList()
            {
                return _profile.CheckCertificateRevocationList;
            }

            public void RemoveNetwork(BitChatNetwork network)
            {
                lock (_networks)
                {
                    _networks.Remove(network.NetworkID);
                }
            }

            #endregion

            #region ConnectionManager handling

            private BitChatNetwork FindBitChatNetwork(Connection connection, BinaryID channelName)
            {
                //find network by channel name
                lock (_networks)
                {
                    foreach (KeyValuePair<BinaryID, BitChatNetwork> item in _networks)
                    {
                        BinaryID computedChannelName = item.Value.GetChannelName(connection.LocalPeerID, connection.RemotePeerID);

                        if (computedChannelName.Equals(channelName))
                            return item.Value;
                    }
                }

                return null;
            }

            private void BitChatNetworkChannelRequest(Connection connection, BinaryID channelName, Stream channel)
            {
                try
                {
                    BitChatNetwork network = FindBitChatNetwork(connection, channelName);

                    if (network == null)
                        throw new BitChatException("Network not found for given channel name.");

                    SecureChannelStream secureChannel = new SecureChannelServerStream(channel, connection.RemotePeerEP, _profile.LocalCertificateStore, _trustedRootCertificates, this, _supportedCryptoOptions, _reNegotiateOnBytesSent, _reNegotiateAfterSeconds, network.SharedSecret);

                    network.JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel, _profile.CheckCertificateRevocationList);
                }
                catch
                {
                    channel.Dispose();
                }
            }

            private void ProxyNetworkPeersAvailable(Connection viaConnection, BinaryID channelName, List<IPEndPoint> peerEPs)
            {
                try
                {
                    BitChatNetwork network = FindBitChatNetwork(viaConnection, channelName);

                    if (network == null)
                        throw new BitChatException("Network not found for given channel name.");

                    network.MakeConnection(viaConnection, peerEPs);
                }
                catch
                { }
            }

            #endregion

            #region ISecureChannelSecurityManager support

            bool ISecureChannelSecurityManager.ProceedConnection(Certificate remoteCertificate)
            {
                return true;
            }

            #endregion

            #region properties

            public BitChatProfile Profile
            { get { return _profile; } }

            public int LocalPort
            { get { return _connectionManager.LocalPort; } }

            public UPnPDeviceStatus UPnPStatus
            { get { return _connectionManager.UPnPStatus; } }

            public IPEndPoint UPnPExternalEP
            { get { return _connectionManager.UPnPExternalEP; } }

            #endregion
        }
    }
}
