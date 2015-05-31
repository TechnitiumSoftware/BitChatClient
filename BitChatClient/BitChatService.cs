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
                _bitChats.Add(_manager.CreateBitChat(bitChatInfo.NetworkName, bitChatInfo.SharedSecret, bitChatInfo.PeerCertificateList, bitChatInfo.SharedFileList, bitChatInfo.TrackerURIs));

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

        public BitChat CreateBitChat(string networkName, string sharedSecret)
        {
            BitChat bitChat = _manager.CreateBitChat(networkName, sharedSecret, new Certificate[] { }, new BitChatProfile.SharedFileInfo[] { }, null);

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

        public UPnPStatus UPnPStatus
        { get { return _manager.UPnPStatus; } }

        public IPEndPoint ExternalSelfEP
        { get { return _manager.ExternalSelfEP; } }

        #endregion

        private class InternalBitChatService : IBitChatManager, IBitChatNetworkManager, ISecureChannelSecurityManager, IDisposable
        {
            #region variables

            BitChatService _service;
            BitChatProfile _profile;
            Certificate[] _trustedRootCertificates;
            SecureChannelCryptoOptionFlags _supportedCryptoOptions;

            ConnectionManager _connectionManager;
            LocalPeerDiscovery _localDiscovery;
            Dictionary<BinaryID, BitChatNetwork> _networks = new Dictionary<BinaryID, BitChatNetwork>();

            #endregion

            #region constructor

            public InternalBitChatService(BitChatService service, BitChatProfile profile, Certificate[] trustedRootCertificates, SecureChannelCryptoOptionFlags supportedCryptoOptions)
            {
                _service = service;
                _profile = profile;
                _trustedRootCertificates = trustedRootCertificates;
                _supportedCryptoOptions = supportedCryptoOptions;

                _connectionManager = new ConnectionManager(_profile.LocalEP, ChannelRequest);
                _localDiscovery = new LocalPeerDiscovery(41733, _connectionManager.LocalEP.Port);
                _localDiscovery.PeerDiscovered += _localDiscovery_PeerDiscovered;

                _profile.LocalEP = _connectionManager.LocalEP;
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
                    if (_localDiscovery != null)
                        _localDiscovery.Dispose();

                    if (_connectionManager != null)
                        _connectionManager.Dispose();

                    _disposed = true;
                }
            }

            #endregion

            #region public

            public BitChat CreateBitChat(string networkName, string sharedSecret, Certificate[] knownPeerCerts, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs)
            {
                BitChatNetwork network = new BitChatNetwork(networkName, sharedSecret, knownPeerCerts, this, this);

                lock (_networks)
                {
                    _networks.Add(network.NetworkID, network);
                }

                _localDiscovery.StartAnnounce(network.NetworkID);

                if (trackerURIs == null)
                    trackerURIs = _profile.TrackerURIs;

                return new BitChat(this, _profile, network, sharedFileInfoList, trackerURIs);
            }

            public void UpdateProfile(BitChatProfile.BitChatInfo[] bitChatInfoList)
            {
                _profile.UpdateBitChatInfo(bitChatInfoList);
            }

            #endregion

            #region LocalDiscovery support

            private void _localDiscovery_PeerDiscovered(LocalPeerDiscovery sender, IPEndPoint peerEP, List<BinaryID> networkIDs)
            {
                foreach (BinaryID networkID in networkIDs)
                {
                    lock (_networks)
                    {
                        if (_networks.ContainsKey(networkID))
                            _networks[networkID].MakeConnection(peerEP);
                    }
                }
            }

            #endregion

            #region IBitChatManager support

            public IPEndPoint GetLocalEP()
            {
                return _connectionManager.LocalEP;
            }

            public void RemoveBitChat(BitChat chat)
            {
                lock (_service._bitChats)
                {
                    _service._bitChats.Remove(chat);
                }
            }

            #endregion

            #region IBitChatNetworkManager support

            public Connection MakeConnection(IPEndPoint peerEP)
            {
                return _connectionManager.MakeConnection(peerEP);
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

            #region ChannelRequest support

            private void ChannelRequest(Connection connection, BinaryID channelName, ChannelType type, Stream channel)
            {
                try
                {
                    switch (type)
                    {
                        case ChannelType.BitChatNetwork:
                            BitChatNetwork network;

                            lock (_networks)
                            {
                                network = _networks[channelName];
                            }

                            SecureChannelStream secureChannel = new SecureChannelServerStream(channel, connection.RemotePeerEP, _profile.LocalCertificateStore, _trustedRootCertificates, this, _supportedCryptoOptions, network.SharedSecret);

                            network.JoinNetwork(secureChannel.RemotePeerCertificate.IssuedTo.EmailAddress.Address, secureChannel, _profile.CheckCertificateRevocationList);
                            break;
                    }
                }
                catch
                {
                    channel.Dispose();
                }
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

            public UPnPStatus UPnPStatus
            { get { return _connectionManager.UPnPStatus; } }

            public IPEndPoint ExternalSelfEP
            { get { return _connectionManager.ExternalSelfEP; } }

            #endregion
        }
    }
}
