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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using BitChatClient;
using System.IO;
using TechnitiumLibrary.Security.Cryptography;
using TechnitiumLibrary.Net;
using BitChatClient.Network.SecureChannel;

namespace BitChatAppMono.UserControls
{
    public partial class ChatMessageView : CustomPanel
    {
        #region events

        public event EventHandler SettingsModified;

        #endregion

        #region variables

        BitChat _chat;
        ChatListItem _chatItem;

        bool _skipSettingsModifiedEvent = false;

        #endregion

        #region constructor

        public ChatMessageView()
        {
            InitializeComponent();
        }

        public ChatMessageView(BitChat chat, ChatListItem chatItem)
        {
            InitializeComponent();

            _chat = chat;
            _chatItem = chatItem;

            _chat.MessageReceived += _chat_MessageReceived;
            _chat.PeerAdded += _chat_PeerAdded;
            _chat.PeerHasRevokedCertificate += _chat_PeerHasRevokedCertificate;
            _chat.PeerSecureChannelException += _chat_PeerSecureChannelException;

            this.Title = _chat.NetworkName;
        }

        #endregion

        #region bitchat events

        internal void peer_StateChanged(object sender, EventArgs e)
        {
            BitChat.Peer peer = sender as BitChat.Peer;

            string message;

            if (peer.IsOnline)
                message = peer.PeerCertificate.IssuedTo.Name + " is online";
            else
                message = peer.PeerCertificate.IssuedTo.Name + " is offline";

            AddMessage(new ChatMessageInfoItem(message, DateTime.Now));

            if (!_chatItem.Selected)
                _chatItem.SetNewMessage(message);
        }

        private void _chat_PeerAdded(BitChat sender, BitChat.Peer peer)
        {
            AddMessage(new ChatMessageInfoItem(peer.PeerCertificate.IssuedTo.Name + " joined chat", DateTime.Now));
        }

        private void _chat_PeerHasRevokedCertificate(BitChat sender, InvalidCertificateException ex)
        {
            AddMessage(new ChatMessageInfoItem(ex.Message));
        }

        private void _chat_PeerSecureChannelException(BitChat sender, SecureChannelException ex)
        {
            string peerInfo;

            if (ex.PeerCertificate == null)
                peerInfo = "[" + ex.PeerEP.ToString() + "]";
            else
                peerInfo = ex.PeerCertificate.IssuedTo.Name + " <" + ex.PeerCertificate.IssuedTo.EmailAddress.Address + "> [" + ex.PeerEP.ToString() + "]";

            string desc;

            if (ex.Code == SecureChannelCode.RemoteError)
                desc = "RemoteError: " + (ex.InnerException as SecureChannelException).Code.ToString();
            else
                desc = ex.Code.ToString();

            AddMessage(new ChatMessageInfoItem("Secure channel with peer '" + peerInfo + "' encountered '" + desc + "' exception.", DateTime.Now));
        }

        private void _chat_MessageReceived(BitChat.Peer sender, string message)
        {
            bool myMessage = sender.PeerCertificate.IssuedTo.EmailAddress.Address.Equals(_chat.LocalCertificate.IssuedTo.EmailAddress.Address);

            AddMessage(new ChatMessageItem(sender, message, DateTime.Now, myMessage));

            if (!_chatItem.Selected)
                _chatItem.SetNewMessage(sender.PeerCertificate.IssuedTo.Name + ": " + message);
        }

        #endregion

        #region protected UI code

        protected override void OnResize(EventArgs e)
        {
            _skipSettingsModifiedEvent = true;

            base.OnResize(e);

            if (splitContainer1 != null)
                txtMessage.Size = new Size(splitContainer1.Panel2.Width - 1 - 2 - btnSend.Width - 1, splitContainer1.Panel2.Height - 2);

            _skipSettingsModifiedEvent = false;
        }

        #endregion

        #region public

        public void ReadSettingsFrom(BinaryReader bR)
        {
            _skipSettingsModifiedEvent = true;
            splitContainer1.SplitterDistance = splitContainer1.Height - bR.ReadInt32();
            _skipSettingsModifiedEvent = false;
        }

        public void WriteSettingsTo(BinaryWriter bW)
        {
            bW.Write(splitContainer1.Height - splitContainer1.SplitterDistance);
        }

        public void ShareFiles(string[] fileNames)
        {
            foreach (string filename in fileNames)
            {
                AddMessage(new ChatMessageInfoItem("Please wait while '" + Path.GetFileName(filename) + "' of " + WebUtilities.GetFormattedSize((new FileInfo(filename)).Length) + " is being shared"));
            }

            Action<string[]> d = new Action<string[]>(ShareFileAsync);
            d.BeginInvoke(fileNames, null, null);
        }

        #endregion

        #region UI code

        private void splitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if ((SettingsModified != null) && !_skipSettingsModifiedEvent)
                SettingsModified(this, EventArgs.Empty);
        }

        private void txtMessage_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                btnSend_Click(null, null);

                e.Handled = true;
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (txtMessage.Text != "")
            {
                AddMessage(new ChatMessageItem(_chat.SelfPeer, txtMessage.Text, DateTime.Now, true));

                _chat.SendTextMessage(txtMessage.Text);

                txtMessage.Text = "";
                txtMessage.Focus();
            }
        }

        private void btnShareFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog oFD = new OpenFileDialog())
            {
                oFD.Title = "Select files to share ...";
                oFD.CheckFileExists = true;
                oFD.Multiselect = true;

                if (oFD.ShowDialog(this) == DialogResult.OK)
                {
                    ShareFiles(oFD.FileNames);
                }
            }
        }

        #endregion

        #region private

        private void ShareFileAsync(string[] filenames)
        {
            foreach (string filename in filenames)
            {
                try
                {
                    _chat.ShareFile(filename);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error encountered while sharing file.\r\n\r\n" + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void AddMessage(CustomListViewItem item)
        {
            CustomListViewItem lastItem = customListView1.GetLastItem();

            bool showDate = false;

            if (lastItem == null)
                showDate = true;
            else
            {
                if (lastItem.GetType().Equals(typeof(ChatMessageItem)))
                {
                    if (DateTime.Now.Date > (lastItem as ChatMessageItem).MessageDate.Date)
                        showDate = true;
                }
                else if (lastItem.GetType().Equals(typeof(ChatMessageInfoItem)))
                {
                    ChatMessageInfoItem xItem = lastItem as ChatMessageInfoItem;

                    if (xItem.IsDateSet() && (DateTime.Now.Date > xItem.MessageDate.Date))
                        showDate = true;
                }
            }

            if (showDate)
                customListView1.AddItem(new ChatMessageInfoItem(DateTime.Now.ToString("dddd, MMMM d, yyyy")));

            customListView1.AddItem(item);

            if (_chatItem.Selected)
                customListView1.ScrollToBottom();
        }

        #endregion
    }
}
