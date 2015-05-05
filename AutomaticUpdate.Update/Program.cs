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
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using TechnitiumLibrary.IO;

namespace AutomaticUpdate.Update
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            #region wait for running instance to exit

            do
            {
                bool createdNewMutex;

                Mutex app = new Mutex(true, args[0], out createdNewMutex);

                app.Dispose();

                if (createdNewMutex)
                    break;

                Thread.Sleep(1000);
            }
            while (true);


            #endregion

            try
            {
                string pkgFile;

                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        pkgFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", "").Replace("/", "\\")), "update.pkg");
                        break;

                    case PlatformID.Unix:
                        pkgFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file://", "")), "update.pkg");
                        break;

                    default:
                        throw new Exception("Platform not supported.");
                }

                using (Package package = new Package(pkgFile, PackageMode.Open))
                {
                    package.ExtractAll(ExtractLocation.Custom, args[1], true);
                }

                MessageBox.Show("Update was installed successfully. Click OK to continue.", "Update Installed - Technitium Automatic Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occured while updating application:\r\n\r\n" + ex.Message + "\r\n\r\nChanges were automatically rolled back.", "Update Error - Technitium Automatic Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
