namespace BitChatAppMono.UserControls
{
    partial class UserListItem
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
            this.labEmail = new System.Windows.Forms.Label();
            this.labName = new System.Windows.Forms.Label();
            this.labIcon = new System.Windows.Forms.Label();
            this.picNetwork = new System.Windows.Forms.PictureBox();
            this.picIcon = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.picNetwork)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // labEmail
            // 
            this.labEmail.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labEmail.AutoEllipsis = true;
            this.labEmail.Font = new System.Drawing.Font("Arial", 9F);
            this.labEmail.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.labEmail.Location = new System.Drawing.Point(54, 31);
            this.labEmail.Name = "labEmail";
            this.labEmail.Size = new System.Drawing.Size(163, 15);
            this.labEmail.TabIndex = 2;
            this.labEmail.Text = "shreyas@technitium.com";
            this.labEmail.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // labName
            // 
            this.labName.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labName.AutoEllipsis = true;
            this.labName.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.labName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.labName.Location = new System.Drawing.Point(53, 7);
            this.labName.Name = "labName";
            this.labName.Size = new System.Drawing.Size(138, 22);
            this.labName.TabIndex = 1;
            this.labName.Text = "Shreyas Zare";
            this.labName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // labIcon
            // 
            this.labIcon.BackColor = System.Drawing.Color.Gray;
            this.labIcon.Font = new System.Drawing.Font("Arial", 16F, System.Drawing.FontStyle.Bold);
            this.labIcon.ForeColor = System.Drawing.Color.White;
            this.labIcon.Location = new System.Drawing.Point(3, 3);
            this.labIcon.Name = "labIcon";
            this.labIcon.Size = new System.Drawing.Size(48, 48);
            this.labIcon.TabIndex = 0;
            this.labIcon.Text = "SZ";
            this.labIcon.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // picNetwork
            // 
            this.picNetwork.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.picNetwork.Image = global::BitChatAppMono.Properties.Resources.NoNetwork;
            this.picNetwork.Location = new System.Drawing.Point(194, 4);
            this.picNetwork.Name = "picNetwork";
            this.picNetwork.Size = new System.Drawing.Size(24, 24);
            this.picNetwork.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.picNetwork.TabIndex = 3;
            this.picNetwork.TabStop = false;
            // 
            // picIcon
            // 
            this.picIcon.Location = new System.Drawing.Point(3, 3);
            this.picIcon.Name = "picIcon";
            this.picIcon.Size = new System.Drawing.Size(48, 48);
            this.picIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picIcon.TabIndex = 5;
            this.picIcon.TabStop = false;
            this.picIcon.Visible = false;
            // 
            // UserListItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.BackColor = System.Drawing.Color.White;
            this.Controls.Add(this.picNetwork);
            this.Controls.Add(this.labEmail);
            this.Controls.Add(this.labName);
            this.Controls.Add(this.labIcon);
            this.Controls.Add(this.picIcon);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "UserListItem";
            this.Size = new System.Drawing.Size(220, 55);
            ((System.ComponentModel.ISupportInitialize)(this.picNetwork)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picIcon)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label labIcon;
        private System.Windows.Forms.Label labName;
        private System.Windows.Forms.Label labEmail;
        private System.Windows.Forms.PictureBox picNetwork;
        private System.Windows.Forms.PictureBox picIcon;
    }
}
