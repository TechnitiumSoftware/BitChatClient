/*
Technitium Bit Chat
Copyright (C) 2016  Shreyas Zare (shreyas@technitium.com)

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

using BitChatCore;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatApp
{
    public partial class frmViewProfile : Form
    {
        #region variables

        BitChatProfile _profile;
        BitChat.Peer _peer;

        Image _profileImage;
        bool _changesMade = false;

        #endregion

        #region constructor

        public frmViewProfile(BitChatProfile profile, BitChat.Peer peer)
        {
            InitializeComponent();

            _profile = profile;
            _peer = peer;

            CertificateProfile certProfile;
            byte[] profileImage = null;

            if (_peer == null)
            {
                certProfile = _profile.LocalCertificateStore.Certificate.IssuedTo;
                profileImage = _profile.ProfileImage;
            }
            else
            {
                lnkView.Text = "View User Details";

                if (!_peer.IsSelf)
                {
                    labIcon.Cursor = Cursors.Default;
                    picIcon.Cursor = Cursors.Default;
                }

                certProfile = _peer.PeerCertificate.IssuedTo;

                if (_peer.IsOnline)
                    profileImage = _peer.ProfileImage;
            }

            //name
            if (certProfile.FieldExists(CertificateProfileFlags.Name))
            {
                string name = certProfile.Name;

                //name icon
                if (name.Length > 0)
                {
                    labIcon.Text = name.Substring(0, 1).ToUpper();

                    int x = name.LastIndexOf(" ", StringComparison.CurrentCultureIgnoreCase);
                    if (x > 0)
                    {
                        labIcon.Text += name.Substring(x + 1, 1).ToUpper();
                    }
                    else if (name.Length > 1)
                    {
                        labIcon.Text += name.Substring(1, 1).ToLower();
                    }
                }
                else
                {
                    labIcon.Text = "";
                }

                if ((_peer == null) || _peer.IsOnline)
                    labIcon.BackColor = Color.FromArgb(102, 153, 255);
                else
                    labIcon.BackColor = Color.Gray;

                labName.Text = name;
            }
            else
            {
                labIcon.Text = "";
                labName.Text = "{missing name}";
                labName.ForeColor = Color.Red;
            }

            //email
            if (certProfile.FieldExists(CertificateProfileFlags.EmailAddress))
            {
                labEmail.Text = certProfile.EmailAddress.Address;
            }
            else
            {
                labEmail.Text = "{missing email address}";
                labEmail.ForeColor = Color.Red;
            }

            //location
            if (certProfile.FieldExists(CertificateProfileFlags.City | CertificateProfileFlags.State | CertificateProfileFlags.Country))
            {
                labLocation.Text = "";

                if (certProfile.FieldExists(CertificateProfileFlags.City))
                    labLocation.Text = certProfile.City + ", ";

                if (certProfile.FieldExists(CertificateProfileFlags.State))
                    labLocation.Text += certProfile.State + ", ";

                if (certProfile.FieldExists(CertificateProfileFlags.Country))
                    labLocation.Text += certProfile.Country;
            }
            else
            {
                labLocation.Text = "{missing location}";
                labLocation.ForeColor = Color.Red;
            }

            //image icon
            if (profileImage != null)
            {
                using (MemoryStream mS = new MemoryStream(profileImage))
                {
                    _profileImage = Image.FromStream(mS);
                    picIcon.Image = _profileImage;
                }

                picIcon.Visible = true;
                labIcon.Visible = false;
            }
        }

        #endregion

        #region form code

        private void mnuChangePhoto_Click(object sender, EventArgs e)
        {
            using (frmImageDialog frm = new frmImageDialog())
            {
                if (frm.ShowDialog(this) == DialogResult.OK)
                {
                    _profileImage = frm.SelectedImage;
                    picIcon.Image = _profileImage;

                    using (MemoryStream mS = new MemoryStream(4096))
                    {
                        frm.SelectedImage.Save(mS, System.Drawing.Imaging.ImageFormat.Jpeg);
                        _profile.ProfileImage = mS.ToArray();
                    }

                    _changesMade = true;

                    picIcon.Visible = true;
                    labIcon.Visible = false;
                }
            }
        }

        private void mnuRemovePhoto_Click(object sender, EventArgs e)
        {
            _profile.ProfileImage = null;
            _profileImage = null;
            picIcon.Image = Properties.Resources.change_photo;

            _changesMade = true;

            picIcon.Visible = false;
            labIcon.Visible = true;
        }

        private void labIcon_MouseUp(object sender, MouseEventArgs e)
        {
            if ((_peer == null) || _peer.IsSelf)
            {
                switch (e.Button)
                {
                    case MouseButtons.Left:
                        mnuChangePhoto_Click(null, null);
                        break;

                    case MouseButtons.Right:
                        mnuRemovePhoto.Enabled = (_profileImage != null);
                        Control control = sender as Control;
                        mnuProfileImage.Show(control, e.Location);
                        break;
                }
            }
        }

        private void labIcon_MouseEnter(object sender, EventArgs e)
        {
            if ((_peer == null) || _peer.IsSelf)
            {
                if (_profileImage == null)
                {
                    picIcon.Visible = true;
                    labIcon.Visible = false;
                }
                else
                {
                    picIcon.Image = Properties.Resources.change_photo;
                }
            }
        }

        private void picIcon_MouseEnter(object sender, EventArgs e)
        {
            if ((_peer == null) || _peer.IsSelf)
            {
                if (_profileImage != null)
                    picIcon.Image = Properties.Resources.change_photo;
            }
        }

        private void picIcon_MouseLeave(object sender, EventArgs e)
        {
            if ((_peer == null) || _peer.IsSelf)
            {
                if (_profileImage == null)
                {
                    labIcon.Visible = true;
                    picIcon.Visible = false;
                }
                else
                {
                    picIcon.Image = _profileImage;
                }
            }
        }

        private void lnkView_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (_peer == null)
            {
                using (frmViewCertificate frm = new frmViewCertificate(_profile.LocalCertificateStore.Certificate))
                {
                    frm.ShowDialog(this);
                }
            }
            else
            {
                using (frmViewUserDetails frm = new frmViewUserDetails(_peer))
                {
                    frm.ShowDialog(this);
                }
            }
        }

        private void labName_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mnuCopyUtility.Tag = sender;
                mnuCopyUtility.Show(sender as Control, e.Location);
            }
        }

        private void mnuCopy_Click(object sender, EventArgs e)
        {
            Label label = mnuCopyUtility.Tag as Label;

            if (label != null)
            {
                try
                {
                    Clipboard.Clear();
                    Clipboard.SetText(label.Text);
                }
                catch
                { }
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            if (_changesMade)
                this.DialogResult = DialogResult.OK;

            this.Close();
        }

        #endregion
    }
}
