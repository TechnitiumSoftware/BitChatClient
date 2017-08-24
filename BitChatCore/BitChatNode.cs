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
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore
{
    public delegate void InvalidCertificateDetected(BitChatNode client, InvalidCertificateException e);
    public delegate void BitChatInvitation(BitChatNode client, BitChat chat);

    public class BitChatNode : IDisposable
    {
        #region events

        public event InvalidCertificateDetected InvalidCertificateDetected;
        public event BitChatInvitation BitChatInvitationReceived;

        #endregion

        #region variables

        const int BIT_CHAT_INVITATION_TRACKER_UPDATE_INTERVAL = 120;

        readonly SynchronizationContext _syncCxt = SynchronizationContext.Current;

        readonly BitChatProfile _profile;
        readonly Certificate[] _trustedRootCertificates;
        readonly SecureChannelCryptoOptionFlags _supportedCryptoOptions;

        readonly Dictionary<BinaryNumber, BitChat> _chats = new Dictionary<BinaryNumber, BitChat>(10);

        ConnectionManager _connectionManager;
        LocalPeerDiscovery _localDiscovery;
        TcpRelayClient _tcpRelayClient;

        //inbound invitation tracker
        readonly BinaryNumber _maskedEmailAddress;
        TrackerManager _inboundInvitationDhtOnlyTrackerClient;

        #endregion

        #region constructor

        static BitChatNode()
        {
            if (ServicePointManager.DefaultConnectionLimit < 100)
                ServicePointManager.DefaultConnectionLimit = 100;
        }

        public BitChatNode(BitChatProfile profile, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions)
        {
            _profile = profile;
            _trustedRootCertificates = trustedRootCertificates;
            _supportedCryptoOptions = supportedCryptoOptions;

            _maskedEmailAddress = BitChatNetwork.GetMaskedEmailAddress(_profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress);

            _profile.ProxyUpdated += profile_ProxyUpdated;
            _profile.ProfileImageChanged += profile_ProfileImageChanged;
        }

        #endregion

        #region IDisposable

        ~BitChatNode()
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
                lock (_chats)
                {
                    foreach (KeyValuePair<BinaryNumber, BitChat> chat in _chats)
                        chat.Value.Dispose();

                    _chats.Clear();
                }

                if (_tcpRelayClient != null)
                    _tcpRelayClient.Dispose();

                if (_connectionManager != null)
                    _connectionManager.Dispose();

                if (_localDiscovery != null)
                    _localDiscovery.Dispose();

                if (_inboundInvitationDhtOnlyTrackerClient != null)
                    _inboundInvitationDhtOnlyTrackerClient.Dispose();

                TcpRelayService.StopAllTcpRelays();

                _disposed = true;
            }
        }

        #endregion

        #region private event functions

        private void RaiseEventBitChatInvitationReceived(BitChat chat)
        {
            _syncCxt.Send(BitChatInvitationReceivedCallback, chat);
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

        private void profile_ProxyUpdated(object sender, EventArgs e)
        {
            //stop all existing relay connections if any; internet connectivity check will auto start tcp relay client if needed via new proxy route
            if (_tcpRelayClient != null)
            {
                _tcpRelayClient.Dispose();
                _tcpRelayClient = null;
            }

            _connectionManager.ClientProfileProxyUpdated();

            lock (_chats)
            {
                foreach (KeyValuePair<BinaryNumber, BitChat> chat in _chats)
                {
                    try
                    {
                        chat.Value.ClientProfileProxyUpdated();
                    }
                    catch
                    { }
                }
            }
        }

        private void profile_ProfileImageChanged(object sender, EventArgs e)
        {
            lock (_chats)
            {
                foreach (KeyValuePair<BinaryNumber, BitChat> chat in _chats)
                {
                    try
                    {
                        chat.Value.ClientProfileImageChanged();
                    }
                    catch
                    { }
                }
            }
        }

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

        private void CreateInvitationPrivateChat(BinaryNumber hashedPeerEmailAddress, BinaryNumber networkID, IPEndPoint peerEP, string message)
        {
            BitChatNetwork network = new BitChatNetwork(_connectionManager, _trustedRootCertificates, _supportedCryptoOptions, hashedPeerEmailAddress, networkID, null, BitChatNetworkStatus.Offline, peerEP.ToString(), message);
            BitChat chat = CreateBitChat(network, BinaryNumber.GenerateRandomNumber160().ToString(), BinaryNumber.GenerateRandomNumber256().Number, 0, null, new BitChatProfile.SharedFileInfo[] { }, _profile.TrackerURIs, true, false, false);

            RaiseEventBitChatInvitationReceived(chat);
        }

        private BitChat CreateBitChat(BitChatNetwork network, string messageStoreID, byte[] messageStoreKey, long groupImageDateModified, byte[] groupImage, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs, bool enableTracking, bool sendInvitation, bool mute)
        {
            BitChat chat;

            lock (_chats)
            {
                if (_chats.ContainsKey(network.NetworkID))
                {
                    if (network.Type == BitChatNetworkType.PrivateChat)
                        throw new BitChatException("Bit Chat for '" + network.NetworkName + "' already exists.");
                    else
                        throw new BitChatException("Bit Chat group '" + network.NetworkName + "' already exists.");
                }

                chat = new BitChat(_syncCxt, _localDiscovery, network, messageStoreID, messageStoreKey, groupImageDateModified, groupImage, sharedFileInfoList, trackerURIs, enableTracking, sendInvitation, mute);

                chat.Leave += BitChat_Leave;
                chat.SetupTcpRelay += BitChat_SetupTcpRelay;
                chat.RemoveTcpRelay += BitChat_RemoveTcpRelay;
                network.NetworkChanged += Network_NetworkChanged;

                _chats.Add(chat.NetworkID, chat);
            }

            if (enableTracking && (network.Status == BitChatNetworkStatus.Online))
            {
                //setup tcp relay for network if available
                if (_tcpRelayClient != null)
                    _tcpRelayClient.AddNetwork(network.NetworkID, trackerURIs);
            }

            return chat;
        }

        private void ManageInboundInvitationTracking()
        {
            if (_profile.AllowInboundInvitations)
            {
                if (_profile.AllowOnlyLocalInboundInvitations)
                    DisableInboundInvitationTracker();
                else
                    EnableInboundInvitationTracker();

                _localDiscovery.StartTracking(_maskedEmailAddress); //start local tracking
            }
            else
            {
                DisableInboundInvitationTracker();
                _localDiscovery.StopTracking(_maskedEmailAddress); //stop local tracking
            }
        }

        private void EnableInboundInvitationTracker()
        {
            if (_inboundInvitationDhtOnlyTrackerClient == null)
            {
                _inboundInvitationDhtOnlyTrackerClient = new TrackerManager(_maskedEmailAddress, _connectionManager.LocalPort, _connectionManager.IPv4DhtNode, _connectionManager.IPv6DhtNode, BIT_CHAT_INVITATION_TRACKER_UPDATE_INTERVAL);
                _inboundInvitationDhtOnlyTrackerClient.StartTracking();

                if (_tcpRelayClient != null)
                    _tcpRelayClient.AddNetwork(_maskedEmailAddress, new Uri[] { });
            }
        }

        private void DisableInboundInvitationTracker()
        {
            if (_inboundInvitationDhtOnlyTrackerClient != null)
            {
                _inboundInvitationDhtOnlyTrackerClient.StopTracking();
                _inboundInvitationDhtOnlyTrackerClient = null;

                if (_tcpRelayClient != null)
                    _tcpRelayClient.RemoveNetwork(_maskedEmailAddress);
            }
        }

        private void BitChat_Leave(object sender, EventArgs e)
        {
            BitChat chat = sender as BitChat;

            lock (_chats)
            {
                _chats.Remove(chat.NetworkID);
            }

            if (_tcpRelayClient != null)
                _tcpRelayClient.RemoveNetwork(chat.NetworkID);
        }

        private void BitChat_SetupTcpRelay(object sender, EventArgs e)
        {
            BitChat chat = sender as BitChat;

            if (_tcpRelayClient != null)
                _tcpRelayClient.AddNetwork(chat.NetworkID, chat.GetTrackerURIs());
        }

        private void BitChat_RemoveTcpRelay(object sender, EventArgs e)
        {
            BitChat chat = sender as BitChat;

            if (_tcpRelayClient != null)
                _tcpRelayClient.RemoveNetwork(chat.NetworkID);
        }

        private void Network_NetworkChanged(BitChatNetwork network, BinaryNumber newNetworkID)
        {
            lock (_chats)
            {
                BitChat chat = _chats[network.NetworkID];

                _chats.Add(newNetworkID, chat);
                _chats.Remove(network.NetworkID);
            }
        }

        private void LocalDiscovery_PeerDiscovered(LocalPeerDiscovery sender, IPEndPoint peerEP, BinaryNumber networkID)
        {
            lock (_chats)
            {
                if (_chats.ContainsKey(networkID))
                {
                    _chats[networkID].Network.MakeConnection(peerEP);
                }
                else
                {
                    foreach (KeyValuePair<BinaryNumber, BitChat> chat in _chats)
                    {
                        if (networkID.Equals(chat.Value.Network.MaskedPeerEmailAddress))
                        {
                            chat.Value.Network.SendInvitation(new IPEndPoint[] { peerEP });
                            break;
                        }
                    }
                }
            }
        }

        private void ConnectionManager_IPv4InternetConnectivityStatusChanged(object sender, EventArgs e)
        {
            IPEndPoint externalEP = _connectionManager.IPv4ExternalEndPoint;

            if (externalEP == null)
            {
                //no incoming connection possible; start tcp relay client
                if (_tcpRelayClient == null)
                    _tcpRelayClient = new TcpRelayClient(_connectionManager);
            }
            else
            {
                //can receive incoming connection; no need for running tcp relay client
                if (_tcpRelayClient != null)
                {
                    _tcpRelayClient.Dispose();
                    _tcpRelayClient = null;
                }
            }
        }

        private void ConnectionManager_BitChatNetworkChannelInvitation(BinaryNumber hashedPeerEmailAddress, IPEndPoint peerEP, string message)
        {
            BinaryNumber networkID = BitChatNetwork.GetNetworkID(_profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress, hashedPeerEmailAddress);
            bool networkExists;

            lock (_chats)
            {
                networkExists = _chats.ContainsKey(networkID);
            }

            if (!networkExists)
                CreateInvitationPrivateChat(hashedPeerEmailAddress, networkID, peerEP, message);
        }

        private void ConnectionManager_BitChatNetworkChannelRequest(Connection connection, BinaryNumber channelName, Stream channel)
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

        private void ConnectionManager_TcpRelayPeersAvailable(Connection viaConnection, BinaryNumber channelName, List<IPEndPoint> peerEPs)
        {
            try
            {
                BitChatNetwork network = FindBitChatNetwork(viaConnection, channelName);

                if ((network != null) && (network.Status == BitChatNetworkStatus.Online))
                    network.MakeConnection(viaConnection, peerEPs);
            }
            catch
            { }
        }

        private BitChatNetwork FindBitChatNetwork(Connection connection, BinaryNumber channelName)
        {
            //find network by channel name
            lock (_chats)
            {
                foreach (KeyValuePair<BinaryNumber, BitChat> chat in _chats)
                {
                    BitChatNetwork network = chat.Value.Network;

                    if (network.Status == BitChatNetworkStatus.Online)
                    {
                        BinaryNumber computedChannelName = network.GetChannelName(connection.LocalPeerID, connection.RemotePeerID);

                        if (computedChannelName.Equals(channelName))
                            return network;
                    }
                }
            }

            return null;
        }

        #endregion

        #region public

        public void Start()
        {
            if (_connectionManager != null)
                return;

            //set min threads since the default value is too small for client at startup due to multiple chats queuing too many tasks immediately
            {
                int minWorker, minIOC;
                ThreadPool.GetMinThreads(out minWorker, out minIOC);

                minWorker = Environment.ProcessorCount * 32;
                ThreadPool.SetMinThreads(minWorker, minIOC);
            }

            //verify root certs
            foreach (Certificate trustedCert in _trustedRootCertificates)
                trustedCert.Verify(_trustedRootCertificates);

            //verify profile cert
            _profile.LocalCertificateStore.Certificate.Verify(_trustedRootCertificates);

            //start connection manager
            _connectionManager = new ConnectionManager(_profile);
            _connectionManager.IPv4InternetConnectivityStatusChanged += ConnectionManager_IPv4InternetConnectivityStatusChanged;
            _connectionManager.BitChatNetworkChannelInvitation += ConnectionManager_BitChatNetworkChannelInvitation;
            _connectionManager.BitChatNetworkChannelRequest += ConnectionManager_BitChatNetworkChannelRequest;
            _connectionManager.TcpRelayPeersAvailable += ConnectionManager_TcpRelayPeersAvailable;

            //start local peer discovery
            LocalPeerDiscovery.StartListener(41733);
            _localDiscovery = new LocalPeerDiscovery(_connectionManager.LocalPort);
            _localDiscovery.PeerDiscovered += LocalDiscovery_PeerDiscovered;

            //check inbound invitation tracking
            ManageInboundInvitationTracking();

            foreach (BitChatProfile.BitChatInfo bitChatInfo in _profile.BitChatInfoList)
            {
                try
                {
                    BitChatNetwork network;

                    if (bitChatInfo.Type == BitChatNetworkType.PrivateChat)
                    {
                        if (bitChatInfo.PeerEmailAddress == null)
                            network = new BitChatNetwork(_connectionManager, _trustedRootCertificates, _supportedCryptoOptions, bitChatInfo.HashedPeerEmailAddress, bitChatInfo.NetworkID, bitChatInfo.NetworkSecret, bitChatInfo.NetworkStatus, bitChatInfo.InvitationSender, bitChatInfo.InvitationMessage);
                        else
                            network = new BitChatNetwork(_connectionManager, _trustedRootCertificates, _supportedCryptoOptions, bitChatInfo.PeerEmailAddress, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.NetworkSecret, bitChatInfo.PeerCertificateList, bitChatInfo.NetworkStatus, bitChatInfo.InvitationSender, bitChatInfo.InvitationMessage);
                    }
                    else
                    {
                        network = new BitChatNetwork(_connectionManager, _trustedRootCertificates, _supportedCryptoOptions, bitChatInfo.NetworkName, bitChatInfo.SharedSecret, bitChatInfo.NetworkID, bitChatInfo.NetworkSecret, bitChatInfo.PeerCertificateList, bitChatInfo.NetworkStatus);
                    }

                    BitChat chat = CreateBitChat(network, bitChatInfo.MessageStoreID, bitChatInfo.MessageStoreKey, bitChatInfo.GroupImageDateModified, bitChatInfo.GroupImage, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs, bitChatInfo.EnableTracking, bitChatInfo.SendInvitation, bitChatInfo.Mute);
                    _chats.Add(chat.NetworkID, chat);
                }
                catch
                { }
            }

            //check profile cert revocation
            ThreadPool.QueueUserWorkItem(CheckCertificateRevocationAsync, new Certificate[] { _profile.LocalCertificateStore.Certificate });

            //check trusted root cert revocation
            ThreadPool.QueueUserWorkItem(CheckCertificateRevocationAsync, _trustedRootCertificates);
        }

        public BitChat CreatePrivateChat(MailAddress peerEmailAddress, string sharedSecret, bool enableTracking, bool dhtOnlyTracking, string invitationMessage)
        {
            Uri[] trackerURIs;

            if (dhtOnlyTracking)
                trackerURIs = new Uri[] { };
            else
                trackerURIs = _profile.TrackerURIs;

            BitChatNetwork network = new BitChatNetwork(_connectionManager, _trustedRootCertificates, _supportedCryptoOptions, peerEmailAddress, sharedSecret, null, null, new Certificate[] { }, BitChatNetworkStatus.Online, _profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress.Address, invitationMessage);
            return CreateBitChat(network, BinaryNumber.GenerateRandomNumber160().ToString(), BinaryNumber.GenerateRandomNumber256().Number, 0, null, new BitChatProfile.SharedFileInfo[] { }, trackerURIs, enableTracking, !string.IsNullOrEmpty(invitationMessage), false);
        }

        public BitChat CreateGroupChat(string networkName, string sharedSecret, bool enableTracking, bool dhtOnlyTracking)
        {
            Uri[] trackerURIs;

            if (dhtOnlyTracking)
                trackerURIs = new Uri[] { };
            else
                trackerURIs = _profile.TrackerURIs;

            BitChatNetwork network = new BitChatNetwork(_connectionManager, _trustedRootCertificates, _supportedCryptoOptions, networkName, sharedSecret, null, null, new Certificate[] { }, BitChatNetworkStatus.Online);
            return CreateBitChat(network, BinaryNumber.GenerateRandomNumber160().ToString(), BinaryNumber.GenerateRandomNumber256().Number, -1, null, new BitChatProfile.SharedFileInfo[] { }, trackerURIs, enableTracking, false, false);
        }

        public BitChat[] GetBitChatList()
        {
            lock (_chats)
            {
                BitChat[] chats = new BitChat[_chats.Values.Count];
                _chats.Values.CopyTo(chats, 0);

                return chats;
            }
        }

        public void UpdateProfile()
        {
            List<BitChatProfile.BitChatInfo> bitChatInfoList = new List<BitChatProfile.BitChatInfo>(_chats.Count);

            lock (_chats)
            {
                foreach (KeyValuePair<BinaryNumber, BitChat> chat in _chats)
                    bitChatInfoList.Add(chat.Value.GetBitChatInfo());
            }

            _profile.BitChatInfoList = bitChatInfoList.ToArray();

            List<IPEndPoint> bootstrapDhtNodes = new List<IPEndPoint>();

            bootstrapDhtNodes.AddRange(_connectionManager.IPv4DhtNode.GetAllNodeEPs());
            bootstrapDhtNodes.AddRange(_connectionManager.IPv6DhtNode.GetAllNodeEPs());

            if (bootstrapDhtNodes.Count > 0)
                _profile.BootstrapDhtNodes = bootstrapDhtNodes.ToArray();

            ManageInboundInvitationTracking();
        }

        public void ReCheckConnectivity()
        {
            _connectionManager.ReCheckConnectivity();
        }

        #endregion

        #region properties

        public BitChatProfile Profile
        { get { return _profile; } }

        public BinaryNumber LocalPeerID
        { get { return _connectionManager.LocalPeerID; } }

        public int LocalPort
        { get { return _connectionManager.LocalPort; } }

        public BinaryNumber IPv4DhtNodeID
        { get { return _connectionManager.IPv4DhtNode.LocalNodeID; } }

        public BinaryNumber IPv6DhtNodeID
        { get { return _connectionManager.IPv6DhtNode.LocalNodeID; } }

        public int IPv4DhtTotalNodes
        { get { return _connectionManager.IPv4DhtNode.GetTotalNodes(); } }

        public int IPv6DhtTotalNodes
        { get { return _connectionManager.IPv6DhtNode.GetTotalNodes(); } }

        public InternetConnectivityStatus IPv4InternetStatus
        { get { return _connectionManager.IPv4InternetStatus; } }

        public InternetConnectivityStatus IPv6InternetStatus
        { get { return _connectionManager.IPv6InternetStatus; } }

        public UPnPDeviceStatus UPnPStatus
        { get { return _connectionManager.UPnPStatus; } }

        public IPAddress UPnPDeviceIP
        { get { return _connectionManager.UPnPDeviceIP; } }

        public IPAddress UPnPExternalIP
        { get { return _connectionManager.UPnPExternalIP; } }

        public IPEndPoint IPv4ExternalEndPoint
        { get { return _connectionManager.IPv4ExternalEndPoint; } }

        public IPEndPoint IPv6ExternalEndPoint
        { get { return _connectionManager.IPv6ExternalEndPoint; } }

        public IPEndPoint[] TcpRelayNodes
        {
            get
            {
                if (_tcpRelayClient == null)
                    return new IPEndPoint[] { };

                return _tcpRelayClient.Nodes;
            }
        }

        #endregion
    }
}
