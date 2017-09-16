/*
Technitium Bit Chat
Copyright (C) 2017  Shreyas Zare (shreyas@technitium.com)

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
using System.Text;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmNetworkInfo : Form
    {
        BitChatNode _node;
        Timer _updateTimer;

        public frmNetworkInfo(BitChatNode node)
        {
            InitializeComponent();

            _node = node;
        }

        private void frmNetworkInfo_Load(object sender, EventArgs e)
        {
            listView1.Items.Add("Local Peer ID").SubItems.Add("Loading...");
            listView1.Items.Add("Local Port").SubItems.Add("Loading...");

            listView1.Items.Add("IPv4 DHT Node ID").SubItems.Add("Loading...");
            listView1.Items.Add("IPv4 DHT Nodes").SubItems.Add("Loading...");
            listView1.Items.Add("IPv6 DHT Node ID").SubItems.Add("Loading...");

            listView1.Items.Add("IPv6 DHT Nodes").SubItems.Add("Loading...");
            listView1.Items.Add("IPv4 Internet Status").SubItems.Add("Loading...");
            listView1.Items.Add("IPv6 Internet Status").SubItems.Add("Loading...");

            listView1.Items.Add("UPnP Status").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP Device IP").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP External IP").SubItems.Add("Loading...");

            listView1.Items.Add("Proxy Server").SubItems.Add("Loading...");
            listView1.Items.Add("IPv4 External End Point").SubItems.Add("Loading...");
            listView1.Items.Add("IPv6 External End Point").SubItems.Add("Loading...");

            listView1.Items.Add("Tcp Relays").SubItems.Add("Loading...");

            updateTimer_Tick(null, null);

            _updateTimer = new Timer();
            _updateTimer.Interval = 2000;
            _updateTimer.Tick += updateTimer_Tick;
            _updateTimer.Start();
        }

        private void updateTimer_Tick(object sender, EventArgs e)
        {
            listView1.Items[0].SubItems[1].Text = _node.LocalPeerID.ToString();
            listView1.Items[1].SubItems[1].Text = _node.LocalPort.ToString();

            listView1.Items[2].SubItems[1].Text = _node.IPv4DhtNodeID.ToString();
            listView1.Items[3].SubItems[1].Text = _node.IPv4DhtTotalNodes.ToString();
            listView1.Items[4].SubItems[1].Text = _node.IPv6DhtNodeID.ToString();

            listView1.Items[5].SubItems[1].Text = _node.IPv6DhtTotalNodes.ToString();
            listView1.Items[6].SubItems[1].Text = _node.IPv4InternetStatus.ToString();
            listView1.Items[7].SubItems[1].Text = _node.IPv6InternetStatus.ToString();

            listView1.Items[8].SubItems[1].Text = _node.UPnPStatus.ToString();

            IPAddress upnpExternalIP = _node.UPnPExternalIP;
            IPAddress upnpDeviceIP = _node.UPnPDeviceIP;

            if ((upnpExternalIP == null) || (upnpDeviceIP == null))
                listView1.Items[9].SubItems[1].Text = "";
            else
                listView1.Items[9].SubItems[1].Text = upnpDeviceIP.ToString();

            if (upnpExternalIP == null)
                listView1.Items[10].SubItems[1].Text = "";
            else
                listView1.Items[10].SubItems[1].Text = upnpExternalIP.ToString();

            switch (_node.IPv4InternetStatus)
            {
                case BitChatCore.Network.Connections.InternetConnectivityStatus.HttpProxyInternetConnection:
                case BitChatCore.Network.Connections.InternetConnectivityStatus.Socks5ProxyInternetConnection:
                    listView1.Items[11].SubItems[1].Text = _node.Profile.ProxyAddress + ":" + _node.Profile.ProxyPort;
                    listView1.Items[12].SubItems[1].Text = "Incoming connections blocked by proxy";
                    break;

                case BitChatCore.Network.Connections.InternetConnectivityStatus.Identifying:
                case BitChatCore.Network.Connections.InternetConnectivityStatus.NoInternetConnection:
                    listView1.Items[11].SubItems[1].Text = "";
                    listView1.Items[12].SubItems[1].Text = "";
                    break;

                default:
                    listView1.Items[11].SubItems[1].Text = "";

                    IPEndPoint ep = _node.IPv4ExternalEndPoint;

                    if (ep == null)
                        listView1.Items[12].SubItems[1].Text = "Incoming connections blocked by NAT/Firewall";
                    else
                        listView1.Items[12].SubItems[1].Text = ep.ToString();

                    break;
            }

            switch (_node.IPv6InternetStatus)
            {
                case BitChatCore.Network.Connections.InternetConnectivityStatus.HttpProxyInternetConnection:
                case BitChatCore.Network.Connections.InternetConnectivityStatus.Socks5ProxyInternetConnection:
                    listView1.Items[11].SubItems[1].Text = _node.Profile.ProxyAddress + ":" + _node.Profile.ProxyPort;
                    listView1.Items[13].SubItems[1].Text = "Incoming connections blocked by proxy";
                    break;

                case BitChatCore.Network.Connections.InternetConnectivityStatus.Identifying:
                case BitChatCore.Network.Connections.InternetConnectivityStatus.NoInternetConnection:
                    listView1.Items[11].SubItems[1].Text = "";
                    listView1.Items[13].SubItems[1].Text = "";
                    break;

                default:
                    listView1.Items[11].SubItems[1].Text = "";

                    IPEndPoint ep = _node.IPv6ExternalEndPoint;

                    if (ep == null)
                        listView1.Items[13].SubItems[1].Text = "Incoming connections blocked by NAT/Firewall";
                    else
                        listView1.Items[13].SubItems[1].Text = ep.ToString();

                    break;
            }

            if (_node.TcpRelayNodes.Length > 0)
            {
                string tmp = "";

                foreach (IPEndPoint proxyNodeEP in _node.TcpRelayNodes)
                    tmp += ", " + proxyNodeEP.ToString();

                listView1.Items[14].SubItems[1].Text = tmp.Substring(2);
            }
            else
            {
                listView1.Items[14].SubItems[1].Text = "";
            }
        }

        private void frmNetworkInfo_FormClosing(object sender, FormClosingEventArgs e)
        {
            _updateTimer.Stop();
        }

        private void btnRecheck_Click(object sender, EventArgs e)
        {
            _node.ReCheckConnectivity();
        }

        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                switch (listView1.SelectedItems[0].Text)
                {
                    case "IPv4 DHT Nodes":
                        {
                            IPEndPoint[] nodes = _node.GetIPv4DhtNodes();
                            StringBuilder strData = new StringBuilder(512);

                            foreach (IPEndPoint node in nodes)
                                strData.AppendLine(node.ToString());

                            MessageBox.Show(strData.ToString(), "IPv4 DHT Nodes");
                        }
                        break;

                    case "IPv6 DHT Nodes":
                        {
                            IPEndPoint[] nodes = _node.GetIPv6DhtNodes();
                            StringBuilder strData = new StringBuilder(512);

                            foreach (IPEndPoint node in nodes)
                                strData.AppendLine(node.ToString());

                            MessageBox.Show(strData.ToString(), "IPv6 DHT Nodes");
                        }
                        break;
                }
            }
        }
    }
}
