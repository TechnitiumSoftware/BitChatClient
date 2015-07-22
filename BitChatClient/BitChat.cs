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

using BitChatClient.FileSharing;
using BitChatClient.Network;
using BitChatClient.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient
{
    public delegate void PeerNotification(BitChat sender, BitChat.Peer peer);
    public delegate void PeerHasRevokedCertificate(BitChat sender, InvalidCertificateException ex);
    public delegate void MessageReceived(BitChat.Peer sender, string message);
    public delegate void FileAdded(BitChat sender, SharedFile sharedFile);
    public delegate void PeerSecureChannelException(BitChat sender, SecureChannelException ex);

    public enum BitChatNetworkStatus
    {
        NoNetwork = 0,
        PartialNetwork = 1,
        FullNetwork = 2
    }

    public class BitChat : IDisposable
    {
        #region events

        public event PeerNotification PeerAdded;
        public event PeerNotification PeerTyping;
        public event PeerHasRevokedCertificate PeerHasRevokedCertificate;
        public event PeerSecureChannelException PeerSecureChannelException;
        public event MessageReceived MessageReceived;
        public event FileAdded FileAdded;
        public event EventHandler Leave;

        #endregion

        #region variables

        SynchronizationContext _syncCxt = SynchronizationContext.Current;

        IBitChatManager _manager;
        BitChatProfile _profile;
        BitChatNetwork _network;

        List<BitChat.Peer> _peers = new List<Peer>();
        Dictionary<BinaryID, SharedFile> _sharedFiles = new Dictionary<BinaryID, SharedFile>();

        //torrent tracker
        const int _TRACKER_TIMER_CHECK_INTERVAL = 10000;
        List<TrackerClient> _trackers = new List<TrackerClient>();
        Timer _trackerUpdateTimer;

        //noop timer
        const int NOOP_PACKET_TIME_SECONDS = 15000;
        Timer _NOOPTimer;

        //network status
        Peer _selfPeer;
        List<PeerInfo> _connectedPeerList = new List<PeerInfo>();
        List<PeerInfo> _disconnectedPeerList = new List<PeerInfo>();
        BitChatNetworkStatus _networkStatus = BitChatNetworkStatus.NoNetwork;

        bool _updateNetworkStatusTriggered;
        bool _updateNetworkStatusRunning;
        object _updateNetworkStatusLock = new object();
        Timer _updateNetworkStatusTimer;
        Timer _reCheckNetworkStatusTimer; // to retry connection to disconnected peers
        const int NETWORK_STATUS_TIMER_INTERVAL = 1000;
        const int NETWORK_STATUS_RECHECK_TIMER_INTERVAL = 10000;

        #endregion

        #region constructor

        internal BitChat(IBitChatManager manager, BitChatProfile profile, BitChatNetwork network, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs)
        {
            _manager = manager;
            _profile = profile;
            _network = network;
            _network.VirtualPeerAdded += _network_VirtualPeerAdded;
            _network.VirtualPeerHasRevokedCertificate += _network_VirtualPeerHasRevokedCertificate;
            _network.VirtualPeerSecureChannelException += _network_VirtualPeerSecureChannelException;

            foreach (BitChatNetwork.VirtualPeer virtualPeer in _network.GetVirtualPeerList())
            {
                Peer peer = new Peer(virtualPeer, this);

                if (peer.IsSelf)
                    _selfPeer = peer;

                _peers.Add(peer);
            }

            foreach (BitChatProfile.SharedFileInfo info in sharedFileInfoList)
            {
                try
                {
                    _sharedFiles.Add(info.FileMetaData.FileID, SharedFile.LoadFile(info, this, _syncCxt));
                }
                catch
                { }
            }

            //start tracking
            _manager.StartLocalTracking(_network.NetworkID);
            StartTracking(trackerURIs);

            //start noop timer
            _NOOPTimer = new Timer(NOOPTimerCallback, null, NOOP_PACKET_TIME_SECONDS, Timeout.Infinite);

            //start network update timer
            _updateNetworkStatusTimer = new Timer(UpdateNetworkStatusCallback, null, NETWORK_STATUS_TIMER_INTERVAL, Timeout.Infinite);
            _reCheckNetworkStatusTimer = new Timer(ReCheckNetworkStatusCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        #endregion

        #region IDisposable

        ~BitChat()
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
                //stop noop timer
                if (_NOOPTimer != null)
                {
                    _NOOPTimer.Dispose();
                    _NOOPTimer = null;
                }

                //stop tracking
                _manager.StopLocalTracking(_network.NetworkID);
                StopTracking();

                //stop network
                _network.Dispose();

                //stop shared files
                foreach (KeyValuePair<BinaryID, SharedFile> sharedFile in _sharedFiles)
                    sharedFile.Value.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region private event functions

        private void RaiseEventPeerAdded(Peer peer)
        {
            _syncCxt.Post(PeerAddedCallback, peer);
        }

        private void PeerAddedCallback(object state)
        {
            try
            {
                PeerAdded(this, state as Peer);
            }
            catch { }
        }

        private void RaiseEventPeerTyping(Peer peer)
        {
            _syncCxt.Send(PeerTypingCallback, peer);
        }

        private void PeerTypingCallback(object state)
        {
            try
            {
                PeerTyping(this, state as Peer);
            }
            catch { }
        }

        private void RaiseEventPeerHasRevokedCertificate(InvalidCertificateException ex)
        {
            _syncCxt.Post(PeerHasRevokedCertificateCallback, ex);
        }

        private void PeerHasRevokedCertificateCallback(object state)
        {
            try
            {
                PeerHasRevokedCertificate(this, state as InvalidCertificateException);
            }
            catch { }
        }

        private void RaiseEventPeerSecureChannelException(SecureChannelException ex)
        {
            _syncCxt.Post(PeerSecureChannelExceptionCallback, ex);
        }

        private void PeerSecureChannelExceptionCallback(object state)
        {
            try
            {
                PeerSecureChannelException(this, state as SecureChannelException);
            }
            catch { }
        }

        private void RaiseEventMessageReceived(Peer peer, string message)
        {
            _syncCxt.Post(MessageReceivedCallback, new object[] { peer, message });
        }

        private void MessageReceivedCallback(object state)
        {
            try
            {
                MessageReceived((Peer)((object[])state)[0], (string)((object[])state)[1]);
            }
            catch { }
        }

        private void RaiseEventFileAdded(SharedFile file)
        {
            _syncCxt.Post(FileAddedCallback, file);
        }

        private void FileAddedCallback(object state)
        {
            try
            {
                FileAdded(this, state as SharedFile);
            }
            catch { }
        }

        private void RaiseEventLeave()
        {
            _syncCxt.Post(LeaveCallback, null);
        }

        private void LeaveCallback(object state)
        {
            try
            {
                Leave(this, EventArgs.Empty);
            }
            catch { }
        }

        #endregion

        #region public

        internal BitChatProfile.BitChatInfo GetBitChatInfo()
        {
            List<Certificate> peerCerts = new List<Certificate>();
            List<BitChatProfile.SharedFileInfo> sharedFileInfo = new List<BitChatProfile.SharedFileInfo>();
            List<Uri> trackerURIs = new List<Uri>();

            lock (_peers)
            {
                foreach (BitChat.Peer peer in _peers)
                {
                    if (!peer.IsSelf)
                        peerCerts.Add(peer.PeerCertificate);
                }
            }

            lock (_sharedFiles)
            {
                foreach (KeyValuePair<BinaryID, SharedFile> sharedFile in _sharedFiles)
                {
                    if (sharedFile.Value.State != SharedFileState.Advertisement)
                        sharedFileInfo.Add(sharedFile.Value.GetSharedFileInfo());
                }
            }

            lock (_trackers)
            {
                foreach (TrackerClient tracker in _trackers)
                    trackerURIs.Add(tracker.TrackerUri);
            }

            if (_network.Type == BitChatNetworkType.PrivateChat)
                return new BitChatProfile.BitChatInfo(BitChatNetworkType.PrivateChat, _network.PeerEmailAddress.Address, _network.SharedSecret, _network.NetworkID, peerCerts.ToArray(), sharedFileInfo.ToArray(), trackerURIs.ToArray());
            else
                return new BitChatProfile.BitChatInfo(BitChatNetworkType.GroupChat, _network.NetworkName, _network.SharedSecret, _network.NetworkID, peerCerts.ToArray(), sharedFileInfo.ToArray(), trackerURIs.ToArray());
        }

        public BitChat.Peer[] GetPeerList()
        {
            lock (_peers)
            {
                return _peers.ToArray();
            }
        }

        public SharedFile[] GetSharedFileList()
        {
            lock (_sharedFiles)
            {
                SharedFile[] sharedFilesList = new SharedFile[_sharedFiles.Count];
                _sharedFiles.Values.CopyTo(sharedFilesList, 0);
                return sharedFilesList;
            }
        }

        public void SendTypingNotification()
        {
            byte[] packetData = BitChatMessage.CreateTypingNotification();
            _network.WritePacketBroadcast(packetData, 0, packetData.Length);
        }

        public void SendTextMessage(string message)
        {
            byte[] packetData = BitChatMessage.CreateTextMessage(message);
            _network.WritePacketBroadcast(packetData, 0, packetData.Length);
        }

        internal void WritePacketBroadcast(byte[] data, int offset, int count)
        {
            _network.WritePacketBroadcast(data, offset, count);
        }

        public void ShareFile(string filePath, string hashAlgo = "SHA1")
        {
            SharedFile sharedFile = SharedFile.ShareFile(filePath, hashAlgo, this, _syncCxt);

            lock (_sharedFiles)
            {
                if (!_sharedFiles.ContainsKey(sharedFile.MetaData.FileID))
                {
                    _sharedFiles.Add(sharedFile.MetaData.FileID, sharedFile);

                    if (FileAdded != null)
                        RaiseEventFileAdded(sharedFile);

                    //advertise file
                    SendFileAdvertisement(sharedFile);
                }
            }
        }

        public void LeaveChat()
        {
            //remove shared files
            lock (_sharedFiles)
            {
                List<SharedFile> _toRemove = new List<SharedFile>();

                foreach (KeyValuePair<BinaryID, SharedFile> sharedFile in _sharedFiles)
                    _toRemove.Add(sharedFile.Value);

                foreach (SharedFile sharedFile in _toRemove)
                    sharedFile.Remove(this);
            }

            //remove chat
            _manager.RemoveBitChat(this);

            //remove network
            _network.RemoveNetwork();

            //dispose
            this.Dispose();

            if (Leave != null)
                RaiseEventLeave();
        }

        internal void RemoveSharedFile(SharedFile file)
        {
            lock (_sharedFiles)
            {
                _sharedFiles.Remove(file.MetaData.FileID);
            }
        }

        #endregion

        #region private

        private void _network_VirtualPeerAdded(BitChatNetwork sender, BitChatNetwork.VirtualPeer virtualPeer)
        {
            Peer peer = new Peer(virtualPeer, this);

            lock (_peers)
            {
                _peers.Add(peer);
            }

            if (PeerAdded != null)
                RaiseEventPeerAdded(peer);
        }

        private void _network_VirtualPeerHasRevokedCertificate(BitChatNetwork sender, InvalidCertificateException ex)
        {
            if (PeerHasRevokedCertificate != null)
                RaiseEventPeerHasRevokedCertificate(ex);
        }

        private void _network_VirtualPeerSecureChannelException(BitChatNetwork sender, SecureChannelException ex)
        {
            if (PeerSecureChannelException != null)
                RaiseEventPeerSecureChannelException(ex);
        }

        private void SendFileAdvertisement(SharedFile sharedFile)
        {
            byte[] packetData = BitChatMessage.CreateFileAdvertisement(sharedFile.MetaData);
            _network.WritePacketBroadcast(packetData, 0, packetData.Length);
        }

        #endregion

        #region PeerExchange implementation

        private void DoPeerExchange()
        {
            //find connected peers 
            List<PeerInfo> peerList = _network.GetConnectedPeerList();
            List<Peer> onlinePeers = new List<Peer>();

            lock (_peers)
            {
                foreach (Peer currentPeer in _peers)
                {
                    if (currentPeer.IsOnline)
                        onlinePeers.Add(currentPeer);
                }
            }

            foreach (Peer onlinePeer in onlinePeers)
            {
                //send other peers ep list to online peer
                byte[] packetData = BitChatMessage.CreatePeerExchange(peerList);
                onlinePeer.WritePacket(packetData, 0, packetData.Length);
            }
        }

        private void TriggerUpdateNetworkStatus()
        {
            lock (_updateNetworkStatusLock)
            {
                if (!_updateNetworkStatusTriggered)
                {
                    _updateNetworkStatusTriggered = true;

                    if (!_updateNetworkStatusRunning)
                        _updateNetworkStatusTimer.Change(NETWORK_STATUS_TIMER_INTERVAL, Timeout.Infinite);
                }
            }
        }

        private void ReCheckNetworkStatusCallback(object state)
        {
            TriggerUpdateNetworkStatus();
        }

        private void UpdateNetworkStatusCallback(object state)
        {
            lock (_updateNetworkStatusLock)
            {
                if (_updateNetworkStatusRunning)
                    return;

                _updateNetworkStatusRunning = true;
                _updateNetworkStatusTriggered = false;
            }

            try
            {
                BitChatNetworkStatus oldStatus = _networkStatus;

                //find network wide connected peer ep list
                List<PeerInfo> uniqueConnectedPeerList = new List<PeerInfo>();

                List<Peer> onlinePeers = new List<Peer>();
                List<Peer> offlinePeers = new List<Peer>();

                lock (_peers)
                {
                    foreach (Peer currentPeer in _peers)
                    {
                        if (currentPeer.IsOnline)
                            onlinePeers.Add(currentPeer);
                        else
                            offlinePeers.Add(currentPeer);
                    }
                }

                foreach (Peer onlinePeer in onlinePeers)
                {
                    onlinePeer.UpdateUniqueConnectedPeerList(uniqueConnectedPeerList);
                }

                //find self connected & disconnected peer list
                List<PeerInfo> connectedPeerList;
                List<PeerInfo> disconnectedPeerList;

                connectedPeerList = _network.GetConnectedPeerList();

                //update self connected list
                UpdateUniquePeerList(uniqueConnectedPeerList, connectedPeerList);

                //remove self from unique connected peer list
                PeerInfo selfPeerInfo = _network.GetSelfPeerInfo();
                uniqueConnectedPeerList.Remove(selfPeerInfo);

                //update connected peer's network status
                foreach (Peer onlinePeer in onlinePeers)
                {
                    onlinePeer.UpdateNetworkStatus(uniqueConnectedPeerList);
                }

                foreach (Peer offlinePeer in offlinePeers)
                {
                    offlinePeer.SetNoNetworkStatus();
                }

                //find disconnected list
                disconnectedPeerList = GetMissingPeerList(connectedPeerList, uniqueConnectedPeerList);

                //update disconnected peer's network status
                List<PeerInfo> dummyUniqueConnectedPeerList = new List<PeerInfo>(1);
                dummyUniqueConnectedPeerList.Add(selfPeerInfo);

                foreach (PeerInfo peerInfo in disconnectedPeerList)
                {
                    //search all offline peers for comparison
                    foreach (Peer offlinePeer in offlinePeers)
                    {
                        if (offlinePeer.PeerCertificate.IssuedTo.EmailAddress.Address.Equals(peerInfo.PeerEmail))
                        {
                            offlinePeer.UpdateNetworkStatus(dummyUniqueConnectedPeerList);
                            break;
                        }
                    }
                }

                BitChatNetworkStatus networkStatus;

                if (disconnectedPeerList.Count > 0)
                {
                    networkStatus = BitChatNetworkStatus.PartialNetwork;

                    _network.MakeConnection(disconnectedPeerList);
                }
                else
                {
                    networkStatus = BitChatNetworkStatus.FullNetwork;
                }

                lock (_connectedPeerList)
                {
                    _connectedPeerList.Clear();
                    _connectedPeerList.AddRange(connectedPeerList);
                    _disconnectedPeerList = disconnectedPeerList;
                    _networkStatus = networkStatus;
                }

                if (_network.Type == BitChatNetworkType.PrivateChat)
                {
                    if (connectedPeerList.Count > 0)
                    {
                        _manager.PauseLocalAnnouncement(_network.NetworkID);
                        StopTracking();
                    }
                    else
                    {
                        _manager.ResumeLocalAnnouncement(_network.NetworkID);
                        StartTracking();
                    }
                }
                else
                {
                    if (connectedPeerList.Count > 0)
                        _manager.PauseLocalAnnouncement(_network.NetworkID);
                    else
                        _manager.ResumeLocalAnnouncement(_network.NetworkID);
                }

                if (oldStatus != networkStatus)
                    _selfPeer.RaiseEventNetworkStatusUpdated();
            }
            catch
            { }
            finally
            {
                lock (_updateNetworkStatusLock)
                {
                    if (_updateNetworkStatusTriggered)
                    {
                        _updateNetworkStatusTimer.Change(NETWORK_STATUS_TIMER_INTERVAL, Timeout.Infinite);
                    }
                    else
                    {
                        _updateNetworkStatusTriggered = false;

                        if (_networkStatus == BitChatNetworkStatus.PartialNetwork)
                            _reCheckNetworkStatusTimer.Change(NETWORK_STATUS_RECHECK_TIMER_INTERVAL, Timeout.Infinite);
                    }

                    _updateNetworkStatusRunning = false;
                }
            }
        }

        private static void UpdateUniquePeerList(List<PeerInfo> uniquePeerList, List<PeerInfo> inputList)
        {
            foreach (PeerInfo item in inputList)
            {
                if (!uniquePeerList.Contains(item))
                    uniquePeerList.Add(item);
            }
        }

        private static List<PeerInfo> GetMissingPeerList(List<PeerInfo> mainList, List<PeerInfo> checkList)
        {
            List<PeerInfo> missingList = new List<PeerInfo>();

            foreach (PeerInfo checkEP in checkList)
            {
                if (!mainList.Contains(checkEP))
                    missingList.Add(checkEP);
            }

            return missingList;
        }

        #endregion

        #region NOOP implementation

        private void NOOPTimerCallback(object state)
        {
            try
            {
                byte[] packetData = BitChatMessage.CreateNOOPMessage();
                _network.WritePacketBroadcast(packetData, 0, packetData.Length);
            }
            catch
            { }
            finally
            {
                if (_NOOPTimer != null)
                    _NOOPTimer.Change(NOOP_PACKET_TIME_SECONDS, Timeout.Infinite);
            }
        }

        #endregion

        #region TrackerClient implementation

        public TrackerClient[] GetTrackers()
        {
            lock (_trackers)
            {
                return _trackers.ToArray();
            }
        }

        public TrackerClient AddTracker(Uri trackerURI)
        {
            lock (_trackers)
            {
                foreach (TrackerClient tracker in _trackers)
                {
                    if (tracker.TrackerUri.Equals(trackerURI))
                        return null;
                }

                TrackerClient newTracker = TrackerClient.Create(trackerURI, _network.NetworkID.ID, TrackerClientID.CreateDefaultID());

                _trackers.Add(newTracker);

                return newTracker;
            }
        }

        public void RemoveTracker(TrackerClient tracker)
        {
            lock (_trackers)
            {
                _trackers.Remove(tracker);
            }
        }

        private void StartTracking(Uri[] trackerURIs = null)
        {
            if (trackerURIs != null)
            {
                lock (_trackers)
                {
                    _trackers.Clear();

                    foreach (Uri trackerURI in trackerURIs)
                        _trackers.Add(TrackerClient.Create(trackerURI, _network.NetworkID.ID, TrackerClientID.CreateDefaultID()));
                }
            }

            if (_trackerUpdateTimer == null)
                _trackerUpdateTimer = new Timer(UpdateTracker, TrackerClientEvent.Started, 1000, Timeout.Infinite);
        }

        private void StopTracking()
        {
            if (_trackerUpdateTimer == null)
                return;

            _trackerUpdateTimer.Dispose();
            _trackerUpdateTimer = null;
        }

        private void UpdateTracker(object state)
        {
            try
            {
                TrackerClientEvent @event;

                if (state == null)
                    @event = TrackerClientEvent.None;
                else
                    @event = (TrackerClientEvent)state;

                IPEndPoint localEP = _manager.GetLocalEP();

                lock (_trackers)
                {
                    foreach (TrackerClient tracker in _trackers)
                    {
                        if (!tracker.IsUpdating && (tracker.NextUpdateIn().TotalSeconds < 1))
                            ThreadPool.QueueUserWorkItem(new WaitCallback(UpdateTrackerAsync), new object[] { tracker, @event, localEP });
                    }
                }
            }
            catch
            { }
            finally
            {
                if (_trackerUpdateTimer != null)
                    _trackerUpdateTimer.Change(_TRACKER_TIMER_CHECK_INTERVAL, Timeout.Infinite);
            }
        }

        private void UpdateTrackerAsync(object state)
        {
            object[] parameters = state as object[];

            TrackerClient tracker = parameters[0] as TrackerClient;
            TrackerClientEvent @event = (TrackerClientEvent)parameters[1];
            IPEndPoint localEP = parameters[2] as IPEndPoint;

            try
            {
                tracker.Update(@event, localEP);

                _network.MakeConnection(tracker.Peers);
            }
            catch
            { }
        }

        #endregion

        #region properties

        public BitChatNetworkType NetworkType
        { get { return _network.Type; } }

        public MailAddress PeerEmailAddress
        { get { return _network.PeerEmailAddress; } }

        public string NetworkName
        { get { return _network.NetworkName; } }

        public string SharedSecret
        { get { return _network.SharedSecret; } }

        public Certificate LocalCertificate
        { get { return _profile.LocalCertificateStore.Certificate; } }

        public Peer SelfPeer
        { get { return _selfPeer; } }

        public bool IsTrackerRunning
        { get { return (_trackerUpdateTimer != null); } }

        #endregion

        public class Peer
        {
            #region events

            public event EventHandler StateChanged;
            public event EventHandler NetworkStatusUpdated;

            #endregion

            #region variables

            BitChatNetwork.VirtualPeer _virtualPeer;
            BitChat _bitchat;

            List<PeerInfo> _connectedPeerList = new List<PeerInfo>();
            List<PeerInfo> _disconnectedPeerList = new List<PeerInfo>();
            BitChatNetworkStatus _networkStatus = BitChatNetworkStatus.NoNetwork;

            bool _isSelfPeer;
            bool _lastStatus = false;

            #endregion

            #region constructor

            internal Peer(BitChatNetwork.VirtualPeer virtualPeer, BitChat bitchat)
            {
                _virtualPeer = virtualPeer;
                _bitchat = bitchat;

                _isSelfPeer = (_virtualPeer.PeerCertificate.IssuedTo.EmailAddress.Address == _bitchat._profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress.Address);

                _virtualPeer.PacketReceived += _virtualPeer_PacketReceived;
                _virtualPeer.StreamStateChanged += _virtualPeer_StreamStateChanged;
            }

            #endregion

            #region private event functions

            private void RaiseEventStateChanged()
            {
                _bitchat._syncCxt.Post(StateChangedCallback, null);
            }

            private void StateChangedCallback(object state)
            {
                try
                {
                    StateChanged(this, EventArgs.Empty);
                }
                catch { }
            }

            internal void RaiseEventNetworkStatusUpdated()
            {
                _bitchat._syncCxt.Post(NetworkStatusUpdatedCallBack, null);
            }

            private void NetworkStatusUpdatedCallBack(object state)
            {
                try
                {
                    NetworkStatusUpdated(this, EventArgs.Empty);
                }
                catch { }
            }

            #endregion

            #region private

            private void _virtualPeer_StreamStateChanged(object sender, EventArgs args)
            {
                //trigger peer exchange for entire network
                _bitchat.DoPeerExchange();

                if (_virtualPeer.IsOnline)
                {
                    DoSendSharedFileMetaData();
                }
                else
                {
                    lock (_bitchat._sharedFiles)
                    {
                        foreach (KeyValuePair<BinaryID, SharedFile> item in _bitchat._sharedFiles)
                        {
                            item.Value.RemovePeerOrSeeder(this);
                        }
                    }

                    lock (_connectedPeerList)
                    {
                        _connectedPeerList.Clear();
                        _disconnectedPeerList.Clear();
                    }

                    _bitchat.TriggerUpdateNetworkStatus();
                }

                if (!_isSelfPeer)
                {
                    if (_lastStatus != _virtualPeer.IsOnline)
                    {
                        _lastStatus = _virtualPeer.IsOnline;

                        if (StateChanged != null)
                            RaiseEventStateChanged();
                    }
                }
            }

            private void _virtualPeer_PacketReceived(BitChatNetwork.VirtualPeer sender, Stream packetDataStream, IPEndPoint remotePeerEP)
            {
                switch (BitChatMessage.ReadType(packetDataStream))
                {
                    case BitChatMessageType.TypingNotification:
                        #region Typing Notification
                        {
                            if (_bitchat.PeerTyping != null)
                                _bitchat.RaiseEventPeerTyping(this);

                            break;
                        }
                        #endregion

                    case BitChatMessageType.Text:
                        #region Text
                        {
                            if (_bitchat.MessageReceived != null)
                                _bitchat.RaiseEventMessageReceived(this, BitChatMessage.ReadTextMessage(packetDataStream));

                            break;
                        }
                        #endregion

                    case BitChatMessageType.FileAdvertisement:
                        #region FileAdvertisement
                        {
                            SharedFile sharedFile = SharedFile.PrepareDownloadFile(BitChatMessage.ReadFileAdvertisement(packetDataStream), _bitchat, this, _bitchat._profile, _bitchat._syncCxt);

                            lock (_bitchat._sharedFiles)
                            {
                                if (_bitchat._sharedFiles.ContainsKey(sharedFile.MetaData.FileID))
                                {
                                    //file already exists
                                    if (sharedFile.IsComplete)
                                    {
                                        //remove the seeder
                                        sharedFile.RemovePeerOrSeeder(this);
                                    }
                                    else
                                    {
                                        sharedFile.AddChat(_bitchat);
                                        sharedFile.AddSeeder(this); //add the seeder

                                        byte[] packetData = BitChatMessage.CreateFileParticipate(sharedFile.MetaData.FileID);
                                        WritePacket(packetData, 0, packetData.Length);
                                    }
                                }
                                else
                                {
                                    //file doesnt exists
                                    _bitchat._sharedFiles.Add(sharedFile.MetaData.FileID, sharedFile);

                                    if (_bitchat.FileAdded != null)
                                        _bitchat.RaiseEventFileAdded(sharedFile);
                                }
                            }

                            break;
                        }
                        #endregion

                    case BitChatMessageType.FileBlockRequest:
                        #region FileBlockRequest
                        {
                            FileBlockRequest blockRequest = BitChatMessage.ReadFileBlockRequest(packetDataStream);
                            SharedFile sharedFile;

                            lock (_bitchat._sharedFiles)
                            {
                                sharedFile = _bitchat._sharedFiles[blockRequest.FileID];
                            }

                            if (sharedFile.State == FileSharing.SharedFileState.Paused)
                                return;

                            if (!sharedFile.PeerExists(this))
                                return;

                            FileBlockDataPart blockData = sharedFile.ReadBlock(blockRequest.BlockNumber, blockRequest.BlockOffset, blockRequest.Length);

                            byte[] packetData = BitChatMessage.CreateFileBlockResponse(blockData);
                            _virtualPeer.WritePacket(packetData, 0, packetData.Length);

                            break;
                        }
                        #endregion

                    case BitChatMessageType.FileBlockResponse:
                        #region FileBlockResponse
                        {
                            FileBlockDataPart blockData = BitChatMessage.ReadFileBlockData(packetDataStream);
                            SharedFile sharedFile;

                            lock (_bitchat._sharedFiles)
                            {
                                sharedFile = _bitchat._sharedFiles[blockData.FileID];
                            }

                            if (sharedFile.State == FileSharing.SharedFileState.Paused)
                                return;

                            if (!sharedFile.PeerExists(this))
                                return;

                            SharedFile.FileBlockDownloadManager downloadingBlock = sharedFile.GetDownloadingBlock(blockData.BlockNumber);
                            if (downloadingBlock != null)
                            {
                                if (downloadingBlock.IsThisDownloadPeerSet(this))
                                {
                                    if (!downloadingBlock.SetBlockData(blockData))
                                    {
                                        byte[] packetData = BitChatMessage.CreateFileBlockRequest(downloadingBlock.GetNextRequest());
                                        _virtualPeer.WritePacket(packetData, 0, packetData.Length);
                                    }
                                }
                            }

                            break;
                        }
                        #endregion

                    case BitChatMessageType.FileBlockWanted:
                        #region FileBlockWanted
                        {
                            FileBlockWanted blockWanted = BitChatMessage.ReadFileBlockWanted(packetDataStream);
                            SharedFile sharedFile;

                            lock (_bitchat._sharedFiles)
                            {
                                sharedFile = _bitchat._sharedFiles[blockWanted.FileID];
                            }

                            if (sharedFile.State == FileSharing.SharedFileState.Paused)
                                return;

                            if (!sharedFile.PeerExists(this))
                                return;

                            if (sharedFile.IsBlockAvailable(blockWanted.BlockNumber))
                            {
                                byte[] packetData = BitChatMessage.CreateFileBlockAvailable(blockWanted);
                                _virtualPeer.WritePacket(packetData, 0, packetData.Length);
                            }

                            break;
                        }
                        #endregion

                    case BitChatMessageType.FileBlockAvailable:
                        #region FileBlockAvailable
                        {
                            FileBlockWanted blockWanted = BitChatMessage.ReadFileBlockWanted(packetDataStream);
                            SharedFile sharedFile;

                            lock (_bitchat._sharedFiles)
                            {
                                sharedFile = _bitchat._sharedFiles[blockWanted.FileID];
                            }

                            if (sharedFile.IsComplete)
                                return;

                            if (sharedFile.State == FileSharing.SharedFileState.Paused)
                                return;

                            if (!sharedFile.PeerExists(this))
                                return;

                            SharedFile.FileBlockDownloadManager downloadingBlock = sharedFile.GetDownloadingBlock(blockWanted.BlockNumber);
                            if (downloadingBlock != null)
                            {
                                if (downloadingBlock.SetDownloadPeer(this))
                                {
                                    byte[] packetData = BitChatMessage.CreateFileBlockRequest(downloadingBlock.GetNextRequest());
                                    _virtualPeer.WritePacket(packetData, 0, packetData.Length);
                                }
                            }

                            break;
                        }
                        #endregion

                    case BitChatMessageType.FileShareParticipate:
                        #region FileShareParticipate
                        {
                            BinaryID fileID = BitChatMessage.ReadFileID(packetDataStream);

                            lock (_bitchat._sharedFiles)
                            {
                                _bitchat._sharedFiles[fileID].AddPeer(this);
                            }

                            break;
                        }
                        #endregion

                    case BitChatMessageType.FileShareUnparticipate:
                        #region FileShareUnparticipate
                        {
                            BinaryID fileID = BitChatMessage.ReadFileID(packetDataStream);

                            lock (_bitchat._sharedFiles)
                            {
                                _bitchat._sharedFiles[fileID].RemovePeerOrSeeder(this);
                            }

                            break;
                        }
                        #endregion

                    case BitChatMessageType.PeerExchange:
                        #region PeerExchange

                        List<PeerInfo> peerList = BitChatMessage.ReadPeerExchange(packetDataStream);

                        lock (_connectedPeerList)
                        {
                            //reason: for the lock to be valid for use
                            _connectedPeerList.Clear();
                            _connectedPeerList.AddRange(peerList);
                        }

                        _bitchat._network.MakeConnection(peerList);

                        //start network status check
                        _bitchat.TriggerUpdateNetworkStatus();
                        break;

                        #endregion

                    case BitChatMessageType.NOOP:
                        Debug.Write("Peer.PacketReceived", "NOOP received from: " + sender.PeerCertificate.IssuedTo.EmailAddress.Address + " [" + remotePeerEP.Address.ToString() + "]");
                        break;
                }
            }

            private void DoSendSharedFileMetaData()
            {
                foreach (KeyValuePair<BinaryID, SharedFile> sharedFile in _bitchat._sharedFiles)
                {
                    if (sharedFile.Value.State == SharedFileState.Sharing)
                    {
                        byte[] packetData = BitChatMessage.CreateFileAdvertisement(sharedFile.Value.MetaData);
                        _virtualPeer.WritePacket(packetData, 0, packetData.Length);
                    }
                }
            }

            #endregion

            #region public

            internal void UpdateUniqueConnectedPeerList(List<PeerInfo> uniqueConnectedPeerList)
            {
                lock (_connectedPeerList)
                {
                    UpdateUniquePeerList(uniqueConnectedPeerList, _connectedPeerList);
                }
            }

            internal void UpdateNetworkStatus(List<PeerInfo> uniqueConnectedPeerList)
            {
                BitChatNetworkStatus oldStatus = _networkStatus;

                lock (_connectedPeerList)
                {
                    //compare this peer's connected peer list to the other peer list to find disconnected peer list
                    _disconnectedPeerList = GetMissingPeerList(_connectedPeerList, uniqueConnectedPeerList);
                    //remove self from the disconnected list
                    _disconnectedPeerList.Remove(_virtualPeer.GetPeerInfo());

                    if (_disconnectedPeerList.Count > 0)
                        _networkStatus = BitChatNetworkStatus.PartialNetwork;
                    else
                        _networkStatus = BitChatNetworkStatus.FullNetwork;
                }

                if (oldStatus != _networkStatus)
                    RaiseEventNetworkStatusUpdated();
            }

            internal void SetNoNetworkStatus()
            {
                BitChatNetworkStatus oldStatus = _networkStatus;

                _networkStatus = BitChatNetworkStatus.NoNetwork;

                if (oldStatus != _networkStatus)
                    RaiseEventNetworkStatusUpdated();
            }

            internal void WritePacket(byte[] packetData, int offset, int count)
            {
                _virtualPeer.WritePacket(packetData, offset, count);
            }

            public override string ToString()
            {
                if (_virtualPeer.PeerCertificate != null)
                    return _virtualPeer.PeerCertificate.IssuedTo.Name;

                return "unknown";
            }

            #endregion

            #region properties

            public PeerInfo[] ConnectedWith
            {
                get
                {
                    if (_isSelfPeer)
                    {
                        lock (_bitchat._connectedPeerList)
                        {
                            return _bitchat._connectedPeerList.ToArray();
                        }
                    }
                    else
                    {
                        lock (_connectedPeerList)
                        {
                            return _connectedPeerList.ToArray();
                        }
                    }
                }
            }

            public PeerInfo[] NotConnectedWith
            {
                get
                {
                    if (_isSelfPeer)
                    {
                        lock (_bitchat._connectedPeerList)
                        {
                            return _bitchat._disconnectedPeerList.ToArray();
                        }
                    }
                    else
                    {
                        lock (_connectedPeerList)
                        {
                            return _disconnectedPeerList.ToArray();
                        }
                    }
                }
            }

            public BitChatNetworkStatus NetworkStatus
            {
                get
                {
                    if (_isSelfPeer)
                        return _bitchat._networkStatus;
                    else
                        return _networkStatus;
                }
            }

            public SecureChannelCryptoOptionFlags CipherSuite
            { get { return _virtualPeer.CipherSuite; } }

            public Certificate PeerCertificate
            {
                get
                {
                    if (_isSelfPeer)
                        return _bitchat._profile.LocalCertificateStore.Certificate;
                    else
                        return _virtualPeer.PeerCertificate;
                }
            }

            public bool IsOnline
            {
                get
                {
                    if (_isSelfPeer)
                        return true;
                    else
                        return _virtualPeer.IsOnline;
                }
            }

            public bool IsSelf
            { get { return _isSelfPeer; } }

            #endregion
        }
    }
}
