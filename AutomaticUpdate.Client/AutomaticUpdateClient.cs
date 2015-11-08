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
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.Proxy;
using TechnitiumLibrary.Security.Cryptography;

namespace AutomaticUpdate.Client
{
    public class AutomaticUpdateClient : IDisposable
    {
        #region events

        public delegate void ErrorEventHandler(object sender, Exception ex);

        public event EventHandler UpdateAvailable;
        public event EventHandler NoUpdateAvailable;
        public event ErrorEventHandler UpdateError;
        public event EventHandler ExitApplication;

        #endregion

        #region variables

        string _mutexName;
        string _currentVersion;

        Uri _checkUpdateURL;
        int _checkUpdateIntervalDays;
        Certificate[] _trustedRootCAs;

        DateTime _lastUpdateCheckedOn;
        DateTime _lastModifiedGMT;

        NetProxy _proxy;

        UpdateInfo _updateInfo;

        Timer _checkTimer;
        SynchronizationContext _context;

        Action<object> _checkUpdate;
        Action _downloadInstall;

        #endregion

        #region constructor

        public AutomaticUpdateClient(string mutexName, string currentVersion, Uri checkUpdateURL, int checkUpdateIntervalDays, Certificate[] trustedRootCAs, DateTime lastUpdateCheckedOn, DateTime lastModifiedGMT)
        {
            _context = SynchronizationContext.Current;

            _mutexName = mutexName;
            _currentVersion = currentVersion;

            _checkUpdateURL = checkUpdateURL;
            _checkUpdateIntervalDays = checkUpdateIntervalDays;
            _trustedRootCAs = trustedRootCAs;

            _lastUpdateCheckedOn = lastUpdateCheckedOn;
            _lastModifiedGMT = lastModifiedGMT;

            _checkTimer = new Timer(CheckForUpdateAsync, null, 10000, 1 * 60 * 60 * 1000);
        }

        #endregion

        #region IDisposable

        ~AutomaticUpdateClient()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool _disposed = false;

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                //stop timer
                if (_checkTimer != null)
                {
                    _checkTimer.Dispose();
                    _checkTimer = null;
                }

