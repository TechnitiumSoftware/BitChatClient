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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.UPnP.Networking;

namespace BitChatClient.Network.Connections
{
    public enum UPnPStatus
    {
        Unknown = 0,
        NoInternetConnection = 1,
        PortForwardingNotRequired = 2,
        UPnPDeviceNotFound = 3,
        PortForwarded = 4
    }

    class ConnectionManager : IDisposable
    {
        #region variables

        const int SOCKET_SEND_TIMEOUT = 30000; //30 sec socket timeout; application protocol NOOPs at 15 sec
        const int SOCKET_RECV_TIMEOUT = 90000; //keep socket open for long time to allow tunnelling requests between time

        IPEndPoint _externalSelfEP;
        ChannelRequest _requestHandler;

        BinaryID _localPeerID;

        Dictionary<string, object> _makeConnectionList = new Dictionary<string, object>();

        Dictionary<string, Connection> _connectionListByConnectionID = new Dictionary<string, Connection>();
        Dictionary<BinaryID, Connection> _connectionListByPeerID = new Dictionary<BinaryID, Connection>();

        //tcp
        TcpListener _tcpListener;
        Thread _tcpListenerThread;

        //UPnP
        const int UPNP_TIMER_INTERVAL = 60 * 1000;
        InternetGatewayDevice _upnp;
        Timer _upnpTimer;
        UPnPStatus _upnpStatus;

        #endregion

        #region constructor

        public ConnectionManager(IPEndPoint localEP, ChannelRequest requestHandler)
        {
            try
            {
                _tcpListener = new TcpListener(localEP);
                _tcpListener.Start(10);
            }
            catch
            {
                _tcpListener = new TcpListener(new IPEndPoint(localEP.Address, 0));
                _tcpListener.Start(10);
            }

            _externalSelfEP = (IPEndPoint)_tcpListener.LocalEndpoint;
            _requestHandler = requestHandler;
            _localPeerID = BinaryID.GenerateRandomID160();

            //start accepting connections
            _tcpListenerThread = new Thread(AcceptTcpConnectionAsync);
            _tcpListenerThread.IsBackground = true;
            _tcpListenerThread.Start();

            //start upnp process
            _upnpTimer = new Timer(UPnPTimerCallback, null, 1000, Timeout.Infinite);
        }

        #endregion

        #region IDisposable

        ~ConnectionManager()
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
                //shutdown tcp
                if (_tcpListener != null)
                    _tcpListener.Stop();

                if (_tcpListenerThread != null)
                    _tcpListenerThread.Abort();

                //shutdown upnp port mapping
                if (_upnpTimer != null)
                {
                    _upnpTimer.Dispose();
                    _upnpTimer = null;
                }

                //stop channel services
                List<Connection> connectionList = new List<Connection>();

                lock (_connectionListByConnectionID)
                {
                    foreach (KeyValuePair<string, Connection> connection in _connectionListByConnectionID)
                        connectionList.Add(connection.Value);
                }

                foreach (Connection connection in connectionList)
                {
                    try
                    {
                        connection.Dispose();
                    }
                    catch
                    { }
                }

