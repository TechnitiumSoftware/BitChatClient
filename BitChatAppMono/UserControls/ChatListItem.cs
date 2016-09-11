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
using System.Drawing;
using System.IO;

namespace BitChatApp.UserControls
{
    public partial class ChatListItem : CustomListViewItem
    {
        #region variables

        DateTime _messageDate;
        int _unreadMessageCount;
        bool _isOffline;

        #endregion

        #region constructor

        public ChatListItem(string title)
        {
            InitializeComponent();

            labLastMessage.Text = "";
            SetLastMessageDate();
            SetTitle(title);
            ResetUnreadMessageCount();
        }

        #endregion

        #region private

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

        #endregion

        #region public

        public void SetTitle(string title)
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

        public void SetLastMessage(string message, DateTime messageDate, bool unread)
        {
            _messageDate = messageDate;

            labLastMessage.Text = message;
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

        public void SetImageIcon(byte[] image)
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

        public override string ToString()
        {
            SetLastMessageDate();

            return ((int)(DateTime.UtcNow - _messageDate).TotalSeconds).ToString().PadLeft(12, '0');
        }

        #endregion

        #region properties
        
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
