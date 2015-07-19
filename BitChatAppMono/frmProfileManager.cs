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
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BitChatAppMono
{
    public partial class frmProfileManager : Form
    {
        #region variables

        string _localAppData;

        BitChatProfile _profile;
        string _profileFilePath;

        bool _loaded = false;

        #endregion

        #region constructor

        public frmProfileManager(string localAppData, bool loaded)
        {
            InitializeComponent();

            _localAppData = localAppData;

            //find & list profiles
            string[] profiles = Directory.GetFiles(_localAppData, "*.profile", SearchOption.TopDirectoryOnly);

            foreach (string profile in profiles)
            {
                if (profile.EndsWith(".profile"))
                    lstProfiles.Items.Add(Path.GetFileNameWithoutExtension(profile));
            }

            _loaded = loaded;
        }

        #endregion

        #region private

        private void frmProfileManager_Load(object sender, EventArgs e)
        {
            if (_loaded)
                return;

            switch (lstProfiles.Items.Count)
            {
                case 0:
                    using (frmWelcome frm = new frmWelcome(_localAppData))
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
                                btnImportProfile_Click(null, null);
                                break;

                            default:
                                this.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                                this.Close();
                                break;
                        }
                    }
                    break;

                case 1:
                    _profileFilePath = Path.Combine(_localAppData, (lstProfiles.Items[0] as string) + ".profile");

                    using (frmPassword frm = new frmPassword(_profileFilePath))
                    {
                        switch (frm.ShowDialog(this))
                        {
                            case System.Windows.Forms.DialogResult.OK:
                                _profile = frm.Profile;

                                this.DialogResult = System.Windows.Forms.DialogResult.OK;
                                this.Close();
                                break;

                            case System.Windows.Forms.DialogResult.Yes:
                                btnNewProfile_Click(null, null);
                                break;
                        }
                    }
                    break;

                default:
                    break;
            }
        }

        private void lstProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnStart.Enabled = (lstProfiles.SelectedItem != null);
            btnReIssueProfile.Enabled = btnStart.Enabled;
            btnDeleteProfile.Enabled = btnStart.Enabled;
            btnExportProfile.Enabled = btnStart.Enabled;
        }

        private void lstProfiles_DoubleClick(object sender, EventArgs e)
        {
            if (lstProfiles.SelectedItem != null)
                btnStart_Click(null, null);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            _profileFilePath = Path.Combine(_localAppData, (lstProfiles.SelectedItem as string) + ".profile");

            using (frmPassword frm = new frmPassword(_profileFilePath))
            {
                if (frm.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    _profile = frm.Profile;

                    this.DialogResult = System.Windows.Forms.DialogResult.OK;
                    this.Close();
                }
            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            this.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Close();
        }

        private void btnNewProfile_Click(object sender, EventArgs e)
        {
            this.Hide();

            using (frmRegister frm = new frmRegister(_localAppData))
            {
                if (frm.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    _profile = frm.Profile;
                    _profileFilePath = frm.ProfileFilePath;

                    string profileName = Path.GetFileNameWithoutExtension(_profileFilePath);

                    lstProfiles.Items.Add(profileName);
                    lstProfiles.SelectedItem = profileName;
                }
            }

            this.Show();
        }

        private void btnReIssueProfile_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Reissuing a profile certificate will allow you register again with the same email address and change your information in the profile certificate while keeping all your profile settings intact.\r\n\r\nAre you sure you want to reissue the selected profile?\r\n\r\nWARNING! This will revoke the previously issued profile certificate however, your settings will remain intact.", "Reissue Profile Certificate?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == System.Windows.Forms.DialogResult.Yes)
            {
                this.Hide();

                _profileFilePath = Path.Combine(_localAppData, (lstProfiles.SelectedItem as string) + ".profile");

                using (frmPassword frm = new frmPassword(_profileFilePath))
                {
                    if (frm.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                    {
                        _profile = frm.Profile;

                        using (frmRegister frmReg = new frmRegister(_localAppData, _profile, _profileFilePath, true))
                        {
                            if (frmReg.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                            {
                                _profile = frmReg.Profile;
                                _profileFilePath = frmReg.ProfileFilePath;

                                string profileName = Path.GetFileNameWithoutExtension(_profileFilePath);
                                lstProfiles.SelectedItem = profileName;
                            }
                        }
                    }
                }

                this.Show();
            }
        }

        private void btnDeleteProfile_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to permanently delete selected profile?\r\n\r\nWarning! This will delete the profile file permanently and hence cannot be undone.", "Delete Profile Permanently?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == System.Windows.Forms.DialogResult.Yes)
            {
                List<string> toRemove = new List<string>();

                foreach (string profile in lstProfiles.SelectedItems)
                {
                    try
                    {
                        File.Delete(Path.Combine(_localAppData, profile + ".profile"));
                        File.Delete(Path.Combine(_localAppData, profile + ".profile.bak"));

                        toRemove.Add(profile);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error! Cannot delete profile '" + profile + "' due to following error:\r\n\r\n" + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                foreach (string profile in toRemove)
                    lstProfiles.Items.Remove(profile);
            }
        }

        private void btnImportProfile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog oFD = new OpenFileDialog())
            {
                oFD.Filter = "Bit Chat Profile File (*.profile)|*.profile";
                oFD.Title = "Import Bit Chat Profile File...";
                oFD.CheckFileExists = true;
                oFD.Multiselect = false;

                if (oFD.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        string profileName = Path.GetFileNameWithoutExtension(oFD.FileName);

                        File.Copy(oFD.FileName, Path.Combine(_localAppData, profileName + ".profile"));

                        lstProfiles.Items.Add(profileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error! Cannot import profile due to following error:\r\n\r\n" + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void btnExportProfile_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sFD = new SaveFileDialog())
            {
                sFD.Title = "Export Bit Chat Profile File As...";
                sFD.Filter = "Bit Chat Profile File (*.profile)|*.profile";
                sFD.DefaultExt = ".profile";
                sFD.CheckPathExists = true;
                sFD.OverwritePrompt = true;

                if (sFD.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        File.Copy(Path.Combine(_localAppData, lstProfiles.SelectedItem + ".profile"), sFD.FileName, true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error! Cannot export profile due to following error:\r\n\r\n" + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void frmProfileManager_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
                btnDeleteProfile_Click(null, null);
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
