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

using AutomaticUpdate.Client;
using BitChatApp.UserControls;
using BitChatClient;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Net.Firewall;
using TechnitiumLibrary.Security.Cryptography;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BitChatClient.Network.SecureChannel;

namespace BitChatApp
{
    public partial class frmMain : Form, IDebug
    {
        #region variables

        AppLink _link;

        BitChatProfile _profile;
        string _profileFilePath;

        FileStream _debugFile;
        StreamWriter _debugWriter;

        BitChatService _service;

        AutomaticUpdateClient _updateClient;
        DateTime _lastUpdateCheckedOn;
        DateTime _lastModifiedGMT;

        SoundPlayer sndMessageNotification = new SoundPlayer(Properties.Resources.MessageNotification);

        #endregion

        #region constructor

        public frmMain(BitChatProfile profile, string profileFilePath, string cmdLine)
        {
            InitializeComponent();

            _profile = profile;
            _profileFilePath = profileFilePath;

            SecureChannelCryptoOptionFlags cryptoOptions;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    if (Environment.OSVersion.Version.Major > 5)
                        cryptoOptions = SecureChannelCryptoOptionFlags.ECDHE256_RSA_WITH_AES256_CBC_HMAC_SHA256 | SecureChannelCryptoOptionFlags.DHE2048_RSA_WITH_AES256_CBC_HMAC_SHA256;
                    else
                        cryptoOptions = SecureChannelCryptoOptionFlags.DHE2048_RSA_WITH_AES256_CBC_HMAC_SHA256;
                    break;

                default:
                    cryptoOptions = SecureChannelCryptoOptionFlags.DHE2048_RSA_WITH_AES256_CBC_HMAC_SHA256;
                    break;
            }

            //start bitchat service
            _service = new BitChatService(profile, Program.TRUSTED_CERTIFICATES, cryptoOptions, InvalidCertificateEvent);

