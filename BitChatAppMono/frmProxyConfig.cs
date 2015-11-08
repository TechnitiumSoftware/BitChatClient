using BitChatClient;
using System;
using System.Net;
using System.Windows.Forms;
using TechnitiumLibrary.Net.Proxy;

namespace BitChatAppMono
{
    public partial class frmProxyConfig : Form
    {

        string _proxyAddress;
        ushort _proxyPort = 0;

        public frmProxyConfig()
        {
            InitializeComponent();
        }

        public frmProxyConfig(NetProxyType proxyType, string proxyAddress, int proxyPort, NetworkCredential proxyCredentials)
        {
            InitializeComponent();

            cmbProxy.SelectedIndex = (int)proxyType;
            txtProxyAddress.Text = proxyAddress;
            txtProxyPort.Text = proxyPort.ToString();

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

        private void cmbProxy_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnCheckProxy.Enabled = (cmbProxy.SelectedIndex != 0);
            txtProxyAddress.Enabled = btnCheckProxy.Enabled;
            txtProxyPort.Enabled = btnCheckProxy.Enabled;
            chkProxyAuth.Enabled = btnCheckProxy.Enabled;
            txtProxyUser.Enabled = chkProxyAuth.Enabled && chkProxyAuth.Checked;
            txtProxyPass.Enabled = chkProxyAuth.Enabled && chkProxyAuth.Checked;
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if ((cmbProxy.SelectedIndex != 0) && (string.IsNullOrWhiteSpace(txtProxyAddress.Text)))
            {
                MessageBox.Show("The proxy address is missing. Please enter a valid proxy address.", "Proxy Address Missing!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _proxyAddress = txtProxyAddress.Text;

            if (!ushort.TryParse(txtProxyPort.Text, out _proxyPort))
            {
                MessageBox.Show("The proxy port number specified is invalid. The number must be in 0-65535 range.", "Invalid Proxy Port Specified!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if ((chkProxyAuth.Checked) && (string.IsNullOrWhiteSpace(txtProxyUser.Text)))
            {
                MessageBox.Show("The proxy username is missing. Please enter a username.", "Proxy Username Missing!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }

        private void btnCheckProxy_Click(object sender, EventArgs e)
        {
            try
            {
                NetProxyType proxyType = (NetProxyType)cmbProxy.SelectedIndex;
                NetProxy proxy;
                NetworkCredential credentials = null;

                if (chkProxyAuth.Checked)
                    credentials = new NetworkCredential(txtProxyUser.Text, txtProxyPass.Text);

                switch (proxyType)
                {
                    case NetProxyType.Http:
                        proxy = new NetProxy(new WebProxyEx(new Uri("http://" + txtProxyAddress.Text + ":" + int.Parse(txtProxyPort.Text)), false, new string[] { }, credentials));
                        break;

                    case NetProxyType.Socks5:
                        proxy = new NetProxy(new SocksClient(txtProxyAddress.Text, int.Parse(txtProxyPort.Text), credentials));
                        break;

                    default:
                        return;
                }

                proxy.CheckProxyAccess();

                MessageBox.Show("Bit Chat was able to connect to the proxy server successfully.", "Proxy Check Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Proxy Check Failed!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public NetProxyType ProxyType
        { get { return (NetProxyType)cmbProxy.SelectedIndex; } }

        public string ProxyAddress
        { get { return _proxyAddress; } }

        public int ProxyPort
        { get { return _proxyPort; } }

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
    }
}
