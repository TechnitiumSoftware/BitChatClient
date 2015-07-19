using BitChatApp.UserControls;
namespace BitChatApp
{
    partial class frmMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmMain));
            this.mnuChat = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuLeaveChat = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuProperties = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuAddPrivateChat2 = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuAddGroupChat2 = new System.Windows.Forms.ToolStripMenuItem();
            this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
            this.mnuSysTray = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuShowBitChat = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuSwitchProfile = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuCheckUpdate = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator5 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuExit = new System.Windows.Forms.ToolStripMenuItem();
            this.mainContainer = new System.Windows.Forms.SplitContainer();
            this.panel1 = new System.Windows.Forms.Panel();
            this.btnPlusButton = new BitChatApp.UserControls.CustomButton();
            this.lblUserName = new System.Windows.Forms.Label();
            this.lstChats = new BitChatApp.UserControls.CustomListView();
            this.panelGetStarted = new System.Windows.Forms.Panel();
            this.label2 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.btnCreateChat = new BitChatApp.UserControls.CustomButton();
            this.mnuAddGroupChat1 = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator4 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuProfileSettings = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuPlus = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuAddPrivateChat1 = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuSwitchProfile2 = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuCheckUpdate2 = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuAbout2 = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuExit2 = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuChat.SuspendLayout();
            this.mnuSysTray.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainContainer)).BeginInit();
            this.mainContainer.Panel1.SuspendLayout();
            this.mainContainer.Panel2.SuspendLayout();
            this.mainContainer.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnPlusButton)).BeginInit();
            this.panelGetStarted.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnCreateChat)).BeginInit();
            this.mnuPlus.SuspendLayout();
            this.SuspendLayout();
            // 
            // mnuChat
            // 
            this.mnuChat.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuLeaveChat,
            this.mnuProperties,
            this.toolStripSeparator1,
            this.mnuAddPrivateChat2,
            this.mnuAddGroupChat2});
            this.mnuChat.Name = "chatContextMenu";
            this.mnuChat.Size = new System.Drawing.Size(164, 98);
            // 
            // mnuLeaveChat
            // 
            this.mnuLeaveChat.Name = "mnuLeaveChat";
            this.mnuLeaveChat.Size = new System.Drawing.Size(163, 22);
            this.mnuLeaveChat.Text = "&Leave Chat";
            this.mnuLeaveChat.Click += new System.EventHandler(this.mnuLeaveChat_Click);
            // 
            // mnuProperties
            // 
            this.mnuProperties.Name = "mnuProperties";
            this.mnuProperties.Size = new System.Drawing.Size(163, 22);
            this.mnuProperties.Text = "P&roperties";
            this.mnuProperties.Click += new System.EventHandler(this.mnuProperties_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(160, 6);
            // 
            // mnuAddPrivateChat2
            // 
            this.mnuAddPrivateChat2.Name = "mnuAddPrivateChat2";
            this.mnuAddPrivateChat2.Size = new System.Drawing.Size(163, 22);
            this.mnuAddPrivateChat2.Text = "Add &Private Chat";
            this.mnuAddPrivateChat2.Click += new System.EventHandler(this.mnuAddPrivateChat_Click);
            // 
            // mnuAddGroupChat2
            // 
            this.mnuAddGroupChat2.Name = "mnuAddGroupChat2";
            this.mnuAddGroupChat2.Size = new System.Drawing.Size(163, 22);
            this.mnuAddGroupChat2.Text = "Add &Group Chat";
            this.mnuAddGroupChat2.Click += new System.EventHandler(this.mnuAddGroupChat_Click);
            // 
            // notifyIcon1
            // 
            this.notifyIcon1.ContextMenuStrip = this.mnuSysTray;
            this.notifyIcon1.Icon = ((System.Drawing.Icon)(resources.GetObject("notifyIcon1.Icon")));
            this.notifyIcon1.Text = "Technitium Bit Chat";
            this.notifyIcon1.BalloonTipClicked += new System.EventHandler(this.notifyIcon1_BalloonTipClicked);
            this.notifyIcon1.DoubleClick += new System.EventHandler(this.notifyIcon1_DoubleClick);
            // 
            // mnuSysTray
            // 
            this.mnuSysTray.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuShowBitChat,
            this.toolStripSeparator3,
            this.mnuSwitchProfile,
            this.mnuCheckUpdate,
            this.mnuAbout,
            this.toolStripSeparator5,
            this.mnuExit});
            this.mnuSysTray.Name = "systrayMenu";
            this.mnuSysTray.Size = new System.Drawing.Size(174, 126);
            // 
            // mnuShowBitChat
            // 
            this.mnuShowBitChat.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.mnuShowBitChat.Name = "mnuShowBitChat";
            this.mnuShowBitChat.Size = new System.Drawing.Size(173, 22);
            this.mnuShowBitChat.Text = "Bit Chat";
            this.mnuShowBitChat.Click += new System.EventHandler(this.mnuShowBitChat_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(170, 6);
            // 
            // mnuSwitchProfile
            // 
            this.mnuSwitchProfile.Name = "mnuSwitchProfile";
            this.mnuSwitchProfile.Size = new System.Drawing.Size(173, 22);
            this.mnuSwitchProfile.Text = "&Switch Profile";
            this.mnuSwitchProfile.Click += new System.EventHandler(this.mnuSwitchProfile_Click);
            // 
            // mnuCheckUpdate
            // 
            this.mnuCheckUpdate.Name = "mnuCheckUpdate";
            this.mnuCheckUpdate.Size = new System.Drawing.Size(173, 22);
            this.mnuCheckUpdate.Text = "Check For &Updates";
            this.mnuCheckUpdate.Click += new System.EventHandler(this.mnuCheckUpdate_Click);
            // 
            // mnuAbout
            // 
            this.mnuAbout.Image = global::BitChatApp.Properties.Resources.logo2;
            this.mnuAbout.Name = "mnuAbout";
            this.mnuAbout.Size = new System.Drawing.Size(173, 22);
            this.mnuAbout.Text = "&About Bit Chat";
            this.mnuAbout.Click += new System.EventHandler(this.mnuAbout_Click);
            // 
            // toolStripSeparator5
            // 
            this.toolStripSeparator5.Name = "toolStripSeparator5";
            this.toolStripSeparator5.Size = new System.Drawing.Size(170, 6);
            // 
            // mnuExit
            // 
            this.mnuExit.Name = "mnuExit";
            this.mnuExit.Size = new System.Drawing.Size(173, 22);
            this.mnuExit.Text = "E&xit";
            this.mnuExit.Click += new System.EventHandler(this.mnuExit_Click);
            // 
            // mainContainer
            // 
            this.mainContainer.CausesValidation = false;
            this.mainContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.mainContainer.Location = new System.Drawing.Point(0, 0);
            this.mainContainer.Name = "mainContainer";
            // 
            // mainContainer.Panel1
            // 
            this.mainContainer.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.mainContainer.Panel1.Controls.Add(this.panel1);
            this.mainContainer.Panel1.Controls.Add(this.lstChats);
            this.mainContainer.Panel1MinSize = 200;
            // 
            // mainContainer.Panel2
            // 
            this.mainContainer.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(232)))));
            this.mainContainer.Panel2.Controls.Add(this.panelGetStarted);
            this.mainContainer.Panel2.Resize += new System.EventHandler(this.mainContainer_Panel2_Resize);
            this.mainContainer.Panel2MinSize = 200;
            this.mainContainer.Size = new System.Drawing.Size(944, 501);
            this.mainContainer.SplitterDistance = 277;
            this.mainContainer.TabIndex = 5;
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(153)))), ((int)(((byte)(255)))));
            this.panel1.Controls.Add(this.btnPlusButton);
            this.panel1.Controls.Add(this.lblUserName);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(277, 36);
            this.panel1.TabIndex = 14;
            // 
            // btnPlusButton
            // 
            this.btnPlusButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPlusButton.Image = ((System.Drawing.Image)(resources.GetObject("btnPlusButton.Image")));
            this.btnPlusButton.ImageHover = ((System.Drawing.Image)(resources.GetObject("btnPlusButton.ImageHover")));
            this.btnPlusButton.ImageMouseDown = ((System.Drawing.Image)(resources.GetObject("btnPlusButton.ImageMouseDown")));
            this.btnPlusButton.Location = new System.Drawing.Point(250, 7);
            this.btnPlusButton.Name = "btnPlusButton";
            this.btnPlusButton.Size = new System.Drawing.Size(24, 24);
            this.btnPlusButton.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.btnPlusButton.TabIndex = 14;
            this.btnPlusButton.TabStop = false;
            this.btnPlusButton.Click += new System.EventHandler(this.btnPlusButton_Click);
            // 
            // lblUserName
            // 
            this.lblUserName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblUserName.AutoEllipsis = true;
            this.lblUserName.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(153)))), ((int)(((byte)(255)))));
            this.lblUserName.Cursor = System.Windows.Forms.Cursors.Hand;
            this.lblUserName.Font = new System.Drawing.Font("Arial", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblUserName.ForeColor = System.Drawing.Color.White;
            this.lblUserName.Location = new System.Drawing.Point(24, 1);
            this.lblUserName.Name = "lblUserName";
            this.lblUserName.Size = new System.Drawing.Size(226, 34);
            this.lblUserName.TabIndex = 11;
            this.lblUserName.Text = "Username";
            this.lblUserName.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblUserName.Click += new System.EventHandler(this.lblUserName_Click);
            this.lblUserName.MouseEnter += new System.EventHandler(this.lblUserName_MouseEnter);
            this.lblUserName.MouseLeave += new System.EventHandler(this.lblUserName_MouseLeave);
            // 
            // lstChats
            // 
            this.lstChats.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstChats.AutoScroll = true;
            this.lstChats.AutoScrollToBottom = false;
            this.lstChats.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.lstChats.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.lstChats.Location = new System.Drawing.Point(0, 36);
            this.lstChats.Name = "lstChats";
            this.lstChats.SeparatorColor = System.Drawing.Color.FromArgb(((int)(((byte)(39)))), ((int)(((byte)(53)))), ((int)(((byte)(65)))));
            this.lstChats.Size = new System.Drawing.Size(277, 462);
            this.lstChats.SortItems = true;
            this.lstChats.TabIndex = 13;
            this.lstChats.ItemClick += new System.EventHandler(this.lstChats_ItemClick);
            this.lstChats.ItemMouseUp += new System.Windows.Forms.MouseEventHandler(this.lstChats_ItemMouseUp);
            this.lstChats.ItemKeyUp += new System.Windows.Forms.KeyEventHandler(this.lstChats_ItemKeyUp);
            this.lstChats.DoubleClick += new System.EventHandler(this.lstChats_DoubleClick);
            this.lstChats.KeyUp += new System.Windows.Forms.KeyEventHandler(this.lstChats_KeyUp);
            this.lstChats.MouseUp += new System.Windows.Forms.MouseEventHandler(this.lstChats_MouseUp);
            // 
            // panelGetStarted
            // 
            this.panelGetStarted.Controls.Add(this.label2);
            this.panelGetStarted.Controls.Add(this.label4);
            this.panelGetStarted.Controls.Add(this.btnCreateChat);
            this.panelGetStarted.Location = new System.Drawing.Point(102, 126);
            this.panelGetStarted.Name = "panelGetStarted";
            this.panelGetStarted.Size = new System.Drawing.Size(488, 197);
            this.panelGetStarted.TabIndex = 21;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Arial", 48F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label2.Location = new System.Drawing.Point(3, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(360, 75);
            this.label2.TabIndex = 19;
            this.label2.Text = "get started";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Arial", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label4.Location = new System.Drawing.Point(213, 75);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(275, 36);
            this.label4.TabIndex = 20;
            this.label4.Text = "create a chat now!";
            // 
            // btnCreateChat
            // 
            this.btnCreateChat.BackColor = System.Drawing.Color.Transparent;
            this.btnCreateChat.Image = ((System.Drawing.Image)(resources.GetObject("btnCreateChat.Image")));
            this.btnCreateChat.ImageHover = ((System.Drawing.Image)(resources.GetObject("btnCreateChat.ImageHover")));
            this.btnCreateChat.ImageMouseDown = ((System.Drawing.Image)(resources.GetObject("btnCreateChat.ImageMouseDown")));
            this.btnCreateChat.Location = new System.Drawing.Point(178, 139);
            this.btnCreateChat.Name = "btnCreateChat";
            this.btnCreateChat.Size = new System.Drawing.Size(134, 44);
            this.btnCreateChat.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.btnCreateChat.TabIndex = 2;
            this.btnCreateChat.TabStop = false;
            this.btnCreateChat.Click += new System.EventHandler(this.btnCreateChat_Click);
            // 
            // mnuAddGroupChat1
            // 
            this.mnuAddGroupChat1.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.mnuAddGroupChat1.Name = "mnuAddGroupChat1";
            this.mnuAddGroupChat1.Size = new System.Drawing.Size(173, 22);
            this.mnuAddGroupChat1.Text = "Add &Group Chat";
            this.mnuAddGroupChat1.Click += new System.EventHandler(this.mnuAddGroupChat_Click);
            // 
            // toolStripSeparator4
            // 
            this.toolStripSeparator4.Name = "toolStripSeparator4";
            this.toolStripSeparator4.Size = new System.Drawing.Size(170, 6);
            // 
            // mnuProfileSettings
            // 
            this.mnuProfileSettings.Name = "mnuProfileSettings";
            this.mnuProfileSettings.Size = new System.Drawing.Size(173, 22);
            this.mnuProfileSettings.Text = "Profile &Settings";
            this.mnuProfileSettings.Click += new System.EventHandler(this.mnuProfileSettings_Click);
            // 
            // mnuPlus
            // 
            this.mnuPlus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuAddPrivateChat1,
            this.mnuAddGroupChat1,
            this.toolStripSeparator4,
            this.mnuProfileSettings,
            this.mnuSwitchProfile2,
            this.mnuCheckUpdate2,
            this.mnuAbout2,
            this.toolStripSeparator2,
            this.mnuExit2});
            this.mnuPlus.Name = "addChatContextMenu";
            this.mnuPlus.Size = new System.Drawing.Size(174, 192);
            // 
            // mnuAddPrivateChat1
            // 
            this.mnuAddPrivateChat1.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.mnuAddPrivateChat1.Name = "mnuAddPrivateChat1";
            this.mnuAddPrivateChat1.Size = new System.Drawing.Size(173, 22);
            this.mnuAddPrivateChat1.Text = "Add &Private Chat";
            this.mnuAddPrivateChat1.Click += new System.EventHandler(this.mnuAddPrivateChat_Click);
            // 
            // mnuSwitchProfile2
            // 
            this.mnuSwitchProfile2.Name = "mnuSwitchProfile2";
            this.mnuSwitchProfile2.Size = new System.Drawing.Size(173, 22);
            this.mnuSwitchProfile2.Text = "S&witch Profile";
            this.mnuSwitchProfile2.Click += new System.EventHandler(this.mnuSwitchProfile_Click);
            // 
            // mnuCheckUpdate2
            // 
            this.mnuCheckUpdate2.Name = "mnuCheckUpdate2";
            this.mnuCheckUpdate2.Size = new System.Drawing.Size(173, 22);
            this.mnuCheckUpdate2.Text = "Check For &Updates";
            this.mnuCheckUpdate2.Click += new System.EventHandler(this.mnuCheckUpdate_Click);
            // 
            // mnuAbout2
            // 
            this.mnuAbout2.Image = global::BitChatApp.Properties.Resources.logo2;
            this.mnuAbout2.Name = "mnuAbout2";
            this.mnuAbout2.Size = new System.Drawing.Size(173, 22);
            this.mnuAbout2.Text = "&About Bit Chat";
            this.mnuAbout2.Click += new System.EventHandler(this.mnuAbout_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(170, 6);
            // 
            // mnuExit2
            // 
            this.mnuExit2.Name = "mnuExit2";
            this.mnuExit2.Size = new System.Drawing.Size(173, 22);
            this.mnuExit2.Text = "E&xit";
            this.mnuExit2.Click += new System.EventHandler(this.mnuExit_Click);
            // 
            // frmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(232)))));
            this.ClientSize = new System.Drawing.Size(944, 501);
            this.Controls.Add(this.mainContainer);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MinimumSize = new System.Drawing.Size(960, 540);
            this.Name = "frmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Technitium Bit Chat";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.frmMain_FormClosing);
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.frmMain_FormClosed);
            this.Load += new System.EventHandler(this.frmMain_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.frmMain_KeyDown);
            this.mnuChat.ResumeLayout(false);
            this.mnuSysTray.ResumeLayout(false);
            this.mainContainer.Panel1.ResumeLayout(false);
            this.mainContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainContainer)).EndInit();
            this.mainContainer.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnPlusButton)).EndInit();
            this.panelGetStarted.ResumeLayout(false);
            this.panelGetStarted.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnCreateChat)).EndInit();
            this.mnuPlus.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip mnuChat;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem mnuLeaveChat;
        private System.Windows.Forms.ToolStripMenuItem mnuProperties;
        private System.Windows.Forms.NotifyIcon notifyIcon1;
        private System.Windows.Forms.ContextMenuStrip mnuSysTray;
        private System.Windows.Forms.ToolStripMenuItem mnuExit;
        private System.Windows.Forms.ToolStripMenuItem mnuShowBitChat;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.SplitContainer mainContainer;
        private System.Windows.Forms.Label lblUserName;
        private BitChatApp.UserControls.CustomListView lstChats;
        private BitChatApp.UserControls.CustomButton btnCreateChat;
        private System.Windows.Forms.Panel panelGetStarted;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Panel panel1;
        private CustomButton btnPlusButton;
        private System.Windows.Forms.ToolStripMenuItem mnuSwitchProfile;
        private System.Windows.Forms.ToolStripMenuItem mnuAbout;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator5;
        private System.Windows.Forms.ToolStripMenuItem mnuCheckUpdate;
        private System.Windows.Forms.ToolStripMenuItem mnuAddGroupChat2;
        private System.Windows.Forms.ToolStripMenuItem mnuAddGroupChat1;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator4;
        private System.Windows.Forms.ToolStripMenuItem mnuProfileSettings;
        private System.Windows.Forms.ContextMenuStrip mnuPlus;
        private System.Windows.Forms.ToolStripMenuItem mnuSwitchProfile2;
        private System.Windows.Forms.ToolStripMenuItem mnuCheckUpdate2;
        private System.Windows.Forms.ToolStripMenuItem mnuAbout2;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem mnuExit2;
        private System.Windows.Forms.ToolStripMenuItem mnuAddPrivateChat2;
        private System.Windows.Forms.ToolStripMenuItem mnuAddPrivateChat1;

    }
}

