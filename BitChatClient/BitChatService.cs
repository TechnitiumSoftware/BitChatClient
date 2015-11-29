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
using BitChatClient.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.Proxy;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient
{
    public delegate void InvalidCertificateEvent(BitChatService sender, InvalidCertificateException e);

    public class BitChatService : IDisposable
    {
        #region variables

        SynchronizationContext _syncCxt = SynchronizationContext.Current;
        InvalidCertificateEvent _invalidCertEventHandler;

        BitChatProfile _profile;

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

            _profile = profile;
            _invalidCertEventHandler = invalidCertEventHandler;

            _manager = new InternalBitChatService(this, profile, trustedRootCertificates, supportedCryptoOptions);

            foreach (BitChatProfile.BitChatInfo bitChatInfo in profile.BitChatInfoList)
            {
                if (bitChatInfo.Type == BitChatNetworkType.PrivateChat)
                    _bitChats.Add(_manager.CreateBitChat(new MailAddress(bitChatInfo.NetworkNameOrPeerEmailAddress), bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs, bitChatInfo.EnableTracking));
                else
                    _bitChats.Add(_manager.CreateBitChat(bitChatInfo.NetworkNameOrPeerEmailAddress, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs, bitChatInfo.EnableTracking));
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
                    cert.VerifyRevocationList(_profile.Proxy);
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

        public BitChat CreateBitChat(MailAddress peerEmailAddress, string sharedSecret, bool enableTracking)
        {
            BitChat bitChat = _manager.CreateBitChat(peerEmailAddress, sharedSecret, null, new Certificate[] { }, new BitChatProfile.SharedFileInfo[] { }, null, enableTracking);

            lock (_bitChats)
            {
                _bitChats.Add(bitChat);
            }

            return bitChat;
        }

        public BitChat CreateBitChat(string networkName, string sharedSecret, bool enableTracking)
        {
            BitChat bitChat = _manager.CreateBitChat(networkName, sharedSecret, null, new Certificate[] { }, new BitChatProfile.SharedFileInfo[] { }, null, enableTracking);

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
            List<BitChatProfile.BitChatInfo> bitChatInfoList = new List<BitChatProfile.BitChatInfo>(_bitChats.Count);

            lock (_bitChats)
            {
                foreach (BitChat chat in _bitChats)
                {
                    bitChatInfoList.Add(chat.GetBitChatInfo());
                }
            }

            _profile.BitChatInfoList = bitChatInfoList.ToArray();

            IPEndPoint[] dhtNodes = _manager.GetDhtConnectedNodes();
            if (dhtNodes.Length > 0)
                _profile.BootstrapDhtNodes = dhtNodes;
        }

        public void ReCheckConnectivity()
        {
            _manager.ReCheckConnectivity();
        }

        #endregion

        #region properties

        public BitChatProfile Profile
        { get { return _manager.Profile; } }

        public INetworkInfo NetworkInfo
        { get { return _manager; } }

        #endregion

        private class InternalBitChatService : IBitChatManager, IBitChatNetworkManager, ISecureChannelSecurityManager, INetworkInfo, IDisposable
        {
            #region variables

            BitChatService _service;
            BitChatProfile _profile;
            Certificate[] _trustedRootCertificates;
            SecureChannelCryptoOptionFlags _supportedCryptoOptions;

            ConnectionManager _connectionManager;
            LocalPeerDiscovery _localDiscovery;
            Dictionary<BinaryID, BitChatNetwork> _networks = new Dictionary<BinaryID, BitChatNetwork>();

            //relay nodes
            const int TCP_RELAY_MAX_CONNECTIONS = 3;
            const int TCP_RELAY_CHECK_INTERVAL = 30000; //30sec

            Dictionary<IPEndPoint, Connection> _relayConnections = new Dictionary<IPEndPoint, Connection>();
            Timer _relayConnectionCheckTimer;

            const int RE_NEGOTIATE_AFTER_BYTES_SENT = 104857600; //100mb
            const int RE_NEGOTIATE_AFTER_SECONDS = 3600; //1hr

            #endregion

            #region constructor

            public InternalBitChatService(BitChatService service, BitChatProfile profile, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions)
            {
                _service = service;
                _profile = profile;
                _trustedRootCertificates = trustedRootCertificates;
                _supportedCryptoOptions = supportedCryptoOptions;

                _connectionManager = new ConnectionManager(_profile, BitChatNetworkChannelRequest, RelayNetworkPeersAvailable);
                _connectionManager.InternetConnectivityStatusChanged += ConnectionManager_InternetConnectivityStatusChanged;

                LocalPeerDiscovery.StartListener(41733);
                _localDiscovery = new LocalPeerDiscovery(_connectionManager.LocalPort);
                _localDiscovery.PeerDiscovered += _localDiscovery_PeerDiscovered;
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
                    if (_relayConnectionCheckTimer != null)
                    {
                        _relayConnectionCheckTimer.Dispose();
                        _relayConnectionCheckTimer = null;
                    }

                    if (_connectionManager != null)
                        _connectionManager.Dispose();

                    if (_localDiscovery != null)
                        _localDiscovery.Dispose();

                    _disposed = true;
                }
            }

            #endregion

            #region Tcp Relay Connection

            private void ConnectionManager_InternetConnectivityStatusChanged(object sender, EventArgs e)
            {
                IPEndPoint externalEP = _connectionManager.ExternalEndPoint;

                if (externalEP == null)
                {
                    //no incoming connection possible; setup relay network
                    AddTcpRelayNodes();

                    if (_relayConnectionCheckTimer == null)
                        _relayConnectionCheckTimer = new Timer(RelayConnectionCheckTimerCallback, null, TCP_RELAY_CHECK_INTERVAL, Timeout.Infinite);
                }
                else
                {
                    //can receive incoming connection; no need for setting up relay network;
                    if (_relayConnectionCheckTimer != null)
                    {
                        _relayConnectionCheckTimer.Dispose();
                        _relayConnectionCheckTimer = null;

                        BinaryID[] networkIDs;

                        lock (_networks)
                        {
                            networkIDs = new BinaryID[_networks.Keys.Count];
                            _networks.Keys.CopyTo(networkIDs, 0);
                        }

                        lock (_relayConnections)
                        {
                            foreach (Connection connection in _relayConnections.Values)
                            {
                                ThreadPool.QueueUserWorkItem(RemoveRelayNetworksFromConnectionAsync, new object[] { connection, networkIDs });
                            }

                            _relayConnections.Clear();
                        }
                    }
                }
            }

            private void AddTcpRelayNodes()
            {
                //if less number of relay node connections, try to find new relay nodes

                bool addRelayNodes;

                lock (_relayConnections)
                {
                    addRelayNodes = (_relayConnections.Count < TCP_RELAY_MAX_CONNECTIONS);
                }

                if (addRelayNodes)
                {
                    IPEndPoint[] nodeEPs = _connectionManager.DhtClient.GetAllNodes();

                    foreach (IPEndPoint relayNodeEP in nodeEPs)
                    {
                        lock (_relayConnections)
                        {
                            if (_relayConnections.Count >= TCP_RELAY_MAX_CONNECTIONS)
                                return;

                            if (_relayConnections.ContainsKey(relayNodeEP))
                                continue;
                        }

                        if (NetUtilities.IsPrivateIP(relayNodeEP.Address))
                            continue;

                        ThreadPool.QueueUserWorkItem(AddTcpRelayNodeAsync, relayNodeEP);
                    }
                }
            }

            private void AddTcpRelayNodeAsync(object state)
            {
                IPEndPoint relayNodeEP = state as IPEndPoint;

                try
                {
                    Connection viaConnection = _connectionManager.MakeConnection(relayNodeEP);

                    lock (_relayConnections)
                    {
                        if (_relayConnections.Count < TCP_RELAY_MAX_CONNECTIONS)
                        {
                            if (!_relayConnections.ContainsKey(relayNodeEP))
                            {
                                //new tcp relay, add to list
                                viaConnection.Disposed += RelayConnection_Disposed;
                                _relayConnections.Add(relayNodeEP, viaConnection);
                            }
                        }
                        else
                        {
                            return; //have enough tcp relays
                        }
                    }

                    BinaryID[] networkIDs;

                    lock (_networks)
                    {
                        networkIDs = new BinaryID[_networks.Keys.Count];
                        _networks.Keys.CopyTo(networkIDs, 0);
                    }

                    if (!viaConnection.RequestStartRelayNetwork(networkIDs, _profile.TrackerURIs))
                    {
                        lock (_relayConnections)
                        {
                            _relayConnections.Remove(relayNodeEP);
                        }
                    }
                }
                catch
                {
                    lock (_relayConnections)
                    {
                        _relayConnections.Remove(relayNodeEP);
                    }
                }
            }

            private void RelayConnectionCheckTimerCallback(object state)
            {
                try
                {
                    //send noop to all connections
                    lock (_relayConnections)
                    {
                        foreach (Connection connection in _relayConnections.Values)
                        {
                            try
                            {
                                connection.SendNOOP();
                            }
                            catch
                            { }
                        }
                    }

                    AddTcpRelayNodes();
                }
                catch
                { }
                finally
                {
                    if (_relayConnectionCheckTimer != null)
                        _relayConnectionCheckTimer.Change(TCP_RELAY_CHECK_INTERVAL, Timeout.Infinite);
                }
            }

            private void RelayConnection_Disposed(object sender, EventArgs e)
            {
                Connection relayConnection = sender as Connection;

                lock (_relayConnections)
                {
                    _relayConnections.Remove(relayConnection.RemotePeerEP);
                }
            }

            private void SetupRelayNetworkOnConnectionAsync(object state)
            {
                try
                {
                    object[] parameters = state as object[];

                    Connection viaConnection = parameters[0] as Connection;
                    BinaryID networkID = parameters[1] as BinaryID;
                    Uri[] trackerURIs = parameters[2] as Uri[];

                    viaConnection.RequestStartRelayNetwork(new BinaryID[] { networkID }, trackerURIs);
                }
                catch
                { }
            }

            private void RemoveRelayNetworksFromConnectionAsync(object state)
            {
                try
                {
                    object[] parameters = state as object[];

                    Connection viaConnection = parameters[0] as Connection;
                    BinaryID[] networkIDs = parameters[1] as BinaryID[];

                    viaConnection.RequestStopRelayNetwork(networkIDs);
                }
                catch
                { }
            }

            #endregion

            #region public

            public BitChat CreateBitChat(MailAddress peerEmailAddress, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs, bool enableTracking)
            {
                BitChatNetwork network = new BitChatNetwork(peerEmailAddress, sharedSecret, networkID, knownPeerCerts, this, this);

                lock (_networks)
                {
                    if (_networks.ContainsKey(network.NetworkID))
                        throw new BitChatException("Bit Chat for email address '" + peerEmailAddress.Address + "' already exists.");

                    _networks.Add(network.NetworkID, network);
                }

                if (trackerURIs == null)
                    trackerURIs = _profile.TrackerURIs;

                if (trackerURIs.Length > 0)
                {
                    lock (_relayConnections)
                    {
                        foreach (Connection connection in _relayConnections.Values)
                        {
                            ThreadPool.QueueUserWorkItem(SetupRelayNetworkOnConnectionAsync, new object[] { connection, networkID, trackerURIs });
                        }
                    }
                }

                return new BitChat(this, _connectionManager, _profile, network, sharedFileInfoList, trackerURIs, enableTracking);
            }

            public BitChat CreateBitChat(string networkName, string sharedSecret, BinaryID networkID, Certificate[] knownPeerCerts, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs, bool enableTracking)
            {
                BitChatNetwork network = new BitChatNetwork(networkName, sharedSecret, networkID, knownPeerCerts, this, this);

                lock (_networks)
                {
                    if (_networks.ContainsKey(network.NetworkID))
                        throw new BitChatException("Bit Chat group with name '" + networkName + "' already exists.");

                    _networks.Add(network.NetworkID, network);
                }

                if (trackerURIs == null)
                    trackerURIs = _profile.TrackerURIs;

                if (trackerURIs.Length > 0)
                {
                    lock (_relayConnections)
                    {
                        foreach (Connection connection in _relayConnections.Values)
                        {
                            ThreadPool.QueueUserWorkItem(SetupRelayNetworkOnConnectionAsync, new object[] { connection, networkID, trackerURIs });
                        }
                    }
                }

                return new BitChat(this, _connectionManager, _profile, network, sharedFileInfoList, trackerURIs, enableTracking);
            }

            public IPEndPoint[] GetDhtConnectedNodes()
            {
                return _connectionManager.DhtClient.GetAllNodes();
            }

            #endregion

            #region LocalDiscovery support

            private void _localDiscovery_PeerDiscovered(LocalPeerDiscovery sender, IPEndPoint peerEP, BinaryID networkID)
            {
                lock (_networks)
                {
                    if (_networks.ContainsKey(networkID))
                        _networks[networkID].MakeConnection(peerEP);
                }

                //add peerEP to DHT
                _connectionManager.DhtClient.AddNode(peerEP);
            }

            #endregion

            #region IBitChatManager support

            public void RemoveBitChat(BitChat chat)
            {
                lock (_service._bitChats)
                {
                    _service._bitChats.Remove(chat);
                }

                lock (_relayConnections)
                {
                    foreach (Connection connection in _relayConnections.Values)
                    {
                        ThreadPool.QueueUserWorkItem(RemoveRelayNetworksFromConnectionAsync, new object[] { connection, new BinaryID[] { chat.NetworkID } });
                    }
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
                return RE_NEGOTIATE_AFTER_BYTES_SENT;
            }

            public int GetReNegotiateAfterSeconds()
            {
                return RE_NEGOTIATE_AFTER_SECONDS + 60;
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

            public NetProxy GetProxy()
            {
                return _profile.Proxy;
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

                    SecureChannelStream secureChannel = new SecureChannelServerStream(channel, connection.RemotePeerEP, _profile.LocalCertificateStore, _trustedRootCertificates, this, _supportedCryptoOptions, RE_NEGOTIATE_AFTER_BYTES_SENT, RE_NEGOTIATE_AFTER_SECONDS, network.SharedSecret);

                    network.JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel, _profile.CheckCertificateRevocationList);
                }
                catch
                {
                    channel.Dispose();
                }
            }

            private void RelayNetworkPeersAvailable(Connection viaConnection, BinaryID channelName, IEnumerable<IPEndPoint> peerEPs)
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

            public void ReCheckConnectivity()
            {
                _connectionManager.ReCheckConnectivity();
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

            public BinaryID LocalPeerID
            { get { return _connectionManager.LocalPeerID; } }

            public int LocalPort
            { get { return _connectionManager.LocalPort; } }

            public BinaryID DhtNodeID
            { get { return _connectionManager.DhtClient.LocalNodeID; } }

            public int DhtLocalPort
            { get { return _connectionManager.DhtClient.LocalPort; } }

            public int DhtTotalNodes
            { get { return _connectionManager.DhtClient.GetTotalNodes(); } }

            public InternetConnectivityStatus InternetStatus
            { get { return _connectionManager.InternetStatus; } }

            public UPnPDeviceStatus UPnPStatus
            { get { return _connectionManager.UPnPStatus; } }

            public IPAddress UPnPDeviceIP
            { get { return _connectionManager.UPnPDeviceIP; } }

            public IPAddress UPnPExternalIP
            { get { return _connectionManager.UPnPExternalIP; } }

            public IPEndPoint ExternalEndPoint
            { get { return _connectionManager.ExternalEndPoint; } }

            public IPEndPoint[] TcpRelayNodes
            {
                get
                {
                    lock (_relayConnections)
                    {
                        IPEndPoint[] relayNodeEPs = new IPEndPoint[_relayConnections.Count];
                        _relayConnections.Keys.CopyTo(relayNodeEPs, 0);

                        return relayNodeEPs;
                    }
                }
            }

            #endregion
        }
    }
}
