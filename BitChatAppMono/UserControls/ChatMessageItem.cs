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
using TechnitiumLibrary.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace BitChatAppMono.UserControls
{
    public partial class ChatMessageItem : CustomListViewItem
    {
        BitChat.Peer _senderPeer;
        DateTime _date;

        public ChatMessageItem()
        {
            InitializeComponent();
        }

        public ChatMessageItem(BitChat.Peer senderPeer, string message, DateTime date, bool myMessage)
        {
            InitializeComponent();

            _senderPeer = senderPeer;
            _date = date;

            lblUsername.Text = _senderPeer.PeerCertificate.IssuedTo.Name;
            txtMessage.Text = message;
            lblDateTime.Text = date.ToString("HH:mm");

            if (myMessage)
                lblUsername.ForeColor = Color.FromArgb(63, 186, 228);

            OnResize(EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if ((txtMessage != null) && txtMessage.Text != "")
            {
                Size msgSize = TextRenderer.MeasureText(txtMessage.Text, txtMessage.Font, new Size(txtMessage.Size.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                if (this.Height != msgSize.Height)
                    this.Height = msgSize.Height + 5; //5 pixel padding
            }
        }

        protected override void OnMouseOver(bool hovering)
        {
            if (hovering)
                this.BackColor = Color.FromArgb(241, 245, 249);
            else
                this.BackColor = Color.White;

            txtMessage.BackColor = this.BackColor;
        }

        private void lblUsername_Click(object sender, EventArgs e)
        {
            using (frmViewUser frm = new frmViewUser(_senderPeer))
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

        public DateTime MessageDate
        { get { return _date; } }
    }
}
