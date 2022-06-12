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
using System.Threading;
using System.Windows.Forms;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatApp
{
    static class Program
    {
        #region variables

        public const string MUTEX_NAME = "BitChatApp";
        public readonly static Uri SIGNUP_URI = new Uri("https://bitchat.im/api/signup.aspx");
        public readonly static Uri UPDATE_URI = new Uri("https://bitchat.im/download/linux/update.bin");
        public const int UPDATE_CHECK_INTERVAL_DAYS = 1;

        static string _rootCert = @"
Q0UBAQpUQ0EtMjAxNC0xQ1AB7wMAAM8DAAAgVGVjaG5pdGl1bSBDZXJ0aWZpY2F0ZSBBdXRob3Jp
dHkCEWNhQHRlY2huaXRpdW0uY29tFmh0dHA6Ly90ZWNobml0aXVtLmNvbS8NVmlraHJvbGkgRWFz
dAZNdW1iYWkLTWFoYXJhc2h0cmEFSW5kaWEGNDAwMDc5AXpulVQAAAAAesH8XQAAAAAB8wI8UlNB
S2V5VmFsdWU+PE1vZHVsdXM+cFBlZENzY2p5Z3Y1MDg5aDR1WGg2cnRZRUdhYklGV2xjbmZXaVJy
Ky8rZkNVR2NrYUVmVUxMVjlPaTJjSE1TaVFGVjNzaUs0N2VtSDlmUGd2WG8yYW5iWlREYW9TRVY4
WEc4STNvVkVuZG95RVZEL0JZVmVYcmQrZ2FnWWtMZmw5aEtNNVJNaUhHbDR2N2hpTWFPdkRWUUxy
ZWhOQjltcU15RzN3bk1ST3IxdSs5Tld3enFTZlR4a0VVS0ZhUGc4MlRnVi8rcFU2UEEzL0pYK3Na
MnpzZHhhcm1kSGFhTW41QTR5OWJkSm41eEUwUXlMMEkxMDFWSCtPMkE0ZmRNNVcvbXhJcjBiU2Z3
dlRjNThHR1QrdjFsR2RTWmpRZitxYXgxZG0yMzBHQy9xRTd3Y2NEK1pLaFNsTDF2VmlUMmJINXBs
TEo5cFppeEVnT3R4SnVJUTlGRUsvcDNDVEFrNHAvTFgwdmZ3TUZReUtaaUROVTlPZ3pUQW1FRFBF
VXc0eUVlUFlBV0c0QkVhbVRUUi9TZUlvZWxzTi8wU1ZURVhRb0tQeE5PZGM5QmhCZU1UTGRWVkZH
QTdSc09PRzd6eUFUeGl0RlpYeElhd21CSkpxTWFVRVdjR0t3eHAvSHFqTmJYYUFKTUZXUGdiL0sy
YmxpQlQzNzFtcWdEbElwZzY4OVg2aUdaZXBNRWVxcW1lckR4QWMzYUJNZFlEZHo2TWkzUlp1NE5P
aHVPSUErYjZWaWtGeU9tdXBscTc1THkyekN3Q1YzaDl0QkprUndFVWx3TDYyQ1FobHdUaDBRWlpE
Y3ZNREt5VDhOelVZZklpZWtGR2Q0SDJRZ3pBYWFMVUpNSExwcU5IYmpqdXdBWHFyZXJ2aVl4NHNR
Z0QzZUJLaGt0K3VTUzRnUXM9PC9Nb2R1bHVzPjxFeHBvbmVudD5BUUFCPC9FeHBvbmVudD48L1JT
QUtleVZhbHVlPiNodHRwczovL3RlY2huaXRpdW0uY29tL2NybC9jcmwuYXNweAFTTgEAAn/m6RMT
OVhQ7xyyc8NwVWbs+jA6cnPpks+yL6j+Di3BEcmT9HlDRIGVz8wBlI0qEtsJ1OsY7blYkJM8qm6s
qK4IlC6JxwzMgDQouaj7yPoMvZiHKNZnn0ikQTHln5IVgBjjnSRTpvzKpnhpRl069xB5kWLTJqIS
ozTnoccrn2pCrVPqhwcJHDZf7IBHlVn7VOfiBHm73yBDpSBvYWtkl+qojPL1P0RVtT+dhEG3HJF+
2gfLC3kXGtLyQSuOtjEsU38pSMr2/2DnOXan/5KDUEOUrW3gQ2BWKv4HK7RqLOZqUSigQIu2a5qV
w5W5oJrk7Dr2Uou0KLGaV1BofdGSKTem5Kz4gsSC3zwP8kjPmqETA7RkAPs8nL+kTi/20j5BWgIx
S2QVWrwjV4k4urVu91pyjY5P7JJOO3SN85TOQ9wdeVv0lthdd4cED7HFE+trqyfCeZD4BMKihXsZ
BF+Xh/PhxxJjLoHwsphPF7qGaltcAEYrK88zS0c1KOPdJQsbjqSJerwMT5al/OYSp7FPXn3POo+s
xN9SGwJcanbJC8cP02Bq3bxTW+GHXU+dEr1LY2eBXej5lB2RFs8gJ5uP8cmjzwWMX/Ib6aNIs6IM
4d1HijZ3z5iokvPkmO8qwYt0lSSysexs/fY9JLTmyJX4ww10amMLAg7qfYbN33Wv5qplBlNIQTI1
NgEA
";

        public readonly static Certificate[] TRUSTED_CERTIFICATES;

        static Mutex _app;

        #endregion

        #region public

        static Program()
        {
            TRUSTED_CERTIFICATES = new Certificate[1];

            using (MemoryStream mS = new MemoryStream(Convert.FromBase64String(_rootCert)))
            {
                TRUSTED_CERTIFICATES[0] = new Certificate(mS);
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                #region check for multiple instances

                bool createdNewMutex;

                _app = new Mutex(true, MUTEX_NAME, out createdNewMutex);

                if (!createdNewMutex)
                {
                    MessageBox.Show("Bit Chat is already running. Please click on the Bit Chat icon to open the chat window.", "Bit Chat Already Running!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                #endregion

                #region profile manager

                while (true)
                {
                    frmProfileManager mgr = new frmProfileManager();

                    if (mgr.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            if (mgr.Profile != null)
                            {
                                bool loadMainForm = false;

                                if ((mgr.Profile.LocalCertificateStore.Certificate.Type == CertificateType.User) && (mgr.Profile.LocalCertificateStore.Certificate.Capability == CertificateCapability.UserAuthentication))
                                {
                                    loadMainForm = true;
                                }
                                else
                                {
                                    using (frmRegister frm = new frmRegister(mgr.Profile, mgr.ProfileFilePath, mgr.IsPortableApp, mgr.ProfileFolder, false))
                                    {
                                        loadMainForm = (frm.ShowDialog() == DialogResult.OK);
                                    }
                                }

                                if (loadMainForm)
                                {
                                    using (frmMain frm = new frmMain(mgr.Profile, mgr.ProfileFilePath, string.Join(" ", args)))
                                    {
                                        Application.Run(frm);

                                        if (frm.DialogResult != DialogResult.Ignore)
                                            break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error! " + ex.Message, "Error - Bit Chat Profile Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                #endregion
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error! " + ex.Message + "\r\n\r\nClick OK to quit the application.", "Error - Bit Chat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion
    }
}
