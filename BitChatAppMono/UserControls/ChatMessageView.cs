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
        DateTime lastTypingNotification;

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

            _chat.MessageReceived += chat_MessageReceived;
            _chat.PeerAdded += chat_PeerAdded;
            _chat.PeerTyping += chat_PeerTyping;
            _chat.PeerHasRevokedCertificate += chat_PeerHasRevokedCertificate;
            _chat.PeerSecureChannelException += chat_PeerSecureChannelException;
            _chat.PeerHasChangedCertificate += chat_PeerHasChangedCertificate;

            if (_chat.NetworkType == BitChatClient.Network.BitChatNetworkType.PrivateChat)
            {
                if (_chat.NetworkName == null)
                    this.Title = _chat.PeerEmailAddress.Address;
                else
                    this.Title = _chat.NetworkName + " <" + _chat.PeerEmailAddress.Address + ">";
            }
            else
            {
                this.Title = _chat.NetworkName;
            }
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

            if (_chat.NetworkType == BitChatClient.Network.BitChatNetworkType.PrivateChat)
            {
                _chatItem.SetTitle(peer.PeerCertificate.IssuedTo.Name);
                this.Title = peer.PeerCertificate.IssuedTo.Name + " <" + peer.PeerCertificate.IssuedTo.EmailAddress.Address + ">";
            }
        }

        private void chat_MessageReceived(BitChat.Peer sender, string message)
        {
            bool myMessage = sender.PeerCertificate.IssuedTo.EmailAddress.Address.Equals(_chat.LocalCertificate.IssuedTo.EmailAddress.Address);

            AddMessage(new ChatMessageItem(sender, message, DateTime.Now, myMessage));

            if (!_chatItem.Selected)
                _chatItem.SetNewMessage(sender.PeerCertificate.IssuedTo.Name + ": " + message);

            ShowPeerTypingNotification(sender.PeerCertificate.IssuedTo.Name, false);
        }

        private void chat_PeerAdded(BitChat sender, BitChat.Peer peer)
        {
            AddMessage(new ChatMessageInfoItem(peer.PeerCertificate.IssuedTo.Name + " joined chat", DateTime.Now));

            if (sender.NetworkType == BitChatClient.Network.BitChatNetworkType.PrivateChat)
            {
                _chatItem.SetTitle(peer.PeerCertificate.IssuedTo.Name);
                this.Title = peer.PeerCertificate.IssuedTo.Name + " <" + peer.PeerCertificate.IssuedTo.EmailAddress.Address + ">";
            }
        }

        private void chat_PeerTyping(BitChat sender, BitChat.Peer peer)
        {
            ShowPeerTypingNotification(peer.PeerCertificate.IssuedTo.Name, true);
        }

        private void chat_PeerHasRevokedCertificate(BitChat sender, InvalidCertificateException ex)
        {
            AddMessage(new ChatMessageInfoItem(ex.Message));
        }

        private void chat_PeerSecureChannelException(BitChat sender, SecureChannelException ex)
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

        private void chat_PeerHasChangedCertificate(BitChat sender, Certificate cert)
        {
            AddMessage(new ChatMessageInfoItem("Warning! Peer '" + cert.IssuedTo.EmailAddress.Address + "' has changed his profile certificate [serial number: " + cert.SerialNumber + ", expires on: " + cert.ExpiresOnUTC.ToShortDateString() + "]", DateTime.Now));
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
                lastTypingNotification = DateTime.UtcNow.AddSeconds(-10);
            }
            else
            {
                DateTime current = DateTime.UtcNow;

                if ((current - lastTypingNotification).TotalSeconds > 5)
                {
                    lastTypingNotification = current;
                    _chat.SendTypingNotification();
                }
            }
        }

        private void txtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.V:
                        if (Clipboard.ContainsFileDropList())
                        {
                            List<string> fileNames = new List<string>();

                            foreach (string filePath in Clipboard.GetFileDropList())
                            {
                                if (File.Exists(filePath))
                                    fileNames.Add(filePath);
                            }

                            ShareFiles(fileNames.ToArray());
                            e.Handled = true;
                            e.SuppressKeyPress = true;
                        }
                        break;

                    case Keys.Back:
                        int i = txtMessage.Text.LastIndexOf(' ');

                        if (i > -1)
                        {
                            txtMessage.Text = txtMessage.Text.Substring(0, i);
                            txtMessage.SelectionStart = i;
                        }
                        else
                        {
                            txtMessage.Text = "";
                            txtMessage.SelectionStart = 0;
                        }

                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        break;
                }
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

        private void ShowPeerTypingNotification(string peerName, bool add)
        {
            lock (timerTypingNotification)
            {
                List<string> peerNames;

                if (labTypingNotification.Tag == null)
                {
                    peerNames = new List<string>(3);
                    labTypingNotification.Tag = peerNames;
                }
                else
                {
                    peerNames = labTypingNotification.Tag as List<string>;
                }

                {
                    if (add)
                    {
                        if (!peerNames.Contains(peerName))
                            peerNames.Add(peerName);
                    }
                    else
                    {
                        if (peerName == null)
                            peerNames.Clear();
                        else
                            peerNames.Remove(peerName);
                    }
                }

                switch (peerNames.Count)
                {
                    case 0:
                        labTypingNotification.Text = "";
                        break;

                    case 1:
                        labTypingNotification.Text = peerNames[0] + " is typing...";
                        break;

                    case 2:
                        labTypingNotification.Text = peerNames[0] + " and " + peerNames[1] + " are typing...";
                        break;

                    default:
                        string tmp = peerNames[0];

                        for (int i = 1; i < peerNames.Count - 1; i++)
                        {
                            tmp += ", " + peerNames[i];
                        }

                        tmp += " and " + peerNames[peerNames.Count - 1];
                        labTypingNotification.Text = tmp + " are typing...";
                        break;
                }

                if (peerName != null)
                {
                    timerTypingNotification.Stop();
                    timerTypingNotification.Start();
                }
            }
        }

        private void timerTypingNotification_Tick(object sender, EventArgs e)
        {
            lock (timerTypingNotification)
            {
                timerTypingNotification.Stop();

                ShowPeerTypingNotification(null, false);
            }
        }

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
