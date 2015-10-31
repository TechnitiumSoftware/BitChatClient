using BitChatClient;
using BitChatClient.Network;
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
    public partial class frmNetworkInfo : Form
    {
        BitChatService _service;
        Timer _updateTimer;

        public frmNetworkInfo()
        {
            InitializeComponent();
        }

        public frmNetworkInfo(BitChatService service)
        {
            InitializeComponent();

            _service = service;

            listView1.Items.Add("Local Peer ID").SubItems.Add("Loading...");
            listView1.Items.Add("Local Port").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Node ID").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Port").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Nodes").SubItems.Add("Loading...");
            listView1.Items.Add("Internet Status").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP Status").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP Device IP").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP External IP").SubItems.Add("Loading...");
            listView1.Items.Add("Socks Proxy IP").SubItems.Add("Loading...");
            listView1.Items.Add("External End Point").SubItems.Add("Loading...");
            listView1.Items.Add("Proxy Nodes").SubItems.Add("Loading...");

            _updateTimer = new Timer();
            _updateTimer.Interval = 2000;
            _updateTimer.Tick += _updateTimer_Tick;
            _updateTimer.Start();
        }

        private void _updateTimer_Tick(object sender, EventArgs e)
        {
            INetworkInfo info = _service.NetworkInfo;

            listView1.Items[0].SubItems[1].Text = info.LocalPeerID.ToString();
            listView1.Items[1].SubItems[1].Text = info.LocalPort.ToString();
            listView1.Items[2].SubItems[1].Text = info.DhtNodeID.ToString();
            listView1.Items[3].SubItems[1].Text = info.DhtLocalPort.ToString();
            listView1.Items[4].SubItems[1].Text = info.DhtTotalNodes.ToString();
            listView1.Items[5].SubItems[1].Text = info.InternetStatus.ToString();
            listView1.Items[6].SubItems[1].Text = info.UPnPStatus.ToString();

            if (info.UPnPExternalIP == null)
                listView1.Items[7].SubItems[1].Text = "";
            else
                listView1.Items[7].SubItems[1].Text = info.UPnPDeviceIP.ToString();

            if (info.UPnPExternalIP == null)
                listView1.Items[8].SubItems[1].Text = "";
            else
                listView1.Items[8].SubItems[1].Text = info.UPnPExternalIP.ToString();

            if (info.SocksProxyEndPoint == null)
                listView1.Items[9].SubItems[1].Text = "";
            else
                listView1.Items[9].SubItems[1].Text = info.SocksProxyEndPoint.ToString();

            if (info.ExternalEndPoint == null)
                listView1.Items[10].SubItems[1].Text = "";
            else
                listView1.Items[10].SubItems[1].Text = info.ExternalEndPoint.ToString();

            if (info.ProxyNodes.Length > 0)
            {
                string tmp = "";

                foreach (IPEndPoint proxyNodeEP in info.ProxyNodes)
                    tmp += ", " + proxyNodeEP.ToString();

                listView1.Items[11].SubItems[1].Text = tmp.Substring(2);
            }
            else
            {
                listView1.Items[11].SubItems[1].Text = "";
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
