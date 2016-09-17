namespace BitChatApp.UserControls
{
    partial class BitChatPanel
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.bitChatPanelSplitContainer = new System.Windows.Forms.SplitContainer();
            this.chatOptionsSplitContainer = new System.Windows.Forms.SplitContainer();
            this.lstUsers = new BitChatApp.UserControls.CustomListViewPanel();
            this.lstFiles = new BitChatApp.UserControls.CustomListViewPanel();
            this.mnuUserList = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuViewUserProfile = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuMessageUser = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.bitChatPanelSplitContainer)).BeginInit();
            this.bitChatPanelSplitContainer.Panel2.SuspendLayout();
            this.bitChatPanelSplitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.chatOptionsSplitContainer)).BeginInit();
            this.chatOptionsSplitContainer.Panel1.SuspendLayout();
            this.chatOptionsSplitContainer.Panel2.SuspendLayout();
            this.chatOptionsSplitContainer.SuspendLayout();
            this.mnuUserList.SuspendLayout();
            this.SuspendLayout();
            // 
            // bitChatPanelSplitContainer
            // 
            this.bitChatPanelSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bitChatPanelSplitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.bitChatPanelSplitContainer.Location = new System.Drawing.Point(0, 0);
            this.bitChatPanelSplitContainer.Name = "bitChatPanelSplitContainer";
            // 
            // bitChatPanelSplitContainer.Panel1
            // 
            this.bitChatPanelSplitContainer.Panel1.BackColor = System.Drawing.Color.Transparent;
            this.bitChatPanelSplitContainer.Panel1.Padding = new System.Windows.Forms.Padding(3, 6, 2, 6);
            // 
            // bitChatPanelSplitContainer.Panel2
            // 
            this.bitChatPanelSplitContainer.Panel2.Controls.Add(this.chatOptionsSplitContainer);
            this.bitChatPanelSplitContainer.Panel2MinSize = 100;
            this.bitChatPanelSplitContainer.Size = new System.Drawing.Size(738, 431);
            this.bitChatPanelSplitContainer.SplitterDistance = 486;
            this.bitChatPanelSplitContainer.SplitterWidth = 2;
            this.bitChatPanelSplitContainer.TabIndex = 12;
            this.bitChatPanelSplitContainer.SplitterMoved += new System.Windows.Forms.SplitterEventHandler(this.SplitContainer_SplitterMoved);
            // 
            // chatOptionsSplitContainer
            // 
            this.chatOptionsSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chatOptionsSplitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.chatOptionsSplitContainer.Location = new System.Drawing.Point(0, 0);
            this.chatOptionsSplitContainer.Name = "chatOptionsSplitContainer";
            this.chatOptionsSplitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // chatOptionsSplitContainer.Panel1
            // 
            this.chatOptionsSplitContainer.Panel1.Controls.Add(this.lstUsers);
            this.chatOptionsSplitContainer.Panel1.Padding = new System.Windows.Forms.Padding(3, 6, 6, 3);
            // 
            // chatOptionsSplitContainer.Panel2
            // 
            this.chatOptionsSplitContainer.Panel2.Controls.Add(this.lstFiles);
            this.chatOptionsSplitContainer.Panel2.Padding = new System.Windows.Forms.Padding(3, 3, 6, 6);
            this.chatOptionsSplitContainer.Panel2MinSize = 100;
            this.chatOptionsSplitContainer.Size = new System.Drawing.Size(250, 431);
            this.chatOptionsSplitContainer.SplitterDistance = 176;
            this.chatOptionsSplitContainer.SplitterWidth = 2;
            this.chatOptionsSplitContainer.TabIndex = 12;
            this.chatOptionsSplitContainer.SplitterMoved += new System.Windows.Forms.SplitterEventHandler(this.SplitContainer_SplitterMoved);
            // 
            // lstUsers
            // 
            this.lstUsers.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(223)))));
            this.lstUsers.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstUsers.Location = new System.Drawing.Point(3, 6);
            this.lstUsers.Name = "lstUsers";
            this.lstUsers.Padding = new System.Windows.Forms.Padding(1, 1, 2, 2);
            this.lstUsers.SeperatorSize = 0;
            this.lstUsers.Size = new System.Drawing.Size(241, 167);
            this.lstUsers.SortItems = true;
            this.lstUsers.TabIndex = 0;
            this.lstUsers.Title = "People";
            this.lstUsers.ItemMouseUp += new System.Windows.Forms.MouseEventHandler(this.lstUsers_ItemMouseUp);
            // 
            // lstFiles
            // 
            this.lstFiles.AllowDrop = true;
            this.lstFiles.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(223)))));
            this.lstFiles.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstFiles.Location = new System.Drawing.Point(3, 3);
            this.lstFiles.Name = "lstFiles";
            this.lstFiles.Padding = new System.Windows.Forms.Padding(1, 1, 2, 2);
            this.lstFiles.SeperatorSize = 0;
            this.lstFiles.Size = new System.Drawing.Size(241, 244);
            this.lstFiles.SortItems = true;
            this.lstFiles.TabIndex = 0;
            this.lstFiles.Title = "Files";
            this.lstFiles.DragDrop += new System.Windows.Forms.DragEventHandler(this.lstFiles_DragDrop);
            this.lstFiles.DragEnter += new System.Windows.Forms.DragEventHandler(this.lstFiles_DragEnter);
            // 
            // mnuUserList
            // 
            this.mnuUserList.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuViewUserProfile,
            this.mnuMessageUser});
            this.mnuUserList.Name = "mnuUserList";
            this.mnuUserList.Size = new System.Drawing.Size(147, 48);
            // 
            // mnuViewUserProfile
            // 
            this.mnuViewUserProfile.Name = "mnuViewUserProfile";
            this.mnuViewUserProfile.Size = new System.Drawing.Size(146, 22);
            this.mnuViewUserProfile.Text = "&View Profile";
            this.mnuViewUserProfile.Click += new System.EventHandler(this.mnuViewUserProfile_Click);
            // 
            // mnuMessageUser
            // 
            this.mnuMessageUser.Name = "mnuMessageUser";
            this.mnuMessageUser.Size = new System.Drawing.Size(146, 22);
            this.mnuMessageUser.Text = "Message User";
            this.mnuMessageUser.Click += new System.EventHandler(this.mnuMessageUser_Click);
            // 
            // BitChatPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(232)))), ((int)(((byte)(232)))));
            this.Controls.Add(this.bitChatPanelSplitContainer);
            this.Name = "BitChatPanel";
            this.Size = new System.Drawing.Size(738, 431);
            this.bitChatPanelSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.bitChatPanelSplitContainer)).EndInit();
            this.bitChatPanelSplitContainer.ResumeLayout(false);
            this.chatOptionsSplitContainer.Panel1.ResumeLayout(false);
            this.chatOptionsSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.chatOptionsSplitContainer)).EndInit();
            this.chatOptionsSplitContainer.ResumeLayout(false);
            this.mnuUserList.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer bitChatPanelSplitContainer;
        private System.Windows.Forms.SplitContainer chatOptionsSplitContainer;
        private UserControls.CustomListViewPanel lstUsers;
        private CustomListViewPanel lstFiles;
        private System.Windows.Forms.ContextMenuStrip mnuUserList;
        private System.Windows.Forms.ToolStripMenuItem mnuViewUserProfile;
        private System.Windows.Forms.ToolStripMenuItem mnuMessageUser;
    }
}
