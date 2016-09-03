namespace BitChatApp
{
    partial class frmViewCertificate
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmViewCertificate));
            this.btnClose = new System.Windows.Forms.Button();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.label28 = new System.Windows.Forms.Label();
            this.labCertificateStatus = new System.Windows.Forms.Label();
            this.label24 = new System.Windows.Forms.Label();
            this.labExpiresOnDate = new System.Windows.Forms.Label();
            this.labIssuedOnDate = new System.Windows.Forms.Label();
            this.label23 = new System.Windows.Forms.Label();
            this.labIssuedByLocation = new System.Windows.Forms.Label();
            this.labIssuedByEmail = new System.Windows.Forms.Label();
            this.labIssuedByName = new System.Windows.Forms.Label();
            this.label22 = new System.Windows.Forms.Label();
            this.labIssuedToLocation = new System.Windows.Forms.Label();
            this.labIssuedToEmail = new System.Windows.Forms.Label();
            this.labIssuedToName = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.txtCertValue = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.lstCertFields = new System.Windows.Forms.ListBox();
            this.label2 = new System.Windows.Forms.Label();
            this.lnkViewIssuerCert = new System.Windows.Forms.LinkLabel();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            this.tabPage2.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnClose.Location = new System.Drawing.Point(268, 415);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(75, 23);
            this.btnClose.TabIndex = 2;
            this.btnClose.Text = "&Close";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Location = new System.Drawing.Point(12, 12);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(331, 399);
            this.tabControl1.TabIndex = 5;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.label28);
            this.tabPage1.Controls.Add(this.labCertificateStatus);
            this.tabPage1.Controls.Add(this.label24);
            this.tabPage1.Controls.Add(this.labExpiresOnDate);
            this.tabPage1.Controls.Add(this.labIssuedOnDate);
            this.tabPage1.Controls.Add(this.label23);
            this.tabPage1.Controls.Add(this.labIssuedByLocation);
            this.tabPage1.Controls.Add(this.labIssuedByEmail);
            this.tabPage1.Controls.Add(this.labIssuedByName);
            this.tabPage1.Controls.Add(this.label22);
            this.tabPage1.Controls.Add(this.labIssuedToLocation);
            this.tabPage1.Controls.Add(this.labIssuedToEmail);
            this.tabPage1.Controls.Add(this.labIssuedToName);
            this.tabPage1.Controls.Add(this.label15);
            this.tabPage1.Controls.Add(this.label14);
            this.tabPage1.Controls.Add(this.pictureBox2);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(323, 373);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Info";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // label28
            // 
            this.label28.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label28.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.label28.Location = new System.Drawing.Point(6, 329);
            this.label28.Name = "label28";
            this.label28.Size = new System.Drawing.Size(311, 35);
            this.label28.TabIndex = 47;
            this.label28.Text = "Information in green color indicates that it was verified by the issuer before is" +
    "suing the certificate.";
            // 
            // labCertificateStatus
            // 
            this.labCertificateStatus.AutoSize = true;
            this.labCertificateStatus.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labCertificateStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.labCertificateStatus.Location = new System.Drawing.Point(81, 43);
            this.labCertificateStatus.Name = "labCertificateStatus";
            this.labCertificateStatus.Size = new System.Drawing.Size(223, 14);
            this.labCertificateStatus.TabIndex = 46;
            this.labCertificateStatus.Text = "Certificate signature was verified to be valid.";
            // 
            // label24
            // 
            this.label24.AutoSize = true;
            this.label24.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label24.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label24.Location = new System.Drawing.Point(15, 279);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(69, 15);
            this.label24.TabIndex = 45;
            this.label24.Text = "Expires On";
            // 
            // labExpiresOnDate
            // 
            this.labExpiresOnDate.AutoSize = true;
            this.labExpiresOnDate.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labExpiresOnDate.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.labExpiresOnDate.Location = new System.Drawing.Point(37, 298);
            this.labExpiresOnDate.Name = "labExpiresOnDate";
            this.labExpiresOnDate.Size = new System.Drawing.Size(85, 14);
            this.labExpiresOnDate.TabIndex = 44;
            this.labExpiresOnDate.Text = "Expires On Date";
            // 
            // labIssuedOnDate
            // 
            this.labIssuedOnDate.AutoSize = true;
            this.labIssuedOnDate.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIssuedOnDate.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.labIssuedOnDate.Location = new System.Drawing.Point(37, 255);
            this.labIssuedOnDate.Name = "labIssuedOnDate";
            this.labIssuedOnDate.Size = new System.Drawing.Size(81, 14);
            this.labIssuedOnDate.TabIndex = 43;
            this.labIssuedOnDate.Text = "Issued On Date";
            // 
            // label23
            // 
            this.label23.AutoSize = true;
            this.label23.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label23.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label23.Location = new System.Drawing.Point(15, 237);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(64, 15);
            this.label23.TabIndex = 42;
            this.label23.Text = "Issued On";
            // 
            // labIssuedByLocation
            // 
            this.labIssuedByLocation.AutoEllipsis = true;
            this.labIssuedByLocation.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIssuedByLocation.ForeColor = System.Drawing.Color.Green;
            this.labIssuedByLocation.Location = new System.Drawing.Point(37, 212);
            this.labIssuedByLocation.Name = "labIssuedByLocation";
            this.labIssuedByLocation.Size = new System.Drawing.Size(267, 15);
            this.labIssuedByLocation.TabIndex = 41;
            this.labIssuedByLocation.Text = "Issued By Location";
            // 
            // labIssuedByEmail
            // 
            this.labIssuedByEmail.AutoEllipsis = true;
            this.labIssuedByEmail.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIssuedByEmail.ForeColor = System.Drawing.Color.Green;
            this.labIssuedByEmail.Location = new System.Drawing.Point(37, 195);
            this.labIssuedByEmail.Name = "labIssuedByEmail";
            this.labIssuedByEmail.Size = new System.Drawing.Size(267, 15);
            this.labIssuedByEmail.TabIndex = 40;
            this.labIssuedByEmail.Text = "ca@example.com";
            // 
            // labIssuedByName
            // 
            this.labIssuedByName.AutoEllipsis = true;
            this.labIssuedByName.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIssuedByName.ForeColor = System.Drawing.Color.Green;
            this.labIssuedByName.Location = new System.Drawing.Point(37, 178);
            this.labIssuedByName.Name = "labIssuedByName";
            this.labIssuedByName.Size = new System.Drawing.Size(267, 15);
            this.labIssuedByName.TabIndex = 39;
            this.labIssuedByName.Text = "Issued By Name";
            // 
            // label22
            // 
            this.label22.AutoSize = true;
            this.label22.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label22.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label22.Location = new System.Drawing.Point(15, 158);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(62, 15);
            this.label22.TabIndex = 38;
            this.label22.Text = "Issued By";
            // 
            // labIssuedToLocation
            // 
            this.labIssuedToLocation.AutoEllipsis = true;
            this.labIssuedToLocation.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIssuedToLocation.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.labIssuedToLocation.Location = new System.Drawing.Point(37, 130);
            this.labIssuedToLocation.Name = "labIssuedToLocation";
            this.labIssuedToLocation.Size = new System.Drawing.Size(267, 15);
            this.labIssuedToLocation.TabIndex = 37;
            this.labIssuedToLocation.Text = "Issued To Location";
            // 
            // labIssuedToEmail
            // 
            this.labIssuedToEmail.AutoEllipsis = true;
            this.labIssuedToEmail.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIssuedToEmail.ForeColor = System.Drawing.Color.Green;
            this.labIssuedToEmail.Location = new System.Drawing.Point(37, 113);
            this.labIssuedToEmail.Name = "labIssuedToEmail";
            this.labIssuedToEmail.Size = new System.Drawing.Size(267, 15);
            this.labIssuedToEmail.TabIndex = 36;
            this.labIssuedToEmail.Text = "email@example.com";
            // 
            // labIssuedToName
            // 
            this.labIssuedToName.AutoEllipsis = true;
            this.labIssuedToName.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labIssuedToName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.labIssuedToName.Location = new System.Drawing.Point(37, 96);
            this.labIssuedToName.Name = "labIssuedToName";
            this.labIssuedToName.Size = new System.Drawing.Size(267, 17);
            this.labIssuedToName.TabIndex = 35;
            this.labIssuedToName.Text = "Issued To Name";
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label15.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label15.Location = new System.Drawing.Point(15, 76);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(61, 15);
            this.label15.TabIndex = 34;
            this.label15.Text = "Issued To";
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Font = new System.Drawing.Font("Arial", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label14.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(57)))), ((int)(((byte)(69)))));
            this.label14.Location = new System.Drawing.Point(81, 22);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(165, 16);
            this.label14.TabIndex = 33;
            this.label14.Text = "Certificate Information";
            // 
            // pictureBox2
            // 
            this.pictureBox2.BackColor = System.Drawing.Color.White;
            this.pictureBox2.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox2.Image")));
            this.pictureBox2.Location = new System.Drawing.Point(18, 17);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(48, 48);
            this.pictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.pictureBox2.TabIndex = 32;
            this.pictureBox2.TabStop = false;
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.txtCertValue);
            this.tabPage2.Controls.Add(this.label3);
            this.tabPage2.Controls.Add(this.lstCertFields);
            this.tabPage2.Controls.Add(this.label2);
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(323, 373);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Details";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // txtCertValue
            // 
            this.txtCertValue.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtCertValue.Font = new System.Drawing.Font("Courier New", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtCertValue.Location = new System.Drawing.Point(6, 155);
            this.txtCertValue.Multiline = true;
            this.txtCertValue.Name = "txtCertValue";
            this.txtCertValue.ReadOnly = true;
            this.txtCertValue.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtCertValue.Size = new System.Drawing.Size(311, 212);
            this.txtCertValue.TabIndex = 3;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(6, 138);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(37, 14);
            this.label3.TabIndex = 2;
            this.label3.Text = "Value";
            // 
            // lstCertFields
            // 
            this.lstCertFields.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstCertFields.FormattingEnabled = true;
            this.lstCertFields.Items.AddRange(new object[] {
            "Version",
            "Certificate Type",
            "Serial Number",
            "Issued To",
            "Certificate Capability",
            "Issued On",
            "Expires On",
            "Public Key Encryption Algorithm",
            "Public Key",
            "Certificate Revocation URI",
            "Signature Hash Algorithm",
            "Signature Algorithm",
            "Signature",
            "Issued By"});
            this.lstCertFields.Location = new System.Drawing.Point(6, 23);
            this.lstCertFields.Name = "lstCertFields";
            this.lstCertFields.Size = new System.Drawing.Size(311, 108);
            this.lstCertFields.TabIndex = 1;
            this.lstCertFields.SelectedIndexChanged += new System.EventHandler(this.lstCertFields_SelectedIndexChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Arial", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(3, 6);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(100, 14);
            this.label2.TabIndex = 0;
            this.label2.Text = "Certificate Fields";
            // 
            // lnkViewIssuerCert
            // 
            this.lnkViewIssuerCert.AutoSize = true;
            this.lnkViewIssuerCert.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(153)))), ((int)(((byte)(255)))));
            this.lnkViewIssuerCert.Location = new System.Drawing.Point(12, 420);
            this.lnkViewIssuerCert.Name = "lnkViewIssuerCert";
            this.lnkViewIssuerCert.Size = new System.Drawing.Size(111, 13);
            this.lnkViewIssuerCert.TabIndex = 48;
            this.lnkViewIssuerCert.TabStop = true;
            this.lnkViewIssuerCert.Text = "View Issuer Certificate";
            this.lnkViewIssuerCert.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.lnkViewIssuerCert_LinkClicked);
            // 
            // frmViewCertificate
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(250)))), ((int)(((byte)(250)))));
            this.CancelButton = this.btnClose;
            this.ClientSize = new System.Drawing.Size(354, 443);
            this.Controls.Add(this.lnkViewIssuerCert);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.btnClose);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "frmViewCertificate";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Certificate Viewer";
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.PictureBox pictureBox2;
        private System.Windows.Forms.Label labIssuedByLocation;
        private System.Windows.Forms.Label labIssuedByEmail;
        private System.Windows.Forms.Label labIssuedByName;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.Label labIssuedToLocation;
        private System.Windows.Forms.Label labIssuedToEmail;
        private System.Windows.Forms.Label labIssuedToName;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label labCertificateStatus;
        private System.Windows.Forms.Label label24;
        private System.Windows.Forms.Label labExpiresOnDate;
        private System.Windows.Forms.Label labIssuedOnDate;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.Label label28;
        private System.Windows.Forms.TextBox txtCertValue;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ListBox lstCertFields;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.LinkLabel lnkViewIssuerCert;


    }
}