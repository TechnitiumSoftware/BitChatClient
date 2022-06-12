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
    public partial class SharedFileItem : CustomListViewItem
    {
        #region events

        public event EventHandler FileDownloaded;
        public event EventHandler FileRemoved;
        public event EventHandler FileDownloadStarted;
        public event EventHandler ShareFile;

        #endregion

        #region variables

        Color _BackColorAvailable = Color.FromArgb(255, 238, 215);

        SharedFile _sharedFile;
        BitChat _chat;

        string _fileSizeFormatted;
        bool _lockLabSpeed = false;

        #endregion

        #region constructor

        public SharedFileItem(SharedFile file, BitChat chat)
        {
            InitializeComponent();

            _sharedFile = file;
            _chat = chat;

            _fileSizeFormatted = WebUtilities.GetFormattedSize(_sharedFile.MetaData.FileSize);

            labFileName.Text = _sharedFile.MetaData.FileName;
            labSpeed.Text = "";

            if (_sharedFile.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peer)";

            _sharedFile.FileDownloadStarted += sharedFile_FileDownloadStarted;
            _sharedFile.FileDownloaded += sharedFile_FileDownloaded;
            _sharedFile.FileBlockDownloaded += sharedFile_FileBlockDownloaded;
            _sharedFile.PeerCountUpdate += sharedFile_PeerCountUpdate;
            _sharedFile.FileTransferSpeedUpdate += sharedFile_FileTransferSpeedUpdate;
            _sharedFile.FilePaused += sharedFile_FilePaused;

            _chat.FileRemoved += chat_FileRemoved;

            if (_sharedFile.State == SharedFileState.Advertisement)
            {
                this.BackColor = _BackColorAvailable;

                pbFileProgress.Visible = false;
            }
            else if (_sharedFile.IsComplete)
            {
                labSpeed.ForeColor = Color.Blue;
                pbFileProgress.Visible = false;
            }
            else
            {
                pbFileProgress.Visible = true;
                pbFileProgress.Value = _sharedFile.PercentComplete;
            }
        }

        #endregion

        #region protected

        protected override void OnMouseOver(bool hovering)
        {
            _lockLabSpeed = hovering;

            switch (_sharedFile.State)
            {
                case SharedFileState.Advertisement:
                    if (hovering)
                        labSpeed.Text = "Available";
                    else
                        labSpeed.Text = "";

                    ShowButtons(hovering, false, hovering);
                    break;

                case SharedFileState.Sharing:
                    if (hovering)
                    {
                        this.BackColor = Color.FromArgb(241, 245, 249);
                        labSpeed.Text = "Sharing";
                    }
                    else
                    {
                        this.BackColor = Color.White;
                        labSpeed.Text = "";
                    }

                    ShowButtons(false, hovering, hovering);
                    break;

                case SharedFileState.Downloading:
                    if (hovering)
                    {
                        this.BackColor = Color.FromArgb(241, 245, 249);
                        labSpeed.Text = "Downloading";
                    }
                    else
                    {
                        this.BackColor = Color.White;
                        labSpeed.Text = "";
                    }

                    ShowButtons(false, hovering, hovering);
                    break;

                case SharedFileState.Paused:
                    if (hovering)
                    {
                        this.BackColor = Color.FromArgb(241, 245, 249);
                        labSpeed.Text = "Paused";
                    }
                    else
                    {
                        this.BackColor = Color.White;
                        labSpeed.Text = "";
                    }

                    ShowButtons(hovering, false, hovering);
                    break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                switch (_sharedFile.State)
                {
                    case SharedFileState.Advertisement:
                        startDownloadToolStripMenuItem.Visible = true;
                        startSharingToolStripMenuItem.Visible = false;
                        pauseToolStripMenuItem.Visible = false;
                        shareToolStripMenuItem.Visible = false;
                        removeToolStripMenuItem.Visible = true;
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

                    default:
                        return;
                }

                contextMenuStrip1.Show(this, e.Location);
            }
        }

        #endregion

        #region public

        public override string ToString()
        {
            string prepend;

            if (_sharedFile.IsComplete)
            {
                prepend = "2";
            }
            else
            {
                switch (_sharedFile.State)
                {
                    case SharedFileState.Advertisement:
                        prepend = "0";
                        break;

                    default:
                        prepend = "1";
                        break;
                }
            }

            return prepend + _sharedFile.MetaData.FileName;
        }

        #endregion

        #region private

        private void sharedFile_FileDownloadStarted(object sender, EventArgs e)
        {
            labFileName.Text = _sharedFile.MetaData.FileName;
            labSpeed.Text = "";

            if (_sharedFile.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peer)";

            this.BackColor = Color.White;

            pbFileProgress.Visible = true;
            pbFileProgress.Value = _sharedFile.PercentComplete;

            if (FileDownloadStarted != null)
                FileDownloadStarted(this, EventArgs.Empty);

            SortListView();
        }

        private void sharedFile_FileDownloaded(object sender, EventArgs args)
        {
            pbFileProgress.Visible = false;
            labSpeed.Text = "";
            labSpeed.ForeColor = Color.Blue;
            this.BackColor = Color.White;

            if (_sharedFile.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peer)";

            if (FileDownloaded != null)
                FileDownloaded(this, EventArgs.Empty);

            SortListView();
        }

        private void sharedFile_FileBlockDownloaded(object sender, EventArgs args)
        {
            pbFileProgress.Value = _sharedFile.PercentComplete;
        }

        private void sharedFile_FileTransferSpeedUpdate(object sender, EventArgs args)
        {
            if (_lockLabSpeed)
                return;

            if (_sharedFile.IsComplete)
                labSpeed.Text = WebUtilities.GetFormattedSpeed(_sharedFile.BytesUploadedLastSecond);
            else
                labSpeed.Text = WebUtilities.GetFormattedSpeed(_sharedFile.BytesDownloadedLastSecond);
        }

        private void sharedFile_PeerCountUpdate(object sender, EventArgs args)
        {
            if (_sharedFile.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _sharedFile.TotalPeers + " peer)";
        }

        private void chat_FileRemoved(BitChat chat, SharedFile sharedFile)
        {
            if (_sharedFile == sharedFile)
                FileRemoved?.Invoke(this, EventArgs.Empty);
        }

        private void sharedFile_FilePaused(object sender, EventArgs e)
        {
            labSpeed.Text = "";
        }

        private void startDownloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnStart_Click(null, null);
        }

        private void startSharingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnStart_Click(null, null);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            Action<object> d = new Action<object>(StartAsync);
            d.BeginInvoke(null, null, null);

            ShowButtons(false, true, true);
        }

        private void StartAsync(object obj)
        {
            try
            {
                _sharedFile.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error! " + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnPause_Click(null, null);
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            _sharedFile.Pause();

            labSpeed.Text = "";
            ShowButtons(true, false, true);
        }

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnRemove_Click(null, null);
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            _chat.RemoveSharedFile(_sharedFile);
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

        private void shareToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShareFile?.Invoke(_sharedFile, EventArgs.Empty);
        }

        private void ShowButtons(bool start, bool pause, bool delete)
        {
            btnRemove.Visible = delete;

            if (pause)
            {
                if (delete)
                    btnPause.Left = btnRemove.Left - 16 - 2;
                else
                    btnPause.Left = btnRemove.Left;

                btnPause.Visible = true;
                btnStart.Visible = false;
            }
            else if (start)
            {
                if (delete)
                    btnStart.Left = btnRemove.Left - 16 - 2;
                else
                    btnStart.Left = btnRemove.Left;

                btnPause.Visible = false;
                btnStart.Visible = true;
            }
            else
            {
                btnPause.Visible = false;
                btnStart.Visible = false;
            }
        }

        #endregion
    }
}
