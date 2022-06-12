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

using BitChatCore;
using BitChatCore.FileSharing;
using BitChatCore.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatApp.UserControls
{
    public partial class ChatMessageView : CustomPanel
    {
        #region events

        public event EventHandler SettingsModified;
        public event EventHandler ShareFile;

        #endregion

        #region variables

        const int MESSAGE_COUNT_PER_SCROLL = 20;

        BitChat _chat;
        ChatListItem _chatItem;

        bool _skipSettingsModifiedEvent = false;
        DateTime lastTypingNotification;

        #endregion

        #region constructor

        public ChatMessageView(BitChat chat, ChatListItem chatItem)
        {
            InitializeComponent();

            _chat = chat;
            _chatItem = chatItem;

            _chat.MessageReceived += chat_MessageReceived;
            _chat.MessageDeliveryNotification += chat_MessageDeliveryNotification;
            _chat.FileAdded += chat_FileAdded;
            _chat.PeerAdded += chat_PeerAdded;
            _chat.PeerTyping += chat_PeerTyping;
            _chat.PeerHasRevokedCertificate += chat_PeerHasRevokedCertificate;
            _chat.PeerSecureChannelException += chat_PeerSecureChannelException;
            _chat.PeerHasChangedCertificate += chat_PeerHasChangedCertificate;

            this.Title = _chat.NetworkDisplayTitle;

            if (_chat.NetworkType == BitChatCore.Network.BitChatNetworkType.GroupChat)
                _chat.GroupImageChanged += chat_GroupImageChanged;

            //load stored messages
            int totalMessageCount = _chat.GetMessageCount();
            if (totalMessageCount > 0)
            {
                try
                {
                    customListView1.ReplaceItems(ConvertToListViewItems(_chat.GetLastMessages(totalMessageCount, MESSAGE_COUNT_PER_SCROLL), true));
                    customListView1.ScrollToBottom();
                }
                catch
                { }
            }
        }

        #endregion

        #region bitchat events

        private void chat_GroupImageChanged(BitChat sender, BitChat.Peer peer)
        {
            _chat.WriteInfoMessage(peer.PeerCertificate.IssuedTo.Name + " changed group photo");
        }

        private void chat_MessageReceived(BitChat.Peer sender, MessageItem message)
        {
            if (message.Type == MessageType.Info)
            {
                AddMessage(new ChatMessageInfoItem(message), sender.IsSelf);
            }
            else
            {
                AddMessage(new ChatMessageTextItem(sender, message), sender.IsSelf);

                string msg = message.Message;

                if (msg.Length > 100)
                    msg = msg.Substring(0, 100);

                ShowPeerTypingNotification(sender.PeerCertificate.IssuedTo.Name, false);
            }
        }

        private void chat_MessageDeliveryNotification(BitChat.Peer sender, MessageItem message)
        {
            foreach (Control item in customListView1.Controls)
            {
                ChatMessageTextItem msgItem = item as ChatMessageTextItem;
                if ((msgItem != null) && (msgItem.Message.MessageNumber == message.MessageNumber))
                {
                    msgItem.DeliveryNotification(message);
                    break;
                }
            }
        }

        private void chat_FileAdded(BitChat.Peer peer, MessageItem message, SharedFile sharedFile)
        {
            ChatMessageFileItem fileItem = new ChatMessageFileItem(peer, message, sharedFile);
            AddMessage(fileItem, true);

            fileItem.ShareFile += FileItem_ShareFile;
        }

        private void chat_PeerAdded(BitChat sender, BitChat.Peer peer)
        {
            _chat.WriteInfoMessage(peer.PeerCertificate.IssuedTo.Name + " joined chat");

            if (sender.NetworkType == BitChatCore.Network.BitChatNetworkType.PrivateChat)
                this.Title = peer.PeerCertificate.IssuedTo.Name + " <" + peer.PeerCertificate.IssuedTo.EmailAddress.Address + ">";
        }

        private void chat_PeerTyping(BitChat sender, BitChat.Peer peer)
        {
            ShowPeerTypingNotification(peer.PeerCertificate.IssuedTo.Name, true);
        }

        private void chat_PeerHasRevokedCertificate(BitChat sender, InvalidCertificateException ex)
        {
            _chat.WriteInfoMessage(ex.Message);
        }

        private void chat_PeerSecureChannelException(BitChat sender, SecureChannelException ex)
        {
            string peerInfo;

            if (ex.PeerCertificate == null)
                peerInfo = "[" + ex.PeerEP.ToString() + "]";
            else
                peerInfo = ex.PeerCertificate.IssuedTo.Name + " <" + ex.PeerCertificate.IssuedTo.EmailAddress.Address + "> [" + ex.PeerEP.ToString() + "]";

            _chat.WriteInfoMessage("Secure channel with peer '" + peerInfo + "' encountered '" + ex.Code.ToString() + "' exception.");

            if (ex.InnerException != null)
                _chat.WriteInfoMessage(ex.InnerException.ToString());
        }

        private void chat_PeerHasChangedCertificate(BitChat sender, Certificate cert)
        {
            _chat.WriteInfoMessage("Warning! Peer '" + cert.IssuedTo.EmailAddress.Address + "' has changed profile certificate [serial number: " + cert.SerialNumber + ", issued on: " + cert.IssuedOnUTC.ToShortDateString() + "]");
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

        public void SetFocusMessageEditor()
        {
            txtMessage.Focus();
        }

        public void TrimMessageList()
        {
            if (customListView1.IsScrolledToBottom())
                customListView1.TrimListFromTop(MESSAGE_COUNT_PER_SCROLL);
        }

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
            AddMessage(new ChatMessageInfoItem(new MessageItem("Please wait while file(s) are being shared")), true);

            Action<string[]> d = new Action<string[]>(ShareFileAsync);
            d.BeginInvoke(fileNames, null, null);
        }

        #endregion

        #region UI code

        private void FileItem_ShareFile(object sender, EventArgs e)
        {
            ShareFile?.Invoke(sender, e);
        }

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
                        string msgRight = txtMessage.Text.Substring(0, txtMessage.SelectionStart);
                        string msgLeft = txtMessage.Text.Substring(txtMessage.SelectionStart);

                        int i = msgRight.TrimEnd().LastIndexOfAny(new char[] { ' ', '\n' });

                        if (i > -1)
                        {
                            i++;
                            txtMessage.Text = msgRight.Substring(0, i) + msgLeft;
                            txtMessage.SelectionStart = i;
                        }
                        else
                        {
                            txtMessage.Text = msgLeft;
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

        private List<CustomListViewItem> ConvertToListViewItems(MessageItem[] items, bool updateLastMessageInChatItem)
        {
            BitChat.Peer[] peerList = _chat.GetPeerList();
            SharedFile[] fileList = _chat.GetSharedFileList();

            List<CustomListViewItem> listItems = new List<CustomListViewItem>(items.Length);
            DateTime lastItemDate = new DateTime();
            string lastMessage = null;
            DateTime lastMessageDate = new DateTime();

            foreach (MessageItem item in items)
            {
                if (lastItemDate.Date < item.MessageDate.Date)
                {
                    lastItemDate = item.MessageDate;
                    listItems.Add(new ChatMessageInfoItem(new MessageItem(lastItemDate)));
                }

                switch (item.Type)
                {
                    case MessageType.Info:
                        listItems.Add(new ChatMessageInfoItem(item));
                        break;

                    case MessageType.TextMessage:
                    case MessageType.InvitationMessage:
                        {
                            BitChat.Peer sender = null;

                            foreach (BitChat.Peer peer in peerList)
                            {
                                if (peer.PeerCertificate.IssuedTo.EmailAddress.Address.Equals(item.Sender))
                                {
                                    sender = peer;
                                    break;
                                }
                            }

                            listItems.Add(new ChatMessageTextItem(sender, item));

                            if (sender == null)
                                lastMessage = item.Sender + ": " + item.Message;
                            else if (sender.IsSelf)
                                lastMessage = item.Message;
                            else
                                lastMessage = sender.PeerCertificate.IssuedTo.Name + ": " + item.Message;

                            lastMessageDate = item.MessageDate;
                        }
                        break;

                    case MessageType.SharedFileMetaData:
                        {
                            BitChat.Peer sender = null;
                            SharedFile file = null;

                            foreach (BitChat.Peer peer in peerList)
                            {
                                if (peer.PeerCertificate.IssuedTo.EmailAddress.Address.Equals(item.Sender))
                                {
                                    sender = peer;
                                    break;
                                }
                            }

                            foreach (SharedFile sharedFile in fileList)
                            {
                                if (sharedFile.MetaData.FileID.Equals(item.SharedFileMetaData.FileID))
                                {
                                    file = sharedFile;
                                    break;
                                }
                            }

                            ChatMessageFileItem fileItem = new ChatMessageFileItem(sender, item, file);
                            listItems.Add(fileItem);

                            fileItem.ShareFile += FileItem_ShareFile;

                            if (sender == null)
                                lastMessage = item.Sender + " shared a file";
                            else if (sender.IsSelf)
                                lastMessage = "file was shared";
                            else
                                lastMessage = sender.PeerCertificate.IssuedTo.Name + " shared a file";

                            lastMessageDate = item.MessageDate;
                        }
                        break;
                }
            }

            if (updateLastMessageInChatItem && (lastMessage != null))
                _chatItem.SetLastMessage(lastMessage, lastMessageDate, false);

            return listItems;
        }

        private void customListView1_ScrolledNearStart(object sender, EventArgs e)
        {
            foreach (CustomListViewItem item in customListView1.Controls)
            {
                IChatMessageItem messageItem = item as IChatMessageItem;

                if (messageItem.Message.MessageNumber == 0)
                {
                    return;
                }
                else if (messageItem.Message.MessageNumber > -1)
                {
                    customListView1.InsertItemsAtTop(ConvertToListViewItems(_chat.GetLastMessages(messageItem.Message.MessageNumber, MESSAGE_COUNT_PER_SCROLL), false));
                    return;
                }
            }
        }

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

                    case 3:
                        labTypingNotification.Text = peerNames[0] + ", " + peerNames[1] + " and " + peerNames[2] + " are typing...";
                        break;

                    default:
                        labTypingNotification.Text = "many people are typing...";
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

        private void AddMessage(CustomListViewItem item, bool selfSender)
        {
            CustomListViewItem lastItem = customListView1.GetLastItem();

            bool insertDateInfo = false;
            DateTime itemDate = (item as IChatMessageItem).Message.MessageDate;

            if (lastItem == null)
            {
                insertDateInfo = true;
            }
            else
            {
                if (itemDate.Date > (lastItem as IChatMessageItem).Message.MessageDate.Date)
                    insertDateInfo = true;
            }

            bool wasScrolledToBottom = customListView1.IsScrolledToBottom();

            if (insertDateInfo)
                customListView1.AddItem(new ChatMessageInfoItem(new MessageItem(DateTime.UtcNow)));

            customListView1.AddItem(item);

            if (_chatItem.Selected && (wasScrolledToBottom || selfSender))
                customListView1.ScrollToBottom();
        }

        #endregion
    }
}
