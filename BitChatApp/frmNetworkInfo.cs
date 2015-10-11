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
        INetworkInfo _info;
        Timer _updateTimer;

        public frmNetworkInfo()
        {
            InitializeComponent();
        }

        public frmNetworkInfo(INetworkInfo info)
        {
            InitializeComponent();

            _info = info;

            listView1.Items.Add("Local Peer ID").SubItems.Add("Loading...");
            listView1.Items.Add("Local Port").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Node ID").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Port").SubItems.Add("Loading...");
            listView1.Items.Add("DHT Nodes").SubItems.Add("Loading...");
            listView1.Items.Add("Internet Status").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP Status").SubItems.Add("Loading...");
            listView1.Items.Add("UPnP External IP").SubItems.Add("Loading...");
            listView1.Items.Add("External End Point").SubItems.Add("Loading...");
            listView1.Items.Add("Proxy Nodes").SubItems.Add("Loading...");

            _updateTimer = new Timer();
            _updateTimer.Interval = 2000;
            _updateTimer.Tick += _updateTimer_Tick;
            _updateTimer.Start();
        }

        private void _updateTimer_Tick(object sender, EventArgs e)
        {
            listView1.Items[0].SubItems[1].Text = _info.LocalPeerID.ToString();
            listView1.Items[1].SubItems[1].Text = _info.LocalPort.ToString();
            listView1.Items[2].SubItems[1].Text = _info.DhtNodeID.ToString();
            listView1.Items[3].SubItems[1].Text = _info.DhtLocalPort.ToString();
            listView1.Items[4].SubItems[1].Text = _info.DhtTotalNodes.ToString();
            listView1.Items[5].SubItems[1].Text = _info.InternetStatus.ToString();
            listView1.Items[6].SubItems[1].Text = _info.UPnPStatus.ToString();

            if (_info.UPnPExternalIP == null)
                listView1.Items[7].SubItems[1].Text = "";
            else
                listView1.Items[7].SubItems[1].Text = _info.UPnPExternalIP.ToString();

            if (_info.ExternalEP == null)
                listView1.Items[8].SubItems[1].Text = "";
            else
                listView1.Items[8].SubItems[1].Text = _info.ExternalEP.ToString();

            if (_info.ProxyNodes.Length > 0)
            {
                string tmp = "";

                foreach (IPEndPoint proxyNodeEP in _info.ProxyNodes)
                    tmp += ", " + proxyNodeEP.ToString();

                listView1.Items[9].SubItems[1].Text = tmp.Substring(2);
            }
            else
            {
                listView1.Items[9].SubItems[1].Text = "";
            }
        }

        private void frmNetworkInfo_FormClosed(object sender, FormClosedEventArgs e)
        {
            _updateTimer.Stop();
        }
    }
}
