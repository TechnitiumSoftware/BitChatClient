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
using System.Windows.Forms;

namespace BitChatAppMono
{
    public partial class frmWelcome : Form
    {
        #region variables

        bool _isPortableApp;
        string _profileFolder;

        BitChatProfile _profile;
        string _profileFilePath;

        #endregion

        #region constructors

        public frmWelcome(bool isPortableApp, string profileFolder)
        {
            InitializeComponent();

            _isPortableApp = isPortableApp;
            _profileFolder = profileFolder;
        }

        #endregion

        #region private

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://bitchat.im");
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("http://blog.technitium.com/2015/11/how-to-register-profile-get-started.html");
        }

        private void frmRegisterNow_Click(object sender, EventArgs e)
        {
            this.Hide();

            using (frmRegister frm = new frmRegister(_isPortableApp, _profileFolder))
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
