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
using BitChatCore.FileSharing;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TechnitiumLibrary.Net;

namespace BitChatApp.UserControls
{
    public partial class ChatMessageFileItem : CustomListViewItem, IChatMessageItem
    {
        #region events

        public event EventHandler ShareFile;

        #endregion  

        #region variables

        BitChat.Peer _senderPeer;
        MessageItem _message;
        SharedFile _sharedFile;

        #endregion

        #region constructor

        public ChatMessageFileItem(BitChat.Peer senderPeer, MessageItem message, SharedFile sharedFile = null)
        {
            InitializeComponent();

            _senderPeer = senderPeer;
            _message = message;
            _sharedFile = sharedFile;

            lblFileName.Text = _message.SharedFileMetaData.FileName;
            lblContentType.Text = _message.SharedFileMetaData.ContentType.ToString();
            lblFileSize.Text = WebUtilities.GetFormattedSize(_message.SharedFileMetaData.FileSize);

            TimeSpan span = DateTime.UtcNow.Date - _message.MessageDate.Date;

            if (span.TotalDays >= 7)
                lblDateTime.Text = _message.MessageDate.ToLocalTime().ToString();
            else if (span.TotalDays >= 2)
                lblDateTime.Text = _message.MessageDate.ToLocalTime().DayOfWeek.ToString() + " " + _message.MessageDate.ToLocalTime().ToShortTimeString();
            else if (span.TotalDays >= 1)
                lblDateTime.Text = "Yesterday " + _message.MessageDate.ToLocalTime().ToShortTimeString();
            else
                lblDateTime.Text = _message.MessageDate.ToLocalTime().ToShortTimeString();

            toolTip1.SetToolTip(lblDateTime, _message.MessageDate.ToLocalTime().ToString());

            if (_sharedFile != null)
            {
                _sharedFile.FileDownloadStarted += sharedFile_FileDownloadStarted;
                _sharedFile.FileSharingStarted += sharedFile_FileSharingStarted;
                _sharedFile.FilePaused += sharedFile_FilePaused;
                _sharedFile.FileDownloaded += sharedFile_FileDownloaded;

                linkAction.Visible = true;

                switch (_sharedFile.State)
                {
                    case SharedFileState.Advertisement:
                        linkAction.Text = "Download";
                        break;

                    case SharedFileState.Sharing:
                        linkAction.Text = "Open";
                        break;

                    case SharedFileState.Downloading:
                        linkAction.Text = "Pause";
                        break;

                    case SharedFileState.Paused:
                        if (_sharedFile.IsComplete)
                            linkAction.Text = "Open";
                        else
                            linkAction.Text = "Download";

                        break;
                }
            }

            if (_senderPeer == null)
            {
                lblUsername.Text = _message.Sender;
            }
            else
            {
                lblUsername.Text = _senderPeer.PeerCertificate.IssuedTo.Name;

                if (_senderPeer.IsSelf)
                {
                    lblUsername.ForeColor = Color.FromArgb(63, 186, 228);
                    pnlBubble.Left = this.Width - pnlBubble.Width - 20;
                    pnlBubble.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    picPointLeft.Visible = false;
                    picPointRight.Visible = true;
                }

                _senderPeer.BitChat.FileRemoved += BitChat_FileRemoved;
            }
        }

        #endregion

        #region form code

        private void linkAction_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            switch (linkAction.Text)
            {
                case "Download":
                    _sharedFile.Start();
                    break;

                case "Pause":
                    _sharedFile.Pause();
                    break;

                case "Open":
                    openFileToolStripMenuItem_Click(null, null);
                    break;
            }
        }

        private void sharedFile_FileDownloadStarted(object sender, EventArgs e)
        {
            linkAction.Text = "Pause";
        }

        private void sharedFile_FileSharingStarted(object sender, EventArgs e)
        {
            linkAction.Text = "Open";
        }

        private void sharedFile_FilePaused(object sender, EventArgs e)
        {
            if (_sharedFile.IsComplete)
                linkAction.Text = "Open";
            else
                linkAction.Text = "Download";
        }

