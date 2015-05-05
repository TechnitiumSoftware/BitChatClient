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
using System.Text;
using System.Windows.Forms;

namespace BitChatApp.UserControls
{
    public partial class UserListItem : CustomListViewItem
    {
        #region variables

        BitChat.Peer _peer;

        #endregion

        #region constructor

        public UserListItem(BitChat.Peer peer)
        {
            InitializeComponent();

            _peer = peer;

            _peer.StateChanged += _peer_StateChanged;
            _peer.NetworkStatusUpdated += _peer_NetworkStatusUpdated;

            if (_peer.PeerCertificate.IssuedTo.FieldExists(CertificateProfileFlags.Name))
            {
                string name = _peer.PeerCertificate.IssuedTo.Name;

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

                labName.Text = name;
            }
            else
            {
                labIcon.Text = "";
                labName.Text = "{missing name}";
                labName.ForeColor = Color.Red;
            }

            labEmail.Text = _peer.PeerCertificate.IssuedTo.EmailAddress.Address;

            _peer_StateChanged(null, null);
            _peer_NetworkStatusUpdated(null, null);
        }

        #endregion

        #region private

        private void _peer_StateChanged(object sender, EventArgs e)
        {
            if (_peer.IsOnline)
                labIcon.BackColor = Color.FromArgb(102, 153, 255);
            else
                labIcon.BackColor = Color.Gray;

            SortListView();
        }

        private void _peer_NetworkStatusUpdated(object sender, EventArgs e)
        {
            switch (_peer.NetworkStatus)
            {
                case BitChatNetworkStatus.NoNetwork:
                    //labName.Text = _peer.PeerCertificate.IssuedTo.Name + " [N]";
                    picNetwork.Image = BitChatApp.Properties.Resources.NoNetwork;
                    break;

                case BitChatNetworkStatus.PartialNetwork:
                    //labName.Text = _peer.PeerCertificate.IssuedTo.Name + " [P]";
                    picNetwork.Image = BitChatApp.Properties.Resources.PartialNetwork;
                    break;

                case BitChatNetworkStatus.FullNetwork:
                    //labName.Text = _peer.PeerCertificate.IssuedTo.Name + " [F]";
                    picNetwork.Image = BitChatApp.Properties.Resources.FullNetwork;
                    break;
            }
        }

        #endregion

        #region protected

        protected override void OnMouseOver(bool hovering)
        {
            if (hovering)
                this.BackColor = Color.FromArgb(241, 245, 249);
            else
                this.BackColor = Color.White;
        }

        #endregion

        #region public

        public override string ToString()
        {
            string prepend;

            if (_peer.IsOnline)
            {
                if (_peer.IsSelf)
                {
                    prepend = "0";
                }
                else
                {
                    prepend = "1";
                }
            }
            else
            {
                prepend = "2";
            }

            return prepend + _peer.PeerCertificate.IssuedTo.Name;
        }

        #endregion

        #region property

        public BitChat.Peer Peer
        { get { return _peer; } }

        #endregion
    }
}