                _disposed = true;
            }
        }

        #endregion

        #region private

        private void AcceptTcpConnectionAsync()
        {
            try
            {
                do
                {
                    Socket socket = _tcpListener.AcceptSocket();

                    try
                    {
                        socket.NoDelay = true;
                        socket.SendTimeout = SOCKET_SEND_TIMEOUT;
                        socket.ReceiveTimeout = SOCKET_RECV_TIMEOUT;

                        AcceptConnectionInitiateProtocol(new NetworkStream(socket), socket.RemoteEndPoint as IPEndPoint);
                    }
                    catch
                    { }
                }
                while (true);
            }
            catch
            {
            }
        }

        private Connection AddConnection(Stream networkStream, BinaryID remotePeerID, IPEndPoint remotePeerEP)
        {
            lock (_connectionListByConnectionID)
            {
                //check for self
                if (_localPeerID.Equals(remotePeerID))
                {
                    _externalSelfEP = new IPEndPoint(remotePeerEP.Address, _externalSelfEP.Port);
                    return null;
                }

                string connectionID = remotePeerEP.ToString();

                //check for existing connection by connection id
                if (_connectionListByConnectionID.ContainsKey(connectionID))
                    return null;

                //check for existing connection by peer id
                if (_connectionListByPeerID.ContainsKey(remotePeerID))
                {
                    Connection existingConnection = _connectionListByPeerID[remotePeerID];

                    //compare existing and new peer ip end-point
                    if (AllowNewConnection(existingConnection.RemotePeerEP, remotePeerEP))
                    {
                        //remove existing connection and allow new connection
                        existingConnection.Dispose();
                    }
                    else
                    {
                        //keep existing connection
                        return null;
                    }
                }

                //add connection
                Connection connection = new Connection(networkStream, remotePeerID, remotePeerEP, this, _requestHandler);
                _connectionListByConnectionID.Add(connectionID, connection);
                _connectionListByPeerID.Add(remotePeerID, connection);

                //start service
                connection.Start();

                return connection;
            }
        }

        private bool AllowNewConnection(IPEndPoint existingIP, IPEndPoint newIP)
        {
            if (existingIP.AddressFamily != newIP.AddressFamily)
            {
                if (existingIP.AddressFamily == AddressFamily.InterNetwork)
                    return false;
            }

            if (existingIP.AddressFamily == AddressFamily.InterNetwork)
            {
                if (NetUtilities.IsPrivateIPv4(existingIP.Address))
                    return false;
            }

            return true;
        }

        internal bool IsPeerConnectionAvailable(IPEndPoint remotePeerEP)
        {
            string connectionID = remotePeerEP.ToString();

            lock (_connectionListByConnectionID)
            {
                return _connectionListByConnectionID.ContainsKey(connectionID);
            }
        }

        internal Connection GetExistingConnection(IPEndPoint remotePeerEP)
        {
            string connectionID = remotePeerEP.ToString();

            lock (_connectionListByConnectionID)
            {
                if (_connectionListByConnectionID.ContainsKey(connectionID))
                    return _connectionListByConnectionID[connectionID];

                return null;
            }
        }

        internal void RemoveConnection(Connection connection)
        {
            lock (_connectionListByConnectionID)
            {
                _connectionListByConnectionID.Remove(connection.RemotePeerEP.ToString());
                _connectionListByPeerID.Remove(connection.RemotePeerID);
            }
        }

        private Connection MakeVirtualConnection(IPEndPoint remotePeerEP)
        {
            //ping all connected peer channels
            object lockObject = new object();
            Connection[] placeholder = new Connection[] { null };

            lock (lockObject)
            {
                lock (_connectionListByConnectionID)
                {
                    if (_connectionListByConnectionID.Count == 0)
                        throw new Exception("No peer available for virtual connection.");

                    foreach (KeyValuePair<string, Connection> item in _connectionListByConnectionID)
                        ThreadPool.QueueUserWorkItem(RequestPeerStatusAsync, new object[] { item.Value, remotePeerEP, lockObject, placeholder });
                }

                if (!Monitor.Wait(lockObject, 20000))
                    throw new Exception("Timed out while waiting for available peers for virtual connection.");

                Connection proxyPeerConnection = placeholder[0];

                //create tunnel via proxy peer
                Stream proxyNetworkStream = proxyPeerConnection.RequestProxyTunnelChannel(remotePeerEP);

                //make new connection protocol begins
                return MakeConnectionInitiateProtocol(proxyNetworkStream, remotePeerEP);
            }
        }

        private void RequestPeerStatusAsync(object state)
        {
            object[] param = state as object[];

            Connection connection = param[0] as Connection;
            IPEndPoint remotePeerEP = param[1] as IPEndPoint;
            object lockObject = param[2];
            Connection[] placeholder = param[3] as Connection[];

            try
            {
                if (connection.RequestPeerStatus(remotePeerEP))
                {
                    lock (lockObject)
                    {
                        placeholder[0] = connection;
                        Monitor.Pulse(lockObject);
                    }
                }
            }
            catch
            { }
        }

        internal Connection AcceptConnectionInitiateProtocol(Stream networkStream, IPEndPoint remotePeerEP)
        {
            //read version
            int version = networkStream.ReadByte();

            switch (version)
            {
                case 1:
                    //read service port
                    byte[] remoteServicePort = new byte[2];
                    networkStream.Read(remoteServicePort, 0, 2);
                    remotePeerEP = new IPEndPoint(remotePeerEP.Address, BitConverter.ToUInt16(remoteServicePort, 0));

                    //read peer id
                    byte[] peerID = new byte[20];
                    networkStream.Read(peerID, 0, 20);
                    BinaryID remotePeerID = new BinaryID(peerID);

                    //add
                    Connection connection = AddConnection(networkStream, remotePeerID, remotePeerEP);
                    if (connection != null)
                    {
                        //send ok
                        networkStream.WriteByte(0);
                        networkStream.Write(_localPeerID.ID, 0, 20);
                    }
                    else
                    {
                        //send cancel
                        networkStream.WriteByte(1);
                        networkStream.Close();
                    }
                    return connection;

                default:
                    networkStream.Close();
                    throw new IOException("Cannot accept remote connection: protocol version not supported.");
            }
        }

        private Connection MakeConnectionInitiateProtocol(Stream networkStream, IPEndPoint remotePeerEP)
        {
            try
            {
                //send version
                networkStream.WriteByte(1);

                //send service port
                networkStream.Write(BitConverter.GetBytes(Convert.ToUInt16(_externalSelfEP.Port)), 0, 2);

                //send peer id
                networkStream.Write(_localPeerID.ID, 0, 20);

                //read response
                int response = networkStream.ReadByte();
                if (response == 0)
                {
                    byte[] buffer = new byte[20];
                    networkStream.Read(buffer, 0, 20);
                    BinaryID remotePeerID = new BinaryID(buffer);

                    Connection connection = AddConnection(networkStream, remotePeerID, remotePeerEP);
                    if (connection == null)
                    {
                        //check for existing connection again!
                        Connection existingConnection = GetExistingConnection(remotePeerEP);
                        if (existingConnection != null)
                        {
                            networkStream.Dispose();
                            return existingConnection;
                        }

                        throw new IOException("Cannot connect to remote peer: connection already exists.");
                    }

                    return connection;
                }
                else
                {
                    Thread.Sleep(500); //wait so that other thread gets time to add his connection in list so that this thread can pick same connection to proceed

                    //check for existing connection again!
                    Connection existingConnection = GetExistingConnection(remotePeerEP);
                    if (existingConnection != null)
                    {
                        networkStream.Dispose();
                        return existingConnection;
                    }

                    throw new IOException("Cannot connect to remote peer: request rejected.");
                }
            }
            catch
            {
                networkStream.Dispose();
                throw;
            }
        }

        #endregion

        #region UPnP

        private void UPnPTimerCallback(object state)
        {
            try
            {
                NetworkInfo defaultNetworkInfo = NetUtilities.GetDefaultNetworkInfo();
                if (defaultNetworkInfo == null)
                {
                    //no internet available;
                    _upnpStatus = UPnPStatus.NoInternetConnection;
                    return;
                }

                if (defaultNetworkInfo.IsPublicIP)
                {
                    //public ip so no need to do port forwarding
                    _upnpStatus = UPnPStatus.PortForwardingNotRequired;
                    return;
                }

                IPEndPoint LocalNetworkEP = new IPEndPoint(defaultNetworkInfo.LocalIP, ((IPEndPoint)_tcpListener.LocalEndpoint).Port);

                try
                {
                    if ((_upnp == null) || (!_upnp.NetworkBroadcastAddress.Equals(defaultNetworkInfo.BroadcastIP)))
                        _upnp = InternetGatewayDevice.Discover(defaultNetworkInfo.BroadcastIP, 30000);
                }
                catch
                {
                    _upnpStatus = UPnPStatus.UPnPDeviceNotFound;
                    throw;
                }

                //find external ip from router
                try
                {
                    IPAddress externalIP = _upnp.GetExternalIPAddress();

                    if (!_externalSelfEP.Address.Equals(externalIP))
                        _externalSelfEP = new IPEndPoint(externalIP, _externalSelfEP.Port);
                }
                catch
                { }

                int externalPort = LocalNetworkEP.Port;
                bool isTCPMapped = false;

                try
                {
                    int loopCount = 0;

                    while (true)
                    {
                        PortMappingEntry portMap = _upnp.GetSpecificPortMappingEntry(ProtocolType.Tcp, externalPort);

                        if (portMap == null)
                            break; //port available

                        if (portMap.InternalEP.Equals(LocalNetworkEP))
                        {
                            //port already mapped with us
                            isTCPMapped = true;
                            _upnpStatus = UPnPStatus.PortForwarded;
                            break;
                        }

                        //find new port for mapping
                        if (externalPort < ushort.MaxValue)
                            externalPort++;
                        else
                            externalPort = 1024;

                        if (loopCount > ushort.MaxValue)
                            return;

                        loopCount++;
                    }
                }
                catch { }

                if (!isTCPMapped)
                {
                    try
                    {
                        _upnp.AddPortMapping(ProtocolType.Tcp, externalPort, LocalNetworkEP, "BitChat - TCP");

                        if (_externalSelfEP.Port != externalPort)
                            _externalSelfEP = new IPEndPoint(_externalSelfEP.Address, externalPort);

                        _upnpStatus = UPnPStatus.PortForwarded;

                        Debug.Write("BitChatClient.UPnPTimerCallback", "tcp port mapped " + externalPort);
                    }
                    catch
                    {
                        try
                        {
                            _upnp.DeletePortMapping(ProtocolType.Tcp, externalPort);
                            _upnp.AddPortMapping(ProtocolType.Tcp, externalPort, LocalNetworkEP, "BitChat - TCP");

                            if (_externalSelfEP.Port != externalPort)
                                _externalSelfEP = new IPEndPoint(_externalSelfEP.Address, externalPort);

                            _upnpStatus = UPnPStatus.PortForwarded;

                            Debug.Write("BitChat.UPnPTimerCallback", "tcp port mapped " + externalPort);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Write("BitChat.UPnPTimerCallback", ex);
            }
            finally
            {
                if (_upnpTimer != null)
                {
                    switch (_upnpStatus)
                    {
                        case Connections.UPnPStatus.UPnPDeviceNotFound:
                            _upnpTimer.Change(10000, Timeout.Infinite);
                            break;

                        default:
                            _upnpTimer.Change(UPNP_TIMER_INTERVAL, Timeout.Infinite);
                            break;
                    }
                }
            }
        }

        #endregion

        #region public

        public Connection MakeConnection(IPEndPoint remotePeerEP)
        {
            //prevent multiple connection requests to same remote end-point
            string connectionID = remotePeerEP.ToString();

            lock (_makeConnectionList)
            {
                if (_makeConnectionList.ContainsKey(connectionID))
                    throw new BitChatException("Connection attempt for end-point already in progress.");

                _makeConnectionList.Add(connectionID, null);
            }

            try
            {
                //check if self
                if (_externalSelfEP.Equals(remotePeerEP))
                    throw new IOException("Cannot connect to remote port: self connection.");

                //check existing connection
                Connection existingConnection = GetExistingConnection(remotePeerEP);
                if (existingConnection != null)
                    return existingConnection;

                try
                {
                    //try new tcp connection
                    TcpClient client = new TcpClient();
                    client.Connect(remotePeerEP);

                    client.NoDelay = true;
                    client.SendTimeout = SOCKET_SEND_TIMEOUT;
                    client.ReceiveTimeout = SOCKET_RECV_TIMEOUT;

                    return MakeConnectionInitiateProtocol(new NetworkStream(client.Client), remotePeerEP);
                }
                catch (SocketException)
                {
                    //try virtual connection
                    return MakeVirtualConnection(remotePeerEP);
                }
            }
            finally
            {
                lock (_makeConnectionList)
                {
                    _makeConnectionList.Remove(connectionID);
                }
            }
        }

        #endregion

        #region properties

        public BinaryID LocalPeerID
        { get { return _localPeerID; } }

        public IPEndPoint LocalEP
        { get { return _externalSelfEP; } }

        public UPnPStatus UPnPStatus
        { get { return _upnpStatus; } }

        public IPEndPoint ExternalSelfEP
        { get { return _externalSelfEP; } }

        #endregion
    }
}
