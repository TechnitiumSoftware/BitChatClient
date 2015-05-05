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
            ((System.ComponentModel.ISupportInitialize)(this.picNetwork)).BeginInit();
            this.SuspendLayout();
            // 
            // labEmail
            // 
            this.labEmail.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labEmail.AutoEllipsis = true;
            this.labEmail.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labEmail.ForeColor = System.Drawing.Color.Black;
            this.labEmail.Location = new System.Drawing.Point(36, 20);
            this.labEmail.Name = "labEmail";
            this.labEmail.Size = new System.Drawing.Size(159, 15);
            this.labEmail.TabIndex = 2;
            this.labEmail.Text = "shreyas@technitium.com";
            this.labEmail.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // labName
            // 
            this.labName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labName.AutoEllipsis = true;
            this.labName.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labName.ForeColor = System.Drawing.Color.Black;
            this.labName.Location = new System.Drawing.Point(36, 3);
            this.labName.Name = "labName";
            this.labName.Size = new System.Drawing.Size(159, 17);
            this.labName.TabIndex = 1;
            this.labName.Text = "Shreyas Zare";
            this.labName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // labIcon
            // 
            this.labIcon.BackColor = System.Drawing.Color.Gray;
            this.labIcon.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIcon.ForeColor = System.Drawing.Color.White;
            this.labIcon.Location = new System.Drawing.Point(3, 3);
            this.labIcon.Name = "labIcon";
            this.labIcon.Size = new System.Drawing.Size(32, 32);
            this.labIcon.TabIndex = 0;
            this.labIcon.Text = "SZ";
            this.labIcon.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // picNetwork
            // 
            this.picNetwork.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.picNetwork.Image = global::BitChatAppMono.Properties.Resources.NoNetwork;
            this.picNetwork.Location = new System.Drawing.Point(194, 6);
            this.picNetwork.Name = "picNetwork";
            this.picNetwork.Size = new System.Drawing.Size(24, 24);
            this.picNetwork.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.picNetwork.TabIndex = 3;
            this.picNetwork.TabStop = false;
            // 
            // UserListItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.BackColor = System.Drawing.Color.White;
            this.Controls.Add(this.picNetwork);
            this.Controls.Add(this.labEmail);
            this.Controls.Add(this.labName);
            this.Controls.Add(this.labIcon);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "UserListItem";
            this.Size = new System.Drawing.Size(220, 39);
            ((System.ComponentModel.ISupportInitialize)(this.picNetwork)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label labIcon;
        private System.Windows.Forms.Label labName;
        private System.Windows.Forms.Label labEmail;
        private System.Windows.Forms.PictureBox picNetwork;
    }
}
