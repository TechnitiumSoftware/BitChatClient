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

using BitChatClient;
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

        bool _changesMade = false;

        #endregion

        #region constructor

        public frmViewProfile()
        {
            InitializeComponent();
        }

        public frmViewProfile(BitChatProfile profile)
        {
            InitializeComponent();

            _profile = profile;

            labIcon.ContextMenuStrip = this.mnuProfileImage;
            picIcon.ContextMenuStrip = this.mnuProfileImage;

            //name
            if (_profile.LocalCertificateStore.Certificate.IssuedTo.FieldExists(CertificateProfileFlags.Name))
            {
                string name = _profile.LocalCertificateStore.Certificate.IssuedTo.Name;

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

                labIcon.BackColor = Color.FromArgb(102, 153, 255);
                labName.Text = name;
            }
            else
            {
                labIcon.Text = "";
                labName.Text = "{missing name}";
                labName.ForeColor = Color.Red;
            }

            //email
            if (_profile.LocalCertificateStore.Certificate.IssuedTo.FieldExists(CertificateProfileFlags.EmailAddress))
            {
                labEmail.Text = _profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress.Address;
            }
            else
            {
                labEmail.Text = "{missing email address}";
                labEmail.ForeColor = Color.Red;
            }

            //location
            if (_profile.LocalCertificateStore.Certificate.IssuedTo.FieldExists(CertificateProfileFlags.City | CertificateProfileFlags.State | CertificateProfileFlags.Country))
            {
                labLocation.Text = "";

                if (_profile.LocalCertificateStore.Certificate.IssuedTo.FieldExists(CertificateProfileFlags.City))
                    labLocation.Text = _profile.LocalCertificateStore.Certificate.IssuedTo.City + ", ";

                if (_profile.LocalCertificateStore.Certificate.IssuedTo.FieldExists(CertificateProfileFlags.State))
                    labLocation.Text += _profile.LocalCertificateStore.Certificate.IssuedTo.State + ", ";

                if (_profile.LocalCertificateStore.Certificate.IssuedTo.FieldExists(CertificateProfileFlags.Country))
                    labLocation.Text += _profile.LocalCertificateStore.Certificate.IssuedTo.Country;
            }
            else
            {
                labLocation.Text = "{missing location}";
                labLocation.ForeColor = Color.Red;
            }

            //image icon
            if (_profile.ProfileImageLarge != null)
            {
                using (MemoryStream mS = new MemoryStream(_profile.ProfileImageLarge))
                {
                    picIcon.Image = Image.FromStream(mS);
                }

                picIcon.Visible = true;
                labIcon.Visible = false;
            }
        }

        public frmViewProfile(BitChat.Peer peer)
        {
            InitializeComponent();

            _peer = peer;

            lnkView.Text = "View User Details";
            labIcon.Cursor = Cursors.Default;
            picIcon.Cursor = Cursors.Default;

            //name
            if (_peer.PeerCertificate.IssuedTo.FieldExists(CertificateProfileFlags.Name))
            {
                string name = _peer.PeerCertificate.IssuedTo.Name;

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

                if (_peer.IsOnline)
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
            if (_peer.PeerCertificate.IssuedTo.FieldExists(CertificateProfileFlags.EmailAddress))
            {
                labEmail.Text = _peer.PeerCertificate.IssuedTo.EmailAddress.Address;
            }
            else
            {
                labEmail.Text = "{missing email address}";
                labEmail.ForeColor = Color.Red;
            }

            //location
            if (_peer.PeerCertificate.IssuedTo.FieldExists(CertificateProfileFlags.City | CertificateProfileFlags.State | CertificateProfileFlags.Country))
            {
                labLocation.Text = "";

                if (_peer.PeerCertificate.IssuedTo.FieldExists(CertificateProfileFlags.City))
                    labLocation.Text = _peer.PeerCertificate.IssuedTo.City + ", ";

                if (_peer.PeerCertificate.IssuedTo.FieldExists(CertificateProfileFlags.State))
                    labLocation.Text += _peer.PeerCertificate.IssuedTo.State + ", ";

                if (_peer.PeerCertificate.IssuedTo.FieldExists(CertificateProfileFlags.Country))
                    labLocation.Text += _peer.PeerCertificate.IssuedTo.Country;
            }
            else
            {
                labLocation.Text = "{missing location}";
                labLocation.ForeColor = Color.Red;
            }

            //image icon
            if (_peer.ProfileImageLarge != null)
            {
                using (MemoryStream mS = new MemoryStream(_peer.ProfileImageLarge))
                {
                    picIcon.Image = Image.FromStream(mS);
                }

                picIcon.Visible = true;
                labIcon.Visible = false;
            }
        }

        #endregion

        #region form code

        private void mnuProfileImage_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            mnuRemovePhoto.Enabled = picIcon.Visible;
        }

        private void mnuChangePhoto_Click(object sender, EventArgs e)
        {
            using (frmImageDialog frm = new frmImageDialog())
            {
                if (frm.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    picIcon.Image = frm.SelectedImageLarge;

                    byte[] imageSmall;
                    byte[] imageLarge;

                    using (MemoryStream mS = new MemoryStream(4096))
                    {
                        frm.SelectedImageSmall.Save(mS, System.Drawing.Imaging.ImageFormat.Jpeg);
                        imageSmall = mS.ToArray();
                    }

                    using (MemoryStream mS = new MemoryStream(4096))
                    {
                        frm.SelectedImageLarge.Save(mS, System.Drawing.Imaging.ImageFormat.Jpeg);
                        imageLarge = mS.ToArray();
                    }

                    _profile.SetProfileImage(imageSmall, imageLarge);

                    _changesMade = true;

                    picIcon.Visible = true;
                    labIcon.Visible = false;
                }
            }
        }

        private void mnuRemovePhoto_Click(object sender, EventArgs e)
        {
            _profile.SetProfileImage(null, null);

            _changesMade = true;

            picIcon.Visible = false;
            labIcon.Visible = true;
        }

        private void labIcon_MouseUp(object sender, MouseEventArgs e)
        {
            if (_peer == null)
                mnuProfileImage.Show(labIcon, e.Location);
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

        private void btnClose_Click(object sender, EventArgs e)
        {
            if (_changesMade)
                this.DialogResult = System.Windows.Forms.DialogResult.OK;

            this.Close();
        }

        #endregion
    }
}
