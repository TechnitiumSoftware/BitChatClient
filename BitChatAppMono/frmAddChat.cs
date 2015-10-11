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

using BitChatClient.Network;
using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Windows.Forms;

namespace BitChatAppMono
{
    public partial class frmAddChat : Form
    {
        #region variables

        BitChatNetworkType _type;

        #endregion

        #region Form Code

        public frmAddChat()
        {
            InitializeComponent();

            _type = BitChatNetworkType.GroupChat;
            this.Text = "Add Group Chat";
        }

        public frmAddChat(BitChatNetworkType type)
        {
            InitializeComponent();

            _type = type;

            if (type == BitChatNetworkType.PrivateChat)
            {
                this.Text = "Add Private Chat";

                label1.Text = "Peer's Email Address";
                label3.Text = "(case insensitive name. example: user@example.com)";
                label4.Text = "Both peers must use same Shared Secret and enter each other's email address.";
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            txtNetworkNameOrPeerEmailAddress.Text = txtNetworkNameOrPeerEmailAddress.Text.Trim();

            if (string.IsNullOrEmpty(txtNetworkNameOrPeerEmailAddress.Text))
            {
                if (_type == BitChatNetworkType.PrivateChat)
                    MessageBox.Show("Please enter an email address of your peer to chat with.", "Invalid Email Address", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    MessageBox.Show("Please enter a network name for the new chat.", "Missing Network Name", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return;
            }

            if (_type == BitChatNetworkType.PrivateChat)
            {
                try
                {
                    MailAddress x = new MailAddress(txtNetworkNameOrPeerEmailAddress.Text);
                }
                catch
                {
                    MessageBox.Show("Please enter a valid email address of your peer to chat with.", "Invalid Email Address", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    return;
                }
            }

            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        public bool OnlyLanChat
        { get { return chkLANChat.Checked; } }

        #endregion
    }
}