                _disposed = true;
            }
        }

        #endregion

        #region event methods

        private void RaiseEventUpdateAvailable()
        {
            _context.Post(UpdateAvailableEventCall, null);
        }

        private void UpdateAvailableEventCall(object state)
        {
            if (UpdateAvailable != null)
                UpdateAvailable(this, EventArgs.Empty);
        }

        private void RaiseEventNoUpdateAvailable()
        {
            _context.Post(NoUpdateAvailableEventCall, null);
        }

        private void NoUpdateAvailableEventCall(object state)
        {
            if (NoUpdateAvailable != null)
                NoUpdateAvailable(this, EventArgs.Empty);
        }

        private void RaiseEventUpdateError(Exception ex)
        {
            _context.Post(UpdateErrorEventCall, ex);
        }

        private void UpdateErrorEventCall(object state)
        {
            if (UpdateError != null)
                UpdateError(this, state as Exception);
        }

        private void RaiseEventExitApplication()
        {
            _context.Post(ExitApplicationEventCall, null);
        }

        private void ExitApplicationEventCall(object state)
        {
            if (ExitApplication != null)
                ExitApplication(this, EventArgs.Empty);
        }

        #endregion

        #region private

        private void CheckForUpdateAsync(object state)
        {
            if (state == null)
            {
                if (DateTime.UtcNow < _lastUpdateCheckedOn.AddDays(_checkUpdateIntervalDays))
                    return;
            }

            //update last check time
            _lastUpdateCheckedOn = DateTime.UtcNow;

            try
            {
                using (WebClientEx client = new WebClientEx())
                {
                    client.Proxy = _proxy;
                    client.UserAgent = GetUserAgent();
                    client.IfModifiedSince = _lastModifiedGMT;

                    byte[] responseData = client.DownloadData(_checkUpdateURL);

                    _updateInfo = new UpdateInfo(new MemoryStream(responseData, false));
                    _lastModifiedGMT = DateTime.Parse(client.ResponseHeaders["Last-Modified"]);

                    if (_updateInfo.IsUpdateAvailable(_currentVersion))
                    {
                        //update available, stop timer
                        _checkTimer.Change(Timeout.Infinite, Timeout.Infinite);

                        //raise event to user UI
                        RaiseEventUpdateAvailable();
                    }
                    else
                    {
                        //if manual check then raise event
                        if (state != null)
                            RaiseEventNoUpdateAvailable();
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;

                if (response != null)
                {
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.NotModified:
                        case HttpStatusCode.NotFound:
                            //if manual check then raise event
                            if (state != null)
                                RaiseEventNoUpdateAvailable();
                            break;

                        default:
                            //if manual check then raise event
                            if (state != null)
                                RaiseEventUpdateError(ex);
                            break;
                    }
                }
                else
                {
                    //if manual check then raise event
                    if (state != null)
                        RaiseEventUpdateError(ex);
                }
            }
            catch (Exception ex)
            {
                //if manual check then raise event
                if (state != null)
                    RaiseEventUpdateError(ex);
            }

            if (state != null)
                _checkUpdate = null;
        }

        private void DownloadAndInstallAsync()
        {
            bool exitApp = false;

            try
            {
                string tmpGZFile = Path.GetTempFileName();
                string tmpPkgFile = Path.GetTempFileName();
                string tmpFolder;

                do
                {
                    tmpFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                } while (Directory.Exists(tmpFolder));

                Directory.CreateDirectory(tmpFolder);

                try
                {
                    #region download update file

                    using (WebClientEx client = new WebClientEx())
                    {
                        client.Proxy = _proxy;
                        client.UserAgent = GetUserAgent();
                        client.DownloadFile(_updateInfo.DownloadURI, tmpGZFile);
                    }

                    #endregion

                    #region verify signature, extract & execute

                    using (FileStream sGZ = new FileStream(tmpGZFile, FileMode.Open, FileAccess.Read))
                    {
                        //verify signature
                        if (!_updateInfo.DownloadSignature.Verify(sGZ, _trustedRootCAs))
                            throw new Exception("Update file signature does not match.");

                        using (FileStream sPkg = new FileStream(tmpPkgFile, FileMode.Create, FileAccess.ReadWrite))
                        {
                            //unzip
                            sGZ.Position = 0;
                            using (GZipStream unzip = new GZipStream(sGZ, CompressionMode.Decompress, true))
                            {
                                unzip.CopyTo(sPkg);
                            }

                            //extract
                            sPkg.Position = 0;
                            Package updatePackage = new Package(sPkg, PackageMode.Open);
                            updatePackage.ExtractAll(ExtractLocation.Custom, tmpFolder, true);

                            //execute
                            switch (Environment.OSVersion.Platform)
                            {
                                case PlatformID.Win32NT:
                                    {
                                        string appFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", "").Replace("/", "\\"));

                                        ProcessStartInfo pInfo = new ProcessStartInfo(Path.Combine(tmpFolder, "update.exe"), "\"" + _mutexName + "\" \"" + appFolder + "\"");
                                        pInfo.UseShellExecute = true;
                                        pInfo.Verb = "runas";

                                        Process.Start(pInfo);
                                    }
                                    break;

                                case PlatformID.Unix:
                                    {
                                        string appFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file://", ""));

                                        ProcessStartInfo pInfo = new ProcessStartInfo("mono", "\"" + Path.Combine(tmpFolder, "update.exe") + "\" \"" + _mutexName + "\" \"" + appFolder + "\"");
                                        pInfo.UseShellExecute = false;

                                        Process.Start(pInfo);
                                    }
                                    break;

                                default:
                                    throw new Exception("Platform not supported.");
                            }

                            exitApp = true;
                        }
                    }

                    #endregion
                }
                finally
                {
                    File.Delete(tmpGZFile);
                    File.Delete(tmpPkgFile);
                }
            }
            catch (Exception ex)
            {
                RaiseEventUpdateError(ex);
            }

            _downloadInstall = null;

            if (exitApp)
                RaiseEventExitApplication();
        }

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

            return "Mozilla/5.0 (" + operatingSystem + ") AutomaticUpdate/1.0";
        }

        #endregion

        #region public

        public void CheckForUpdate()
        {
            if (_checkUpdate == null)
            {
                _lastModifiedGMT = new DateTime();

                _checkUpdate = new Action<object>(CheckForUpdateAsync);
                _checkUpdate.BeginInvoke(new object(), null, null);
            }
        }

        public void DownloadAndInstall()
        {
            if (_updateInfo == null)
                throw new Exception("Not checked for update yet.");

            if (_downloadInstall == null)
            {
                _downloadInstall = new Action(DownloadAndInstallAsync);
                _downloadInstall.BeginInvoke(null, null);
            }
        }

        #endregion

        #region properties

        public UpdateInfo UpdateInfo
        { get { return _updateInfo; } }

        public DateTime LastUpdateCheckedOn
        { get { return _lastUpdateCheckedOn; } }

        public DateTime LastModifiedGMT
        { get { return _lastModifiedGMT; } }

        public NetProxy Proxy
        {
            get { return _proxy; }
            set { _proxy = value; }
        }

        #endregion
    }
}
