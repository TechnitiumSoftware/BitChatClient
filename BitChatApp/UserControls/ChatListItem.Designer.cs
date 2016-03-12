namespace BitChatApp.UserControls
{
    partial class ChatListItem
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
            this.labIcon = new System.Windows.Forms.Label();
            this.labTitle = new System.Windows.Forms.Label();
            this.labLastMessage = new System.Windows.Forms.Label();
            this.labNewMessageCount = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // labIcon
            // 
            this.labIcon.BackColor = System.Drawing.Color.White;
            this.labIcon.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIcon.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(78)))));
            this.labIcon.Location = new System.Drawing.Point(3, 4);
            this.labIcon.Name = "labIcon";
            this.labIcon.Size = new System.Drawing.Size(32, 32);
            this.labIcon.TabIndex = 0;
            this.labIcon.Text = "TO";
            this.labIcon.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // labTitle
            // 
            this.labTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labTitle.AutoEllipsis = true;
            this.labTitle.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labTitle.ForeColor = System.Drawing.Color.White;
            this.labTitle.Location = new System.Drawing.Point(38, 4);
            this.labTitle.Name = "labTitle";
            this.labTitle.Size = new System.Drawing.Size(168, 17);
            this.labTitle.TabIndex = 1;
            this.labTitle.Text = "Title Of Bit Chat";
            this.labTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // labLastMessage
            // 
            this.labLastMessage.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labLastMessage.AutoEllipsis = true;
            this.labLastMessage.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labLastMessage.ForeColor = System.Drawing.Color.White;
            this.labLastMessage.Location = new System.Drawing.Point(38, 22);
            this.labLastMessage.Name = "labLastMessage";
            this.labLastMessage.Size = new System.Drawing.Size(199, 14);
            this.labLastMessage.TabIndex = 2;
            this.labLastMessage.Text = "Sender: new chat message";
            // 
            // labNewMessageCount
            // 
            this.labNewMessageCount.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.labNewMessageCount.AutoEllipsis = true;
            this.labNewMessageCount.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(227)))), ((int)(((byte)(71)))), ((int)(((byte)(36)))));
            this.labNewMessageCount.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labNewMessageCount.ForeColor = System.Drawing.Color.White;
            this.labNewMessageCount.Location = new System.Drawing.Point(210, 3);
            this.labNewMessageCount.Name = "labNewMessageCount";
            this.labNewMessageCount.Size = new System.Drawing.Size(28, 17);
            this.labNewMessageCount.TabIndex = 3;
            this.labNewMessageCount.Text = "999";
            this.labNewMessageCount.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ChatListItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(78)))));
            this.Controls.Add(this.labNewMessageCount);
            this.Controls.Add(this.labLastMessage);
            this.Controls.Add(this.labTitle);
            this.Controls.Add(this.labIcon);
            this.Name = "ChatListItem";
            this.SeparatorColor = System.Drawing.Color.FromArgb(((int)(((byte)(236)))), ((int)(((byte)(237)))), ((int)(((byte)(238)))));
            this.Size = new System.Drawing.Size(240, 40);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label labIcon;
        private System.Windows.Forms.Label labTitle;
        private System.Windows.Forms.Label labLastMessage;
        private System.Windows.Forms.Label labNewMessageCount;
    }
}
