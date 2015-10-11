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
using TechnitiumLibrary.Net.BitTorrent;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace BitChatAppMono
{
    public partial class frmChatProperties : Form
    {
        BitChat _chat;
        BitChatProfile _profile;

        Timer _timer;

        public frmChatProperties(BitChat chat, BitChatProfile profile)
        {
            InitializeComponent();

            _chat = chat;
            _profile = profile;

            chkLANChat.Checked = !_chat.EnableTracking;

            if (_chat.NetworkType == BitChatClient.Network.BitChatNetworkType.PrivateChat)
            {
                label1.Text = "Peer's Email Address";
                txtNetwork.Text = chat.PeerEmailAddress.Address;
            }
            else
            {
                txtNetwork.Text = chat.NetworkName;
            }

            ListViewItem dhtItem = lstTrackerInfo.Items.Add("DHT");

            dhtItem.SubItems.Add("");
            dhtItem.SubItems.Add("");
            dhtItem.SubItems.Add("");

            foreach (TrackerClient tracker in _chat.GetTrackers())
            {
                ListViewItem item = lstTrackerInfo.Items.Add(tracker.TrackerUri.AbsoluteUri);
                item.Tag = tracker;

                item.SubItems.Add("");
                item.SubItems.Add("");
                item.SubItems.Add("");
            }

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += _timer_Tick;
            _timer.Start();
        }

        private void _timer_Tick(object sender, EventArgs e)
        {
            lock (lstTrackerInfo.Items)
            {
                bool tracking = _chat.IsTrackerRunning;

                foreach (ListViewItem item in lstTrackerInfo.Items)
                {
                    if (tracking)
                    {
                        TrackerClient tracker = item.Tag as TrackerClient;

                        if (tracker == null)
                        {
                            item.SubItems[1].Text = "working";
                            item.SubItems[2].Text = "";
                            item.SubItems[3].Text = _chat.GetTotalDhtPeers().ToString();
                        }
                        else
                        {
                            string strUpdateIn = "updating...";
                            TimeSpan updateIn = tracker.NextUpdateIn();

                            if (updateIn.TotalSeconds > 1)
                            {
                                strUpdateIn = "";
                                if (updateIn.Hours > 0)
                                    strUpdateIn = updateIn.Hours + "h ";

                                if (updateIn.Minutes > 0)
                                    strUpdateIn += updateIn.Minutes + "m ";

                                strUpdateIn += updateIn.Seconds + "s";
                            }

                            item.SubItems[1].Text = tracker.LastException == null ? "working" : tracker.LastException.Message;
                            item.SubItems[2].Text = strUpdateIn;
                            item.SubItems[3].Text = tracker.Peers.Count.ToString();
                        }
                    }
                    else
                    {
                        item.SubItems[1].Text = "not tracking";
                        item.SubItems[2].Text = "";
                        item.SubItems[3].Text = "";
                    }
                }
            }
        }

        private void lstTrackerInfo_DoubleClick(object sender, EventArgs e)
        {
            showPeersToolStripMenuItem_Click(null, null);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void lstTrackerInfo_MouseUp(object sender, MouseEventArgs e)
        {
            showPeersToolStripMenuItem.Enabled = (lstTrackerInfo.SelectedItems.Count == 1);

            if (lstTrackerInfo.SelectedItems.Count > 0)
            {
                updateTrackerToolStripMenuItem.Enabled = true;
                removeTrackerToolStripMenuItem.Enabled = true;
                copyTrackerToolStripMenuItem.Enabled = true;
            }
            else
            {
                updateTrackerToolStripMenuItem.Enabled = false;
                removeTrackerToolStripMenuItem.Enabled = false;
                copyTrackerToolStripMenuItem.Enabled = false;
            }
        }

        private void showPeersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstTrackerInfo.SelectedItems.Count > 0)
            {
                TrackerClient tracker = lstTrackerInfo.SelectedItems[0].Tag as TrackerClient;

                IEnumerable<IPEndPoint> peerEPs;

                if (tracker == null)
                    peerEPs = _chat.GetDhtPeers();
                else
                    peerEPs = tracker.Peers;

                string peers = "";

                foreach (IPEndPoint peerEP in peerEPs)
                {
                    peers += peerEP.ToString() + "\r\n";
                }

                if (peers == "")
                    MessageBox.Show("No peer returned by the tracker.", "No Peer Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(peers, "Peers List", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void updateTrackerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            lock (lstTrackerInfo.Items)
            {
                foreach (ListViewItem item in lstTrackerInfo.SelectedItems)
                {
                    TrackerClient tracker = item.Tag as TrackerClient;
                    tracker.ScheduleUpdateNow();
                }
            }
        }

        private void addTrackerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (frmAddTracker frm = new frmAddTracker())
            {
                if (frm.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    List<Uri> uriList = frm.TrackerUriList;

                    foreach (Uri uri in uriList)
                    {
                        TrackerClient tracker = _chat.AddTracker(uri);
                        if (tracker != null)
                        {
                            ListViewItem item = new ListViewItem(tracker.TrackerUri.AbsoluteUri);
                            item.Tag = tracker;

                            item.SubItems.Add("");
                            item.SubItems.Add("updating...");
                            item.SubItems.Add("0");

                            lock (lstTrackerInfo.Items)
                            {
                                lstTrackerInfo.Items.Add(item);
                            }
                        }
                    }
                }
            }
        }

        private void addDefaultTrackersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (frmAddTracker frm = new frmAddTracker(_profile.TrackerURIs))
            {
                if (frm.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    List<Uri> uriList = frm.TrackerUriList;

                    foreach (Uri uri in uriList)
                    {
                        TrackerClient tracker = _chat.AddTracker(uri);
                        if (tracker != null)
                        {
                            ListViewItem item = new ListViewItem(tracker.TrackerUri.AbsoluteUri);
                            item.Tag = tracker;

                            item.SubItems.Add("");
                            item.SubItems.Add("updating...");
                            item.SubItems.Add("0");

                            lock (lstTrackerInfo.Items)
                            {
                                lstTrackerInfo.Items.Add(item);
                            }
                        }
                    }
                }
            }
        }

        private void removeTrackerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            lock (lstTrackerInfo.Items)
            {
                foreach (ListViewItem item in lstTrackerInfo.SelectedItems)
                {
                    TrackerClient tracker = item.Tag as TrackerClient;

                    _chat.RemoveTracker(tracker);

                    lstTrackerInfo.Items.Remove(item);
                }
            }
        }

        private void chkShowSecret_CheckedChanged(object sender, EventArgs e)
        {
            if (chkShowSecret.Checked)
                txtSecret.Text = _chat.SharedSecret;
            else
                txtSecret.Text = "########";
        }

        private void copyTrackerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string trackers = "";

            lock (lstTrackerInfo.Items)
            {
                foreach (ListViewItem item in lstTrackerInfo.SelectedItems)
                {
                    TrackerClient tracker = item.Tag as TrackerClient;

                    trackers += tracker.TrackerUri.AbsoluteUri + "\r\n";
                }
            }

            if (trackers != "")
            {
                try
                {
                    Clipboard.Clear();
                    Clipboard.SetText(trackers);
                }
                catch
                { }
            }
        }

        private void copyAllTrackersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string trackers = "";

            lock (lstTrackerInfo.Items)
            {
                foreach (ListViewItem item in lstTrackerInfo.Items)
                {
                    TrackerClient tracker = item.Tag as TrackerClient;

                    trackers += tracker.TrackerUri.AbsoluteUri + "\r\n";
                }
            }

            if (trackers != "")
            {
                try
                {
                    Clipboard.Clear();
                    Clipboard.SetText(trackers);
                }
                catch
                { }
            }
        }

        private void chkLANChat_CheckedChanged(object sender, EventArgs e)
        {
            _chat.EnableTracking = !chkLANChat.Checked;
        }
    }
}