        private void sharedFile_FileDownloaded(object sender, EventArgs e)
        {
            linkAction.Text = "Open";
        }

        private void BitChat_FileRemoved(BitChat chat, SharedFile sharedFile)
        {
            if (_sharedFile == sharedFile)
            {
                linkAction.Visible = false;
                _sharedFile = null;
            }
        }

        private void lblUsername_Click(object sender, EventArgs e)
        {
            if (_senderPeer != null)
            {
                using (frmViewProfile frm = new frmViewProfile(_senderPeer.BitChat.Profile, _senderPeer))
                {
                    frm.ShowDialog(this);
                }
            }
        }

        private void lblUsername_MouseEnter(object sender, EventArgs e)
        {
            lblUsername.Font = new Font(lblUsername.Font, FontStyle.Underline | FontStyle.Bold);
        }

        private void lblUsername_MouseLeave(object sender, EventArgs e)
        {
            lblUsername.Font = new Font(lblUsername.Font, FontStyle.Regular | FontStyle.Bold);
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_sharedFile == null)
            {
                e.Cancel = true;
                return;
            }

            switch (_sharedFile.State)
            {
                case SharedFileState.Advertisement:
                    startDownloadToolStripMenuItem.Visible = true;
                    startSharingToolStripMenuItem.Visible = false;
                    pauseToolStripMenuItem.Visible = false;
                    shareToolStripMenuItem.Visible = false;
                    openFileToolStripMenuItem.Visible = false;
                    openContainingFolderToolStripMenuItem.Visible = false;
                    break;

                case SharedFileState.Sharing:
                    startDownloadToolStripMenuItem.Visible = false;
                    startSharingToolStripMenuItem.Visible = false;
                    pauseToolStripMenuItem.Visible = true;
                    shareToolStripMenuItem.Visible = true;
                    openFileToolStripMenuItem.Visible = true;
                    openContainingFolderToolStripMenuItem.Visible = true;
                    break;

                case SharedFileState.Downloading:
                    startDownloadToolStripMenuItem.Visible = false;
                    startSharingToolStripMenuItem.Visible = false;
                    pauseToolStripMenuItem.Visible = true;
                    shareToolStripMenuItem.Visible = false;
                    openFileToolStripMenuItem.Visible = false;
                    openContainingFolderToolStripMenuItem.Visible = false;
                    break;

                case SharedFileState.Paused:
                    startDownloadToolStripMenuItem.Visible = !_sharedFile.IsComplete;
                    startSharingToolStripMenuItem.Visible = _sharedFile.IsComplete;
                    pauseToolStripMenuItem.Visible = false;
                    shareToolStripMenuItem.Visible = _sharedFile.IsComplete;
                    openFileToolStripMenuItem.Visible = _sharedFile.IsComplete;
                    openContainingFolderToolStripMenuItem.Visible = _sharedFile.IsComplete;
                    break;
            }
        }

        private void startDownloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _sharedFile.Start();
        }

        private void startSharingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _sharedFile.Start();
        }

        private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _sharedFile.Pause();
        }

        private void openFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure to open the file?\r\n\r\nFile: " + _sharedFile.MetaData.FileName + "\r\nType: " + _sharedFile.MetaData.ContentType.MediaType + "\r\nSize: " + WebUtilities.GetFormattedSize(_sharedFile.MetaData.FileSize) + "\r\n\r\nWARNING! Do NOT open files sent by untrusted people as the files may be infected with trojan/virus.", "Open File Warning!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                try
                {
                    Process.Start(_sharedFile.FilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error! " + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void openContainingFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(Path.GetDirectoryName(_sharedFile.FilePath));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error! " + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _senderPeer.BitChat.RemoveSharedFile(_sharedFile);
        }

        private void shareToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShareFile?.Invoke(_sharedFile, EventArgs.Empty);
        }

        #endregion

        #region properties

        public MessageItem Message
        { get { return _message; } }

        #endregion
    }
}
