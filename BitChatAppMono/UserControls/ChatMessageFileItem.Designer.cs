namespace BitChatApp.UserControls
{
    partial class ChatMessageFileItem
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
            this.lblFileName = new System.Windows.Forms.Label();
            this.lblFileSize = new System.Windows.Forms.Label();
            this.lblContentType = new System.Windows.Forms.Label();
            this.pnlBubble = new System.Windows.Forms.Panel();
            this.lblDateTime = new System.Windows.Forms.Label();
            this.lblUsername = new System.Windows.Forms.Label();
            this.linkAction = new System.Windows.Forms.LinkLabel();
            this.picPointLeft = new System.Windows.Forms.PictureBox();
            this.picPointRight = new System.Windows.Forms.PictureBox();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.pnlBubble.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picPointLeft)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picPointRight)).BeginInit();
            this.SuspendLayout();
            // 
            // lblFileName
            // 
            this.lblFileName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblFileName.AutoEllipsis = true;
            this.lblFileName.BackColor = System.Drawing.Color.Transparent;
            this.lblFileName.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblFileName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.lblFileName.Location = new System.Drawing.Point(5, 22);
            this.lblFileName.Name = "lblFileName";
            this.lblFileName.Size = new System.Drawing.Size(240, 14);
            this.lblFileName.TabIndex = 1;
            this.lblFileName.Text = "Image001.jpg";
            // 
            // lblFileSize
            // 
            this.lblFileSize.AutoEllipsis = true;
            this.lblFileSize.AutoSize = true;
            this.lblFileSize.BackColor = System.Drawing.Color.Transparent;
            this.lblFileSize.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblFileSize.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.lblFileSize.Location = new System.Drawing.Point(5, 54);
            this.lblFileSize.Name = "lblFileSize";
            this.lblFileSize.Size = new System.Drawing.Size(52, 14);
            this.lblFileSize.TabIndex = 2;
            this.lblFileSize.Text = "350.5 MB";
            this.lblFileSize.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lblContentType
            // 
            this.lblContentType.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblContentType.AutoEllipsis = true;
            this.lblContentType.BackColor = System.Drawing.Color.Transparent;
            this.lblContentType.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblContentType.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.lblContentType.Location = new System.Drawing.Point(5, 38);
            this.lblContentType.Name = "lblContentType";
            this.lblContentType.Size = new System.Drawing.Size(240, 14);
            this.lblContentType.TabIndex = 3;
            this.lblContentType.Text = "image/jpeg";
            // 
            // pnlBubble
            // 
            this.pnlBubble.BackColor = System.Drawing.Color.White;
            this.pnlBubble.Controls.Add(this.lblDateTime);
            this.pnlBubble.Controls.Add(this.lblUsername);
            this.pnlBubble.Controls.Add(this.linkAction);
            this.pnlBubble.Controls.Add(this.lblFileName);
            this.pnlBubble.Controls.Add(this.lblFileSize);
            this.pnlBubble.Controls.Add(this.lblContentType);
            this.pnlBubble.Location = new System.Drawing.Point(18, 4);
            this.pnlBubble.Name = "pnlBubble";
            this.pnlBubble.Size = new System.Drawing.Size(250, 90);
            this.pnlBubble.TabIndex = 4;
            // 
            // lblDateTime
            // 
            this.lblDateTime.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblDateTime.AutoEllipsis = true;
            this.lblDateTime.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblDateTime.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(100)))), ((int)(((byte)(100)))));
            this.lblDateTime.Location = new System.Drawing.Point(3, 73);
            this.lblDateTime.Name = "lblDateTime";
            this.lblDateTime.Size = new System.Drawing.Size(244, 14);
            this.lblDateTime.TabIndex = 6;
            this.lblDateTime.Text = "12:00 PM";
            this.lblDateTime.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // lblUsername
            // 
            this.lblUsername.AutoEllipsis = true;
            this.lblUsername.AutoSize = true;
            this.lblUsername.Cursor = System.Windows.Forms.Cursors.Hand;
            this.lblUsername.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblUsername.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(78)))));
            this.lblUsername.Location = new System.Drawing.Point(5, 3);
            this.lblUsername.Name = "lblUsername";
            this.lblUsername.Size = new System.Drawing.Size(66, 15);
            this.lblUsername.TabIndex = 5;
            this.lblUsername.Text = "Username";
            this.lblUsername.TextAlign = System.Drawing.ContentAlignment.TopRight;
            this.lblUsername.Click += new System.EventHandler(this.lblUsername_Click);
            this.lblUsername.MouseEnter += new System.EventHandler(this.lblUsername_MouseEnter);
            this.lblUsername.MouseLeave += new System.EventHandler(this.lblUsername_MouseLeave);
            // 
            // linkAction
            // 
            this.linkAction.AutoSize = true;
            this.linkAction.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.linkAction.Location = new System.Drawing.Point(190, 54);
            this.linkAction.Name = "linkAction";
            this.linkAction.Size = new System.Drawing.Size(56, 14);
            this.linkAction.TabIndex = 4;
            this.linkAction.TabStop = true;
            this.linkAction.Text = "Download";
            this.linkAction.Visible = false;
            this.linkAction.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkAction_LinkClicked);
            // 
            // picPointLeft
            // 
            this.picPointLeft.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.picPointLeft.BackColor = System.Drawing.Color.Transparent;
            this.picPointLeft.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.picPointLeft.Image = global::BitChatApp.Properties.Resources.point_left;
            this.picPointLeft.Location = new System.Drawing.Point(4, 74);
            this.picPointLeft.Name = "picPointLeft";
            this.picPointLeft.Size = new System.Drawing.Size(16, 16);
            this.picPointLeft.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.picPointLeft.TabIndex = 7;
            this.picPointLeft.TabStop = false;
            // 
            // picPointRight
            // 
            this.picPointRight.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.picPointRight.BackColor = System.Drawing.Color.Transparent;
            this.picPointRight.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.picPointRight.Image = global::BitChatApp.Properties.Resources.point_right;
            this.picPointRight.Location = new System.Drawing.Point(580, 74);
            this.picPointRight.Name = "picPointRight";
            this.picPointRight.Size = new System.Drawing.Size(16, 16);
            this.picPointRight.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.picPointRight.TabIndex = 8;
            this.picPointRight.TabStop = false;
            this.picPointRight.Visible = false;
            // 
            // ChatMessageFileItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.BackColor = System.Drawing.Color.Transparent;
            this.Controls.Add(this.picPointRight);
            this.Controls.Add(this.picPointLeft);
            this.Controls.Add(this.pnlBubble);
            this.Name = "ChatMessageFileItem";
            this.Size = new System.Drawing.Size(600, 98);
            this.pnlBubble.ResumeLayout(false);
            this.pnlBubble.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picPointLeft)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picPointRight)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblFileName;
        private System.Windows.Forms.Label lblFileSize;
        private System.Windows.Forms.Label lblContentType;
        private System.Windows.Forms.Panel pnlBubble;
        private System.Windows.Forms.LinkLabel linkAction;
        private System.Windows.Forms.PictureBox picPointLeft;
        private System.Windows.Forms.Label lblUsername;
        private System.Windows.Forms.PictureBox picPointRight;
        private System.Windows.Forms.Label lblDateTime;
        private System.Windows.Forms.ToolTip toolTip1;
    }
}
