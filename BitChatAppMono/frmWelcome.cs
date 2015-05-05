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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace BitChatAppMono
{
    public partial class frmWelcome : Form
    {
        #region variables

        string _localAppData;

        BitChatProfile _profile;
        string _profileFilePath;

        #endregion

        #region constructors

        public frmWelcome()
        {
            InitializeComponent();
        }

        public frmWelcome(string localAppData)
        {
            InitializeComponent();

            _localAppData = localAppData;
        }

        #endregion

        #region private

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("http://bitchat.im");
        }

        private void frmRegisterNow_Click(object sender, EventArgs e)
        {
            this.Hide();

            using (frmRegister frm = new frmRegister(_localAppData))
            {
                DialogResult result = frm.ShowDialog(this);

                switch (result)
                {
                    case System.Windows.Forms.DialogResult.OK:
                        _profile = frm.Profile;
                        _profileFilePath = frm.ProfileFilePath;

                        this.DialogResult = System.Windows.Forms.DialogResult.OK;
                        this.Close();
                        break;

                    case System.Windows.Forms.DialogResult.Ignore:
                        this.Show();
                        break;

                    default:
                        this.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                        this.Close();
                        break;
                }
            }
        }

        private void btnAlreadyRegistered_Click(object sender, EventArgs e)
        {
            this.DialogResult = System.Windows.Forms.DialogResult.Ignore;
            this.Close();
        }

        #endregion

        #region properties

        public BitChatProfile Profile
        { get { return _profile; } }

        public string ProfileFilePath
        { get { return _profileFilePath; } }

        #endregion
    }
}
