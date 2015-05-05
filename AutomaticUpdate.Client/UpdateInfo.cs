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
using System.Text;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Security.Cryptography;

namespace AutomaticUpdate.Client
{
    public class UpdateInfo : EventArgs, IWriteStream
    {
        #region variables

        string _updateVersion;
        Uri _downloadURI;
        long _downloadSize;
        Signature _signature;

        #endregion

        #region constructor

        public UpdateInfo(string updateVersion, Uri downloadURI, long downloadSize, Signature signature)
        {
            _updateVersion = updateVersion;
            _downloadURI = downloadURI;
            _downloadSize = downloadSize;
            _signature = signature;

            if (_signature.SigningCertificate.Capability != CertificateCapability.SignFile)
                throw new Exception("Signing certificate is not capable to sign files.");
        }

        public UpdateInfo(Stream s)
        {
            ReadFrom(new BinaryReader(s));
        }

        public UpdateInfo(BinaryReader bR)
        {
            ReadFrom(bR);
        }

        #endregion

        #region private

        private void ReadFrom(BinaryReader bR)
        {
            if (Encoding.ASCII.GetString(bR.ReadBytes(2)) != "UI")
                throw new Exception("Invalid UpdateInfo format.");

            switch (bR.ReadByte()) //version
            {
                case 1:
                    _updateVersion = Encoding.ASCII.GetString(bR.ReadBytes(bR.ReadByte()));
                    _downloadURI = new Uri(Encoding.ASCII.GetString(bR.ReadBytes(bR.ReadByte())));
                    _downloadSize = bR.ReadInt64();
                    _signature = new Signature(bR);

                    if (_signature.SigningCertificate.Capability != CertificateCapability.SignFile)
                        throw new Exception("Signing certificate is not capable to sign files.");

                    break;

                default:
                    throw new Exception("UpdateInfo version not supported.");
            }
        }

        #endregion

        #region public

        public bool IsUpdateAvailable(string currentVersion)
        {
            string[] uVer = _updateVersion.Split(new char[] { '.' });
            string[] cVer = currentVersion.Split(new char[] { '.' });

            int x = uVer.Length;
            if (x > cVer.Length)
                x = cVer.Length;

            for (int i = 0; i < x; i++)
            {
                if (Convert.ToInt32(uVer[i]) > Convert.ToInt32(cVer[i]))
                    return true;
                else if (Convert.ToInt32(uVer[i]) < Convert.ToInt32(cVer[i]))
                    return false;
            }

            if (uVer.Length > cVer.Length)
            {
                for (int i = x; i < uVer.Length; i++)
                {
                    if (Convert.ToInt32(uVer[i]) > 0)
                        return true;
                }
            }

            return false;
        }

        #endregion

        #region IWriteStream

        public void WriteTo(Stream s)
        {
            BinaryWriter bW = new BinaryWriter(s);
            WriteTo(bW);
            bW.Flush();
        }

        public void WriteTo(BinaryWriter bW)
        {
            bW.Write(Encoding.ASCII.GetBytes("UI")); //format
            bW.Write((byte)1); //version

            bW.Write(Convert.ToByte(_updateVersion.Length));
            bW.Write(Encoding.ASCII.GetBytes(_updateVersion), 0, _updateVersion.Length);

            bW.Write(Convert.ToByte(_downloadURI.AbsoluteUri.Length));
            bW.Write(Encoding.ASCII.GetBytes(_downloadURI.AbsoluteUri), 0, _downloadURI.AbsoluteUri.Length);

            bW.Write(_downloadSize);
            _signature.WriteTo(bW);
        }

        public byte[] ToArray()
        {
            using (MemoryStream mS = new MemoryStream())
            {
                WriteTo(mS);
                return mS.ToArray();
            }
        }

        public Stream ToStream()
        {
            MemoryStream mS = new MemoryStream();
            WriteTo(mS);
            mS.Position = 0;
            return mS;
        }

        #endregion

        #region properties

        public string UpdateVersion
        { get { return _updateVersion; } }

        public Uri DownloadURI
        { get { return _downloadURI; } }

        public long DownloadSize
        { get { return _downloadSize; } }

        public Signature DownloadSignature
        { get { return _signature; } }

        #endregion
    }
}
