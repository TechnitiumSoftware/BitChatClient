/*
Technitium Bit Chat
Copyright (C) 2015  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using TechnitiumLibrary.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace BitChatAppMono
{
    public partial class frmViewCertificate : Form
    {
        #region variables

        Certificate _cert;

        #endregion

        #region constructor

        public frmViewCertificate()
        {
            InitializeComponent();
        }

        public frmViewCertificate(Certificate cert)
        {
            InitializeComponent();

            _cert = cert;

            #region issued to data

            //name
            if (_cert.IssuedTo.FieldExists(CertificateProfileFlags.Name))
            {
                labIssuedToName.Text = _cert.IssuedTo.Name;

                if (_cert.IssuedTo.IsFieldVerified(CertificateProfileFlags.Name))
                    labIssuedToName.ForeColor = Color.Green;
                else
                    labIssuedToName.ForeColor = Color.FromArgb(64, 64, 64);
            }
            else
            {
                labIssuedToName.Text = "{missing name}";
                labIssuedToName.ForeColor = Color.Red;
            }

            //email
            if (_cert.IssuedTo.FieldExists(CertificateProfileFlags.EmailAddress))
            {
                labIssuedToEmail.Text = _cert.IssuedTo.EmailAddress.Address;

                if (_cert.IssuedTo.IsFieldVerified(CertificateProfileFlags.EmailAddress))
                    labIssuedToEmail.ForeColor = Color.Green;
                else
                    labIssuedToEmail.ForeColor = Color.FromArgb(64, 64, 64);
            }
            else
            {
                labIssuedToEmail.Text = "{missing email address}";
                labIssuedToEmail.ForeColor = Color.Red;
            }

            //location
            if (_cert.IssuedTo.FieldExists(CertificateProfileFlags.City | CertificateProfileFlags.State | CertificateProfileFlags.Country))
            {
                labIssuedToLocation.Text = "";

                if (_cert.IssuedTo.FieldExists(CertificateProfileFlags.City))
                    labIssuedToLocation.Text = _cert.IssuedTo.City + ", ";

                if (_cert.IssuedTo.FieldExists(CertificateProfileFlags.State))
                    labIssuedToLocation.Text += _cert.IssuedTo.State + ", ";

                if (_cert.IssuedTo.FieldExists(CertificateProfileFlags.Country))
                    labIssuedToLocation.Text += _cert.IssuedTo.Country;

                if (_cert.IssuedTo.IsFieldVerified(CertificateProfileFlags.City | CertificateProfileFlags.State | CertificateProfileFlags.Country))
                    labIssuedToLocation.ForeColor = Color.Green;
                else
                    labIssuedToLocation.ForeColor = Color.FromArgb(64, 64, 64);
            }
            else
            {
                labIssuedToLocation.Text = "{missing location}";
                labIssuedToLocation.ForeColor = Color.Red;
            }

            #endregion

            #region issued by data

            CertificateProfile issuedBy;

            if (_cert.Type == CertificateType.RootCA)
            {
                issuedBy = _cert.IssuedTo;
                lnkViewIssuerCert.Visible = false;
            }
            else
                issuedBy = _cert.IssuerSignature.SigningCertificate.IssuedTo;

            //name
            if (issuedBy.FieldExists(CertificateProfileFlags.Name))
            {
                labIssuedByName.Text = issuedBy.Name;

                if (issuedBy.IsFieldVerified(CertificateProfileFlags.Name))
                    labIssuedByName.ForeColor = Color.Green;
                else
                    labIssuedByName.ForeColor = Color.FromArgb(64, 64, 64);
            }
            else
            {
                labIssuedByName.Text = "{missing name}";
                labIssuedByName.ForeColor = Color.Red;
            }

            //email
            if (issuedBy.FieldExists(CertificateProfileFlags.EmailAddress))
            {
                labIssuedByEmail.Text = issuedBy.EmailAddress.Address;

                if (issuedBy.IsFieldVerified(CertificateProfileFlags.EmailAddress))
                    labIssuedByEmail.ForeColor = Color.Green;
                else
                    labIssuedByEmail.ForeColor = Color.FromArgb(64, 64, 64);
            }
            else
            {
                labIssuedByEmail.Text = "{missing email address}";
                labIssuedByEmail.ForeColor = Color.Red;
            }

            //location
            if (issuedBy.FieldExists(CertificateProfileFlags.City | CertificateProfileFlags.State | CertificateProfileFlags.Country))
            {
                labIssuedByLocation.Text = "";

                if (issuedBy.FieldExists(CertificateProfileFlags.City))
                    labIssuedByLocation.Text = issuedBy.City + ", ";

                if (issuedBy.FieldExists(CertificateProfileFlags.State))
                    labIssuedByLocation.Text += issuedBy.State + ", ";

                if (issuedBy.FieldExists(CertificateProfileFlags.Country))
                    labIssuedByLocation.Text += issuedBy.Country;

                if (issuedBy.IsFieldVerified(CertificateProfileFlags.City | CertificateProfileFlags.State | CertificateProfileFlags.Country))
                    labIssuedByLocation.ForeColor = Color.Green;
                else
                    labIssuedByLocation.ForeColor = Color.FromArgb(64, 64, 64);
            }
            else
            {
                labIssuedByLocation.Text = "{missing location}";
                labIssuedByLocation.ForeColor = Color.Red;
            }

            #endregion

            #region issued dates

            labIssuedOnDate.Text = _cert.IssuedOnUTC.ToString("dd MMM, yyyy HH:mm:ss") + " UTC";
            labExpiresOnDate.Text = _cert.ExpiresOnUTC.ToString("dd MMM, yyyy HH:mm:ss") + " UTC";

            #endregion
        }

        #endregion

        #region private

        private void lstCertFields_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (lstCertFields.SelectedItem as string)
            {
                case "Version":
                    txtCertValue.Text = _cert.Version.ToString();
                    break;

                case "Certificate Type":
                    txtCertValue.Text = _cert.Type.ToString();
                    break;

                case "Serial Number":
                    txtCertValue.Text = _cert.SerialNumber;
                    break;

                case "Issued To":
                    txtCertValue.Text = _cert.IssuedTo.ToString();
                    break;

                case "Certificate Capability":
                    txtCertValue.Text = _cert.Capability.ToString();
                    break;

                case "Issued On":
                    txtCertValue.Text = _cert.IssuedOnUTC.ToString("dd MMM, yyyy HH:mm:ss") + " UTC";
                    break;

                case "Expires On":
                    txtCertValue.Text = _cert.ExpiresOnUTC.ToString("dd MMM, yyyy HH:mm:ss") + " UTC";
                    break;

                case "Public Key Encryption Algorithm":
                    txtCertValue.Text = _cert.PublicKeyEncryptionAlgorithm.ToString();
                    break;

                case "Public Key":
                    txtCertValue.Text = _cert.PublicKeyXML;
                    break;

                case "Certificate Revocation URI":
                    if (_cert.RevocationURL == null)
                        txtCertValue.Text = "not set";
                    else
                        txtCertValue.Text = _cert.RevocationURL.AbsoluteUri;
                    break;

                case "Signature Hash Algorithm":
                    txtCertValue.Text = _cert.IssuerSignature.HashAlgorithm;
                    break;

                case "Signature Algorithm":
                    txtCertValue.Text = _cert.IssuerSignature.SignatureAlgorithm.ToString();
                    break;

                case "Signature":
                    txtCertValue.Text = _cert.IssuerSignature.ToString();
                    break;

                case "Issued By":
                    if (_cert.Type == CertificateType.RootCA)
                        txtCertValue.Text = _cert.IssuedTo.ToString();
                    else
                        txtCertValue.Text = _cert.IssuerSignature.SigningCertificate.IssuedTo.ToString();

                    break;

                default:
                    txtCertValue.Text = "";
                    break;
            }
        }

        private void lnkViewIssuerCert_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (frmViewCertificate frm = new frmViewCertificate(_cert.IssuerSignature.SigningCertificate))
            {
                frm.ShowDialog(this);
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion
    }
}
