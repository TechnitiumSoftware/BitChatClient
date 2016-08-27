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

using BitChatCore.Network;
using BitChatCore.Network.Connections;
using BitChatCore.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore
{
    public delegate void InvalidCertificateDetected(BitChatClient sender, InvalidCertificateException e);
    public delegate void BitChatInvitation(BitChatClient sender, BitChat chat);

    public class BitChatClient : IDisposable
    {
        #region events

        public event InvalidCertificateDetected InvalidCertificateDetected;
        public event BitChatInvitation BitChatInvitationReceived;

        #endregion

        #region variables

        SynchronizationContext _syncCxt = SynchronizationContext.Current;

        BitChatProfile _profile;
        Certificate[] _trustedRootCertificates;
        SecureChannelCryptoOptionFlags _supportedCryptoOptions;

        InternalBitChatService _internal;
        List<BitChat> _bitChats = new List<BitChat>();

        #endregion

        #region constructor

        static BitChatClient()
        {
            if (ServicePointManager.DefaultConnectionLimit < 100)
                ServicePointManager.DefaultConnectionLimit = 100;
        }

        public BitChatClient(BitChatProfile profile, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions)
        {
            _profile = profile;
            _trustedRootCertificates = trustedRootCertificates;
            _supportedCryptoOptions = supportedCryptoOptions;
        }

        #endregion

        #region IDisposable

        ~BitChatClient()
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
                InvalidCertificateDetected?.Invoke(this, state as InvalidCertificateException);
            }
            catch
            { }
        }

        private void CreateBitChat(BinaryID networkID, IPEndPoint peerEP, string message)
        {
            BitChatNetwork network = new BitChatNetwork(_internal.ConnectionManager, _trustedRootCertificates, _supportedCryptoOptions, null, null, networkID, new Certificate[] { }, BitChatNetworkStatus.Offline, peerEP.ToString(), message);
            BitChat bitChat = _internal.CreateBitChat(network, BinaryID.GenerateRandomID160().ToString(), BinaryID.GenerateRandomID256().ID, 0, null, new BitChatProfile.SharedFileInfo[] { }, null, true, false, false);

            lock (_bitChats)
            {
                _bitChats.Add(bitChat);
            }

            RaiseEventBitChatInvitationReceived(bitChat);
        }

        #endregion

        #region public

        public void Start()
        {
            if (_internal != null)
                return;

            //verify root certs
            foreach (Certificate trustedCert in _trustedRootCertificates)
                trustedCert.Verify(_trustedRootCertificates);

            //verify profile cert
            _profile.LocalCertificateStore.Certificate.Verify(_trustedRootCertificates);

            _internal = new InternalBitChatService(this);

            foreach (BitChatProfile.BitChatInfo bitChatInfo in _profile.BitChatInfoList)
            {
                BitChatNetwork network;

                if (bitChatInfo.Type == BitChatNetworkType.PrivateChat)
                    network = new BitChatNetwork(_internal.ConnectionManager, _trustedRootCertificates, _supportedCryptoOptions, bitChatInfo.PeerEmailAddress, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, bitChatInfo.NetworkStatus, bitChatInfo.InvitationSender, bitChatInfo.InvitationMessage);
                else
                    network = new BitChatNetwork(_internal.ConnectionManager, _trustedRootCertificates, _supportedCryptoOptions, bitChatInfo.NetworkName, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.PeerCertificateList, bitChatInfo.NetworkStatus);

                BitChat chat = _internal.CreateBitChat(network, bitChatInfo.MessageStoreID, bitChatInfo.MessageStoreKey, bitChatInfo.GroupImageDateModified, bitChatInfo.GroupImage, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs, bitChatInfo.EnableTracking, bitChatInfo.SendInvitation, bitChatInfo.Mute);
                _bitChats.Add(chat);
            }

            //check profile cert revocation
            ThreadPool.QueueUserWorkItem(CheckCertificateRevocationAsync, new Certificate[] { _profile.LocalCertificateStore.Certificate });

            //check trusted root cert revocation
            ThreadPool.QueueUserWorkItem(CheckCertificateRevocationAsync, _trustedRootCertificates);
        }

        public BitChat CreateBitChat(MailAddress peerEmailAddress, string sharedSecret, bool enableTracking, string invitationMessage)
        {
            BitChatNetwork network = new BitChatNetwork(_internal.ConnectionManager, _trustedRootCertificates, _supportedCryptoOptions, peerEmailAddress, sharedSecret, null, new Certificate[] { }, BitChatNetworkStatus.Online, _profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress.Address, invitationMessage);
            BitChat bitChat = _internal.CreateBitChat(network, BinaryID.GenerateRandomID160().ToString(), BinaryID.GenerateRandomID256().ID, 0, null, new BitChatProfile.SharedFileInfo[] { }, null, enableTracking, true, false);

            lock (_bitChats)
            {
                _bitChats.Add(bitChat);
            }

            return bitChat;
        }

        public BitChat CreateBitChat(string networkName, string sharedSecret, bool enableTracking)
        {
            BitChatNetwork network = new BitChatNetwork(_internal.ConnectionManager, _trustedRootCertificates, _supportedCryptoOptions, networkName, sharedSecret, null, new Certificate[] { }, BitChatNetworkStatus.Online);
            BitChat bitChat = _internal.CreateBitChat(network, BinaryID.GenerateRandomID160().ToString(), BinaryID.GenerateRandomID256().ID, -1, null, new BitChatProfile.SharedFileInfo[] { }, null, enableTracking, false, false);

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

            IPEndPoint[] dhtNodes = _internal.ConnectionManager.DhtClient.GetAllNodes();
            if (dhtNodes.Length > 0)
                _profile.BootstrapDhtNodes = dhtNodes;

            if (_profile.AllowInboundInvitations)
                _internal.EnableInboundInvitationTracker();
            else
                _internal.DisableInboundInvitationTracker();
        }

        public void ReCheckConnectivity()
        {
            _internal.ConnectionManager.ReCheckConnectivity();
        }

        #endregion

        #region properties

        public BitChatProfile Profile
        { get { return _profile; } }

        public BinaryID LocalPeerID
        { get { return _internal.ConnectionManager.LocalPeerID; } }

        public int LocalPort
        { get { return _internal.ConnectionManager.LocalPort; } }

        public BinaryID DhtNodeID
        { get { return _internal.ConnectionManager.DhtClient.LocalNodeID; } }

        public int DhtLocalPort
        { get { return _internal.ConnectionManager.DhtClient.LocalPort; } }

        public int DhtTotalNodes
        { get { return _internal.ConnectionManager.DhtClient.GetTotalNodes(); } }

        public InternetConnectivityStatus InternetStatus
        { get { return _internal.ConnectionManager.InternetStatus; } }

        public UPnPDeviceStatus UPnPStatus
        { get { return _internal.ConnectionManager.UPnPStatus; } }

        public IPAddress UPnPDeviceIP
        { get { return _internal.ConnectionManager.UPnPDeviceIP; } }

        public IPAddress UPnPExternalIP
        { get { return _internal.ConnectionManager.UPnPExternalIP; } }

        public IPEndPoint ExternalEndPoint
        { get { return _internal.ConnectionManager.ExternalEndPoint; } }

        public IPEndPoint[] TcpRelayNodes
        { get { return _internal.TcpRelayNodes; } }

        #endregion

        private class InternalBitChatService : IDisposable
        {
            #region variables

            const int BIT_CHAT_INVITATION_TRACKER_UPDATE_INTERVAL = 120;

            BitChatClient _service;

            ConnectionManager _connectionManager;
            LocalPeerDiscovery _localDiscovery;
            Dictionary<BinaryID, BitChatNetwork> _networks = new Dictionary<BinaryID, BitChatNetwork>();

            //inbound invitation tracker
            readonly BinaryID _maskedEmailAddress;
            TrackerManager _inboundInvitationTrackerManager;

            //relay nodes
            const int TCP_RELAY_MAX_CONNECTIONS = 3;
            const int TCP_RELAY_KEEP_ALIVE_INTERVAL = 30000; //30sec

            Dictionary<IPEndPoint, Connection> _tcpRelayConnections = new Dictionary<IPEndPoint, Connection>();
            Timer _tcpRelayConnectionKeepAliveTimer;

            #endregion

            #region constructor

            public InternalBitChatService(BitChatClient service)
            {
                _service = service;

                _maskedEmailAddress = BitChatNetwork.GetMaskedEmailAddress(_service._profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress);

                _connectionManager = new ConnectionManager(_service._profile);
                _connectionManager.InternetConnectivityStatusChanged += ConnectionManager_InternetConnectivityStatusChanged;
                _connectionManager.BitChatNetworkChannelInvitation += ConnectionManager_BitChatNetworkChannelInvitation;
                _connectionManager.BitChatNetworkChannelRequest += ConnectionManager_BitChatNetworkChannelRequest;
                _connectionManager.TcpRelayPeersAvailable += ConnectionManager_TcpRelayPeersAvailable;

                LocalPeerDiscovery.StartListener(41733);
                _localDiscovery = new LocalPeerDiscovery(_connectionManager.LocalPort);
                _localDiscovery.PeerDiscovered += LocalDiscovery_PeerDiscovered;

                if (_service._profile.AllowInboundInvitations)
                    EnableInboundInvitationTracker();
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

                    if (_inboundInvitationTrackerManager != null)
                        _inboundInvitationTrackerManager.Dispose();

                    _disposed = true;
                }
            }

            #endregion

            #region Tcp Relay Client Implementation

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

                    if (_service._profile.AllowInboundInvitations)
                        networkIDs.Add(_maskedEmailAddress);

                    lock (_networks)
                    {
                        foreach (KeyValuePair<BinaryID, BitChatNetwork> network in _networks)
                        {
                            if (network.Value.Status == BitChatNetworkStatus.Online)
                                networkIDs.Add(network.Key);
                        }
                    }

                    if (!viaConnection.RequestStartTcpRelay(networkIDs.ToArray(), _service._profile.TrackerURIs))
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

            public BitChat CreateBitChat(BitChatNetwork network, string messageStoreID, byte[] messageStoreKey, long groupImageDateModified, byte[] groupImage, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs, bool enableTracking, bool sendInvitation, bool mute)
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
                    network.Disposed += Network_Disposed;
                }

                if (trackerURIs == null)
                    trackerURIs = _service._profile.TrackerURIs;

                if (enableTracking && (network.Status == BitChatNetworkStatus.Online))
                    SetupTcpRelay(network.NetworkID, trackerURIs); //starts tcp relay if available or needed

                BitChat bitChat = new BitChat(_service._syncCxt, _localDiscovery, network, messageStoreID, messageStoreKey, groupImageDateModified, groupImage, sharedFileInfoList, trackerURIs, enableTracking, sendInvitation, mute);

                bitChat.Leave += BitChat_Leave;

                return bitChat;
            }

            public void EnableInboundInvitationTracker()
            {
                if (_inboundInvitationTrackerManager == null)
                {
                    _inboundInvitationTrackerManager = new TrackerManager(_maskedEmailAddress, _connectionManager.LocalPort, _connectionManager.DhtClient, BIT_CHAT_INVITATION_TRACKER_UPDATE_INTERVAL);
                    _inboundInvitationTrackerManager.StartTracking();

                    _localDiscovery.StartTracking(_maskedEmailAddress);

                    SetupTcpRelay(_maskedEmailAddress, new Uri[] { });
                }
            }

            public void DisableInboundInvitationTracker()
            {
                if (_inboundInvitationTrackerManager != null)
                {
                    _inboundInvitationTrackerManager.StopTracking();
                    _inboundInvitationTrackerManager = null;

                    _localDiscovery.StopTracking(_maskedEmailAddress);

                    RemoveTcpRelay(_maskedEmailAddress);
                }
            }

            #endregion

            #region private

            private void Network_Disposed(object sender, EventArgs e)
            {
                lock (_networks)
                {
                    _networks.Remove((sender as BitChatNetwork).NetworkID);
                }
            }

            private void BitChat_Leave(object sender, EventArgs e)
            {
                BitChat bitChat = sender as BitChat;

                lock (_service._bitChats)
                {
                    _service._bitChats.Remove(bitChat);
                }

                RemoveTcpRelay(bitChat.NetworkID);
            }

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

            private void ConnectionManager_BitChatNetworkChannelInvitation(BinaryID networkID, IPEndPoint peerEP, string message)
            {
                bool networkExists;

                lock (_networks)
                {
                    networkExists = _networks.ContainsKey(networkID);
                }

                if (!networkExists)
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

            #endregion

            #region properties

            public ConnectionManager ConnectionManager
            { get { return _connectionManager; } }

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
