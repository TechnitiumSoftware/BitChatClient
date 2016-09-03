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

using BitChatCore.Network;
using System;
using System.Net.Mail;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmAddChat : Form
    {
        #region variables

        BitChatNetworkType _type;

        #endregion

        #region Form Code

        public frmAddChat(BitChatNetworkType type)
        {
            InitializeComponent();

            _type = type;

            if (type == BitChatNetworkType.PrivateChat)
            {
                this.Text = "Add Private Chat";

                label1.Text = "Peer's Email Address";
                label3.Text = "(case insensitive, example: user@example.com)";
                label4.Text = "Both peers must use same Shared Secret and enter each other's email address.";
            }
            else
            {
                this.Text = "Add Group Chat";

                chkSendInvitation.Visible = false;
                txtInvitationMessage.Visible = false;
                label6.Visible = false;
                label7.Visible = false;

                this.Height = 200;
            }
        }

        #endregion

        #region form code

        private void txtSharedSecret_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtSharedSecret.Text))
            {
                chkSendInvitation.Enabled = true;
                txtInvitationMessage.Enabled = chkSendInvitation.Checked;
            }
            else
            {
                chkSendInvitation.Enabled = false;
                txtInvitationMessage.Enabled = false;
            }
        }

        private void chkSendInvitation_CheckedChanged(object sender, EventArgs e)
        {
            txtInvitationMessage.Enabled = chkSendInvitation.Checked;
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

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion

        #region properties

        public string NetworkNameOrPeerEmailAddress
        { get { return txtNetworkNameOrPeerEmailAddress.Text; } }

        public string SharedSecret
        { get { return txtSharedSecret.Text; } }

        public bool OnlyLanChat
        { get { return chkLANChat.Checked; } }

        public string InvitationMessage
        {
            get
            {
                if (chkSendInvitation.Enabled && chkSendInvitation.Checked)
                    return txtInvitationMessage.Text;
                else
                    return null;
            }
        }

        #endregion
    }
}
