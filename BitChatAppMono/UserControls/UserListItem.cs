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
using System.Drawing;
using System.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatAppMono.UserControls
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
            _peer.ProfileImageChanged += _peer_ProfileImageChanged;

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
            {
                _peer_ProfileImageChanged(null, null);
            }
            else
            {
                labIcon.BackColor = Color.Gray;

                labIcon.Visible = true;
                picIcon.Visible = false;
            }

            SortListView();
        }

        private void _peer_NetworkStatusUpdated(object sender, EventArgs e)
        {
            switch (_peer.ConnectivityStatus)
            {
                case BitChatConnectivityStatus.NoNetwork:
                    //labName.Text = _peer.PeerCertificate.IssuedTo.Name + " [N]";
                    picNetwork.Image = BitChatAppMono.Properties.Resources.NoNetwork;
                    break;

                case BitChatConnectivityStatus.PartialNetwork:
                    //labName.Text = _peer.PeerCertificate.IssuedTo.Name + " [P]";
                    picNetwork.Image = BitChatAppMono.Properties.Resources.PartialNetwork;
                    break;

                case BitChatConnectivityStatus.FullNetwork:
                    //labName.Text = _peer.PeerCertificate.IssuedTo.Name + " [F]";
                    picNetwork.Image = BitChatAppMono.Properties.Resources.FullNetwork;
                    break;
            }
        }

        private void _peer_ProfileImageChanged(object sender, EventArgs e)
        {
            if (_peer.ProfileImageSmall == null)
            {
                labIcon.BackColor = Color.FromArgb(102, 153, 255);

                labIcon.Visible = true;
                picIcon.Visible = false;
            }
            else
            {
                using (MemoryStream mS = new MemoryStream(_peer.ProfileImageSmall))
                {
                    picIcon.Image = Image.FromStream(mS);
                }

                labIcon.Visible = false;
                picIcon.Visible = true;
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
