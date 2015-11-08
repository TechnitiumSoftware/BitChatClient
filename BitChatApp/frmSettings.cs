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
using BitChatClient.Network.Connections;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Net;
using TechnitiumLibrary.Net.Proxy;

namespace BitChatApp
{
    public partial class frmSettings : Form
    {
        #region variables

        BitChatService _service;

        List<Uri> _trackers = new List<Uri>();
        ushort _port = 0;

        string _proxyAddress;
        ushort _proxyPort = 0;

        #endregion

        #region constructor

        public frmSettings(BitChatService service)
        {
            InitializeComponent();

            _service = service;

            BitChatProfile profile = service.Profile;

            txtDownloadFolder.Text = profile.DownloadFolder;

            foreach (Uri tracker in profile.TrackerURIs)
            {
                txtTrackers.Text += tracker.AbsoluteUri + "\r\n";
            }

            txtPort.Text = profile.LocalPort.ToString();
            chkUseCRL.Checked = profile.CheckCertificateRevocationList;
            chkUPnP.Checked = profile.EnableUPnP;

            if (profile.Proxy == null)
                cmbProxy.SelectedIndex = 0;
            else
                cmbProxy.SelectedIndex = (int)profile.Proxy.Type;

            btnCheckProxy.Enabled = (cmbProxy.SelectedIndex != 0);
            txtProxyAddress.Text = profile.ProxyAddress;
            txtProxyPort.Text = profile.ProxyPort.ToString();
            txtProxyAddress.Enabled = btnCheckProxy.Enabled;
            txtProxyPort.Enabled = btnCheckProxy.Enabled;
            chkProxyAuth.Enabled = btnCheckProxy.Enabled;

            if (profile.ProxyCredentials == null)
            {
                chkProxyAuth.Checked = false;
            }
            else
            {
                chkProxyAuth.Checked = true;
                txtProxyUser.Text = profile.ProxyCredentials.UserName;
                txtProxyPass.Text = profile.ProxyCredentials.Password;
            }

            txtProxyUser.Enabled = chkProxyAuth.Enabled && chkProxyAuth.Checked;
            txtProxyPass.Enabled = chkProxyAuth.Enabled && chkProxyAuth.Checked;
        }

        #endregion

        #region form code

        private void btnBrowseDLFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fBD = new FolderBrowserDialog())
            {
                fBD.SelectedPath = txtDownloadFolder.Text;
                fBD.Description = "Select a default folder to save downloaded files:";

                if (fBD.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                    txtDownloadFolder.Text = fBD.SelectedPath;
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (chkAccept.Checked)
            {
                if (txtProfilePassword.Text != txtConfirmPassword.Text)
                {
                    MessageBox.Show("Passwords don't match. Please enter password again.", "Passwords Don't Match!", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    txtProfilePassword.Text = "";
                    txtConfirmPassword.Text = "";

                    txtProfilePassword.Focus();
                    return;
                }
            }

            if (!Directory.Exists(txtDownloadFolder.Text))
            {
                MessageBox.Show("Download folder does not exists. Please select a valid folder.", "Download Folder Does Not Exists!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                _trackers.Clear();
                string[] strTrackers = txtTrackers.Text.Split(new char[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string strTracker in strTrackers)
                {
                    _trackers.Add(new Uri(strTracker));
                }
            }
            catch (Exception)
            {
                MessageBox.Show("The tracker URL format is invalid. Please enter a valid tracker URL.", "Invalid Tracker URL!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!ushort.TryParse(txtPort.Text, out _port))
            {
                MessageBox.Show("The port number specified is invalid. The number must be in 0-65535 range.", "Invalid Port Specified!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if ((cmbProxy.SelectedIndex != 0) && (string.IsNullOrWhiteSpace(txtProxyAddress.Text)))
            {
                MessageBox.Show("The proxy address is missing. Please enter a valid proxy address.", "Proxy Address Missing!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _proxyAddress = txtProxyAddress.Text;

            if (!ushort.TryParse(txtProxyPort.Text, out _proxyPort))
            {
                MessageBox.Show("The proxy port number specified is invalid. The number must be in 0-65535 range.", "Invalid Proxy Port Specified!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if ((chkProxyAuth.Checked) && (string.IsNullOrWhiteSpace(txtProxyUser.Text)))
            {
                MessageBox.Show("The proxy username is missing. Please enter a username.", "Proxy Username Missing!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.Close();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(@"http://go.technitium.com/?id=3");
        }

        private void cmbProxy_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnCheckProxy.Enabled = (cmbProxy.SelectedIndex != 0);
            txtProxyAddress.Enabled = btnCheckProxy.Enabled;
            txtProxyPort.Enabled = btnCheckProxy.Enabled;
            chkProxyAuth.Enabled = btnCheckProxy.Enabled;
            txtProxyUser.Enabled = chkProxyAuth.Enabled && chkProxyAuth.Checked;
            txtProxyPass.Enabled = chkProxyAuth.Enabled && chkProxyAuth.Checked;
        }

        private void chkProxyAuth_CheckedChanged(object sender, EventArgs e)
        {
            txtProxyUser.Enabled = chkProxyAuth.Checked;
            txtProxyPass.Enabled = chkProxyAuth.Checked;
        }

        private void btnCheckProxy_Click(object sender, EventArgs e)
        {
            try
            {
                NetProxyType proxyType = (NetProxyType)cmbProxy.SelectedIndex;
                NetProxy proxy;
                NetworkCredential credentials = null;

                if (chkProxyAuth.Checked)
                    credentials = new NetworkCredential(txtProxyUser.Text, txtProxyPass.Text);

                switch (proxyType)
                {
                    case NetProxyType.Http:
                        proxy = new NetProxy(new WebProxyEx(new Uri("http://" + txtProxyAddress.Text + ":" + int.Parse(txtProxyPort.Text)), false, new string[] { }, credentials));
                        break;

                    case NetProxyType.Socks5:
                        proxy = new NetProxy(new SocksClient(txtProxyAddress.Text, int.Parse(txtProxyPort.Text), credentials));
                        break;

                    default:
                        return;
                }

                proxy.CheckProxyAccess();

                MessageBox.Show("Bit Chat was able to connect to the proxy server successfully.", "Proxy Check Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Proxy Check Failed!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region properties

        public bool PasswordChangeRequest
        { get { return chkAccept.Checked; } }

        public string Password
        { get { return txtProfilePassword.Text; } }

        public string DownloadFolder
        { get { return txtDownloadFolder.Text; } }

        public Uri[] Trackers
        { get { return _trackers.ToArray(); } }

        public ushort Port
        { get { return _port; } }

        public bool CheckCertificateRevocationList
        { get { return chkUseCRL.Checked; } }

        public bool EnableUPnP
        { get { return chkUPnP.Checked; } }

        public bool EnableProxy
        { get { return cmbProxy.SelectedIndex != 0; } }

        public NetProxyType ProxyType
        { get { return (NetProxyType)cmbProxy.SelectedIndex; } }

        public string ProxyAddress
        { get { return _proxyAddress; } }

        public int ProxyPort
        { get { return _proxyPort; } }

        public NetworkCredential ProxyCredentials
        {
            get
            {
                if (chkProxyAuth.Checked)
                    return new NetworkCredential(txtProxyUser.Text, txtProxyPass.Text);
                else
                    return null;
            }
        }

        #endregion
    }
}
