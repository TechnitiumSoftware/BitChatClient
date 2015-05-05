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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BitChatApp
{
    delegate void DelegateCommand(string cmd);

    class AppLink : IDisposable
    {
        #region events

        public event DelegateCommand CommandReceived;

        #endregion

        #region variables

        Socket _socket;

        Thread _thread;
        SynchronizationContext _context;

        #endregion

        #region constructor

        public AppLink(int port)
        {
            _context = SynchronizationContext.Current;

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));

            _thread = new Thread(RecvAsync);
            _thread.IsBackground = true;
            _thread.Start();
        }

        #endregion

        #region IDisposable

        ~AppLink()
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
                _socket.Close();
                _thread.Abort();

                _disposed = true;
            }
        }

        #endregion

        #region static

        public static void SendCommand(string cmd, int port)
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                //socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                socket.SendTo(Encoding.UTF8.GetBytes(cmd), new IPEndPoint(IPAddress.Loopback, port));
            }
        }

        #endregion

        #region event methods

        private void RaiseEventCommandReceived(string cmd)
        {
            _context.Post(CommandReceivedAsync, cmd);
        }

        private void CommandReceivedAsync(object state)
        {
            try
            {
                if (CommandReceived != null)
                    CommandReceived(state as string);
            }
            catch
            { }
        }

        #endregion

        #region private

        private void RecvAsync()
        {
            byte[] buffer = new byte[4096];
            EndPoint remoteIP = new IPEndPoint(IPAddress.Any, 0);
            int bytesRecv;

            try
            {
                while (true)
                {
                    bytesRecv = _socket.ReceiveFrom(buffer, 0, 4096, SocketFlags.None, ref remoteIP);

                    RaiseEventCommandReceived(Encoding.UTF8.GetString(buffer, 0, bytesRecv));
                }
            }
            catch
            { }
        }

        #endregion
    }
}
