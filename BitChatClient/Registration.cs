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

using System;
using System.IO;
using System.Net;
using System.Text;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient
{
    public static class Registration
    {
        private static string GetUserAgent()
        {
            OperatingSystem OS = Environment.OSVersion;

            string operatingSystem;

            switch (OS.Platform)
            {
                case PlatformID.Win32NT:
                    operatingSystem = "Windows NT";
                    break;

                default:
                    operatingSystem = OS.Platform.ToString();
                    break;
            }

            operatingSystem += " " + OS.Version.Major + "." + OS.Version.Minor;

            return "Mozilla/5.0 (" + operatingSystem + ")";
        }

        public static void Register(Uri apiUri, Certificate selfSignedCert)
        {
            selfSignedCert.Verify(new Certificate[] { selfSignedCert });

            using (WebClient client = new WebClient())
            {
                client.Headers.Add("User-Agent", GetUserAgent());

                byte[] data = client.UploadData(apiUri.AbsoluteUri + "?cmd=reg", selfSignedCert.ToArray());

                using (BinaryReader bR = new BinaryReader(new MemoryStream(data)))
                {
                    int errorCode = bR.ReadInt32();
                    if (errorCode != 0)
                    {
                        string message = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));
                        string remoteStackTrace = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));

                        throw new Exception(message);
                    }
                }
            }
        }

        public static Certificate GetSignedCertificate(Uri apiUri, CertificateStore certStore)
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("User-Agent", GetUserAgent());

                byte[] data = client.DownloadData(apiUri.AbsoluteUri + "?cmd=dlc&email=" + certStore.Certificate.IssuedTo.EmailAddress.Address);

                using (BinaryReader bR = new BinaryReader(new MemoryStream(data)))
                {
                    int errorCode = bR.ReadInt32();
                    if (errorCode != 0)
                    {
                        string message = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));
                        string remoteStackTrace = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));

                        throw new BitChatException(message);
                    }

                    Certificate cert = new Certificate(bR);

                    if (!cert.IssuedTo.EmailAddress.Equals(certStore.Certificate.IssuedTo.EmailAddress) || (cert.PublicKeyEncryptionAlgorithm != certStore.PrivateKey.Algorithm) || (cert.PublicKeyXML != certStore.PrivateKey.GetPublicKey()))
                        throw new BitChatException("Invalid signed certificate received. Please try again.");

                    return cert;
                }
            }
        }
    }
}
