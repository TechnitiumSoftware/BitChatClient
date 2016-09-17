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

using BitChatCore;
using BitChatCore.FileSharing;
using System;
using System.IO;
using System.Windows.Forms;

namespace BitChatApp.UserControls
{
    public delegate void MessageNotification(BitChat sender, BitChat.Peer messageSender, string message);

    public partial class BitChatPanel : UserControl
    {
        #region events

        public event EventHandler SettingsModified;
        public event MessageNotification MessageNotification;
        public event EventHandler SwitchToPrivateChat;
        public event EventHandler ShareFile;

        #endregion

        #region variables

        BitChat _chat;
        ChatMessageView _view;

        bool _skipSettingsModifiedEvent = false;

        #endregion

        #region constructor

        public BitChatPanel(BitChat chat, ChatListItem chatItem)
        {
            InitializeComponent();

            _chat = chat;

            _chat.PeerAdded += chat_PeerAdded;
            _chat.FileAdded += chat_FileAdded;
            _chat.MessageReceived += chat_MessageReceived;

            //create view
            _view = new ChatMessageView(_chat, chatItem);
            _view.Dock = DockStyle.Fill;
            _view.AllowDrop = true;
            _view.SettingsModified += view_SettingsModified;
            _view.ShareFile += view_ShareFile;
            _view.DragEnter += lstFiles_DragEnter;
            _view.DragDrop += lstFiles_DragDrop;

            //load all peers
            foreach (BitChat.Peer peer in _chat.GetPeerList())
            {
                lstUsers.AddItem(new UserListItem(peer));
                
                peer.StateChanged += peer_StateChanged;
            }

            //load all files
            foreach (SharedFile sharedFile in _chat.GetSharedFileList())
                chat_FileAdded(chat.SelfPeer, null, sharedFile);

            //add view to panel
            bitChatPanelSplitContainer.Panel1.Controls.Add(_view);
        }

        #endregion

        #region bitchat events

        private void chat_PeerAdded(BitChat sender, BitChat.Peer peer)
        {
            lstUsers.AddItem(new UserListItem(peer));

            peer.StateChanged += peer_StateChanged;
        }

        private void chat_FileAdded(BitChat.Peer peer, MessageItem message, SharedFile sharedFile)
        {
            SharedFileItem item = new SharedFileItem(sharedFile, _chat);
            item.FileRemoved += sharedFile_FileRemoved;
            item.ShareFile += view_ShareFile;

            lstFiles.AddItem(item);
        }

        private void chat_MessageReceived(BitChat.Peer peer, MessageItem message)
        {
            if (message.Type != MessageType.Info)
                MessageNotification(_chat, peer, message.Message);
        }

        private void peer_StateChanged(object sender, EventArgs e)
        {
            BitChat.Peer peer = sender as BitChat.Peer;

            string message;

            if (peer.IsOnline)
                message = peer.PeerCertificate.IssuedTo.Name + " is online";
            else
                message = peer.PeerCertificate.IssuedTo.Name + " is offline";

            _chat.WriteInfoMessage(message);

            if (_chat.NetworkType == BitChatCore.Network.BitChatNetworkType.PrivateChat)
                _view.Title = peer.PeerCertificate.IssuedTo.Name + " <" + peer.PeerCertificate.IssuedTo.EmailAddress.Address + ">";

            MessageNotification(_chat, null, message);
        }

        private void sharedFile_FileRemoved(object sender, EventArgs e)
        {
            SharedFileItem item = sender as SharedFileItem;

            lstFiles.RemoveItem(item);
        }

        #endregion

        #region private

        private void mnuViewUserProfile_Click(object sender, EventArgs e)
        {
            UserListItem item = lstUsers.SelectedItem as UserListItem;

            if (item != null)
            {
                using (frmViewProfile frm = new frmViewProfile(_chat.Profile, item.Peer))
                {
                    frm.ShowDialog(this);
                }
            }
        }

        private void mnuMessageUser_Click(object sender, EventArgs e)
        {
            UserListItem item = lstUsers.SelectedItem as UserListItem;

            if (item != null)
                SwitchToPrivateChat(item, EventArgs.Empty);
        }

        private void lstUsers_ItemMouseUp(object sender, MouseEventArgs e)
        {
            UserListItem item = lstUsers.SelectedItem as UserListItem;

            if (item != null)
            {
                if (e.Button == MouseButtons.Right)
                {
                    mnuMessageUser.Enabled = (item.Peer.BitChat.NetworkType == BitChatCore.Network.BitChatNetworkType.GroupChat) && !item.Peer.IsSelf;
                    mnuUserList.Show(sender as Control, e.Location);
                }
                else
                {
                    using (frmViewProfile frm = new frmViewProfile(_chat.Profile, item.Peer))
                    {
                        frm.ShowDialog(this);
                    }
                }
            }
        }
        
        private void SplitContainer_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if ((SettingsModified != null) && !_skipSettingsModifiedEvent)
                SettingsModified(this, EventArgs.Empty);
        }

        private void view_SettingsModified(object sender, EventArgs e)
        {
            if ((SettingsModified != null) && !_skipSettingsModifiedEvent)
                SettingsModified(this, EventArgs.Empty);
        }

        private void view_ShareFile(object sender, EventArgs e)
        {
            ShareFile?.Invoke(sender, e);
        }

        private void lstFiles_DragDrop(object sender, DragEventArgs e)
        {
            string[] fileNames = e.Data.GetData(DataFormats.FileDrop) as string[];

            _view.ShareFiles(fileNames);
        }

        private void lstFiles_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        protected override void OnResize(EventArgs e)
        {
            _skipSettingsModifiedEvent = true;
            base.OnResize(e);
            _skipSettingsModifiedEvent = false;
        }

        #endregion

        #region public

        public void SetFocusMessageEditor()
        {
            _view.SetFocusMessageEditor();
        }

        public void TrimMessageList()
        {
            _view.TrimMessageList();
        }

        public void ReadSettingsFrom(Stream s)
        {
            _skipSettingsModifiedEvent = true;
            ReadSettingsFrom(new BinaryReader(s));
            _skipSettingsModifiedEvent = false;
        }

        public void ReadSettingsFrom(BinaryReader bR)
        {
            bitChatPanelSplitContainer.SplitterDistance = bitChatPanelSplitContainer.Width - bR.ReadInt32();
            chatOptionsSplitContainer.SplitterDistance = chatOptionsSplitContainer.Height - bR.ReadInt32();

            _view.ReadSettingsFrom(bR);
        }

        public void WriteSettingsTo(Stream s)
        {
            BinaryWriter bW = new BinaryWriter(s);
            WriteSettingsTo(bW);
            bW.Flush();
        }

        public void WriteSettingsTo(BinaryWriter bW)
        {
            bW.Write(bitChatPanelSplitContainer.Width - bitChatPanelSplitContainer.SplitterDistance);
            bW.Write(chatOptionsSplitContainer.Height - chatOptionsSplitContainer.SplitterDistance);

            _view.WriteSettingsTo(bW);
        }

        #endregion
    }
}
