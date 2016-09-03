namespace BitChatApp
{
    partial class frmChatProperties
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmChatProperties));
            this.btnClose = new System.Windows.Forms.Button();
            this.lstTrackerInfo = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader3 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader4 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.mnuTracker = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.updateTrackerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.removeTrackerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.copyTrackerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.showPeersToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.addTrackerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.addDefaultTrackersToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.copyAllTrackersToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.txtNetwork = new System.Windows.Forms.TextBox();
            this.txtSecret = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.chkShowSecret = new System.Windows.Forms.CheckBox();
            this.chkLANChat = new System.Windows.Forms.CheckBox();
            this.btnOK = new System.Windows.Forms.Button();
            this.mnuTracker.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnClose.Location = new System.Drawing.Point(597, 254);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(75, 23);
            this.btnClose.TabIndex = 6;
            this.btnClose.Text = "Close";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // lstTrackerInfo
            // 
            this.lstTrackerInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstTrackerInfo.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3,
            this.columnHeader4});
            this.lstTrackerInfo.ContextMenuStrip = this.mnuTracker;
            this.lstTrackerInfo.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.lstTrackerInfo.FullRowSelect = true;
            this.lstTrackerInfo.HideSelection = false;
            this.lstTrackerInfo.Location = new System.Drawing.Point(12, 88);
            this.lstTrackerInfo.Name = "lstTrackerInfo";
            this.lstTrackerInfo.Size = new System.Drawing.Size(660, 160);
            this.lstTrackerInfo.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lstTrackerInfo.TabIndex = 3;
            this.lstTrackerInfo.UseCompatibleStateImageBehavior = false;
            this.lstTrackerInfo.View = System.Windows.Forms.View.Details;
            this.lstTrackerInfo.DoubleClick += new System.EventHandler(this.lstTrackerInfo_DoubleClick);
            this.lstTrackerInfo.MouseUp += new System.Windows.Forms.MouseEventHandler(this.lstTrackerInfo_MouseUp);
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "Name";
            this.columnHeader1.Width = 230;
            // 
            // columnHeader2
            // 
            this.columnHeader2.Text = "Status";
            this.columnHeader2.Width = 280;
            // 
            // columnHeader3
            // 
            this.columnHeader3.Text = "Update In";
            this.columnHeader3.Width = 80;
            // 
            // columnHeader4
            // 
            this.columnHeader4.Text = "Peers";
            this.columnHeader4.Width = 40;
            // 
            // mnuTracker
            // 
            this.mnuTracker.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.updateTrackerToolStripMenuItem,
            this.removeTrackerToolStripMenuItem,
            this.copyTrackerToolStripMenuItem,
            this.showPeersToolStripMenuItem,
            this.toolStripSeparator1,
            this.addTrackerToolStripMenuItem,
            this.addDefaultTrackersToolStripMenuItem,
            this.copyAllTrackersToolStripMenuItem});
            this.mnuTracker.Name = "contextMenuStrip1";
            this.mnuTracker.Size = new System.Drawing.Size(184, 164);
            // 
            // updateTrackerToolStripMenuItem
            // 
            this.updateTrackerToolStripMenuItem.Name = "updateTrackerToolStripMenuItem";
            this.updateTrackerToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.updateTrackerToolStripMenuItem.Text = "&Update Tracker";
            this.updateTrackerToolStripMenuItem.Click += new System.EventHandler(this.updateTrackerToolStripMenuItem_Click);
            // 
            // removeTrackerToolStripMenuItem
            // 
            this.removeTrackerToolStripMenuItem.Name = "removeTrackerToolStripMenuItem";
            this.removeTrackerToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.removeTrackerToolStripMenuItem.Text = "&Remove Tracker";
            this.removeTrackerToolStripMenuItem.Click += new System.EventHandler(this.removeTrackerToolStripMenuItem_Click);
            // 
            // copyTrackerToolStripMenuItem
            // 
            this.copyTrackerToolStripMenuItem.Name = "copyTrackerToolStripMenuItem";
            this.copyTrackerToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.copyTrackerToolStripMenuItem.Text = "&Copy Tracker";
            this.copyTrackerToolStripMenuItem.Click += new System.EventHandler(this.copyTrackerToolStripMenuItem_Click);
            // 
            // showPeersToolStripMenuItem
            // 
            this.showPeersToolStripMenuItem.Name = "showPeersToolStripMenuItem";
            this.showPeersToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.showPeersToolStripMenuItem.Text = "&Show Peers";
            this.showPeersToolStripMenuItem.Click += new System.EventHandler(this.showPeersToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(180, 6);
            // 
            // addTrackerToolStripMenuItem
            // 
            this.addTrackerToolStripMenuItem.Name = "addTrackerToolStripMenuItem";
            this.addTrackerToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.addTrackerToolStripMenuItem.Text = "&Add Tracker";
            this.addTrackerToolStripMenuItem.Click += new System.EventHandler(this.addTrackerToolStripMenuItem_Click);
            // 
            // addDefaultTrackersToolStripMenuItem
            // 
            this.addDefaultTrackersToolStripMenuItem.Name = "addDefaultTrackersToolStripMenuItem";
            this.addDefaultTrackersToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.addDefaultTrackersToolStripMenuItem.Text = "Add &Default Trackers";
            this.addDefaultTrackersToolStripMenuItem.Click += new System.EventHandler(this.addDefaultTrackersToolStripMenuItem_Click);
            // 
            // copyAllTrackersToolStripMenuItem
            // 
            this.copyAllTrackersToolStripMenuItem.Name = "copyAllTrackersToolStripMenuItem";
            this.copyAllTrackersToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.copyAllTrackersToolStripMenuItem.Text = "Copy All &Trackers";
            this.copyAllTrackersToolStripMenuItem.Click += new System.EventHandler(this.copyAllTrackersToolStripMenuItem_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label2.Location = new System.Drawing.Point(12, 70);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(75, 15);
            this.label2.TabIndex = 51;
            this.label2.Text = "Tracker Info";
            // 
            // label1
            // 
            this.label1.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label1.Location = new System.Drawing.Point(12, 14);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(140, 15);
            this.label1.TabIndex = 52;
            this.label1.Text = "Chat Name";
            this.label1.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // txtNetwork
            // 
            this.txtNetwork.Location = new System.Drawing.Point(158, 12);
            this.txtNetwork.Name = "txtNetwork";
            this.txtNetwork.ReadOnly = true;
            this.txtNetwork.Size = new System.Drawing.Size(233, 20);
            this.txtNetwork.TabIndex = 0;
            // 
            // txtSecret
            // 
            this.txtSecret.Location = new System.Drawing.Point(492, 12);
            this.txtSecret.MaxLength = 255;
            this.txtSecret.Name = "txtSecret";
            this.txtSecret.PasswordChar = '#';
            this.txtSecret.Size = new System.Drawing.Size(180, 20);
            this.txtSecret.TabIndex = 1;
            this.txtSecret.Text = "########";
            this.txtSecret.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.txtSecret_KeyPress);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label3.Location = new System.Drawing.Point(397, 14);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(89, 15);
            this.label3.TabIndex = 54;
            this.label3.Text = "Shared Secret";
            // 
            // chkShowSecret
            // 
            this.chkShowSecret.AutoSize = true;
            this.chkShowSecret.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.chkShowSecret.Location = new System.Drawing.Point(492, 38);
            this.chkShowSecret.Name = "chkShowSecret";
            this.chkShowSecret.Size = new System.Drawing.Size(124, 17);
            this.chkShowSecret.TabIndex = 2;
            this.chkShowSecret.Text = "Show Shared Secret";
            this.chkShowSecret.UseVisualStyleBackColor = true;
            this.chkShowSecret.CheckedChanged += new System.EventHandler(this.chkShowSecret_CheckedChanged);
            // 
            // chkLANChat
            // 
            this.chkLANChat.AutoSize = true;
            this.chkLANChat.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.chkLANChat.Location = new System.Drawing.Point(12, 258);
            this.chkLANChat.Name = "chkLANChat";
            this.chkLANChat.Size = new System.Drawing.Size(237, 17);
            this.chkLANChat.TabIndex = 4;
            this.chkLANChat.Text = "Enable only local network (LAN or WiFi) chat";
            this.chkLANChat.UseVisualStyleBackColor = true;
            this.chkLANChat.CheckedChanged += new System.EventHandler(this.chkLANChat_CheckedChanged);
            // 
            // btnOK
            // 
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.Location = new System.Drawing.Point(516, 254);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 5;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // frmChatProperties
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(250)))), ((int)(((byte)(250)))));
            this.CancelButton = this.btnClose;
            this.ClientSize = new System.Drawing.Size(684, 282);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.chkLANChat);
            this.Controls.Add(this.chkShowSecret);
            this.Controls.Add(this.txtSecret);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.txtNetwork);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.lstTrackerInfo);
            this.Controls.Add(this.btnClose);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "frmChatProperties";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Chat Properties";
            this.mnuTracker.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.ListView lstTrackerInfo;
        private System.Windows.Forms.ColumnHeader columnHeader1;
        private System.Windows.Forms.ColumnHeader columnHeader2;
        private System.Windows.Forms.ColumnHeader columnHeader3;
        private System.Windows.Forms.ColumnHeader columnHeader4;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ContextMenuStrip mnuTracker;
        private System.Windows.Forms.ToolStripMenuItem updateTrackerToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem removeTrackerToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem addTrackerToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem showPeersToolStripMenuItem;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtNetwork;
        private System.Windows.Forms.TextBox txtSecret;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox chkShowSecret;
        private System.Windows.Forms.ToolStripMenuItem copyTrackerToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem copyAllTrackersToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addDefaultTrackersToolStripMenuItem;
        private System.Windows.Forms.CheckBox chkLANChat;
        private System.Windows.Forms.Button btnOK;
    }
}