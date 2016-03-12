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

using BitChatClient;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BitChatApp.UserControls
{
    public partial class ChatMessageInfoItem : CustomListViewItem, IChatMessageItem
    {
        const int BORDER_SIZE = 1;

        Color BorderColor = Color.FromArgb(224, 224, 223);
        MessageItem _message;

        public ChatMessageInfoItem()
        {
            InitializeComponent();
        }

        public ChatMessageInfoItem(MessageItem message)
        {
            InitializeComponent();

            _message = message;

            if (string.IsNullOrEmpty(_message.Message))
            {
                label1.Text = _message.MessageDate.ToLocalTime().ToString("dddd, MMMM d, yyyy");
                label2.Visible = false;
            }
            else
            {
                label1.Text = _message.Message;
                label2.Text = _message.MessageDate.ToLocalTime().ToShortTimeString();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                                         BorderColor, 0, ButtonBorderStyle.Solid,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid,
                                         BorderColor, 0, ButtonBorderStyle.Solid,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            this.Refresh();
        }

        private void copyInfoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_message.Message))
                    Clipboard.SetText(label1.Text);
                else
                    Clipboard.SetText("[" + _message.MessageDate.ToString("d MMM, yyyy HH:mm:ss") + "] " + label1.Text);
            }
            catch
            { }
        }

        public MessageItem Message
        { get { return _message; } }
    }
}
