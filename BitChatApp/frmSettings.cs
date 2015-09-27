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

namespace BitChatApp
{
    public partial class frmSettings : Form
    {
        #region variables

        BitChatService _service;

        ushort _port = 0;
        List<Uri> _trackers = new List<Uri>();

        Timer _timer;

        #endregion

        #region constructor

        public frmSettings(BitChatService service)
        {
            InitializeComponent();

            _service = service;

            BitChatProfile profile = service.Profile;

            txtPort.Text = profile.LocalPort.ToString();
            txtDownloadFolder.Text = profile.DownloadFolder;
            chkUseCRL.Checked = profile.CheckCertificateRevocationList;

            foreach (Uri tracker in profile.TrackerURIs)
            {
                txtTrackers.Text += tracker.AbsoluteUri + "\r\n";
            }

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += _timer_Tick;
            _timer.Start();
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

            if (!ushort.TryParse(txtPort.Text, out _port))
            {
                MessageBox.Show("The port number specified is invalid. The number must be in 0-65535 range.", "Invalid Port Specified!", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show("The tracker URL format is invalid. Please enter a valid tracker URL.", "Invalid Tracker URL", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void _timer_Tick(object sender, EventArgs e)
        {
            switch (_service.UPnPStatus)
            {
                case UPnPDeviceStatus.PortForwarded:
                    lblUPnPStatus.Text = "Port forwarded [" + _service.UPnPExternalEP.ToString() + "]";
                    break;

                case UPnPDeviceStatus.PortForwardedNotAccessible:
                    lblUPnPStatus.Text = "Port forwarded not accessible [" + _service.UPnPExternalEP.ToString() + "]";
                    break;

                case UPnPDeviceStatus.ExternalIpPrivate:
                    lblUPnPStatus.Text = "Device WAN IP is private [" + _service.UPnPExternalEP.ToString() + "]";
                    break;

                case UPnPDeviceStatus.PortForwardingFailed:
                    lblUPnPStatus.Text = "Port forwarding failed";
                    break;

                case UPnPDeviceStatus.DeviceNotFound:
                    lblUPnPStatus.Text = "UPnP device not found";
                    break;

                default:
                    lblUPnPStatus.Text = "Unknown";
                    break;
            }
        }

        #endregion

        #region properties

        public bool PasswordChangeRequest
        { get { return chkAccept.Checked; } }

        public string Password
        { get { return txtProfilePassword.Text; } }

        public ushort Port
        { get { return _port; } }

        public string DownloadFolder
        { get { return txtDownloadFolder.Text; } }

        public bool CheckCertificateRevocationList
        { get { return chkUseCRL.Checked; } }

        public Uri[] Trackers
        { get { return _trackers.ToArray(); } }

        #endregion
    }
}
