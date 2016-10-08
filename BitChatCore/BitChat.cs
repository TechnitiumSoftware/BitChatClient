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

using BitChatCore.FileSharing;
using BitChatCore.Network;
using BitChatCore.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore
{
    public delegate void PeerNotification(BitChat chat, BitChat.Peer peer);
    public delegate void PeerHasRevokedCertificate(BitChat chat, InvalidCertificateException ex);
    public delegate void MessageNotification(BitChat.Peer peer, MessageItem message);
    public delegate void FileAddedNotification(BitChat.Peer peer, MessageItem message, SharedFile sharedFile);
    public delegate void FileRemovedNotification(BitChat chat, SharedFile sharedFile);
    public delegate void PeerSecureChannelException(BitChat chat, SecureChannelException ex);
    public delegate void PeerHasChangedCertificate(BitChat chat, Certificate cert);

    public enum BitChatConnectivityStatus
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
        public event PeerHasChangedCertificate PeerHasChangedCertificate;
        public event MessageNotification MessageReceived;
        public event MessageNotification MessageDeliveryNotification;
        public event FileAddedNotification FileAdded;
        public event FileRemovedNotification FileRemoved;
        public event PeerNotification GroupImageChanged;
        internal event EventHandler SetupTcpRelay;
        internal event EventHandler RemoveTcpRelay;
        internal event EventHandler Leave;

        #endregion

        #region variables

        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        const int BIT_CHAT_TRACKER_UPDATE_INTERVAL_SECONDS = 120;

        readonly SynchronizationContext _syncCxt;
        readonly LocalPeerDiscovery _localPeerDiscovery;
        readonly BitChatNetwork _network;
        readonly string _messageStoreID;
        readonly byte[] _messageStoreKey;
        bool _mute; //feature to let ui know to mute notifications for this chat

        readonly MessageStore _store;

        long _groupImageDateModified;
        byte[] _groupImage;

        readonly ReaderWriterLockSlim _peersLock = new ReaderWriterLockSlim();
        readonly List<Peer> _peers = new List<Peer>();

        readonly ReaderWriterLockSlim _sharedFilesLock = new ReaderWriterLockSlim();
        readonly Dictionary<BinaryID, SharedFile> _sharedFiles = new Dictionary<BinaryID, SharedFile>();

        //tracker
        readonly TrackerManager _trackerManager;
        bool _enableTracking;

        //outbound invitation
        readonly TrackerManager _outboundInvitationDhtOnlyTrackerClient;
        bool _sendInvitation;

        //noop timer
        const int NOOP_MESSAGE_TIMER_INTERVAL = 15000;
        Timer _NOOPTimer;

        //network status
        readonly Peer _selfPeer;
        readonly object _peerListLock = new object();
        List<PeerInfo> _connectedPeerList = new List<PeerInfo>();
        List<PeerInfo> _disconnectedPeerList = new List<PeerInfo>();
        BitChatConnectivityStatus _connectivityStatus = BitChatConnectivityStatus.NoNetwork;

        bool _updateNetworkStatusTriggered;
        bool _updateNetworkStatusRunning;
        readonly object _updateNetworkStatusLock = new object();
        Timer _updateNetworkStatusTimer;
        Timer _reCheckNetworkStatusTimer; // to retry connection to disconnected peers
        const int NETWORK_STATUS_TIMER_INTERVAL = 1000;
        const int NETWORK_STATUS_RECHECK_TIMER_INTERVAL = 10000;

        #endregion

        #region constructor

        internal BitChat(SynchronizationContext syncCxt, LocalPeerDiscovery localPeerDiscovery, BitChatNetwork network, string messageStoreID, byte[] messageStoreKey, long groupImageDateModified, byte[] groupImage, BitChatProfile.SharedFileInfo[] sharedFileInfoList, Uri[] trackerURIs, bool enableTracking, bool sendInvitation, bool mute)
        {
            _syncCxt = syncCxt;
            _localPeerDiscovery = localPeerDiscovery;

            _network = network;
            _network.VirtualPeerAdded += network_VirtualPeerAdded;
            _network.VirtualPeerHasRevokedCertificate += network_VirtualPeerHasRevokedCertificate;
            _network.VirtualPeerSecureChannelException += network_VirtualPeerSecureChannelException;
            _network.VirtualPeerHasChangedCertificate += network_VirtualPeerHasChangedCertificate;

            _messageStoreID = messageStoreID;
            _messageStoreKey = messageStoreKey;

            string messageStoreFolder = Path.Combine(_network.ConnectionManager.Profile.ProfileFolder, "messages");
            if (!Directory.Exists(messageStoreFolder))
                Directory.CreateDirectory(messageStoreFolder);

            _store = new MessageStore(new FileStream(Path.Combine(messageStoreFolder, messageStoreID + ".index"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), new FileStream(Path.Combine(messageStoreFolder, messageStoreID + ".data"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), _messageStoreKey);

            _groupImageDateModified = groupImageDateModified;
            _groupImage = groupImage;

            _mute = mute;

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
                    SharedFile sharedFile = SharedFile.LoadFile(info.FilePath, info.FileMetaData, info.BlockAvailable, info.State, _syncCxt);
                    _sharedFiles.Add(info.FileMetaData.FileID, sharedFile);

                    sharedFile.AddChat(this);
                }
                catch
                { }
            }

            //init tracking
            _trackerManager = new TrackerManager(_network.NetworkID, _network.ConnectionManager.LocalPort, _network.ConnectionManager.DhtClient, BIT_CHAT_TRACKER_UPDATE_INTERVAL_SECONDS);
            _trackerManager.Proxy = _network.ConnectionManager.Profile.Proxy;
            _trackerManager.DiscoveredPeers += TrackerManager_DiscoveredPeers;
            _enableTracking = enableTracking;

            if (_network.Status == BitChatNetworkStatus.Offline)
            {
                _trackerManager.AddTracker(trackerURIs);
            }
            else
            {
                //start local peer discovery
                _localPeerDiscovery.StartTracking(_network.NetworkID);
                _localPeerDiscovery.StartAnnouncement(_network.NetworkID);

                //enable tracking
                if (enableTracking)
                    _trackerManager.StartTracking(trackerURIs);
                else
                    _trackerManager.AddTracker(trackerURIs);
            }

            if (_network.Type == BitChatNetworkType.PrivateChat)
            {
                //send invitation
                _sendInvitation = sendInvitation;

                if ((_store.GetMessageCount() == 0) && !string.IsNullOrEmpty(_network.InvitationMessage))
                {
                    if (_network.NetworkName == null)
                        (new MessageItem("User [" + _network.InvitationSender + "] has sent you a private chat invitation.")).WriteTo(_store);

                    (new MessageItem(MessageType.InvitationMessage, DateTime.UtcNow, _network.InvitationSender, _network.InvitationMessage)).WriteTo(_store);

                    if (_network.NetworkName == null)
                        (new MessageItem("To accept the invitation, uncheck the 'Go Offline' chat option from the chat list.")).WriteTo(_store);
                }

                if (_sendInvitation)
                {
                    //init outbound invitation tracker
                    _outboundInvitationDhtOnlyTrackerClient = new TrackerManager(_network.MaskedPeerEmailAddress, _network.ConnectionManager.LocalPort, _network.ConnectionManager.DhtClient, 30, true);
                    _outboundInvitationDhtOnlyTrackerClient.DiscoveredPeers += OutboundInvitationDhtOnlyTrackerManager_DiscoveredPeers;

                    if (_network.Status == BitChatNetworkStatus.Online)
                    {
                        _outboundInvitationDhtOnlyTrackerClient.StartTracking();
                        _localPeerDiscovery.StartAnnouncement(_network.MaskedPeerEmailAddress);
                    }
                }
            }

            //start noop timer
            _NOOPTimer = new Timer(NOOPTimerCallback, null, NOOP_MESSAGE_TIMER_INTERVAL, Timeout.Infinite);

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

                if (_updateNetworkStatusTimer != null)
                {
                    _updateNetworkStatusTimer.Dispose();
                    _updateNetworkStatusTimer = null;
                }

                if (_reCheckNetworkStatusTimer != null)
                {
                    _reCheckNetworkStatusTimer.Dispose();
                    _reCheckNetworkStatusTimer = null;
                }

                //stop tracking
                _localPeerDiscovery.StopTracking(_network.NetworkID);
                _localPeerDiscovery.StopAnnouncement(_network.NetworkID);

                if (_trackerManager != null)
                    _trackerManager.Dispose();

                if (_outboundInvitationDhtOnlyTrackerClient != null)
                    _outboundInvitationDhtOnlyTrackerClient.Dispose();

                //stop shared files
                _sharedFilesLock.EnterWriteLock();
                try
                {
                    foreach (KeyValuePair<BinaryID, SharedFile> sharedFile in _sharedFiles)
                        sharedFile.Value.RemoveChat(this);

                    _sharedFiles.Clear();
                }
                finally
                {
                    _sharedFilesLock.ExitWriteLock();
                }

                _peersLock.EnterWriteLock();
                try
                {
                    _peers.Clear();
                }
                finally
                {
                    _peersLock.ExitWriteLock();
                }

                //close message store
                if (_store != null)
                    _store.Dispose();

                _sharedFilesLock.Dispose();
                _peersLock.Dispose();

                //stop network
                _network.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region private event functions

        private void RaiseEventPeerAdded(Peer peer)
        {
            _syncCxt.Send(PeerAddedCallback, peer);
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
            _syncCxt.Send(PeerHasRevokedCertificateCallback, ex);
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
            _syncCxt.Send(PeerSecureChannelExceptionCallback, ex);
        }

        private void PeerSecureChannelExceptionCallback(object state)
        {
            try
            {
                PeerSecureChannelException(this, state as SecureChannelException);
            }
            catch { }
        }

        private void RaiseEventPeerHasChangedCertificate(Certificate cert)
        {
            _syncCxt.Send(PeerHasChangedCertificateCallback, cert);
        }

        private void PeerHasChangedCertificateCallback(object state)
        {
            try
            {
                PeerHasChangedCertificate(this, state as Certificate);
            }
            catch { }
        }

        private void RaiseEventMessageReceived(Peer peer, MessageItem message)
        {
            _syncCxt.Send(MessageReceivedCallback, new object[] { peer, message });
        }

        private void MessageReceivedCallback(object state)
        {
            try
            {
                MessageReceived((Peer)((object[])state)[0], (MessageItem)((object[])state)[1]);
            }
            catch { }
        }

        private void RaiseEventMessageDeliveryNotification(Peer peer, MessageItem message)
        {
            _syncCxt.Send(MessageDeliveryNotificationCallback, new object[] { peer, message });
        }

        private void MessageDeliveryNotificationCallback(object state)
        {
            try
            {
                MessageDeliveryNotification((Peer)((object[])state)[0], (MessageItem)((object[])state)[1]);
            }
            catch { }
        }

        private void RaiseEventFileAdded(Peer peer, MessageItem message, SharedFile file)
        {
            _syncCxt.Send(FileAddedCallback, new object[] { peer, message, file });
        }

        private void FileAddedCallback(object state)
        {
            try
            {
                object[] parameters = state as object[];
                FileAdded(parameters[0] as Peer, parameters[1] as MessageItem, parameters[2] as SharedFile);
            }
            catch { }
        }

        private void RaiseEventFileRemoved(BitChat chat, SharedFile file)
        {
            _syncCxt.Send(FileRemovedCallback, new object[] { chat, file });
        }

        private void FileRemovedCallback(object state)
        {
            try
            {
                object[] parameters = state as object[];
                FileRemoved(parameters[0] as BitChat, parameters[1] as SharedFile);
            }
            catch { }
        }

        private void RaiseEventGroupImageChanged(Peer peer)
        {
            _syncCxt.Send(GroupImageChangedCallback, peer);
        }

        private void GroupImageChangedCallback(object state)
        {
            try
            {
                GroupImageChanged(this, state as Peer);
            }
            catch { }
        }

        private void RaiseEventLeave()
        {
            _syncCxt.Send(LeaveCallback, null);
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

        public Peer[] GetPeerList()
        {
            _peersLock.EnterReadLock();
            try
            {
                return _peers.ToArray();
            }
            finally
            {
                _peersLock.ExitReadLock();
            }
        }

        public SharedFile[] GetSharedFileList()
        {
            _sharedFilesLock.EnterReadLock();
            try
            {
                SharedFile[] sharedFilesList = new SharedFile[_sharedFiles.Count];
                _sharedFiles.Values.CopyTo(sharedFilesList, 0);
                return sharedFilesList;
            }
            finally
            {
                _sharedFilesLock.ExitReadLock();
            }
        }

        public void SendTypingNotification()
        {
            byte[] messageData = BitChatMessage.CreateTypingNotification();
            _network.WriteMessageBroadcast(messageData, 0, messageData.Length);
        }

        public void SendTextMessage(string message)
        {
            MessageRecipient[] msgRcpt;

            if (_network.Type == BitChatNetworkType.PrivateChat)
            {
                if (_network.NetworkName == null)
                    msgRcpt = new MessageRecipient[] { };
                else
                    msgRcpt = new MessageRecipient[] { new MessageRecipient(_network.NetworkName) };
            }
            else
            {
                _peersLock.EnterReadLock();
                try
                {
                    if (_peers.Count > 1)
                    {
                        msgRcpt = new MessageRecipient[_peers.Count - 1];
                        int i = 0;

                        foreach (Peer peer in _peers)
                        {
                            if (!peer.IsSelf)
                                msgRcpt[i++] = new MessageRecipient(peer.PeerCertificate.IssuedTo.EmailAddress.Address);
                        }
                    }
                    else
                    {
                        msgRcpt = new MessageRecipient[] { };
                    }
                }
                finally
                {
                    _peersLock.ExitReadLock();
                }
            }

            MessageItem msg = new MessageItem(MessageType.TextMessage, DateTime.UtcNow, _network.ConnectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress.Address, message, msgRcpt);
            msg.WriteTo(_store);

            byte[] messageData = BitChatMessage.CreateTextMessage(msg);
            _network.WriteMessageBroadcast(messageData, 0, messageData.Length);

            if (MessageReceived != null)
                RaiseEventMessageReceived(_selfPeer, msg);
        }

        public void WriteInfoMessage(string info)
        {
            MessageItem msg = new MessageItem(info);
            msg.WriteTo(_store);

            if (MessageReceived != null)
                RaiseEventMessageReceived(_selfPeer, msg);
        }

        public MessageItem[] GetLastMessages(int index, int count)
        {
            return MessageItem.GetLastMessageItems(_store, index, count);
        }

        public int GetMessageCount()
        {
            return _store.GetMessageCount();
        }

        public void ShareFile(string filePath, string hashAlgo = "SHA1")
        {
            ShareFile(SharedFile.ShareFile(filePath, hashAlgo, _syncCxt));
        }

        public void ShareFile(SharedFile sharedFile)
        {
            bool fileWasAdded = false;

            _sharedFilesLock.EnterWriteLock();
            try
            {
                if (!_sharedFiles.ContainsKey(sharedFile.MetaData.FileID))
                {
                    _sharedFiles.Add(sharedFile.MetaData.FileID, sharedFile);

                    fileWasAdded = true;
                }
            }
            finally
            {
                _sharedFilesLock.ExitWriteLock();
            }

            if (fileWasAdded)
            {
                sharedFile.AddChat(this);

                MessageItem msg = new MessageItem(_selfPeer.PeerCertificate.IssuedTo.EmailAddress.Address, sharedFile.MetaData);
                msg.WriteTo(_store);

                if (FileAdded != null)
                    RaiseEventFileAdded(_selfPeer, msg, sharedFile);

                //advertise file
                byte[] messageData = BitChatMessage.CreateFileAdvertisement(sharedFile.MetaData);
                _network.WriteMessageBroadcast(messageData, 0, messageData.Length);
            }
        }

        public void RemoveSharedFile(SharedFile file)
        {
            bool fileWasRemoved = false;

            _sharedFilesLock.EnterWriteLock();
            try
            {
                fileWasRemoved = _sharedFiles.Remove(file.MetaData.FileID);
            }
            finally
            {
                _sharedFilesLock.ExitWriteLock();
            }

            if (fileWasRemoved)
            {
                file.RemoveChat(this);

                if (FileRemoved != null)
                    RaiseEventFileRemoved(this, file);
            }
        }

        public void GoOffline()
        {
            _network.GoOffline();
            _localPeerDiscovery.StopTracking(_network.NetworkID);

            if (_network.Type == BitChatNetworkType.PrivateChat)
            {
                if (_sendInvitation)
                {
                    _outboundInvitationDhtOnlyTrackerClient.StopTracking();
                    _localPeerDiscovery.StopAnnouncement(_network.MaskedPeerEmailAddress);
                }
            }

            if (_enableTracking)
                RemoveTcpRelay(this, EventArgs.Empty);

            TriggerUpdateNetworkStatus();
        }

        public void GoOnline()
        {
            _network.GoOnline();
            _localPeerDiscovery.StartTracking(_network.NetworkID);

            if (_network.Type == BitChatNetworkType.PrivateChat)
            {
                if (_sendInvitation)
                {
                    _outboundInvitationDhtOnlyTrackerClient.StartTracking();
                    _localPeerDiscovery.StartAnnouncement(_network.MaskedPeerEmailAddress);
                }
            }

            if (_enableTracking)
                SetupTcpRelay(this, EventArgs.Empty);

            TriggerUpdateNetworkStatus();
        }

        public void LeaveChat()
        {
            //dispose
            this.Dispose();

            //delete message store index and data
            string messageStoreFolder = Path.Combine(_network.ConnectionManager.Profile.ProfileFolder, "messages");

            try
            {
                File.Delete(Path.Combine(messageStoreFolder, _messageStoreID + ".index"));
            }
            catch
            { }

            try
            {
                File.Delete(Path.Combine(messageStoreFolder, _messageStoreID + ".data"));
            }
            catch
            { }

            //raise event to remove object from service and refresh UI
            if (Leave != null)
                RaiseEventLeave();
        }

        public override string ToString()
        {
            return _network.NetworkDisplayName;
        }

        #endregion

        #region private

        internal BitChatProfile.BitChatInfo GetBitChatInfo()
        {
            List<Certificate> peerCerts = new List<Certificate>();
            List<BitChatProfile.SharedFileInfo> sharedFileInfo = new List<BitChatProfile.SharedFileInfo>();

            _peersLock.EnterReadLock();
            try
            {
                foreach (BitChat.Peer peer in _peers)
                {
                    if (!peer.IsSelf)
                        peerCerts.Add(peer.PeerCertificate);
                }
            }
            finally
            {
                _peersLock.ExitReadLock();
            }

            _sharedFilesLock.EnterReadLock();
            try
            {
                foreach (KeyValuePair<BinaryID, SharedFile> sharedFile in _sharedFiles)
                {
                    sharedFileInfo.Add(sharedFile.Value.GetSharedFileInfo());
                }
            }
            finally
            {
                _sharedFilesLock.ExitReadLock();
            }

            if (_network.Type == BitChatNetworkType.PrivateChat)
                return new BitChatProfile.BitChatInfo(BitChatNetworkType.PrivateChat, _network.NetworkName, _network.SharedSecret, _network.HashedPeerEmailAddress, _network.NetworkID, _messageStoreID, _messageStoreKey, 0, null, peerCerts.ToArray(), sharedFileInfo.ToArray(), _trackerManager.GetTrackerURIs(), _enableTracking, _sendInvitation, _network.InvitationSender, _network.InvitationMessage, _network.Status, _mute);
            else
                return new BitChatProfile.BitChatInfo(BitChatNetworkType.GroupChat, _network.NetworkName, _network.SharedSecret, null, _network.NetworkID, _messageStoreID, _messageStoreKey, _groupImageDateModified, _groupImage, peerCerts.ToArray(), sharedFileInfo.ToArray(), _trackerManager.GetTrackerURIs(), _enableTracking, false, null, null, _network.Status, _mute);
        }

        internal void WriteMessageBroadcast(byte[] data, int offset, int count)
        {
            _network.WriteMessageBroadcast(data, offset, count);
        }

        private void network_VirtualPeerAdded(BitChatNetwork sender, BitChatNetwork.VirtualPeer virtualPeer)
        {
            Peer peer = new Peer(virtualPeer, this);

            _peersLock.EnterWriteLock();
            try
            {
                _peers.Add(peer);
            }
            finally
            {
                _peersLock.ExitWriteLock();
            }

            if (PeerAdded != null)
                RaiseEventPeerAdded(peer);

            if (_network.Type == BitChatNetworkType.PrivateChat)
            {
                if (_sendInvitation)
                {
                    _outboundInvitationDhtOnlyTrackerClient.StopTracking();
                    _localPeerDiscovery.StopAnnouncement(_network.MaskedPeerEmailAddress);

                    //disable send invitation in future
                    _sendInvitation = false;
                }
            }
        }

        private void network_VirtualPeerHasRevokedCertificate(BitChatNetwork sender, InvalidCertificateException ex)
        {
            if (PeerHasRevokedCertificate != null)
                RaiseEventPeerHasRevokedCertificate(ex);
        }

        private void network_VirtualPeerSecureChannelException(BitChatNetwork sender, SecureChannelException ex)
        {
            if (PeerSecureChannelException != null)
                RaiseEventPeerSecureChannelException(ex);
        }

        private void network_VirtualPeerHasChangedCertificate(BitChatNetwork sender, Certificate cert)
        {
            if (PeerHasChangedCertificate != null)
                RaiseEventPeerHasChangedCertificate(cert);
        }

        private void OutboundInvitationDhtOnlyTrackerManager_DiscoveredPeers(TrackerManager sender, IEnumerable<IPEndPoint> peerEPs)
        {
            _network.SendInvitation(peerEPs);
        }

        internal void ClientProfileProxyUpdated()
        {
            _trackerManager.Proxy = _network.ConnectionManager.Profile.Proxy;

            GoOffline();
            GoOnline();
        }

        internal void ClientProfileImageChanged()
        {
            _peersLock.EnterReadLock();
            try
            {
                foreach (Peer peer in _peers)
                    peer.ClientProfileImageChanged();
            }
            finally
            {
                _peersLock.ExitReadLock();
            }
        }

        #endregion

        #region PeerExchange implementation

        private void DoPeerExchange()
        {
            //find connected peers 
            List<PeerInfo> peerList = _network.GetConnectedPeerList();
            List<Peer> onlinePeers = new List<Peer>();

            _peersLock.EnterReadLock();
            try
            {
                foreach (Peer currentPeer in _peers)
                {
                    if (currentPeer.IsOnline)
                        onlinePeers.Add(currentPeer);
                }
            }
            finally
            {
                _peersLock.ExitReadLock();
            }

            //send other peers ep list to online peers
            byte[] messageData = BitChatMessage.CreatePeerExchange(peerList);

            foreach (Peer onlinePeer in onlinePeers)
            {
                onlinePeer.WriteMessage(messageData);
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
                BitChatConnectivityStatus oldStatus = _connectivityStatus;
                BitChatConnectivityStatus connectivityStatus;

                if (_network.Status == BitChatNetworkStatus.Offline)
                {
                    #region offline mode

                    //update peer connectivity info to NoNetwork
                    connectivityStatus = BitChatConnectivityStatus.NoNetwork;

                    lock (_peerListLock)
                    {
                        _connectedPeerList.Clear();
                        _disconnectedPeerList.Clear();
                        _connectivityStatus = connectivityStatus;
                    }

                    _peersLock.EnterReadLock();
                    try
                    {
                        foreach (Peer peer in _peers)
                        {
                            peer.SetNoNetworkStatus();
                        }
                    }
                    finally
                    {
                        _peersLock.ExitReadLock();
                    }

                    //stop tracking and local announcement
                    _localPeerDiscovery.StopAnnouncement(_network.NetworkID);
                    _trackerManager.StopTracking();

                    #endregion
                }
                else
                {
                    #region online mode

                    #region update peer connectivity info

                    //find network wide connected peer ep list
                    List<PeerInfo> uniqueConnectedPeerList = new List<PeerInfo>();

                    List<Peer> onlinePeers = new List<Peer>();
                    List<Peer> offlinePeers = new List<Peer>();

                    _peersLock.EnterReadLock();
                    try
                    {
                        foreach (Peer currentPeer in _peers)
                        {
                            if (currentPeer.IsOnline)
                                onlinePeers.Add(currentPeer);
                            else
                                offlinePeers.Add(currentPeer);
                        }
                    }
                    finally
                    {
                        _peersLock.ExitReadLock();
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

                    if (disconnectedPeerList.Count > 0)
                    {
                        connectivityStatus = BitChatConnectivityStatus.PartialNetwork;

                        foreach (PeerInfo peerInfo in disconnectedPeerList)
                            _network.MakeConnection(peerInfo.PeerEPList);
                    }
                    else
                    {
                        connectivityStatus = BitChatConnectivityStatus.FullNetwork;
                    }

                    lock (_peerListLock)
                    {
                        _connectedPeerList = connectedPeerList;
                        _disconnectedPeerList = disconnectedPeerList;
                        _connectivityStatus = connectivityStatus;
                    }

                    #endregion

                    #region manage tracker status as per connectivity status

                    if (_network.Type == BitChatNetworkType.PrivateChat)
                    {
                        if (connectedPeerList.Count > 0)
                        {
                            _localPeerDiscovery.StopAnnouncement(_network.NetworkID);

                            if (_enableTracking)
                                _trackerManager.StopTracking();
                        }
                        else
                        {
                            _localPeerDiscovery.StartAnnouncement(_network.NetworkID);

                            if (_enableTracking)
                            {
                                _trackerManager.ScheduleUpdateNow();
                                _trackerManager.StartTracking();
                            }
                        }
                    }
                    else
                    {
                        if (connectedPeerList.Count > 0)
                        {
                            _localPeerDiscovery.StopAnnouncement(_network.NetworkID);

                            if (_enableTracking)
                            {
                                if (disconnectedPeerList.Count > 0)
                                {
                                    _trackerManager.CustomUpdateInterval = BIT_CHAT_TRACKER_UPDATE_INTERVAL_SECONDS; //set custom update interval for quick retries to tracker/DHT
                                    _trackerManager.ScheduleUpdateNow();
                                    _trackerManager.StartTracking();
                                }
                                else
                                {
                                    _trackerManager.CustomUpdateInterval = 0; //set to default values to avoid frequent calls to tracker/DHT
                                    _trackerManager.StartTracking();
                                }
                            }
                        }
                        else
                        {
                            _localPeerDiscovery.StartAnnouncement(_network.NetworkID);

                            if (_enableTracking)
                            {
                                _trackerManager.CustomUpdateInterval = BIT_CHAT_TRACKER_UPDATE_INTERVAL_SECONDS; //set custom update interval for quick retries to tracker/DHT
                                _trackerManager.ScheduleUpdateNow();
                                _trackerManager.StartTracking();
                            }
                        }
                    }

                    #endregion

                    #endregion
                }

                if (oldStatus != connectivityStatus)
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

                        if (_connectivityStatus == BitChatConnectivityStatus.PartialNetwork)
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
                byte[] messageData = BitChatMessage.CreateNOOPMessage();
                _network.WriteMessageBroadcast(messageData, 0, messageData.Length);
            }
            catch
            { }
            finally
            {
                if (_NOOPTimer != null)
                    _NOOPTimer.Change(NOOP_MESSAGE_TIMER_INTERVAL, Timeout.Infinite);
            }
        }

        #endregion

        #region TrackerManager

        private void TrackerManager_DiscoveredPeers(TrackerManager sender, IEnumerable<IPEndPoint> peerEPs)
        {
            _network.MakeConnection(peerEPs);
        }

        public TrackerClient[] GetTrackers()
        {
            return _trackerManager.GetTrackers();
        }

        public Uri[] GetTrackerURIs()
        {
            return _trackerManager.GetTrackerURIs();
        }

        public int DhtGetTotalPeers()
        {
            return _trackerManager.DhtGetTotalPeers();
        }

        public IPEndPoint[] DhtGetPeers()
        {
            return _trackerManager.DhtGetPeers();
        }

        public void DhtUpdate()
        {
            _trackerManager.DhtUpdate();
        }

        public TimeSpan DhtNextUpdateIn()
        {
            return _trackerManager.DhtNextUpdateIn();
        }

        public Exception DhtLastException()
        {
            return _trackerManager.DhtLastException();
        }

        public TrackerClient AddTracker(Uri trackerURI)
        {
            return _trackerManager.AddTracker(trackerURI);
        }

        public void RemoveTracker(TrackerClient tracker)
        {
            _trackerManager.RemoveTracker(tracker);
        }

        public bool IsTrackerRunning
        { get { return _trackerManager.IsTrackerRunning; } }

        public bool EnableTracking
        {
            get
            {
                return _enableTracking;
            }
            set
            {
                _enableTracking = value;

                if (_network.Status == BitChatNetworkStatus.Online)
                {
                    if (_enableTracking)
                    {
                        if (_network.Type == BitChatNetworkType.GroupChat)
                            _trackerManager.StartTracking();
                        else
                            TriggerUpdateNetworkStatus();
                    }
                    else
                    {
                        _trackerManager.StopTracking();
                    }
                }

                if (_enableTracking && (_network.Status == BitChatNetworkStatus.Online))
                    SetupTcpRelay(this, EventArgs.Empty);
                else
                    RemoveTcpRelay(this, EventArgs.Empty);
            }
        }

        #endregion

        #region properties

        internal BitChatNetwork Network
        { get { return _network; } }

        public BitChatProfile Profile
        { get { return _network.ConnectionManager.Profile; } }

        public BinaryID NetworkID
        { get { return _network.NetworkID; } }

        public BitChatNetworkType NetworkType
        { get { return _network.Type; } }

        public BitChatNetworkStatus NetworkStatus
        { get { return _network.Status; } }

        public string NetworkName
        { get { return _network.NetworkName; } }

        public string NetworkDisplayName
        { get { return _network.NetworkDisplayName; } }

        public string NetworkDisplayTitle
        { get { return _network.NetworkDisplayTitle; } }

        public string SharedSecret
        {
            get { return _network.SharedSecret; }
            set
            {
                _network.SharedSecret = value;

                if (_network.Status == BitChatNetworkStatus.Online)
                {
                    //apply changes immediately
                    GoOffline();
                    GoOnline();
                }
            }
        }

        public Certificate LocalCertificate
        { get { return _network.ConnectionManager.Profile.LocalCertificateStore.Certificate; } }

        public Peer SelfPeer
        { get { return _selfPeer; } }

        public byte[] GroupImage
        {
            get { return _groupImage; }
            set
            {
                if (_network.Type != BitChatNetworkType.GroupChat)
                    throw new InvalidOperationException("Group image can be set only for Bit Chat Groups.");

                _groupImageDateModified = Convert.ToInt64((DateTime.UtcNow - _epoch).TotalMilliseconds);
                _groupImage = value;

                //notify UI to change group image
                if (GroupImageChanged != null)
                    RaiseEventGroupImageChanged(_selfPeer);

                //notify peers
                byte[] messageData = BitChatMessage.CreateGroupImage(_groupImage, _groupImageDateModified);
                _network.WriteMessageBroadcast(messageData, 0, messageData.Length);
            }
        }

        public bool Mute
        {
            get { return _mute; }
            set { _mute = value; }
        }

        #endregion

        public class Peer
        {
            #region events

            public event EventHandler StateChanged;
            public event EventHandler NetworkStatusUpdated;
            public event EventHandler ProfileImageChanged;

            #endregion

            #region variables

            readonly BitChatNetwork.VirtualPeer _virtualPeer;
            readonly BitChat _bitChat;

            long _profileImageDateModified = -1;
            byte[] _profileImage;

            readonly object _peerListLock = new object();
            List<PeerInfo> _connectedPeerList = new List<PeerInfo>();
            List<PeerInfo> _disconnectedPeerList = new List<PeerInfo>();
            BitChatConnectivityStatus _connectivityStatus = BitChatConnectivityStatus.NoNetwork;

            readonly bool _isSelfPeer;
            bool _previousState = false;

            #endregion

            #region constructor

            internal Peer(BitChatNetwork.VirtualPeer virtualPeer, BitChat bitchat)
            {
                _virtualPeer = virtualPeer;
                _bitChat = bitchat;

                _isSelfPeer = (_virtualPeer.PeerCertificate.IssuedTo.EmailAddress.Address == _bitChat._network.ConnectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress.Address);

                _virtualPeer.MessageReceived += virtualPeer_MessageReceived;
                _virtualPeer.StateChanged += virtualPeer_StateChanged;
            }

            #endregion

            #region private event functions

            private void RaiseEventStateChanged()
            {
                _bitChat._syncCxt.Send(StateChangedCallback, null);
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
                _bitChat._syncCxt.Send(NetworkStatusUpdatedCallBack, null);
            }

            private void NetworkStatusUpdatedCallBack(object state)
            {
                try
                {
                    NetworkStatusUpdated(this, EventArgs.Empty);
                }
                catch { }
            }

            private void RaiseEventProfileImageChanged()
            {
                _bitChat._syncCxt.Send(ProfileImageChangedCallback, null);
            }

            private void ProfileImageChangedCallback(object state)
            {
                try
                {
                    ProfileImageChanged(this, EventArgs.Empty);
                }
                catch { }
            }

            #endregion

            #region private

            private void virtualPeer_StateChanged(BitChatNetwork.VirtualPeer.VirtualSession peerSession)
            {
                //trigger peer exchange for entire network
                _bitChat.DoPeerExchange();

                if (_virtualPeer.IsOnline)
                {
                    DoSendProfileImage(peerSession);
                    DoSendSharedFileMetaData(peerSession);

                    switch (_bitChat._network.Type)
                    {
                        case BitChatNetworkType.PrivateChat:
                            ReSendUndeliveredMessages(peerSession); //feature only for private chat. since, group chat can have multiple offline users, sending undelivered messages will create partial & confusing conversation for the one who comes online later.
                            break;

                        case BitChatNetworkType.GroupChat:
                            DoSendGroupImage(peerSession); //group image feature
                            break;
                    }
                }
                else
                {
                    _bitChat._sharedFilesLock.EnterReadLock();
                    try
                    {
                        foreach (KeyValuePair<BinaryID, SharedFile> item in _bitChat._sharedFiles)
                        {
                            item.Value.RemovePeerOrSeeder(peerSession);
                        }
                    }
                    finally
                    {
                        _bitChat._sharedFilesLock.ExitReadLock();
                    }

                    lock (_peerListLock)
                    {
                        _connectedPeerList.Clear();
                        _disconnectedPeerList.Clear();
                    }

                    _bitChat.TriggerUpdateNetworkStatus();
                }

                if (!_isSelfPeer)
                {
                    if (_previousState != _virtualPeer.IsOnline)
                    {
                        _previousState = _virtualPeer.IsOnline;

                        if (StateChanged != null)
                            RaiseEventStateChanged();
                    }
                }
            }

            private void virtualPeer_MessageReceived(BitChatNetwork.VirtualPeer.VirtualSession peerSession, Stream messageDataStream)
            {
                BitChatMessageType type = (BitChatMessageType)messageDataStream.ReadByte();

                switch (type)
                {
                    case BitChatMessageType.TypingNotification:
                        #region Typing Notification

                        if (_bitChat.PeerTyping != null)
                            _bitChat.RaiseEventPeerTyping(this);

                        #endregion
                        break;

                    case BitChatMessageType.Text:
                        #region Text
                        {
                            int messageNumber = BitChatMessage.ReadInt32(messageDataStream);
                            DateTime messageDate = BitChatMessage.ReadDateTime(messageDataStream);
                            string message = Encoding.UTF8.GetString(BitChatMessage.ReadData(messageDataStream));

                            MessageItem msg = new MessageItem(MessageType.TextMessage, messageDate, _virtualPeer.PeerCertificate.IssuedTo.EmailAddress.Address, message);
                            msg.WriteTo(_bitChat._store);

                            if (_bitChat.MessageReceived != null)
                                _bitChat.RaiseEventMessageReceived(this, msg);

                            //send delivery notification
                            byte[] notificationData = BitChatMessage.CreateTextDeliveryNotification(messageNumber);
                            peerSession.WriteMessage(notificationData, 0, notificationData.Length);
                        }
                        #endregion
                        break;

                    case BitChatMessageType.TextDeliveryNotification:
                        #region TextReceivedNotification
                        {
                            int messageNumber = BitChatMessage.ReadInt32(messageDataStream);
                            MessageItem msg;

                            lock (_bitChat._store) //lock to avoid race condition in a group chat. this will prevent message data from getting overwritten.
                            {
                                msg = new MessageItem(_bitChat._store, messageNumber);

                                foreach (MessageRecipient rcpt in msg.Recipients)
                                {
                                    if (rcpt.Name.Equals(_virtualPeer.PeerCertificate.IssuedTo.EmailAddress.Address))
                                    {
                                        rcpt.SetDeliveredStatus();
                                        break;
                                    }
                                }

                                msg.WriteTo(_bitChat._store);
                            }

                            if (_bitChat.MessageDeliveryNotification != null)
                                _bitChat.RaiseEventMessageDeliveryNotification(this, msg);
                        }
                        #endregion
                        break;

                    case BitChatMessageType.FileAdvertisement:
                        #region FileAdvertisement
                        {
                            SharedFile sharedFile = SharedFile.PrepareDownloadFile(new SharedFileMetaData(messageDataStream), _bitChat._syncCxt);
                            bool fileAlreadyExists = false;
                            bool fileWasAdded = false;

                            _bitChat._sharedFilesLock.EnterWriteLock();
                            try
                            {
                                if (_bitChat._sharedFiles.ContainsKey(sharedFile.MetaData.FileID))
                                {
                                    fileAlreadyExists = true;
                                }
                                else
                                {
                                    //file doesnt exists
                                    _bitChat._sharedFiles.Add(sharedFile.MetaData.FileID, sharedFile);

                                    fileWasAdded = true;
                                }
                            }
                            finally
                            {
                                _bitChat._sharedFilesLock.ExitWriteLock();
                            }

                            if (fileAlreadyExists)
                            {
                                if (sharedFile.IsComplete)
                                {
                                    //remove the seeder
                                    sharedFile.RemovePeerOrSeeder(peerSession);
                                }
                                else
                                {
                                    sharedFile.AddSeeder(peerSession); //add the seeder

                                    if (sharedFile.State == SharedFileState.Downloading)
                                    {
                                        byte[] packetData = BitChatMessage.CreateFileParticipate(sharedFile.MetaData.FileID);
                                        peerSession.WriteMessage(packetData, 0, packetData.Length);
                                    }
                                }
                            }
                            else if (fileWasAdded)
                            {
                                sharedFile.AddChat(_bitChat); //add chat
                                sharedFile.AddSeeder(peerSession); //add the seeder

                                MessageItem msg = new MessageItem(this.PeerCertificate.IssuedTo.EmailAddress.Address, sharedFile.MetaData);
                                msg.WriteTo(_bitChat._store);

                                if (_bitChat.FileAdded != null)
                                    _bitChat.RaiseEventFileAdded(this, msg, sharedFile);
                            }
                        }
                        #endregion
                        break;

                    case BitChatMessageType.FileShareParticipate:
                        #region FileShareParticipate
                        {
                            BinaryID fileID = new BinaryID(messageDataStream);
                            SharedFile sharedFile;

                            _bitChat._sharedFilesLock.EnterReadLock();
                            try
                            {
                                sharedFile = _bitChat._sharedFiles[fileID];
                            }
                            finally
                            {
                                _bitChat._sharedFilesLock.ExitReadLock();
                            }

                            sharedFile.AddPeer(peerSession);
                        }
                        #endregion
                        break;

                    case BitChatMessageType.FileShareUnparticipate:
                        #region FileShareUnparticipate
                        {
                            BinaryID fileID = new BinaryID(messageDataStream);
                            SharedFile sharedFile;

                            _bitChat._sharedFilesLock.EnterReadLock();
                            try
                            {
                                sharedFile = _bitChat._sharedFiles[fileID];
                            }
                            finally
                            {
                                _bitChat._sharedFilesLock.ExitReadLock();
                            }

                            sharedFile.RemovePeerOrSeeder(peerSession);
                        }
                        #endregion
                        break;

                    case BitChatMessageType.FileBlockWanted:
                        #region FileBlockWanted
                        {
                            BinaryID fileID = new BinaryID(messageDataStream);
                            int blockNumber = BitChatMessage.ReadInt32(messageDataStream);
                            SharedFile sharedFile;

                            _bitChat._sharedFilesLock.EnterReadLock();
                            try
                            {
                                sharedFile = _bitChat._sharedFiles[fileID];
                            }
                            finally
                            {
                                _bitChat._sharedFilesLock.ExitReadLock();
                            }

                            if (sharedFile.State == SharedFileState.Paused)
                                return;

                            if (!sharedFile.PeerExists(peerSession))
                                return;

                            if (sharedFile.IsBlockAvailable(blockNumber))
                            {
                                byte[] messageData = BitChatMessage.CreateFileBlockAvailable(fileID, blockNumber);
                                peerSession.WriteMessage(messageData, 0, messageData.Length);
                            }
                        }
                        #endregion
                        break;

                    case BitChatMessageType.FileBlockAvailable:
                        #region FileBlockAvailable
                        {
                            BinaryID fileID = new BinaryID(messageDataStream);
                            int blockNumber = BitChatMessage.ReadInt32(messageDataStream);
                            SharedFile sharedFile;

                            _bitChat._sharedFilesLock.EnterReadLock();
                            try
                            {
                                sharedFile = _bitChat._sharedFiles[fileID];
                            }
                            finally
                            {
                                _bitChat._sharedFilesLock.ExitReadLock();
                            }

                            if (sharedFile.IsComplete)
                                return;

                            if (sharedFile.State == SharedFileState.Paused)
                                return;

                            if (!sharedFile.PeerExists(peerSession))
                                return;

                            sharedFile.BlockAvailable(blockNumber, peerSession);
                        }
                        #endregion
                        break;

                    case BitChatMessageType.FileBlockRequest:
                        #region FileBlockRequest
                        {
                            BinaryID fileID = new BinaryID(messageDataStream);
                            int blockNumber = BitChatMessage.ReadInt32(messageDataStream);
                            ushort port = BitChatMessage.ReadUInt16(messageDataStream);
                            SharedFile sharedFile;

                            _bitChat._sharedFilesLock.EnterReadLock();
                            try
                            {
                                sharedFile = _bitChat._sharedFiles[fileID];
                            }
                            finally
                            {
                                _bitChat._sharedFilesLock.ExitReadLock();
                            }

                            if (sharedFile.State == SharedFileState.Paused)
                                return;

                            if (!sharedFile.PeerExists(peerSession))
                                return;

                            Stream dataStream = peerSession.OpenDataStream(port);

                            //start file block transfer async
                            ThreadPool.QueueUserWorkItem(FileBlockRequestHandlerAsync, new object[] { sharedFile, blockNumber, dataStream });
                        }
                        #endregion
                        break;

                    case BitChatMessageType.PeerExchange:
                        #region PeerExchange

                        List<PeerInfo> peerList = BitChatMessage.ReadPeerExchange(messageDataStream);

                        lock (_peerListLock)
                        {
                            _connectedPeerList = peerList;
                        }

                        foreach (PeerInfo peerInfo in peerList)
                        {
                            _bitChat._network.MakeConnection(peerInfo.PeerEPList);
                            _bitChat._network.ConnectionManager.DhtClient.AddNode(peerInfo.PeerEPList);
                        }

                        //start network status check
                        _bitChat.TriggerUpdateNetworkStatus();

                        #endregion
                        break;

                    case BitChatMessageType.ProfileImage:
                        #region Profile Image
                        {
                            long dateModified = BitChatMessage.ReadInt64(messageDataStream);
                            byte[] profileImage = BitChatMessage.ReadData(messageDataStream);

                            if (profileImage.Length == 0)
                                profileImage = null;

                            if (_isSelfPeer)
                            {
                                if (_bitChat._network.ConnectionManager.Profile.SetProfileImage(dateModified, profileImage))
                                    RaiseEventProfileImageChanged();
                            }
                            else if (dateModified > _profileImageDateModified)
                            {
                                _profileImageDateModified = dateModified;
                                _profileImage = profileImage;

                                RaiseEventProfileImageChanged();
                            }
                        }
                        #endregion
                        break;

                    case BitChatMessageType.GroupImage:
                        #region Group Image
                        {
                            if (_bitChat._network.Type == BitChatNetworkType.GroupChat)
                            {
                                long dateModified = BitChatMessage.ReadInt64(messageDataStream);

                                if (dateModified > _bitChat._groupImageDateModified)
                                {
                                    _bitChat._groupImageDateModified = dateModified;
                                    _bitChat._groupImage = BitChatMessage.ReadData(messageDataStream);

                                    if (_bitChat._groupImage.Length == 0)
                                        _bitChat._groupImage = null;

                                    if (_bitChat.GroupImageChanged != null)
                                        _bitChat.RaiseEventGroupImageChanged(this);
                                }
                            }
                        }
                        #endregion
                        break;

                    case BitChatMessageType.NOOP:
                        break;
                }
            }

            private void FileBlockRequestHandlerAsync(object state)
            {
                object[] parameters = state as object[];

                SharedFile sharedFile = parameters[0] as SharedFile;
                int blockNumber = (int)parameters[1];
                Stream dataStream = parameters[2] as Stream;

                try
                {
                    sharedFile.TransferBlock(blockNumber, dataStream);
                }
                catch
                { }
                finally
                {
                    dataStream.Dispose();
                }
            }

            internal void ClientProfileImageChanged()
            {
                if (_isSelfPeer)
                    RaiseEventProfileImageChanged();

                DoSendProfileImage(null);
            }

            private void DoSendProfileImage(BitChatNetwork.VirtualPeer.VirtualSession peerSession)
            {
                byte[] messageData = BitChatMessage.CreateProfileImage(_bitChat._network.ConnectionManager.Profile.ProfileImage, _bitChat._network.ConnectionManager.Profile.ProfileImageDateModified);

                if (peerSession == null)
                    _virtualPeer.WriteMessage(messageData, 0, messageData.Length);
                else
                    peerSession.WriteMessage(messageData, 0, messageData.Length);
            }

            private void DoSendGroupImage(BitChatNetwork.VirtualPeer.VirtualSession peerSession)
            {
                byte[] messageData = BitChatMessage.CreateGroupImage(_bitChat._groupImage, _bitChat._groupImageDateModified);
                peerSession.WriteMessage(messageData, 0, messageData.Length);
            }

            private void DoSendSharedFileMetaData(BitChatNetwork.VirtualPeer.VirtualSession peerSession)
            {
                _bitChat._sharedFilesLock.EnterReadLock();
                try
                {
                    foreach (KeyValuePair<BinaryID, SharedFile> sharedFile in _bitChat._sharedFiles)
                    {
                        if (sharedFile.Value.State == SharedFileState.Sharing)
                        {
                            byte[] messageData = BitChatMessage.CreateFileAdvertisement(sharedFile.Value.MetaData);
                            peerSession.WriteMessage(messageData, 0, messageData.Length);
                        }
                    }
                }
                finally
                {
                    _bitChat._sharedFilesLock.ExitReadLock();
                }
            }

            private void ReSendUndeliveredMessages(BitChatNetwork.VirtualPeer.VirtualSession peerSession)
            {
                List<MessageItem> undeliveredMessages = new List<MessageItem>(10);
                string selfEmailId = _bitChat._selfPeer.PeerCertificate.IssuedTo.EmailAddress.Address;

                for (int i = _bitChat._store.GetMessageCount() - 1; i > -1; i--)
                {
                    MessageItem msg = new MessageItem(_bitChat._store, i);

                    if ((msg.Type == MessageType.TextMessage) && msg.Sender.Equals(selfEmailId))
                    {
                        if (msg.GetDeliveryStatus() == MessageDeliveryStatus.Undelivered)
                            undeliveredMessages.Add(msg);
                        else
                            break;
                    }
                }

                for (int i = undeliveredMessages.Count - 1; i > -1; i--)
                {
                    byte[] messageData = BitChatMessage.CreateTextMessage(undeliveredMessages[i]);
                    peerSession.WriteMessage(messageData, 0, messageData.Length);
                }
            }

            internal void UpdateUniqueConnectedPeerList(List<PeerInfo> uniqueConnectedPeerList)
            {
                lock (_peerListLock)
                {
                    UpdateUniquePeerList(uniqueConnectedPeerList, _connectedPeerList);
                }
            }

            internal void UpdateNetworkStatus(List<PeerInfo> uniqueConnectedPeerList)
            {
                BitChatConnectivityStatus oldStatus = _connectivityStatus;

                lock (_peerListLock)
                {
                    //compare this peer's connected peer list to the other peer list to find disconnected peer list
                    _disconnectedPeerList = GetMissingPeerList(_connectedPeerList, uniqueConnectedPeerList);
                    //remove self from the disconnected list
                    _disconnectedPeerList.Remove(_virtualPeer.GetPeerInfo());

                    if (_disconnectedPeerList.Count > 0)
                        _connectivityStatus = BitChatConnectivityStatus.PartialNetwork;
                    else
                        _connectivityStatus = BitChatConnectivityStatus.FullNetwork;
                }

                if (oldStatus != _connectivityStatus)
                    RaiseEventNetworkStatusUpdated();
            }

            internal void SetNoNetworkStatus()
            {
                BitChatConnectivityStatus oldStatus = _connectivityStatus;

                _connectivityStatus = BitChatConnectivityStatus.NoNetwork;

                if (oldStatus != _connectivityStatus)
                    RaiseEventNetworkStatusUpdated();
            }

            internal void WriteMessage(byte[] messageData)
            {
                _virtualPeer.WriteMessage(messageData, 0, messageData.Length);
            }

            #endregion

            #region public

            public override string ToString()
            {
                if (_virtualPeer.PeerCertificate != null)
                    return _virtualPeer.PeerCertificate.IssuedTo.Name;

                return "unknown";
            }

            #endregion

            #region properties

            public BitChat BitChat
            { get { return _bitChat; } }

            public PeerInfo[] ConnectedWith
            {
                get
                {
                    if (_isSelfPeer)
                    {
                        lock (_bitChat._peerListLock)
                        {
                            return _bitChat._connectedPeerList.ToArray();
                        }
                    }
                    else
                    {
                        lock (_peerListLock)
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
                        lock (_bitChat._peerListLock)
                        {
                            return _bitChat._disconnectedPeerList.ToArray();
                        }
                    }
                    else
                    {
                        lock (_peerListLock)
                        {
                            return _disconnectedPeerList.ToArray();
                        }
                    }
                }
            }

            public BitChatConnectivityStatus ConnectivityStatus
            {
                get
                {
                    if (_isSelfPeer)
                        return _bitChat._connectivityStatus;
                    else
                        return _connectivityStatus;
                }
            }

            public SecureChannelCryptoOptionFlags CipherSuite
            { get { return _virtualPeer.CipherSuite; } }

            public Certificate PeerCertificate
            {
                get
                {
                    if (_isSelfPeer)
                        return _bitChat._network.ConnectionManager.Profile.LocalCertificateStore.Certificate;
                    else
                        return _virtualPeer.PeerCertificate;
                }
            }

            public byte[] ProfileImage
            {
                get
                {
                    if (_isSelfPeer)
                        return _bitChat._network.ConnectionManager.Profile.ProfileImage;
                    else
                        return _profileImage;
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
