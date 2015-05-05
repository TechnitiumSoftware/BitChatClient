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

using BitChatApp.UserControls;
using BitChatClient;
using BitChatClient.FileSharing;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Security.Cryptography;
using System;
using System.Drawing;
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

            _chat.FileAdded += _chat_FileAdded;
            _chat.PeerAdded += _chat_PeerAdded;
            _chat.MessageReceived += _chat_MessageReceived;

            //create view
            _view = new ChatMessageView(_chat, chatItem);
            _view.Dock = DockStyle.Fill;
            _view.AllowDrop = true;
            _view.SettingsModified += _view_SettingsModified;
            _view.DragEnter += lstFiles_DragEnter;
            _view.DragDrop += lstFiles_DragDrop;

            //load all peers
            foreach (BitChat.Peer peer in _chat.GetPeerList())
            {
                lstUsers.AddItem(new UserListItem(peer));
                peer.StateChanged += _view.peer_StateChanged;
                peer.StateChanged += peer_StateChanged;
            }

            //load all files
            foreach (SharedFile sharedFile in _chat.GetSharedFileList())
                _chat_FileAdded(chat, sharedFile);

            //add view to panel
            bitChatPanelSplitContainer.Panel1.Controls.Add(_view);
        }

        #endregion

        #region bitchat events

        private void _chat_PeerAdded(BitChat sender, BitChat.Peer peer)
        {
            lstUsers.AddItem(new UserListItem(peer));
            peer.StateChanged += _view.peer_StateChanged;
            peer.StateChanged += peer_StateChanged;
        }

        private void peer_StateChanged(object sender, EventArgs e)
        {
            BitChat.Peer peer = sender as BitChat.Peer;

            if (peer.IsOnline)
                MessageNotification(_chat, null, peer.PeerCertificate.IssuedTo.Name + " is online");
            else
                MessageNotification(_chat, null, peer.PeerCertificate.IssuedTo.Name + " is offline");
        }

        private void _chat_FileAdded(BitChat sender, SharedFile sharedFile)
        {
            SharedFileItem item = new SharedFileItem(sharedFile, _chat);
            item.FileRemoved += OnFileRemoved;

            lstFiles.AddItem(item);
        }

        private void _chat_MessageReceived(BitChat.Peer sender, string message)
        {
            MessageNotification(_chat, sender, message);
        }

        #endregion

        #region private

        private void lstUsers_ItemClick(object sender, EventArgs e)
        {
            UserListItem item = lstUsers.SelectedItem as UserListItem;

            if (item != null)
            {
                using (frmViewUser frm = new frmViewUser(item.Peer))
                {
                    frm.ShowDialog(this);
                }
            }
        }

        private void OnFileRemoved(object sender, EventArgs e)
        {
            SharedFileItem item = sender as SharedFileItem;

            lstFiles.RemoveItem(item);
        }

        private void SplitContainer_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if ((SettingsModified != null) && !_skipSettingsModifiedEvent)
                SettingsModified(this, EventArgs.Empty);
        }

        private void _view_SettingsModified(object sender, EventArgs e)
        {
            if ((SettingsModified != null) && !_skipSettingsModifiedEvent)
                SettingsModified(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            _skipSettingsModifiedEvent = true;
            base.OnResize(e);
            _skipSettingsModifiedEvent = false;
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

        #endregion

        #region public

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

        #region properties

        public BitChat BitChat
        { get { return _chat; } }

        #endregion
    }
}
