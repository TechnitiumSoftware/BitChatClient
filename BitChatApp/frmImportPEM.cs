using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatApp
{
    public partial class frmImportPEM : Form
    {
        #region variables

        RSAParameters _parameters;

        #endregion

        #region form code

        public frmImportPEM()
        {
            InitializeComponent();
        }

        private void txtRSAKey_TextChanged(object sender, EventArgs e)
        {
            if (!txtRSAKey.Text.Contains("\r\n"))
                txtRSAKey.Text = txtRSAKey.Text.Replace("\n", "\r\n");
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            try
            {
                using (MemoryStream mS = new MemoryStream(Encoding.UTF8.GetBytes(txtRSAKey.Text)))
                {
                    _parameters = PEMFormat.ReadRSAPrivateKey(mS);
                }

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.ImportParameters(_parameters);

                    if (rsa.KeySize < 4096)
                    {
                        MessageBox.Show("The RSA private key must be at least 4096-bit. The current key is " + rsa.KeySize + "-bit.", "Short RSA Private Key", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                this.DialogResult = System.Windows.Forms.DialogResult.OK;
                this.Close();
            }
            catch
            {
                MessageBox.Show("Error in reading PEM format. Please make sure you have pasted the RSA private key in a proper PEM format.", "Invalid PEM Format", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion

        #region properties

        public RSAParameters Parameters
        { get { return _parameters; } }

        #endregion
    }
}
