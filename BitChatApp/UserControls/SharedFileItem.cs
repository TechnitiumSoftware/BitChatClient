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
    enum SharedFileItemType
    {
        Advertisement = 0,
        Downloading = 1,
        Sharing = 2
    }

    public partial class SharedFileItem : CustomListViewItem
    {
        #region events

        public event EventHandler FileDownloaded;
        public event EventHandler FileRemoved;
        public event EventHandler FileDownloadStarted;

        #endregion

        #region variables

        Color _BackColorAvailable = Color.FromArgb(255, 238, 215);

        SharedFile _file;
        BitChat _chat;

        SharedFileItemType _type;

        string _fileSizeFormatted;
        bool _lockLabSpeed = false;

        #endregion

        #region constructor

        public SharedFileItem(SharedFile file, BitChat chat)
        {
            InitializeComponent();

            _file = file;
            _chat = chat;

            _fileSizeFormatted = WebUtilities.GetFormattedSize(_file.MetaData.FileSize);

            labFileName.Text = _file.MetaData.FileName;
            labSpeed.Text = "";

            if (_file.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peer)";

            _file.FileDownloadStarted += OnFileDownloadStarted;
            _file.FileDownloaded += OnFileDownloaded;
            _file.FileBlockDownloaded += OnFileBlockDownloaded;
            _file.PeerCountUpdate += OnPeerCountUpdate;
            _file.FileTransferSpeedUpdate += OnFileTransferSpeedUpdate;

            if (_file.State == SharedFileState.Advertisement)
            {
                _type = SharedFileItemType.Advertisement;
                this.BackColor = _BackColorAvailable;

                pbFileProgress.Visible = false;
            }
            else if (_file.IsComplete)
            {
                _type = SharedFileItemType.Sharing;

                labSpeed.ForeColor = Color.Blue;
                pbFileProgress.Visible = false;
            }
            else
            {
                _type = SharedFileItemType.Downloading;

                pbFileProgress.Visible = true;
                pbFileProgress.Value = _file.PercentComplete;
            }
        }

        #endregion

        #region protected

        protected override void OnMouseOver(bool hovering)
        {
            _lockLabSpeed = hovering;

            switch (_type)
            {
                case SharedFileItemType.Advertisement:
                    if (hovering)
                        labSpeed.Text = "Available";
                    else
                        labSpeed.Text = "";

                    ShowButtons(hovering, false, hovering);
                    break;

                case SharedFileItemType.Sharing:
                    if (hovering)
                    {
                        this.BackColor = Color.FromArgb(241, 245, 249);

                        if (_file.State == SharedFileState.Paused)
                            labSpeed.Text = "Paused";
                        else
                            labSpeed.Text = "Sharing";
                    }
                    else
                    {
                        this.BackColor = Color.White;
                        labSpeed.Text = "";
                    }

                    if (_file.State == SharedFileState.Sharing)
                        ShowButtons(false, hovering, hovering);
                    else
                        ShowButtons(hovering, false, hovering);

                    break;

                case SharedFileItemType.Downloading:
                    if (hovering)
                    {
                        this.BackColor = Color.FromArgb(241, 245, 249);

                        if (_file.State == SharedFileState.Paused)
                            labSpeed.Text = "Paused";
                        else
                            labSpeed.Text = "Downloading";
                    }
                    else
                    {
                        this.BackColor = Color.White;
                        labSpeed.Text = "";
                    }

                    if (_file.State == SharedFileState.Downloading)
                        ShowButtons(false, hovering, hovering);
                    else
                        ShowButtons(hovering, false, hovering);

                    break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                switch (_type)
                {
                    case SharedFileItemType.Advertisement:
                        startDownloadToolStripMenuItem.Visible = true;
                        startSharingToolStripMenuItem.Visible = false;
                        pauseToolStripMenuItem.Visible = false;
                        removeToolStripMenuItem.Visible = true;
                        openFileToolStripMenuItem.Visible = false;
                        openContainingFolderToolStripMenuItem.Visible = false;
                        break;

                    case SharedFileItemType.Sharing:
                        startDownloadToolStripMenuItem.Visible = false;
                        removeToolStripMenuItem.Visible = true;
                        openFileToolStripMenuItem.Visible = true;
                        openContainingFolderToolStripMenuItem.Visible = true;

                        if (_file.State == SharedFileState.Paused)
                        {
                            startSharingToolStripMenuItem.Visible = true;
                            pauseToolStripMenuItem.Visible = false;
                        }
                        else
                        {
                            startSharingToolStripMenuItem.Visible = false;
                            pauseToolStripMenuItem.Visible = true;
                        }

                        break;

                    case SharedFileItemType.Downloading:
                        startSharingToolStripMenuItem.Visible = false;
                        removeToolStripMenuItem.Visible = true;
                        openFileToolStripMenuItem.Visible = false;
                        openContainingFolderToolStripMenuItem.Visible = true;

                        if (_file.State == SharedFileState.Paused)
                        {
                            startDownloadToolStripMenuItem.Visible = true;
                            pauseToolStripMenuItem.Visible = false;
                        }
                        else
                        {
                            startDownloadToolStripMenuItem.Visible = false;
                            pauseToolStripMenuItem.Visible = true;
                        }

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

            if (_file.IsComplete)
            {
                prepend = "2";
            }
            else
            {
                switch (_file.State)
                {
                    case SharedFileState.Advertisement:
                        prepend = "0";
                        break;

                    default:
                        prepend = "1";
                        break;
                }
            }

            return prepend + _file.MetaData.FileName;
        }

        #endregion

        #region private

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
                _file.Start();
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
            _file.Pause();

            labSpeed.Text = "";
            ShowButtons(true, false, true);
        }

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnRemove_Click(null, null);
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            _chat.RemoveSharedFile(_file);

            ShowButtons(false, false, false);

            FileRemoved?.Invoke(this, EventArgs.Empty);
        }

        private void openFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure to open the file?\r\n\r\nFile: " + _file.MetaData.FileName + "\r\nType: " + _file.MetaData.ContentType.MediaType + "\r\nSize: " + WebUtilities.GetFormattedSize(_file.MetaData.FileSize) + "\r\n\r\nWARNING! Do NOT open files sent by untrusted people as the files may be infected with trojan/virus.", "Open File Warning!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                try
                {
                    Process.Start(_file.FilePath);
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
                Process.Start(Path.GetDirectoryName(_file.FilePath));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error! " + ex.Message, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private void OnFileDownloadStarted(object sender, EventArgs e)
        {
            labFileName.Text = _file.MetaData.FileName;
            labSpeed.Text = "";

            if (_file.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peer)";

            if (_type == SharedFileItemType.Advertisement)
            {
                _type = SharedFileItemType.Downloading;
                this.BackColor = Color.White;

                pbFileProgress.Visible = true;
                pbFileProgress.Value = _file.PercentComplete;

                if (FileDownloadStarted != null)
                    FileDownloadStarted(this, EventArgs.Empty);

                SortListView();
            }
        }

        private void OnFileDownloaded(object sender, EventArgs args)
        {
            _type = SharedFileItemType.Sharing;
            pbFileProgress.Visible = false;
            labSpeed.Text = "";
            labSpeed.ForeColor = Color.Blue;
            this.BackColor = Color.White;

            if (_file.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peer)";

            if (FileDownloaded != null)
                FileDownloaded(this, EventArgs.Empty);

            SortListView();
        }

        private void OnFileBlockDownloaded(object sender, EventArgs args)
        {
            pbFileProgress.Value = _file.PercentComplete;
        }

        private void OnFileTransferSpeedUpdate(object sender, EventArgs args)
        {
            if (_lockLabSpeed)
                return;

            if (_file.IsComplete)
                labSpeed.Text = WebUtilities.GetFormattedSpeed(_file.BytesUploadedLastSecond);
            else
                labSpeed.Text = WebUtilities.GetFormattedSpeed(_file.BytesDownloadedLastSecond);
        }

        private void OnPeerCountUpdate(object sender, EventArgs args)
        {
            if (_file.TotalPeers > 1)
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peers)";
            else
                labInfo1.Text = _fileSizeFormatted + " (" + _file.TotalPeers + " peer)";
        }

        #endregion
    }
}
