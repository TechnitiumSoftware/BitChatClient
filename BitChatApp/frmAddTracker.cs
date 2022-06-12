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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmAddTracker : Form
    {
        List<Uri> _trackerUriList = new List<Uri>();

        public frmAddTracker(Uri[] trackers = null)
        {
            InitializeComponent();

            if (trackers != null)
            {
                foreach (Uri tracker in trackers)
                {
                    txtTrackerURL.Text += tracker.AbsoluteUri + "\r\n";
                }
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            try
            {
                string[] uriList = txtTrackerURL.Text.Split(new char[] { ' ', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string uri in uriList)
                    _trackerUriList.Add(new Uri(uri));
            }
            catch
            {
                MessageBox.Show("The tracker URL format is invalid. Please enter a valid tracker URL. Multiple URLs must be separated using space, comma or new line.", "Invalid Tracker URL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        public List<Uri> TrackerUriList
        { get { return _trackerUriList; } }
    }
}
