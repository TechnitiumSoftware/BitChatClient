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
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BitChatApp.UserControls
{
    public partial class ChatListItem : CustomListViewItem
    {
        #region variables

        BitChat _chat;
        BitChat.Peer _peer;
        BitChatPanel _chatPanel;

        string _message;
        DateTime _messageDate;
        int _unreadMessageCount;
        bool _isOffline;

        #endregion

        #region constructor

        public ChatListItem(BitChat chat)
        {
            InitializeComponent();

            _chat = chat;

            SetTitle(_chat.NetworkDisplayName);
            labLastMessage.Text = "";
            SetLastMessageDate();
            ResetUnreadMessageCount();

            _chat.FileAdded += chat_FileAdded;
            _chat.MessageReceived += chat_MessageReceived;
            _chat.PeerAdded += chat_PeerAdded;
            _chat.PeerTyping += chat_PeerTyping;

            if (_chat.NetworkType == BitChatCore.Network.BitChatNetworkType.PrivateChat)
            {
                foreach (BitChat.Peer peer in _chat.GetPeerList())
                {
                    if (!peer.IsSelf)
                    {
                        _peer = peer;
                        peer.ProfileImageChanged += peer_ProfileImageChanged;
                        peer.StateChanged += peer_StateChanged;
                    }
                }
            }
            else
            {
                SetImageIcon(_chat.GroupImage);

                _chat.GroupImageChanged += chat_GroupImageChanged;
            }

            this.GoOffline = (_chat.NetworkStatus == BitChatCore.Network.BitChatNetworkStatus.Offline);

            _chatPanel = new BitChatPanel(_chat, this);
            _chatPanel.Dock = DockStyle.Fill;
        }

        #endregion

        #region private

        private void chat_FileAdded(BitChat.Peer peer, MessageItem message, BitChatCore.FileSharing.SharedFile sharedFile)
        {
            if (peer.IsSelf)
                SetLastMessage("file was shared", message.MessageDate, true);
            else
                SetLastMessage(peer.PeerCertificate.IssuedTo.Name + " shared a file", message.MessageDate, true);
        }

        private void chat_MessageReceived(BitChat.Peer peer, MessageItem message)
        {
            if (message.Type != MessageType.Info)
            {
                string msg = message.Message;

                if (msg.Length > 100)
                    msg = msg.Substring(0, 100);

                if (peer.IsSelf)
                    SetLastMessage(msg, message.MessageDate, true);
                else
                    SetLastMessage(peer.PeerCertificate.IssuedTo.Name + ": " + msg, message.MessageDate, true);
            }
        }

        private void chat_GroupImageChanged(BitChat chat, BitChat.Peer peer)
        {
            SetImageIcon(_chat.GroupImage);
        }

        private void chat_PeerAdded(BitChat chat, BitChat.Peer peer)
        {
            if (_chat.NetworkType == BitChatCore.Network.BitChatNetworkType.PrivateChat)
            {
                SetTitle(peer.PeerCertificate.IssuedTo.Name);

                _peer = peer;
                peer.ProfileImageChanged += peer_ProfileImageChanged;
                peer.StateChanged += peer_StateChanged;
            }
        }

        private void chat_PeerTyping(BitChat chat, BitChat.Peer peer)
        {
            //show typing notification
            if ((_chat.NetworkType == BitChatCore.Network.BitChatNetworkType.PrivateChat) && !peer.IsSelf)
                labLastMessage.Text = "typing...";
            else
                labLastMessage.Text = peer.PeerCertificate.IssuedTo.Name + " is typing...";

            labLastMessage.ForeColor = Color.FromArgb(255, 213, 89);

            timerTypingNotification.Stop();
            timerTypingNotification.Start();
        }

        private void peer_StateChanged(object sender, EventArgs e)
        {
            BitChat.Peer peer = sender as BitChat.Peer;

            SetTitle(peer.PeerCertificate.IssuedTo.Name);

            if (peer.IsOnline)
                peer_ProfileImageChanged(sender, e);
            else
                SetImageIcon(null);
        }

        private void peer_ProfileImageChanged(object sender, EventArgs e)
        {
            BitChat.Peer _peer = sender as BitChat.Peer;
            SetImageIcon(_peer.ProfileImage);
        }

        private void timerTypingNotification_Tick(object sender, EventArgs e)
        {
            //hide typing notification
            labLastMessage.Text = _message;
            labLastMessage.ForeColor = Color.White;
        }

        protected override void OnSelected()
        {
            this.SuspendLayout();

            if (_isOffline)
            {
                if (Selected)
                {
                    this.BackColor = Color.FromArgb(61, 78, 93);
                    labIcon.BackColor = Color.Gray;

                    ResetUnreadMessageCount();
                }
                else
                {
                    this.BackColor = Color.FromArgb(51, 65, 78);
                    labIcon.BackColor = Color.Gray;
                }
            }
            else
            {
                if (Selected)
                {
                    this.BackColor = Color.FromArgb(61, 78, 93);
                    labIcon.BackColor = Color.FromArgb(255, 213, 89);

                    ResetUnreadMessageCount();
                }
                else
                {
                    this.BackColor = Color.FromArgb(51, 65, 78);
                    labIcon.BackColor = Color.White;
                }
            }

            this.ResumeLayout();
        }

        protected override void OnMouseOver(bool hovering)
        {
            if (!Selected)
            {
                if (hovering)
                    this.BackColor = Color.FromArgb(61, 78, 93);
                else
                    this.BackColor = Color.FromArgb(51, 65, 78);
            }
        }

        private void ResetUnreadMessageCount()
        {
            _unreadMessageCount = 0;
            labUnreadMessageCount.Visible = false;
            labLastMessage.Width += labUnreadMessageCount.Width;
        }

        private void SetLastMessageDate()
        {
            if (string.IsNullOrEmpty(labLastMessage.Text))
            {
                labLastMessageDate.Text = "";
            }
            else
            {
                TimeSpan span = DateTime.UtcNow.Date - _messageDate.Date;

                if (span.TotalDays >= 7)
                    labLastMessageDate.Text = _messageDate.ToLocalTime().ToShortDateString();
                else if (span.TotalDays >= 2)
                    labLastMessageDate.Text = _messageDate.ToLocalTime().DayOfWeek.ToString();
                else if (span.TotalDays >= 1)
                    labLastMessageDate.Text = "Yesterday";
                else
                    labLastMessageDate.Text = _messageDate.ToLocalTime().ToShortTimeString();
            }

            labTitle.Width = this.Width - labTitle.Left - labLastMessageDate.Width - 3;
            labLastMessageDate.Left = labTitle.Left + labTitle.Width;
        }

        private void SetTitle(string title)
        {
            labTitle.Text = title;
            labIcon.Text = title.Substring(0, 1).ToUpper();

            int x = title.LastIndexOf(" ", StringComparison.CurrentCultureIgnoreCase);
            if (x > 0)
            {
                labIcon.Text += title.Substring(x + 1, 1).ToUpper();
            }
            else if (title.Length > 1)
            {
                labIcon.Text += title.Substring(1, 1).ToLower();
            }
        }

        private void SetImageIcon(byte[] image)
        {
            if (image == null)
            {
                picIcon.Image = null;

                labIcon.Visible = true;
                picIcon.Visible = false;
            }
            else
            {
                using (MemoryStream mS = new MemoryStream(image))
                {
                    picIcon.Image = new Bitmap(Image.FromStream(mS), picIcon.Size);
                }

                picIcon.Visible = !_isOffline;
                labIcon.Visible = _isOffline;
            }
        }

        #endregion

        #region public

        public void SetLastMessage(string message, DateTime messageDate, bool unread)
        {
            _message = message;
            _messageDate = messageDate;

            timerTypingNotification.Stop();

            labLastMessage.Text = _message;
            labLastMessage.ForeColor = Color.White;

            SetLastMessageDate();

            if (!this.Selected && unread)
            {
                if (_unreadMessageCount < 999)
                    _unreadMessageCount++;

                if (!labUnreadMessageCount.Visible)
                {
                    labUnreadMessageCount.Visible = true;
                    labLastMessage.Width -= labUnreadMessageCount.Width;
                }

                labUnreadMessageCount.Text = _unreadMessageCount.ToString();
            }

            this.SortListView();
        }

        public override string ToString()
        {
            SetLastMessageDate();

            string dateString = ((int)(DateTime.UtcNow - _messageDate).TotalSeconds).ToString().PadLeft(12, '0');

            if (_isOffline)
                return "1" + dateString + labTitle.Text;
            else
                return "0" + dateString + labTitle.Text;
        }

        #endregion

        #region properties

        public BitChat BitChat
        { get { return _chat; } }

        public BitChat.Peer Peer
        { get { return _peer; } }

        public BitChatPanel ChatPanel
        { get { return _chatPanel; } }

        public bool GoOffline
        {
            get { return _isOffline; }
            set
            {
                _isOffline = value;

                if (picIcon.Image != null)
                {
                    picIcon.Visible = !_isOffline;
                    labIcon.Visible = _isOffline;
                }

                OnSelected();
                SortListView();
            }
        }

        #endregion
    }
}
