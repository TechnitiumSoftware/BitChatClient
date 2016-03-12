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
    public partial class ChatMessageItem : CustomListViewItem, IChatMessageItem
    {
        BitChat.Peer _senderPeer;
        MessageItem _message;

        public ChatMessageItem()
        {
            InitializeComponent();
        }

        public ChatMessageItem(BitChat.Peer senderPeer, MessageItem message, bool myMessage)
        {
            InitializeComponent();

            _senderPeer = senderPeer;
            _message = message;

            lblUsername.Text = _senderPeer.PeerCertificate.IssuedTo.Name;
            lblMessage.Text = _message.Message;
            lblDateTime.Text = _message.MessageDate.ToLocalTime().ToShortTimeString();

            if (myMessage)
                lblUsername.ForeColor = Color.FromArgb(63, 186, 228);

            ResizeHeightByTextSize();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            ResizeHeightByTextSize();
        }

        protected override void OnMouseOver(bool hovering)
        {
            if (hovering)
                this.BackColor = Color.FromArgb(241, 245, 249);
            else
                this.BackColor = Color.White;

            lblMessage.BackColor = this.BackColor;
        }

        private void ResizeHeightByTextSize()
        {
            if ((lblMessage != null) && lblMessage.Text != "")
            {
                Size msgSize = TextRenderer.MeasureText(lblMessage.Text, lblMessage.Font, new Size(lblMessage.Size.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                if (this.Height != msgSize.Height)
                    this.Height = msgSize.Height + 5; //5 pixel padding
            }
        }

        private void lblUsername_Click(object sender, EventArgs e)
        {
            using (frmViewProfile frm = new frmViewProfile(_senderPeer))
            {
                frm.ShowDialog();
            }
        }

        private void lblUsername_MouseEnter(object sender, EventArgs e)
        {
            lblUsername.Font = new Font(lblUsername.Font, FontStyle.Underline | FontStyle.Bold);
        }

        private void lblUsername_MouseLeave(object sender, EventArgs e)
        {
            lblUsername.Font = new Font(lblUsername.Font, FontStyle.Regular | FontStyle.Bold);
        }

        private void copyMessageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText("[" + _message.MessageDate.ToString("d MMM, yyyy HH:mm:ss") + "] " + lblUsername.Text + "> " + lblMessage.Text);
            }
            catch
            { }
        }

        public MessageItem Message
        { get { return _message; } }
    }
}
