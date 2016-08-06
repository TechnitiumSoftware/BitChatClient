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
    public delegate void BitChatInvitation(BitChatService sender, BitChat chat);

    public class BitChatService : IDisposable
    {
        #region events

        public event BitChatInvitation BitChatInvitationReceived;

        #endregion

        #region variables

        SynchronizationContext _syncCxt = SynchronizationContext.Current;
        InvalidCertificateEvent _invalidCertEventHandler;

        BitChatProfile _profile;

        InternalBitChatService _internal;
        List<BitChat> _bitChats = new List<BitChat>();

        #endregion

        #region constructor

        static BitChatService()
        {
            if (ServicePointManager.DefaultConnectionLimit < 100)
                ServicePointManager.DefaultConnectionLimit = 100;
        }

        public BitChatService(BitChatProfile profile, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions, InvalidCertificateEvent invalidCertEventHandler)
        {
            //verify root certs
            foreach (Certificate trustedCert in trustedRootCertificates)
                trustedCert.Verify(trustedRootCertificates);

            //verify profile cert
            profile.LocalCertificateStore.Certificate.Verify(trustedRootCertificates);

            _profile = profile;
            _invalidCertEventHandler = invalidCertEventHandler;

            _internal = new InternalBitChatService(this, profile, trustedRootCertificates, supportedCryptoOptions);

            foreach (BitChatProfile.BitChatInfo bitChatInfo in profile.BitChatInfoList)
            {
                BitChatNetwork network;

                if (bitChatInfo.Type == BitChatNetworkType.PrivateChat)
                    network = new BitChatNetwork(bitChatInfo.PeerEmailAddress, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, _internal, _internal, bitChatInfo.NetworkStatus, bitChatInfo.InvitationSender, bitChatInfo.InvitationMessage);
                else
                    network = new BitChatNetwork(bitChatInfo.NetworkName, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, _internal, _internal, bitChatInfo.NetworkStatus);

                BitChat chat = _internal.CreateBitChat(network, bitChatInfo.MessageStoreID, bitChatInfo.MessageStoreKey, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs, bitChatInfo.EnableTracking, bitChatInfo.SendInvitation);
                _bitChats.Add(chat);
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

                if (_internal != null)
                    _internal.Dispose();

                TcpRelayService.StopAllTcpRelays();

                _disposed = true;
            }
        }

        #endregion

        #region private event functions

        private void RaiseEventBitChatInvitationReceived(BitChat bitChat)
        {
            _syncCxt.Post(BitChatInvitationReceivedCallback, bitChat);
        }

        private void BitChatInvitationReceivedCallback(object state)
        {
            try
            {
                BitChatInvitationReceived?.Invoke(this, state as BitChat);
            }
            catch { }
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

        private void CreateBitChat(BinaryID networkID, IPEndPoint peerEP, string message)
        {
            BitChatNetwork network = new BitChatNetwork(null, null, networkID, new Certificate[] { }, _internal, _internal, BitChatNetworkStatus.Offline, peerEP.ToString(), message);
            BitChat bitChat = _internal.CreateBitChat(network, BinaryID.GenerateRandomID160().ToString(), BinaryID.GenerateRandomID256().ID, new BitChatProfile.SharedFileInfo[] { }, null, true, false);

            lock (_bitChats)
            {
                _bitChats.Add(bitChat);
            }

            RaiseEventBitChatInvitationReceived(bitChat);
        }

        #endregion

        #region public

        public BitChat CreateBitChat(MailAddress peerEmailAddress, string sharedSecret, bool enableTracking, string invitationMessage)
        {
            BitChatNetwork network = new BitChatNetwork(peerEmailAddress, sharedSecret, null, new Certificate[] { }, _internal, _internal, BitChatNetworkStatus.Online, _profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress.Address, invitationMessage);
            BitChat bitChat = _internal.CreateBitChat(network, BinaryID.GenerateRandomID160().ToString(), BinaryID.GenerateRandomID256().ID, new BitChatProfile.SharedFileInfo[] { }, null, enableTracking, true);

            lock (_bitChats)
            {
                _bitChats.Add(bitChat);
            }

            return bitChat;
        }

        public BitChat CreateBitChat(string networkName, string sharedSecret, bool enableTracking)
        {
            BitChatNetwork network = new BitChatNetwork(networkName, sharedSecret, null, new Certificate[] { }, _internal, _internal, BitChatNetworkStatus.Online);
            BitChat bitChat = _internal.CreateBitChat(network, BinaryID.GenerateRandomID160().ToString(), BinaryID.GenerateRandomID256().ID, new BitChatProfile.SharedFileInfo[] { }, null, enableTracking, false);

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

            IPEndPoint[] dhtNodes = _internal.GetDhtConnectedNodes();
            if (dhtNodes.Length > 0)
                _profile.BootstrapDhtNodes = dhtNodes;

            if (_profile.EnableInvitation)
                _internal.EnableInvitationTracker();
            else
                _internal.DisableInvitationTracker();
        }

        public void ReCheckConnectivity()
        {
            _internal.ReCheckConnectivity();
        }

        #endregion

        #region properties

        public BitChatProfile Profile
        { get { return _profile; } }

        public INetworkInfo NetworkInfo
        { get { return _internal; } }

        #endregion

        private class InternalBitChatService : IBitChatManager, IBitChatNetworkManager, ISecureChannelSecurityManager, INetworkInfo, IDisposable
        {
            #region variables

            const int BIT_CHAT_INVITATION_TRACKER_UPDATE_INTERVAL = 120;

            BitChatService _service;
            BitChatProfile _profile;
            Certificate[] _trustedRootCertificates;
            SecureChannelCryptoOptionFlags _supportedCryptoOptions;

            ConnectionManager _connectionManager;
            LocalPeerDiscovery _localDiscovery;
            Dictionary<BinaryID, BitChatNetwork> _networks = new Dictionary<BinaryID, BitChatNetwork>();

            //invitation tracker
            readonly BinaryID _maskedEmailAddress;
            TrackerManager _invitationTrackerManager;

            //relay nodes
            const int TCP_RELAY_MAX_CONNECTIONS = 3;
            const int TCP_RELAY_KEEP_ALIVE_INTERVAL = 30000; //30sec

            Dictionary<IPEndPoint, Connection> _tcpRelayConnections = new Dictionary<IPEndPoint, Connection>();
            Timer _tcpRelayConnectionKeepAliveTimer;

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

                _maskedEmailAddress = BitChatNetwork.GetMaskedEmailAddress(_profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress);

                _connectionManager = new ConnectionManager(_profile);
                _connectionManager.InternetConnectivityStatusChanged += ConnectionManager_InternetConnectivityStatusChanged;
                _connectionManager.BitChatNetworkChannelInvitation += ConnectionManager_BitChatNetworkChannelInvitation;
                _connectionManager.BitChatNetworkChannelRequest += ConnectionManager_BitChatNetworkChannelRequest;
                _connectionManager.TcpRelayPeersAvailable += ConnectionManager_TcpRelayPeersAvailable;

                LocalPeerDiscovery.StartListener(41733);
                _localDiscovery = new LocalPeerDiscovery(_connectionManager.LocalPort);
                _localDiscovery.PeerDiscovered += LocalDiscovery_PeerDiscovered;

                if (_profile.EnableInvitation)
                    EnableInvitationTracker();
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
                    if (_tcpRelayConnectionKeepAliveTimer != null)
                    {
                        _tcpRelayConnectionKeepAliveTimer.Dispose();
                        _tcpRelayConnectionKeepAliveTimer = null;
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
                    //no incoming connection possible; start tcp relay client
                    StartTcpRelayClient();
                }
                else
                {
                    //can receive incoming connection; no need for running tcp relay client
                    StopTcpRelayClient();
                }
            }

            private void StartTcpRelayClient()
            {
                FindAndConnectTcpRelayNodes();

                if (_tcpRelayConnectionKeepAliveTimer == null)
                    _tcpRelayConnectionKeepAliveTimer = new Timer(RelayConnectionKeepAliveTimerCallback, null, TCP_RELAY_KEEP_ALIVE_INTERVAL, Timeout.Infinite);
            }

            private void StopTcpRelayClient()
            {
                if (_tcpRelayConnectionKeepAliveTimer != null)
                {
                    _tcpRelayConnectionKeepAliveTimer.Dispose();
                    _tcpRelayConnectionKeepAliveTimer = null;

                    BinaryID[] networkIDs;

                    lock (_networks)
                    {
                        networkIDs = new BinaryID[_networks.Keys.Count];
                        _networks.Keys.CopyTo(networkIDs, 0);
                    }

                    //remove all networks from all tcp relay connections
                    lock (_tcpRelayConnections)
                    {
                        foreach (Connection connection in _tcpRelayConnections.Values)
                        {
                            ThreadPool.QueueUserWorkItem(RemoveTcpRelayFromConnectionAsync, new object[] { connection, networkIDs });
                        }

                        _tcpRelayConnections.Clear();
                    }
                }
            }

            private void FindAndConnectTcpRelayNodes()
            {
                //if less number of relay node connections, try to find new relay nodes

                bool addRelayNodes;

                lock (_tcpRelayConnections)
                {
                    addRelayNodes = (_tcpRelayConnections.Count < TCP_RELAY_MAX_CONNECTIONS);
                }

                if (addRelayNodes)
                {
                    IPEndPoint[] nodeEPs = _connectionManager.DhtClient.GetAllNodes();

                    foreach (IPEndPoint relayNodeEP in nodeEPs)
                    {
                        lock (_tcpRelayConnections)
                        {
                            if (_tcpRelayConnections.Count >= TCP_RELAY_MAX_CONNECTIONS)
                                return;

                            if (_tcpRelayConnections.ContainsKey(relayNodeEP))
                                continue;
                        }

                        if (NetUtilities.IsPrivateIP(relayNodeEP.Address))
                            continue;

                        ThreadPool.QueueUserWorkItem(ConnectTcpRelayNodeAsync, relayNodeEP);
                    }
                }
            }

            private void ConnectTcpRelayNodeAsync(object state)
            {
                IPEndPoint relayNodeEP = state as IPEndPoint;

                try
                {
                    Connection viaConnection = _connectionManager.MakeConnection(relayNodeEP);

                    lock (_tcpRelayConnections)
                    {
                        if (_tcpRelayConnections.Count < TCP_RELAY_MAX_CONNECTIONS)
                        {
                            if (!_tcpRelayConnections.ContainsKey(relayNodeEP))
                            {
                                //new tcp relay, add to list
                                viaConnection.Disposed += RelayConnection_Disposed;
                                _tcpRelayConnections.Add(relayNodeEP, viaConnection);
                            }
                        }
                        else
                        {
                            return; //have enough tcp relays
                        }
                    }

                    List<BinaryID> networkIDs = new List<BinaryID>(10);

                    if (_profile.EnableInvitation)
                        networkIDs.Add(_maskedEmailAddress);

                    lock (_networks)
                    {
                        foreach (KeyValuePair<BinaryID, BitChatNetwork> network in _networks)
                        {
                            if (network.Value.Status == BitChatNetworkStatus.Online)
                                networkIDs.Add(network.Key);
                        }
                    }

                    if (!viaConnection.RequestStartTcpRelay(networkIDs.ToArray(), _profile.TrackerURIs))
                    {
                        lock (_tcpRelayConnections)
                        {
                            _tcpRelayConnections.Remove(relayNodeEP);
                        }
                    }
                }
                catch
                {
                    lock (_tcpRelayConnections)
                    {
                        _tcpRelayConnections.Remove(relayNodeEP);
                    }
                }
            }

            private void RelayConnectionKeepAliveTimerCallback(object state)
            {
                try
                {
                    //send noop to all connections to keep them alive
                    lock (_tcpRelayConnections)
                    {
                        foreach (Connection connection in _tcpRelayConnections.Values)
                        {
                            try
                            {
                                connection.SendNOOP();
                            }
                            catch
                            { }
                        }
                    }

                    FindAndConnectTcpRelayNodes();
                }
                catch
                { }
                finally
                {
                    if (_tcpRelayConnectionKeepAliveTimer != null)
                        _tcpRelayConnectionKeepAliveTimer.Change(TCP_RELAY_KEEP_ALIVE_INTERVAL, Timeout.Infinite);
                }
            }

            private void RelayConnection_Disposed(object sender, EventArgs e)
            {
                Connection relayConnection = sender as Connection;

                lock (_tcpRelayConnections)
                {
                    _tcpRelayConnections.Remove(relayConnection.RemotePeerEP);
                }
            }

            private void SetupTcpRelayOnConnectionAsync(object state)
            {
                try
                {
                    object[] parameters = state as object[];

                    Connection viaConnection = parameters[0] as Connection;
                    BinaryID networkID = parameters[1] as BinaryID;
                    Uri[] trackerURIs = parameters[2] as Uri[];

                    viaConnection.RequestStartTcpRelay(new BinaryID[] { networkID }, trackerURIs);
                }
                catch
                { }
            }

            private void RemoveTcpRelayFromConnectionAsync(object state)
            {
                try
                {
                    object[] parameters = state as object[];

                    Connection viaConnection = parameters[0] as Connection;
                    BinaryID[] networkIDs = parameters[1] as BinaryID[];

                    viaConnection.RequestStopTcpRelay(networkIDs);
                }
                catch
                { }
            }

            private void SetupTcpRelay(BinaryID networkID, Uri[] trackerURIs)
            {
                lock (_tcpRelayConnections)
                {
                    foreach (Connection connection in _tcpRelayConnections.Values)
                    {
                        ThreadPool.QueueUserWorkItem(SetupTcpRelayOnConnectionAsync, new object[] { connection, networkID, trackerURIs });
                    }
                }
            }

            private void RemoveTcpRelay(BinaryID networkID)
            {
                lock (_tcpRelayConnections)
                {
                    foreach (Connection connection in _tcpRelayConnections.Values)
                    {
                        ThreadPool.QueueUserWorkItem(RemoveTcpRelayFromConnectionAsync, new object[] { connection, new BinaryID[] { networkID } });
                    }
                }
            }

            #endregion

            #region public

            public BitChat CreateBitChat(BitChatNetwork network, string messageStoreID, byte[] messageStoreKey, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs, bool enableTracking, bool sendInvitation)
            {
                lock (_networks)
                {
                    if (_networks.ContainsKey(network.NetworkID))
                    {
                        if (network.Type == BitChatNetworkType.PrivateChat)
                            throw new BitChatException("Bit Chat for '" + network.NetworkName + "' already exists.");
                        else
                            throw new BitChatException("Bit Chat group '" + network.NetworkName + "' already exists.");
                    }

                    _networks.Add(network.NetworkID, network);
                }

                if (trackerURIs == null)
                    trackerURIs = _profile.TrackerURIs;

                if (enableTracking && (network.Status == BitChatNetworkStatus.Online))
                    SetupTcpRelay(network.NetworkID, trackerURIs); //starts tcp relay if available or needed

                return new BitChat(_service._syncCxt, this, _connectionManager, _profile, network, messageStoreID, messageStoreKey, sharedFileInfoList, trackerURIs, enableTracking, sendInvitation);
            }

            public IPEndPoint[] GetDhtConnectedNodes()
            {
                return _connectionManager.DhtClient.GetAllNodes();
            }

            public void EnableInvitationTracker()
            {
                if (_invitationTrackerManager == null)
                {
                    _invitationTrackerManager = new TrackerManager(_maskedEmailAddress, _connectionManager.LocalPort, _connectionManager.DhtClient, BIT_CHAT_INVITATION_TRACKER_UPDATE_INTERVAL);
                    _invitationTrackerManager.StartTracking();

                    _localDiscovery.StartTracking(_maskedEmailAddress);

                    SetupTcpRelay(_maskedEmailAddress, new Uri[] { });
                }
            }

            public void DisableInvitationTracker()
            {
                if (_invitationTrackerManager != null)
                {
                    _invitationTrackerManager.StopTracking();
                    _invitationTrackerManager = null;

                    _localDiscovery.StopTracking(_maskedEmailAddress);

                    RemoveTcpRelay(_maskedEmailAddress);
                }
            }

            #endregion

            #region LocalDiscovery support

            private void LocalDiscovery_PeerDiscovered(LocalPeerDiscovery sender, IPEndPoint peerEP, BinaryID networkID)
            {
                lock (_networks)
                {
                    if (_networks.ContainsKey(networkID))
                    {
                        _networks[networkID].MakeConnection(peerEP);
                    }
                    else
                    {
                        foreach (KeyValuePair<BinaryID, BitChatNetwork> networkItem in _networks)
                        {
                            if (networkID.Equals(networkItem.Value.MaskedPeerEmailAddress))
                            {
                                networkItem.Value.SendInvitation(new IPEndPoint[] { peerEP });
                                break;
                            }
                        }
                    }
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

                RemoveTcpRelay(chat.NetworkID);
            }

            public void StartLocalTracking(BinaryID networkID)
            {
                _localDiscovery.StartTracking(networkID);
            }

            public void StopLocalTracking(BinaryID networkID)
            {
                _localDiscovery.StopTracking(networkID);
            }

            public void StartLocalAnnouncement(BinaryID networkID)
            {
                _localDiscovery.StartAnnouncement(networkID);
            }

            public void StopLocalAnnouncement(BinaryID networkID)
            {
                _localDiscovery.StopAnnouncement(networkID);
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

            private void ConnectionManager_BitChatNetworkChannelInvitation(BinaryID networkID, IPEndPoint peerEP, string message)
            {
                _service.CreateBitChat(networkID, peerEP, message);
            }

            private void ConnectionManager_BitChatNetworkChannelRequest(Connection connection, BinaryID channelName, Stream channel)
            {
                try
                {
                    BitChatNetwork network = FindBitChatNetwork(connection, channelName);

                    if (network == null)
                    {
                        channel.Dispose();
                        return;
                    }

                    network.AcceptConnectionAndJoinNetwork(connection, channel);
                }
                catch
                {
                    channel.Dispose();
                }
            }

            private void ConnectionManager_TcpRelayPeersAvailable(Connection viaConnection, BinaryID channelName, List<IPEndPoint> peerEPs)
            {
                try
                {
                    BitChatNetwork network = FindBitChatNetwork(viaConnection, channelName);

                    if (network != null)
                        network.MakeConnection(viaConnection, peerEPs);
                }
                catch
                { }
            }

            private BitChatNetwork FindBitChatNetwork(Connection connection, BinaryID channelName)
            {
                //find network by channel name
                lock (_networks)
                {
                    foreach (KeyValuePair<BinaryID, BitChatNetwork> item in _networks)
                    {
                        BitChatNetwork network = item.Value;

                        if (network.Status != BitChatNetworkStatus.Offline)
                        {
                            BinaryID computedChannelName = network.GetChannelName(connection.LocalPeerID, connection.RemotePeerID);

                            if (computedChannelName.Equals(channelName))
                                return item.Value;
                        }
                    }
                }

                return null;
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
                    lock (_tcpRelayConnections)
                    {
                        IPEndPoint[] relayNodeEPs = new IPEndPoint[_tcpRelayConnections.Count];
                        _tcpRelayConnections.Keys.CopyTo(relayNodeEPs, 0);

                        return relayNodeEPs;
                    }
                }
            }

            #endregion
        }
    }
}