            //add firewall entry
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    AddWindowsFirewallEntry();
                    break;
            }
        }

        #endregion

        #region form code

        private void frmMain_Load(object sender, EventArgs e)
        {
            //load chats and ui views
            lblUserName.Text = _profile.LocalCertificateStore.Certificate.IssuedTo.Name;

            foreach (BitChat chat in _service.GetBitChatList())
                AddChatView(chat);

            lstChats.SelectItem(lstChats.GetFirstItem());
            ShowSelectedChatView();

            //load settings
            bool loadDefaultSettings = true;

            if (_profile.ClientData != null)
            {
                try
                {
                    LoadProfileSettings(_profile.ClientData);
                    loadDefaultSettings = false;
                }
                catch
                { }
            }

            if (loadDefaultSettings)
            {
                this.Width = 960;
                this.Height = 540;

                Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;

                this.Left = workingArea.Width - workingArea.Left - this.Width - 20;
                this.Top = 100;
            }

            //create AppLink
            _link = new AppLink(Program.APP_LINK_PORT);
            _link.CommandReceived += _link_CommandReceived;

            //if (_cmdLine != null)
            //    _link_CommandReceived(_cmdLine);


            //start automatic update client
            _updateClient = new AutomaticUpdateClient(Program.MUTEX_NAME, Application.ProductVersion, Program.UPDATE_URI, Program.UPDATE_CHECK_INTERVAL_DAYS, Program.TRUSTED_CERTIFICATES, _lastUpdateCheckedOn, _lastModifiedGMT);
            _updateClient.ExitApplication += _updateClient_ExitApplication;
            _updateClient.UpdateAvailable += _updateClient_UpdateAvailable;
            _updateClient.NoUpdateAvailable += _updateClient_NoUpdateAvailable;
            _updateClient.UpdateError += _updateClient_UpdateError;


            //show tray icon
            notifyIcon1.Visible = true;
        }

        private void frmMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Shift && (e.KeyCode == Keys.D))
            {
                StartDebugging();
                e.Handled = true;
            }
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            SaveProfile();

            if (_debugFile != null)
            {
                lock (_debugFile)
                {
                    _debugWriter.Flush();
                    _debugFile.Close();

                    _debugWriter = null;
                }
            }

            _link.Dispose();
            _service.Dispose();
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            switch (this.DialogResult)
            {
                case DialogResult.Cancel:
                case DialogResult.None:
                    e.Cancel = true;
                    this.Hide();
                    break;
            }
        }

        private void _link_CommandReceived(string cmd)
        {
            this.Show();
            this.Activate();
        }

        private void btnPlusButton_Click(object sender, EventArgs e)
        {
            mnuPlus.Show(btnPlusButton, new Point(0, btnPlusButton.Height));
        }

        private void lblUserName_MouseEnter(object sender, EventArgs e)
        {
            lblUserName.Font = new Font(lblUserName.Font, FontStyle.Bold);
        }

        private void lblUserName_MouseLeave(object sender, EventArgs e)
        {
            lblUserName.Font = new Font(lblUserName.Font, FontStyle.Regular);
        }

        private void lblUserName_Click(object sender, EventArgs e)
        {
            using (frmViewCertificate frm = new frmViewCertificate(_profile.LocalCertificateStore.Certificate))
            {
                frm.ShowDialog(this);
            }
        }

        private void lstChats_DoubleClick(object sender, EventArgs e)
        {
            ShowSelectedChatView();
        }

        private void lstChats_ItemClick(object sender, EventArgs e)
        {
            ShowSelectedChatView();
        }

        private void lstChats_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                mnuLeaveChat.Enabled = false;
                mnuProperties.Enabled = false;
                mnuChat.Show(lstChats, e.Location);
            }
        }

        private void lstChats_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    ShowSelectedChatView();
                    e.Handled = true;
                    break;

                case Keys.Apps:
                    mnuLeaveChat.Enabled = false;
                    mnuProperties.Enabled = false;
                    mnuChat.Show(lstChats, lstChats.Location);
                    e.Handled = true;
                    break;
            }
        }

        private void lstChats_ItemMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                mnuLeaveChat.Enabled = true;
                mnuProperties.Enabled = true;
                mnuChat.Show(lstChats, e.Location);
            }
        }

        private void lstChats_ItemKeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    ShowSelectedChatView();
                    e.Handled = true;
                    break;

                case Keys.Apps:
                    mnuLeaveChat.Enabled = true;
                    mnuProperties.Enabled = true;
                    mnuChat.Show(lstChats, lstChats.Location);
                    e.Handled = true;
                    break;
            }
        }

        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
            this.Activate();
        }

        private void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            this.Show();
            this.Activate();
        }

        private void mainContainer_Panel2_Resize(object sender, EventArgs e)
        {
            panelGetStarted.Location = new Point(mainContainer.Panel2.Width / 2 - panelGetStarted.Width / 2, mainContainer.Panel2.Height / 2 - panelGetStarted.Height / 2);
        }

        private void btnCreateChat_Click(object sender, EventArgs e)
        {
            mnuAddGroupChat_Click(null, null);
        }

        private void chatPanel_SettingsModified(object sender, EventArgs e)
        {
            BitChatPanel senderPanel = sender as BitChatPanel;

            using (MemoryStream mS = new MemoryStream())
            {
                senderPanel.WriteSettingsTo(mS);

                foreach (Control ctrl in mainContainer.Panel2.Controls)
                {
                    BitChatPanel panel = ctrl as BitChatPanel;

                    if ((panel != null) && !panel.Equals(sender))
                    {
                        mS.Position = 0;
                        panel.ReadSettingsFrom(mS);
                    }
                }
            }
        }

        private void chatPanel_MessageNotification(BitChat sender, BitChat.Peer messageSender, string message)
        {
            if (!this.Visible || !ApplicationIsActivated())
            {
                if (messageSender == null)
                    notifyIcon1.ShowBalloonTip(30000, sender.NetworkName + " - Bit Chat", message, ToolTipIcon.Info);
                else
                    notifyIcon1.ShowBalloonTip(30000, sender.NetworkName + " - Bit Chat", messageSender.PeerCertificate.IssuedTo.Name + ": " + message, ToolTipIcon.Info);

                sndMessageNotification.Play();
            }
        }

        #region menus

        private void mnuAddPrivateChat_Click(object sender, EventArgs e)
        {
            using (frmAddChat frmCreateChat = new frmAddChat(BitChatClient.Network.BitChatNetworkType.PrivateChat))
            {
                if (frmCreateChat.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    BitChat chat = _service.CreateBitChat(new System.Net.Mail.MailAddress(frmCreateChat.txtNetworkNameOrPeerEmailAddress.Text.ToLower()), frmCreateChat.txtPassword.Text);

                    AddChatView(chat);
                }
            }
        }

        private void mnuAddGroupChat_Click(object sender, EventArgs e)
        {
            using (frmAddChat frmCreateChat = new frmAddChat())
            {
                if (frmCreateChat.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    BitChat chat = _service.CreateBitChat(frmCreateChat.txtNetworkNameOrPeerEmailAddress.Text, frmCreateChat.txtPassword.Text);

                    AddChatView(chat);
                }
            }
        }

        private void mnuProfileSettings_Click(object sender, EventArgs e)
        {
            using (frmSettings frm = new frmSettings(_service))
            {
                if (frm.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    if (frm.PasswordChangeRequest)
                        _profile.ChangePassword(frm.Password);

                    if (frm.Port != _profile.LocalEP.Port)
                        _profile.LocalEP = new IPEndPoint(0, frm.Port);

                    _profile.DownloadFolder = frm.DownloadFolder;
                    _profile.CheckCertificateRevocationList = frm.CheckCertificateRevocationList;
                    _profile.TrackerURIs = frm.Trackers;

                    SaveProfile();
                }
            }
        }

        private void mnuSwitchProfile_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Your current profile will be logged off. Are you sure to proceed to change profile?", "Change Profile?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes)
            {
                notifyIcon1.Visible = false;
                this.Hide();
                this.DialogResult = System.Windows.Forms.DialogResult.Ignore;
                this.Close();
            }
        }

        private void mnuCheckUpdate_Click(object sender, EventArgs e)
        {
            mnuCheckUpdate.Enabled = false;

            _updateClient.CheckForUpdate();
        }

        private void mnuAbout_Click(object sender, EventArgs e)
        {
            using (frmAbout frm = new frmAbout())
            {
                frm.ShowDialog(this);
            }
        }

        private void mnuExit_Click(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;
            this.Hide();
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        private void mnuLeaveChat_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure to leave chat?\r\n\r\nWarning! If you wish to join back the same chat again, you will need to remember the Chat Name and Shared Secret.", "Leave Chat?", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == System.Windows.Forms.DialogResult.Yes)
            {
                RemoveSelectedChatView();
            }
        }

        private void mnuProperties_Click(object sender, EventArgs e)
        {
            if (lstChats.SelectedItem != null)
            {
                ChatListItem itm = lstChats.SelectedItem as ChatListItem;
                BitChatPanel chatPanel = itm.Tag as BitChatPanel;

                using (frmChatProperties frm = new frmChatProperties(chatPanel.BitChat))
                {
                    frm.Text = chatPanel.BitChat.NetworkName + " - Properties";
                    frm.ShowDialog(this);
                }
            }
        }

        private void mnuShowBitChat_Click(object sender, EventArgs e)
        {
            this.Show();
            this.Activate();
        }

        #endregion

        #endregion

        #region private

        private void AddWindowsFirewallEntry()
        {
            if (Environment.OSVersion.Version.Major < 6)
            {
                //below vista
                try
                {
                    if (!WindowsFirewall.PortExists(Protocol.TCP, _service.ExternalSelfEP.Port))
                        WindowsFirewall.AddPort("Bit Chat - TCP", Protocol.TCP, _service.ExternalSelfEP.Port, true);

                    if (!WindowsFirewall.PortExists(Protocol.UDP, 41733))
                        WindowsFirewall.AddPort("Bit Chat - Local Discovery", Protocol.UDP, 41733, true);
                }
                catch
                { }
            }
            else
            {
                //vista & above
                try
                {
                    string appPath = Assembly.GetEntryAssembly().CodeBase.Replace("file:///", "").Replace("/", "\\");

                    if (!WindowsFirewall.RuleExistsVista("Bit Chat", appPath))
                        WindowsFirewall.AddRuleVista("Bit Chat", "Allow incoming connection request to Bit Chat application.", FirewallAction.Allow, appPath, Protocol.ANY);
                }
                catch
                { }
            }
        }

        private void InvalidCertificateEvent(BitChatService sender, InvalidCertificateException e)
        {
            MessageBox.Show(e.Message, "Invalid Certificate Detected", MessageBoxButtons.OK, MessageBoxIcon.Error);

            notifyIcon1.Visible = false;
            this.Hide();
            this.DialogResult = System.Windows.Forms.DialogResult.Ignore;
            this.Close();
        }

        private void AddChatView(BitChat chat)
        {
            string title;

            if (chat.NetworkType == BitChatClient.Network.BitChatNetworkType.PrivateChat)
            {
                if (chat.NetworkName == null)
                    title = chat.PeerEmailAddress.Address;
                else
                    title = chat.NetworkName;
            }
            else
            {
                title = chat.NetworkName;
            }

            ChatListItem itm = new ChatListItem(title);

            BitChatPanel chatPanel = new BitChatPanel(chat, itm);
            chatPanel.Dock = DockStyle.Fill;
            chatPanel.SettingsModified += chatPanel_SettingsModified;
            chatPanel.MessageNotification += chatPanel_MessageNotification;

            mainContainer.Panel2.Controls.Add(chatPanel);
            itm.Tag = chatPanel;

            lstChats.AddItem(itm);

            if (lstChats.Controls.Count == 1)
                ShowSelectedChatView();
        }

        private void RemoveSelectedChatView()
        {
            if (lstChats.SelectedItem != null)
            {
                ChatListItem itm = lstChats.SelectedItem as ChatListItem;
                BitChatPanel chatPanel = itm.Tag as BitChatPanel;

                lstChats.RemoveItem(itm);
                mainContainer.Panel2.Controls.Remove(chatPanel);
                chatPanel.SettingsModified -= chatPanel_SettingsModified;

                chatPanel.BitChat.LeaveChat();
            }
        }

        private void ShowSelectedChatView()
        {
            if (lstChats.SelectedItem != null)
            {
                BitChatPanel chatPanel = lstChats.SelectedItem.Tag as BitChatPanel;
                chatPanel.BringToFront();
            }
        }

        private void ShowChatListContextMenu(Point location)
        {
            if (lstChats.SelectedItem != null)
            {
                BitChatPanel chatPanel = lstChats.SelectedItem.Tag as BitChatPanel;
                mnuChat.Show(lstChats, location);
            }
            else
                mnuPlus.Show(lstChats, location);
        }

        private void ReadUISettingsFrom(Stream s)
        {
            BinaryReader bR = new BinaryReader(s);

            byte version = bR.ReadByte();

            switch (version) //version
            {
                case 1:
                case 2:
                    //form location
                    this.Location = new Point(bR.ReadInt32(), bR.ReadInt32());

                    //form size
                    this.Size = new Size(bR.ReadInt32(), bR.ReadInt32());

                    //form maximized
                    if (Convert.ToBoolean(bR.ReadByte()))
                        this.WindowState = FormWindowState.Maximized;

                    //form main container splitter position
                    if (version > 1)
                    {
                        mainContainer.SplitterDistance = mainContainer.Width - bR.ReadInt32();
                    }

                    //first chat panel settings
                    if (Convert.ToBoolean(bR.ReadByte()))
                    {
                        foreach (Control ctrl in mainContainer.Panel2.Controls)
                        {
                            BitChatPanel panel = ctrl as BitChatPanel;

                            if (panel != null)
                            {
                                panel.ReadSettingsFrom(bR);
                                break;
                            }
                        }
                    }
                    break;

                default:
                    throw new Exception("Settings format version not supported.");
            }
        }

        private void WriteUISettingsTo(Stream s)
        {
            BinaryWriter bW = new BinaryWriter(s);

            bW.Write((byte)2); //version

            //form location
            bW.Write(this.Location.X);
            bW.Write(this.Location.Y);

            //form size
            bool maximized = this.WindowState == FormWindowState.Maximized;
            Size size;

            if (maximized)
                size = new Size(960, 540);
            else
                size = this.Size;

            bW.Write(size.Width);
            bW.Write(size.Height);

            //form maximized
            if (maximized)
                bW.Write((byte)1);
            else
                bW.Write((byte)0);

            //form main container splitter position
            bW.Write(mainContainer.Width - mainContainer.SplitterDistance);


            //write first chat panel settings
            bool panelFound = false;

            foreach (Control ctrl in mainContainer.Panel2.Controls)
            {
                BitChatPanel panel = ctrl as BitChatPanel;

                if (panel != null)
                {
                    bW.Write((byte)1);
                    panel.WriteSettingsTo(bW);

                    panelFound = true;
                    break;
                }
            }

            if (!panelFound)
                bW.Write((byte)0);

            bW.Flush();
        }

        private void LoadProfileSettings(byte[] clientData)
        {
            using (Package pkg = new Package(new MemoryStream(clientData, false), PackageMode.Open))
            {
                foreach (PackageItem item in pkg.Items)
                {
                    switch (item.Name)
                    {
                        case "ui":
                            ReadUISettingsFrom(item.DataStream);
                            break;

                        case "au":
                            {
                                BinaryReader bR = new BinaryReader(item.DataStream);

                                _lastUpdateCheckedOn = DateTime.FromBinary(bR.ReadInt64());
                                _lastModifiedGMT = DateTime.FromBinary(bR.ReadInt64());
                            }
                            break;
                    }
                }
            }
        }

        private byte[] SaveProfileSettings()
        {
            using (MemoryStream mS = new MemoryStream())
            {
                Package pkg = new Package(mS, PackageMode.Create);

                {
                    MemoryStream ui = new MemoryStream();
                    WriteUISettingsTo(ui);
                    ui.Position = 0;

                    pkg.AddItem(new PackageItem("ui", ui));
                }

                {
                    MemoryStream au = new MemoryStream();
                    BinaryWriter bW = new BinaryWriter(au);
                    bW.Write(_updateClient.LastUpdateCheckedOn.ToBinary());
                    bW.Write(_updateClient.LastModifiedGMT.ToBinary());
                    bW.Flush();
                    au.Position = 0;

                    pkg.AddItem(new PackageItem("au", au));
                }

                pkg.Flush();

                return mS.ToArray();
            }
        }

        private void SaveProfile()
        {
            //write profile in tmp file
            using (FileStream fS = new FileStream(_profileFilePath + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                _service.UpdateProfile();
                _profile.ClientData = SaveProfileSettings();
                _profile.WriteTo(fS);
            }

            File.Delete(_profileFilePath + ".bak"); //remove old backup file
            File.Move(_profileFilePath, _profileFilePath + ".bak"); //make current profile file as backup file
            File.Move(_profileFilePath + ".tmp", _profileFilePath); //make tmp file as profile file
        }

        #endregion

        #region automatic update client

        private void _updateClient_UpdateError(object sender, Exception ex)
        {
            MessageBox.Show(this, "Automatic Update process encountered following error while checking for updates:\r\n\r\n" + ex.Message, "Error - Bit Chat", MessageBoxButtons.OK, MessageBoxIcon.Error);

            mnuCheckUpdate.Enabled = true;
        }

        private void _updateClient_NoUpdateAvailable(object sender, EventArgs e)
        {
            MessageBox.Show(this, "No new update was available for Bit Chat.", "No Update Available - Bit Chat", MessageBoxButtons.OK, MessageBoxIcon.Information);

            mnuCheckUpdate.Enabled = true;
        }

        private void _updateClient_UpdateAvailable(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "An update is available to download for Bit Chat.\r\n\r\nCurrent Version: " + Application.ProductVersion +
                "\r\nUpdate Version: " + _updateClient.UpdateInfo.UpdateVersion +
                "\r\nUpdate Source: " + _updateClient.UpdateInfo.DownloadURI +
                "\r\nUpdate Size: " + WebUtilities.GetFormattedSize(_updateClient.UpdateInfo.DownloadSize) +
                "\r\n\r\nDo you want to download and install the update now?",
                "Update Available - Bit Chat", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                _updateClient.DownloadAndInstall();
            }

            mnuCheckUpdate.Enabled = true;
        }

        private void _updateClient_ExitApplication(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;
            this.Hide();
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        #endregion

        #region application active check

        public static bool ApplicationIsActivated()
        {
            var activatedHandle = GetForegroundWindow();
            if (activatedHandle == IntPtr.Zero)
            {
                return false;       // No window is currently activated
            }

            var procId = Process.GetCurrentProcess().Id;
            int activeProcId;
            GetWindowThreadProcessId(activatedHandle, out activeProcId);

            return activeProcId == procId;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);

        #endregion

        #region IDebug

        public void Write(string message)
        {
            lock (_debugFile)
            {
                if (_debugWriter != null)
                    _debugWriter.WriteLine(message);
            }
        }

        private void StartDebugging()
        {
            if (_debugFile == null)
            {
                _debugFile = new FileStream(_profileFilePath + ".log", FileMode.Create, FileAccess.Write, FileShare.Read);
                _debugWriter = new StreamWriter(_debugFile);
                _debugWriter.AutoFlush = true;

                BitChatClient.Debug.SetDebug(this);

                this.Text += " [Debugging]";
            }
        }

        #endregion
    }
}
