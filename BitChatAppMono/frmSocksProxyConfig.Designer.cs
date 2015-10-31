namespace BitChatAppMono
{
    partial class frmSocksProxyConfig
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
            this.txtProxyPort = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.txtProxyIP = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.chkProxyAuth = new System.Windows.Forms.CheckBox();
            this.txtProxyPass = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.txtProxyUser = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCheckProxy = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // txtProxyPort
            // 
            this.txtProxyPort.Location = new System.Drawing.Point(164, 77);
            this.txtProxyPort.MaxLength = 5;
            this.txtProxyPort.Name = "txtProxyPort";
            this.txtProxyPort.Size = new System.Drawing.Size(45, 20);
            this.txtProxyPort.TabIndex = 48;
            this.txtProxyPort.Text = "1080";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(103, 80);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(55, 13);
            this.label6.TabIndex = 50;
            this.label6.Text = "Proxy Port";
            // 
            // txtProxyIP
            // 
            this.txtProxyIP.Location = new System.Drawing.Point(164, 51);
            this.txtProxyIP.MaxLength = 15;
            this.txtProxyIP.Name = "txtProxyIP";
            this.txtProxyIP.Size = new System.Drawing.Size(90, 20);
            this.txtProxyIP.TabIndex = 47;
            this.txtProxyIP.Text = "127.0.0.1";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(84, 54);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(74, 13);
            this.label5.TabIndex = 49;
            this.label5.Text = "Proxy Address";
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(360, 32);
            this.label1.TabIndex = 51;
            this.label1.Text = "Please enter the Socks 5 proxy server details below to make Bit Chat use the spec" +
    "ified proxy server for all network communications.";
            // 
            // chkProxyAuth
            // 
            this.chkProxyAuth.AutoSize = true;
            this.chkProxyAuth.Location = new System.Drawing.Point(164, 103);
            this.chkProxyAuth.Name = "chkProxyAuth";
            this.chkProxyAuth.Size = new System.Drawing.Size(130, 17);
            this.chkProxyAuth.TabIndex = 52;
            this.chkProxyAuth.Text = "Enable Authentication";
            this.chkProxyAuth.UseVisualStyleBackColor = true;
            this.chkProxyAuth.CheckedChanged += new System.EventHandler(this.chkProxyAuth_CheckedChanged);
            // 
            // txtProxyPass
            // 
            this.txtProxyPass.Enabled = false;
            this.txtProxyPass.Location = new System.Drawing.Point(164, 152);
            this.txtProxyPass.MaxLength = 255;
            this.txtProxyPass.Name = "txtProxyPass";
            this.txtProxyPass.PasswordChar = '#';
            this.txtProxyPass.Size = new System.Drawing.Size(96, 20);
            this.txtProxyPass.TabIndex = 54;
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(105, 155);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(53, 13);
            this.label7.TabIndex = 56;
            this.label7.Text = "Password";
            // 
            // txtProxyUser
            // 
            this.txtProxyUser.Enabled = false;
            this.txtProxyUser.Location = new System.Drawing.Point(164, 126);
            this.txtProxyUser.MaxLength = 255;
            this.txtProxyUser.Name = "txtProxyUser";
            this.txtProxyUser.Size = new System.Drawing.Size(96, 20);
            this.txtProxyUser.TabIndex = 53;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(103, 129);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(55, 13);
            this.label8.TabIndex = 55;
            this.label8.Text = "Username";
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(297, 194);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 58;
            this.btnCancel.Text = "&Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnOK
            // 
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.Location = new System.Drawing.Point(216, 194);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 57;
            this.btnOK.Text = "&Enable";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCheckProxy
            // 
            this.btnCheckProxy.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnCheckProxy.Location = new System.Drawing.Point(12, 194);
            this.btnCheckProxy.Name = "btnCheckProxy";
            this.btnCheckProxy.Size = new System.Drawing.Size(75, 23);
            this.btnCheckProxy.TabIndex = 59;
            this.btnCheckProxy.Text = "Check Proxy";
            this.btnCheckProxy.UseVisualStyleBackColor = true;
            this.btnCheckProxy.Click += new System.EventHandler(this.btnCheckProxy_Click);
            // 
            // frmSocksProxyConfig
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(250)))), ((int)(((byte)(250)))));
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(384, 225);
            this.Controls.Add(this.btnCheckProxy);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.txtProxyPass);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.txtProxyUser);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.chkProxyAuth);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtProxyPort);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.txtProxyIP);
            this.Controls.Add(this.label5);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "frmSocksProxyConfig";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Configure Socks 5 Proxy";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtProxyPort;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox txtProxyIP;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox chkProxyAuth;
        private System.Windows.Forms.TextBox txtProxyPass;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox txtProxyUser;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCheckProxy;
    }
}