using System;
using System.Net;
using System.Windows.Forms;
using TechnitiumLibrary.Net.Proxy;

namespace BitChatApp
{
    public partial class frmSocksProxyConfig : Form
    {

        IPAddress _proxyIP;
        ushort _proxyPort = 0;

        public frmSocksProxyConfig()
        {
            InitializeComponent();
        }

        public frmSocksProxyConfig(IPEndPoint proxyEP, NetworkCredential proxyCredentials)
        {
            InitializeComponent();

            if (proxyEP != null)
            {
                txtProxyIP.Text = proxyEP.Address.ToString();
                txtProxyPort.Text = proxyEP.Port.ToString();
            }

            if (proxyCredentials != null)
            {
                chkProxyAuth.Checked = true;
                txtProxyUser.Text = proxyCredentials.UserName;
                txtProxyPass.Text = proxyCredentials.Password;
            }
        }

        private void chkProxyAuth_CheckedChanged(object sender, EventArgs e)
        {
            txtProxyUser.Enabled = chkProxyAuth.Checked;
            txtProxyPass.Enabled = chkProxyAuth.Checked;
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (!IPAddress.TryParse(txtProxyIP.Text, out _proxyIP))
            {
                MessageBox.Show("The proxy IP address specified is invalid.", "Invalid Proxy IP Address Specified!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!ushort.TryParse(txtProxyPort.Text, out _proxyPort))
            {
                MessageBox.Show("The proxy port number specified is invalid. The number must be in 0-65535 range.", "Invalid Proxy Port Specified!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (chkProxyAuth.Checked)
            {
                if (string.IsNullOrWhiteSpace(txtProxyUser.Text))
                {
                    MessageBox.Show("The proxy username is missing. Please enter a username.", "Proxy Username Missing!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        public IPEndPoint ProxyEndPoint
        { get { return new IPEndPoint(_proxyIP, _proxyPort); } }

        public NetworkCredential ProxyCredentials
        {
            get
            {
                if (chkProxyAuth.Checked)
                    return new NetworkCredential(txtProxyUser.Text, txtProxyPass.Text);
                else
                    return null;
            }
        }

        private void btnCheckProxy_Click(object sender, EventArgs e)
        {
            try
            {
                NetworkCredential credentials = null;

                if (chkProxyAuth.Checked)
                    credentials = new NetworkCredential(txtProxyUser.Text, txtProxyPass.Text);

                SocksClient proxy = new SocksClient(new IPEndPoint(IPAddress.Parse(txtProxyIP.Text), int.Parse(txtProxyPort.Text)), credentials);

                proxy.CheckProxyAccess();

                MessageBox.Show("Proxy check was successful. Bit Chat was able to connect to the proxy server successfully.", "Proxy Check Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Proxy Check Failed!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
