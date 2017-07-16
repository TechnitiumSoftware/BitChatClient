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
using System.Net;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmNetworkInfo : Form
    {
        BitChatClient _service;
        Timer _updateTimer;

        public frmNetworkInfo(BitChatClient service)
        {
            InitializeComponent();

            _service = service;

            listView1.Items.Add("Local Peer ID").SubItems.Add("Loading...");
            listView1.Items.Add("Local Port").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Node ID").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Nodes").SubItems.Add("Loading...");
            listView1.Items.Add("Internet Status").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP Status").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP Device IP").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP External IP").SubItems.Add("Loading...");
            listView1.Items.Add("Proxy Server").SubItems.Add("Loading...");
            listView1.Items.Add("External End Point").SubItems.Add("Loading...");
            listView1.Items.Add("Tcp Relays").SubItems.Add("Loading...");

            updateTimer_Tick(null, null);

            _updateTimer = new Timer();
            _updateTimer.Interval = 2000;
            _updateTimer.Tick += updateTimer_Tick;
            _updateTimer.Start();
        }

        private void updateTimer_Tick(object sender, EventArgs e)
        {
            listView1.Items[0].SubItems[1].Text = _service.LocalPeerID.ToString();
            listView1.Items[1].SubItems[1].Text = _service.LocalPort.ToString();
            listView1.Items[2].SubItems[1].Text = _service.DhtNodeID.ToString();
            listView1.Items[3].SubItems[1].Text = _service.DhtTotalNodes.ToString();
            listView1.Items[4].SubItems[1].Text = _service.InternetStatus.ToString();
            listView1.Items[5].SubItems[1].Text = _service.UPnPStatus.ToString();

            if (_service.UPnPExternalIP == null)
                listView1.Items[6].SubItems[1].Text = "";
            else
                listView1.Items[6].SubItems[1].Text = _service.UPnPDeviceIP.ToString();

            if (_service.UPnPExternalIP == null)
                listView1.Items[7].SubItems[1].Text = "";
            else
                listView1.Items[7].SubItems[1].Text = _service.UPnPExternalIP.ToString();

            switch (_service.InternetStatus)
            {
                case BitChatCore.Network.Connections.InternetConnectivityStatus.HttpProxyInternetConnection:
                case BitChatCore.Network.Connections.InternetConnectivityStatus.Socks5ProxyInternetConnection:
                    listView1.Items[8].SubItems[1].Text = _service.Profile.ProxyAddress + ":" + _service.Profile.ProxyPort;
                    listView1.Items[9].SubItems[1].Text = "Incoming connections blocked by proxy";
                    break;

                case BitChatCore.Network.Connections.InternetConnectivityStatus.Identifying:
                case BitChatCore.Network.Connections.InternetConnectivityStatus.NoInternetConnection:
                    listView1.Items[8].SubItems[1].Text = "";
                    listView1.Items[9].SubItems[1].Text = "";
                    break;

                default:
                    listView1.Items[8].SubItems[1].Text = "";

                    if (_service.ExternalEndPoint == null)
                        listView1.Items[9].SubItems[1].Text = "Incoming connections blocked by NAT/Firewall";
                    else
                        listView1.Items[9].SubItems[1].Text = _service.ExternalEndPoint.ToString();
                    break;
            }

            if (_service.TcpRelayNodes.Length > 0)
            {
                string tmp = "";

                foreach (IPEndPoint proxyNodeEP in _service.TcpRelayNodes)
                    tmp += ", " + proxyNodeEP.ToString();

                listView1.Items[10].SubItems[1].Text = tmp.Substring(2);
            }
            else
            {
                listView1.Items[10].SubItems[1].Text = "";
            }
        }

        private void frmNetworkInfo_FormClosed(object sender, FormClosedEventArgs e)
        {
            _updateTimer.Stop();
        }

        private void btnRecheck_Click(object sender, EventArgs e)
        {
            _service.ReCheckConnectivity();
        }
    }
}
