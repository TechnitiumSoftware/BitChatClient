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
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmPassword : Form
    {
        #region variables

        string _profileFilePath;

        BitChatProfile _profile;

        #endregion

        #region constructor

        public frmPassword(string profileFilePath)
        {
            InitializeComponent();

            _profileFilePath = profileFilePath;

            labProfileName.Text = Path.GetFileNameWithoutExtension(_profileFilePath);
        }

        #endregion

        #region private

        private void btnOK_Click(object sender, EventArgs e)
        {
            try
            {
                using (FileStream fS = new FileStream(_profileFilePath, FileMode.Open, FileAccess.Read))
                {
                    _profile = new BitChatProfile(fS, txtPassword.Text);
                }

                DialogResult = System.Windows.Forms.DialogResult.OK;
                this.Close();
            }
            catch
            {
                try
                {
                    using (FileStream fS = new FileStream(_profileFilePath + ".bak", FileMode.Open, FileAccess.Read))
                    {
                        _profile = new BitChatProfile(fS, txtPassword.Text);
                    }

                    DialogResult = System.Windows.Forms.DialogResult.OK;
                    this.Close();
                }
                catch
                {
                    MessageBox.Show("Invalid password. Please try again.", "Invalid Password!", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

                    txtPassword.Text = "";
                    txtPassword.Focus();
                }
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void lnkForgotPassword_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (MessageBox.Show("Since the profile password is used to encrypt the profile data, there is no method to recover the encryption password or the profile data.\r\n\r\nTo access Bit Chat, you will need to register a new profile and you can use the same email address for registration. You will lose all your settings and you will have to join all your chats again.\r\n\r\nDo you want to register a new profile now?", "Register New Profile?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes)
            {
                this.DialogResult = System.Windows.Forms.DialogResult.Yes;
                this.Close();
            }
        }

        #endregion

        #region properties

        public BitChatProfile Profile
        { get { return _profile; } }

        #endregion
    }
}
