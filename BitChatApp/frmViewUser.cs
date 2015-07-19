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
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmViewUser : Form
    {
        #region variables

        BitChat.Peer _peer;

        #endregion

        #region constructor

        public frmViewUser()
        {
            InitializeComponent();
        }

        public frmViewUser(BitChat.Peer peer)
        {
            InitializeComponent();

            _peer = peer;

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

            //network status
            switch (_peer.NetworkStatus)
            {
                case BitChatNetworkStatus.NoNetwork:
                    labNetworkStatus.Text = "No Network";
                    labNetworkStatus.ForeColor = Color.DimGray;
                    picNetwork.Image = BitChatApp.Properties.Resources.NoNetwork;
                    break;

                case BitChatNetworkStatus.PartialNetwork:
                    labNetworkStatus.Text = "Partial Network";
                    labNetworkStatus.ForeColor = Color.OrangeRed;
                    picNetwork.Image = BitChatApp.Properties.Resources.PartialNetwork;
                    break;

                case BitChatNetworkStatus.FullNetwork:
                    labNetworkStatus.Text = "Full Network";
                    labNetworkStatus.ForeColor = Color.Green;
                    picNetwork.Image = BitChatApp.Properties.Resources.FullNetwork;
                    break;

                default:
                    labNetworkStatus.Text = "Unknown";
                    labNetworkStatus.ForeColor = Color.DimGray;
                    picNetwork.Image = BitChatApp.Properties.Resources.NoNetwork;
                    break;
            }

            //cipher suite
            labCipherSuite.Text = _peer.CipherSuite.ToString();

            //connected with
            PeerInfo[] connectedWith = _peer.ConnectedWith;

            foreach (PeerInfo peerInfo in connectedWith)
            {
                string peerIPs = null;

                foreach (IPEndPoint peerEP in peerInfo.PeerEPList)
                {
                    if (peerIPs == null)
                        peerIPs = peerEP.ToString();
                    else
                        peerIPs += ", " + peerEP.ToString();
                }

                lstConnectedWith.Items.Add(peerInfo.PeerEmail).SubItems.Add(peerIPs);
            }

            //not connected with
            PeerInfo[] notConnectedWith = _peer.NotConnectedWith;

            foreach (PeerInfo peerInfo in notConnectedWith)
            {
                string peerIPs = null;

                foreach (IPEndPoint peerEP in peerInfo.PeerEPList)
                {
                    if (peerIPs == null)
                        peerIPs = peerEP.ToString();
                    else
                        peerIPs += ", " + peerEP.ToString();
                }

                lstNotConnectedWith.Items.Add(peerInfo.PeerEmail).SubItems.Add(peerIPs);
            }
        }

        #endregion

        #region form code

        private void lnkViewCert_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (frmViewCertificate frm = new frmViewCertificate(_peer.PeerCertificate))
            {
                frm.ShowDialog(this);
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion
    }
}
