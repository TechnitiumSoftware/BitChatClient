/*
Technitium Bit Chat
Copyright (C) 2016  Shreyas Zare (shreyas@technitium.com)

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

using BitChatCore.Network.KademliaDHT;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.Proxy;
using TechnitiumLibrary.Net.UPnP.Networking;

namespace BitChatCore.Network.Connections
{
    public enum InternetConnectivityStatus
    {
        Identifying = 0,
        NoInternetConnection = 1,
        DirectInternetConnection = 2,
        HttpProxyInternetConnection = 3,
        Socks5ProxyInternetConnection = 4,
        NatInternetConnectionViaUPnPRouter = 5,
        NatOrFirewalledInternetConnection = 6
    }

    public enum UPnPDeviceStatus
    {
        Identifying = 0,
        Disabled = 1,
        DeviceNotFound = 2,
        ExternalIpPrivate = 3,
        PortForwarded = 4,
        PortForwardingFailed = 5,
        PortForwardedNotAccessible = 6
    }

    class ConnectionManager : IDhtClientManager, IDisposable
    {
        #region events

        public event EventHandler InternetConnectivityStatusChanged;
        public event BitChatNetworkInvitation BitChatNetworkChannelInvitation;
        public event BitChatNetworkChannelRequest BitChatNetworkChannelRequest;
        public event TcpRelayPeersAvailable TcpRelayPeersAvailable;

        #endregion

        #region variables

        const int SOCKET_SEND_TIMEOUT = 30000; //30 sec socket timeout; application protocol NOOPs at 15 sec
        const int SOCKET_RECV_TIMEOUT = 120000; //keep socket open for long time to allow tunnelling requests between time

        const int DHT_SEED_TRACKER_UPDATE_INTERVAL = 300;

        BitChatProfile _profile;

        BinaryID _localPeerID;

        Dictionary<IPEndPoint, object> _makeConnectionList = new Dictionary<IPEndPoint, object>();
        Dictionary<IPEndPoint, object> _makeVirtualConnectionList = new Dictionary<IPEndPoint, object>();

        Dictionary<IPEndPoint, Connection> _connectionListByConnectionID = new Dictionary<IPEndPoint, Connection>();
        Dictionary<BinaryID, Connection> _connectionListByPeerID = new Dictionary<BinaryID, Connection>();

        //tcp listener
        Socket _tcpListener;
        Thread _tcpListenerThread;

        //dht
        DhtClient _dhtClient;
        BinaryID _dhtSeedingNetworkID = new BinaryID(new byte[] { 0xfa, 0x20, 0xf3, 0x45, 0xe6, 0xbe, 0x43, 0x68, 0xcb, 0x1e, 0x2a, 0xfb, 0xc0, 0x08, 0x0d, 0x95, 0xf1, 0xd1, 0xe6, 0x5b });
        TrackerManager _dhtSeedingTracker;

        //internet connectivity
        const int CONNECTIVITY_CHECK_TIMER_INTERVAL = 60 * 1000;

        Uri CONNECTIVITY_CHECK_WEB_SERVICE = new Uri("https://bitchat.im/connectivity/check.aspx");
        Timer _connectivityCheckTimer;
        InternetConnectivityStatus _internetStatus = InternetConnectivityStatus.Identifying;
        InternetGatewayDevice _upnpDevice;
        UPnPDeviceStatus _upnpDeviceStatus = UPnPDeviceStatus.Identifying;

        //received invitations
        Dictionary<IPEndPoint, DateTime> _receivedInvitations = new Dictionary<IPEndPoint, DateTime>(10);
        const int INVITATION_INFO_EXPIRY_MINUTES = 15;

        int _localPort;
        IPAddress _localLiveIP = null;
        IPAddress _upnpExternalIP = null;
        IPEndPoint _connectivityCheckExternalEP = null;

        #endregion

        #region constructor

        public ConnectionManager(BitChatProfile profile)
        {
            IPEndPoint localEP;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    if (Environment.OSVersion.Version.Major < 6)
                    {
                        //below vista
                        _tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        localEP = new IPEndPoint(IPAddress.Any, profile.LocalPort);
                    }
                    else
                    {
                        //vista & above
                        _tcpListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                        _tcpListener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                        localEP = new IPEndPoint(IPAddress.IPv6Any, profile.LocalPort);
                    }
                    break;

                case PlatformID.Unix: //mono framework
                    if (Socket.OSSupportsIPv6)
                    {
                        _tcpListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                        localEP = new IPEndPoint(IPAddress.IPv6Any, profile.LocalPort);
                    }
                    else
                    {
                        _tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        localEP = new IPEndPoint(IPAddress.Any, profile.LocalPort);
                    }

                    break;

                default: //unknown
                    _tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    localEP = new IPEndPoint(IPAddress.Any, profile.LocalPort);
                    break;
            }

            try
            {
                _tcpListener.Bind(localEP);
                _tcpListener.Listen(10);
            }
            catch
            {
                localEP.Port = 0;

                _tcpListener.Bind(localEP);
                _tcpListener.Listen(10);
            }

            _profile = profile;

            _localPort = (_tcpListener.LocalEndPoint as IPEndPoint).Port;
            _localPeerID = BinaryID.GenerateRandomID160();

            //start dht
            _dhtClient = new DhtClient(_localPort, this);
            _dhtClient.ProxyEnabled = (_profile.Proxy != null);
            _dhtClient.AddNode(profile.BootstrapDhtNodes);

            //setup dht seeding tracker
            _dhtSeedingTracker = new TrackerManager(_dhtSeedingNetworkID, _localPort, null, DHT_SEED_TRACKER_UPDATE_INTERVAL);
            _dhtSeedingTracker.Proxy = _profile.Proxy;
            _dhtSeedingTracker.DiscoveredPeers += dhtSeedingTracker_DiscoveredPeers;
            _dhtSeedingTracker.StartTracking(profile.TrackerURIs);

            //start accepting connections
            _tcpListenerThread = new Thread(AcceptTcpConnectionAsync);
            _tcpListenerThread.IsBackground = true;
            _tcpListenerThread.Start(_tcpListener);

            //start upnp process
            _connectivityCheckTimer = new Timer(ConnectivityCheckTimerCallback, null, 1000, Timeout.Infinite);
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
                    _tcpListener.Dispose();

                //stop dht seeding tracker
                if (_dhtSeedingTracker != null)
                    _dhtSeedingTracker.Dispose();

                //stop dht client
                if (_dhtClient != null)
                    _dhtClient.Dispose();

                //shutdown upnp port mapping
                if (_connectivityCheckTimer != null)
                {
                    _connectivityCheckTimer.Dispose();
                    _connectivityCheckTimer = null;
                }

                //stop channel services
                List<Connection> connectionList = new List<Connection>();

                lock (_connectionListByConnectionID)
                {
                    foreach (Connection connection in _connectionListByConnectionID.Values)
                        connectionList.Add(connection);
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

        private void AcceptTcpConnectionAsync(object parameter)
        {
            Socket tcpListener = parameter as Socket;

            try
            {
                do
                {
                    Socket socket = tcpListener.Accept();

                    try
                    {
                        socket.NoDelay = true;
                        socket.SendTimeout = SOCKET_SEND_TIMEOUT;
                        socket.ReceiveTimeout = SOCKET_RECV_TIMEOUT;

                        IPEndPoint remotePeerEP = socket.RemoteEndPoint as IPEndPoint;

                        if (NetUtilities.IsIPv4MappedIPv6Address(remotePeerEP.Address))
                            remotePeerEP = new IPEndPoint(NetUtilities.ConvertFromIPv4MappedIPv6Address(remotePeerEP.Address), remotePeerEP.Port);

                        ThreadPool.QueueUserWorkItem(AcceptConnectionInitiateProtocolAsync, new object[] { new NetworkStream(socket, true), remotePeerEP });
                    }
                    catch
                    { }
                }
                while (true);
            }
            catch
            { }
        }

        private void AcceptConnectionInitiateProtocolAsync(object parameter)
        {
            object[] parameters = parameter as object[];

            Stream networkStream = parameters[0] as Stream;
            IPEndPoint remotePeerEP = parameters[1] as IPEndPoint;

            try
            {
                AcceptDecoyHttpConnection(networkStream);
                AcceptConnectionInitiateProtocol(networkStream, remotePeerEP);
            }
            catch
            { }
        }

        private Connection AddConnection(Stream networkStream, BinaryID remotePeerID, IPEndPoint remotePeerEP)
        {
            if ((remotePeerEP.AddressFamily == AddressFamily.InterNetworkV6) && (remotePeerEP.Address.ScopeId != 0))
                remotePeerEP = new IPEndPoint(new IPAddress(remotePeerEP.Address.GetAddressBytes()), remotePeerEP.Port);

            lock (_connectionListByConnectionID)
            {
                //check for self
                if (_localPeerID.Equals(remotePeerID))
                    return null;

                //check for existing connection by connection id
                if (_connectionListByConnectionID.ContainsKey(remotePeerEP))
                {
                    Connection existingConnection = _connectionListByConnectionID[remotePeerEP];

                    //check for virtual vs real connection
                    bool currentIsVirtual = Connection.IsStreamProxyTunnelConnection(networkStream);
                    bool existingIsVirtual = existingConnection.IsProxyTunnelConnection;

                    if (existingIsVirtual && !currentIsVirtual)
                    {
                        //existing is virtual and current is real; remove existing connection
                        existingConnection.Dispose();
                    }
                    else if (currentIsVirtual)
                    {
                        //existing is real/virtual and current is virtual; keep existing connection
                        return null;
                    }
                }
                else if (_connectionListByPeerID.ContainsKey(remotePeerID)) //check for existing connection by peer id
                {
                    Connection existingConnection = _connectionListByPeerID[remotePeerID];

                    //check for virtual vs real connection
                    bool currentIsVirtual = Connection.IsStreamProxyTunnelConnection(networkStream);
                    bool existingIsVirtual = existingConnection.IsProxyTunnelConnection;

                    if (existingIsVirtual && !currentIsVirtual)
                    {
                        //existing is virtual and current is real; remove existing connection
                        existingConnection.Dispose();
                    }
                    else if (currentIsVirtual)
                    {
                        //existing is real/virtual and current is virtual; keep existing connection
                        return null;
                    }
                    else
                    {
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
                }

                //add connection
                Connection connection = new Connection(networkStream, remotePeerID, remotePeerEP, this);
                _connectionListByConnectionID.Add(remotePeerEP, connection);
                _connectionListByPeerID.Add(remotePeerID, connection);

                //set event handlers
                connection.BitChatNetworkInvitation += Connection_BitChatNetworkInvitation;
                connection.BitChatNetworkChannelRequest += BitChatNetworkChannelRequest;
                connection.TcpRelayPeersAvailable += TcpRelayPeersAvailable;
                connection.Disposed += Connection_Disposed;

                //start service
                connection.Start();

                return connection;
            }
        }

        private void Connection_BitChatNetworkInvitation(BinaryID hashedEmailAddress, IPEndPoint peerEP, string message)
        {
            //this method is called async by the event
            //this mechanism prevents multiple invitations events from same peerEP
            try
            {
                lock (_receivedInvitations)
                {
                    if (_receivedInvitations.ContainsKey(peerEP))
                    {
                        //check for expiry
                        DateTime receivedOn = _receivedInvitations[peerEP];
                        DateTime currentTime = DateTime.UtcNow;

                        if (receivedOn.AddMinutes(INVITATION_INFO_EXPIRY_MINUTES) > currentTime)
                            return; //current entry still not expired so dont invoke the event to UI

                        //update received on time
                        _receivedInvitations[peerEP] = currentTime;
                    }
                    else
                    {
                        //add entry
                        _receivedInvitations.Add(peerEP, DateTime.UtcNow);
                    }
                }

                BitChatNetworkChannelInvitation?.Invoke(hashedEmailAddress, peerEP, message);
            }
            catch
            { }
            finally
            {
                //remove expired entries
                lock (_receivedInvitations)
                {
                    List<IPEndPoint> removeList = new List<IPEndPoint>(5);
                    DateTime currentTime = DateTime.UtcNow;

                    foreach (KeyValuePair<IPEndPoint, DateTime> item in _receivedInvitations)
                    {
                        if (item.Value.AddMinutes(INVITATION_INFO_EXPIRY_MINUTES) < currentTime)
                            removeList.Add(item.Key);
                    }

                    foreach (IPEndPoint key in removeList)
                        _receivedInvitations.Remove(key);
                }
            }
        }

        private void Connection_Disposed(object sender, EventArgs e)
        {
            //remove connection from connection manager
            Connection connection = sender as Connection;
            IPEndPoint remotePeerEP = connection.RemotePeerEP;

            if ((remotePeerEP.AddressFamily == AddressFamily.InterNetworkV6) && (remotePeerEP.Address.ScopeId != 0))
                remotePeerEP = new IPEndPoint(new IPAddress(remotePeerEP.Address.GetAddressBytes()), remotePeerEP.Port);

            lock (_connectionListByConnectionID)
            {
                _connectionListByConnectionID.Remove(remotePeerEP);
                _connectionListByPeerID.Remove(connection.RemotePeerID);
            }
        }

        private bool AllowNewConnection(IPEndPoint existingIP, IPEndPoint newIP)
        {
            if (existingIP.AddressFamily != newIP.AddressFamily)
                return false;

            if (NetUtilities.IsPrivateIP(existingIP.Address))
                return false;

            return true;
        }

        internal bool IsPeerConnectionAvailable(IPEndPoint remotePeerEP)
        {
            if ((remotePeerEP.AddressFamily == AddressFamily.InterNetworkV6) && (remotePeerEP.Address.ScopeId != 0))
                remotePeerEP = new IPEndPoint(new IPAddress(remotePeerEP.Address.GetAddressBytes()), remotePeerEP.Port);

            lock (_connectionListByConnectionID)
            {
                return _connectionListByConnectionID.ContainsKey(remotePeerEP);
            }
        }

        internal Connection GetExistingConnection(IPEndPoint remotePeerEP)
        {
            if ((remotePeerEP.AddressFamily == AddressFamily.InterNetworkV6) && (remotePeerEP.Address.ScopeId != 0))
                remotePeerEP = new IPEndPoint(new IPAddress(remotePeerEP.Address.GetAddressBytes()), remotePeerEP.Port);

            lock (_connectionListByConnectionID)
            {
                if (_connectionListByConnectionID.ContainsKey(remotePeerEP))
                    return _connectionListByConnectionID[remotePeerEP];

                return null;
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

                    foreach (Connection connection in _connectionListByConnectionID.Values)
                        ThreadPool.QueueUserWorkItem(RequestPeerStatusAsync, new object[] { connection, remotePeerEP, lockObject, placeholder });
                }

                if (!Monitor.Wait(lockObject, 10000))
                    throw new Exception("Timed out while waiting for available peers for virtual connection.");

                Connection proxyPeerConnection = placeholder[0];

                //create tunnel via proxy peer
                Stream proxyNetworkStream = proxyPeerConnection.RequestProxyTunnel(remotePeerEP);

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

        private void AcceptDecoyHttpConnection(Stream networkStream)
        {
            //read http request
            int byteRead;

            while (true)
            {
                byteRead = networkStream.ReadByte();
                if (byteRead == '\r')
                {
                    byteRead = networkStream.ReadByte();
                    if (byteRead == '\n')
                    {
                        byteRead = networkStream.ReadByte();
                        if (byteRead == '\r')
                        {
                            byteRead = networkStream.ReadByte();
                            if (byteRead == '\n')
                            {
                                //http request completed
                                break;
                            }
                        }
                    }
                }
            }

            //write http response
            string httpHeaders = "HTTP/1.1 200 OK\r\nDate: $DATE GMT\r\nServer: Apache\r\nKeep-Alive: timeout=15, max=100\r\nConnection: Keep-Alive\r\nContent-Type: application/octet-stream\r\n\r\n";

            httpHeaders = httpHeaders.Replace("$DATE", DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss"));
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(httpHeaders);

            networkStream.Write(buffer, 0, buffer.Length);
        }

        private void MakeDecoyHttpConnection(Stream networkStream, IPEndPoint remotePeerEP)
        {
            //write http request
            string httpHeaders = "GET / HTTP/1.1\r\nHost: $HOST\r\nConnection: keep-alive\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/59.0.3071.115 Safari/537.36\r\nAccept: text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8\r\nAccept-Encoding: gzip, deflate\r\nAccept-Language: en-GB,en-US;q=0.8,en;q=0.6\r\n\r\n";

            httpHeaders = httpHeaders.Replace("$HOST", remotePeerEP.ToString());
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(httpHeaders);

            networkStream.Write(buffer, 0, buffer.Length);

            //read http response
            int byteRead;

            while (true)
            {
                byteRead = networkStream.ReadByte();
                if (byteRead == '\r')
                {
                    byteRead = networkStream.ReadByte();
                    if (byteRead == '\n')
                    {
                        byteRead = networkStream.ReadByte();
                        if (byteRead == '\r')
                        {
                            byteRead = networkStream.ReadByte();
                            if (byteRead == '\n')
                            {
                                //http response completed
                                break;
                            }
                        }
                    }
                }
            }
        }

        internal void AcceptConnectionInitiateProtocol(Stream networkStream, IPEndPoint remotePeerEP)
        {
            //read version
            int version = networkStream.ReadByte();

            switch (version)
            {
                case 1:
                    //read peer id
                    byte[] peerID = new byte[20];
                    networkStream.Read(peerID, 0, 20);
                    BinaryID remotePeerID = new BinaryID(peerID);

                    //read service port
                    byte[] remoteServicePort = new byte[2];
                    networkStream.Read(remoteServicePort, 0, 2);
                    remotePeerEP = new IPEndPoint(remotePeerEP.Address, BitConverter.ToUInt16(remoteServicePort, 0));

                    //add
                    Connection connection = AddConnection(networkStream, remotePeerID, remotePeerEP);
                    if (connection != null)
                    {
                        //send ok
                        byte[] buffer = new byte[21];

                        buffer[0] = 0; //signal ok
                        Buffer.BlockCopy(_localPeerID.ID, 0, buffer, 1, 20); //peer id

                        networkStream.Write(buffer, 0, 21);
                    }
                    else
                    {
                        //send cancel
                        networkStream.WriteByte(1); //signal cancel
                        networkStream.Close();
                    }

                    return;

                default:
                    networkStream.Close();
                    throw new IOException("Cannot accept remote connection: protocol version not supported.");
            }
        }

        private Connection MakeConnectionInitiateProtocol(Stream networkStream, IPEndPoint remotePeerEP)
        {
            try
            {
                //send request
                {
                    byte[] buffer = new byte[23]; //request buffer

                    buffer[0] = 1; //version
                    Buffer.BlockCopy(_localPeerID.ID, 0, buffer, 1, 20); //peer id
                    Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToUInt16(_localPort)), 0, buffer, 21, 2); //service port

                    networkStream.Write(buffer, 0, 23);
                }

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

        private void dhtSeedingTracker_DiscoveredPeers(TrackerManager sender, IEnumerable<IPEndPoint> peerEPs)
        {
            if (_dhtClient != null)
                _dhtClient.AddNode(peerEPs);
        }

        internal void ClientProfileProxyUpdated()
        {
            NetProxy proxy = _profile.Proxy;

            _dhtClient.ProxyEnabled = (proxy != null);
            _dhtSeedingTracker.Proxy = proxy;

            if (proxy != null)
            {
                //stop tcp relay for all networks since this client switched to proxy and can no longer provide tcp relay service
                TcpRelayService.StopAllTcpRelays();
            }
        }

        #endregion

        #region internet connectivity

        private void ConnectivityCheckTimerCallback(object state)
        {
            if (_upnpDeviceStatus == UPnPDeviceStatus.Identifying)
                _upnpDevice = null;

            InternetConnectivityStatus newInternetStatus = InternetConnectivityStatus.Identifying;
            UPnPDeviceStatus newUPnPStatus;

            if (_profile.EnableUPnP)
                newUPnPStatus = UPnPDeviceStatus.Identifying;
            else
                newUPnPStatus = UPnPDeviceStatus.Disabled;

            try
            {
                if (_profile.Proxy != null)
                {
                    switch (_profile.Proxy.Type)
                    {
                        case NetProxyType.Http:
                            newInternetStatus = InternetConnectivityStatus.HttpProxyInternetConnection;
                            break;

                        case NetProxyType.Socks5:
                            newInternetStatus = InternetConnectivityStatus.Socks5ProxyInternetConnection;
                            break;

                        default:
                            throw new NotSupportedException("Proxy type not supported.");
                    }

                    newUPnPStatus = UPnPDeviceStatus.Disabled;
                    return;
                }

                NetworkInfo defaultNetworkInfo = NetUtilities.GetDefaultNetworkInfo();
                if (defaultNetworkInfo == null)
                {
                    //no internet available;
                    newInternetStatus = InternetConnectivityStatus.NoInternetConnection;
                    newUPnPStatus = UPnPDeviceStatus.Disabled;
                    return;
                }

                if (!NetUtilities.IsPrivateIP(defaultNetworkInfo.LocalIP))
                {
                    //public ip so, direct internet connection available
                    newInternetStatus = InternetConnectivityStatus.DirectInternetConnection;
                    newUPnPStatus = UPnPDeviceStatus.Disabled;
                    _localLiveIP = defaultNetworkInfo.LocalIP;
                    return;
                }
                else
                {
                    _localLiveIP = null;
                }

                if (newUPnPStatus == UPnPDeviceStatus.Disabled)
                {
                    newInternetStatus = InternetConnectivityStatus.NatOrFirewalledInternetConnection;
                    return;
                }

                //check for upnp device

                if (defaultNetworkInfo.LocalIP.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    newInternetStatus = InternetConnectivityStatus.NatOrFirewalledInternetConnection;
                    newUPnPStatus = UPnPDeviceStatus.Disabled;
                    return;
                }

                try
                {
                    if ((_upnpDevice == null) || (!_upnpDevice.NetworkBroadcastAddress.Equals(defaultNetworkInfo.BroadcastIP)))
                        _upnpDevice = InternetGatewayDevice.Discover(defaultNetworkInfo.BroadcastIP, 2000);

                    newInternetStatus = InternetConnectivityStatus.NatInternetConnectionViaUPnPRouter;
                }
                catch
                {
                    newInternetStatus = InternetConnectivityStatus.NatOrFirewalledInternetConnection;
                    newUPnPStatus = UPnPDeviceStatus.DeviceNotFound;
                    throw;
                }

                //find external ip from router
                try
                {
                    _upnpExternalIP = _upnpDevice.GetExternalIPAddress();

                    if (_upnpExternalIP.ToString() == "0.0.0.0")
                    {
                        newInternetStatus = InternetConnectivityStatus.NoInternetConnection;
                        newUPnPStatus = UPnPDeviceStatus.Disabled;
                        return; //external ip not available so no internet connection available
                    }
                    else if (NetUtilities.IsPrivateIP(_upnpExternalIP))
                    {
                        newUPnPStatus = UPnPDeviceStatus.ExternalIpPrivate;
                        return; //no use of doing port forwarding for private upnp ip address
                    }
                }
                catch
                {
                    _upnpExternalIP = null;
                }

                //do upnp port forwarding for Bit Chat
                if (_upnpDevice.ForwardPort(ProtocolType.Tcp, _localPort, new IPEndPoint(defaultNetworkInfo.LocalIP, _localPort), "Bit Chat", true))
                    newUPnPStatus = UPnPDeviceStatus.PortForwarded;
                else
                    newUPnPStatus = UPnPDeviceStatus.PortForwardingFailed;

                //do upnp port forwarding for DHT
                _upnpDevice.ForwardPort(ProtocolType.Udp, _dhtClient.LocalPort, new IPEndPoint(defaultNetworkInfo.LocalIP, _dhtClient.LocalPort), "Bit Chat DHT", true);
            }
            catch (Exception ex)
            {
                Debug.Write("ConnectionManager.ConnectivityCheckTimerCallback", ex);
            }
            finally
            {
                try
                {
                    //validate change in status by performing tests
                    if (_internetStatus != newInternetStatus)
                    {
                        switch (newInternetStatus)
                        {
                            case InternetConnectivityStatus.NoInternetConnection:
                                _localLiveIP = null;
                                _upnpExternalIP = null;
                                _connectivityCheckExternalEP = null;
                                break;

                            case InternetConnectivityStatus.HttpProxyInternetConnection:
                            case InternetConnectivityStatus.Socks5ProxyInternetConnection:
                                if (!_profile.Proxy.IsProxyAvailable())
                                    newInternetStatus = InternetConnectivityStatus.NoInternetConnection;

                                _localLiveIP = null;
                                _upnpExternalIP = null;
                                _connectivityCheckExternalEP = null;
                                break;

                            default:
                                if (WebUtilities.IsWebAccessible())
                                {
                                    switch (newInternetStatus)
                                    {
                                        case InternetConnectivityStatus.DirectInternetConnection:
                                            if (!DoWebCheckIncomingConnection(_localPort))
                                                _localLiveIP = null;

                                            break;

                                        case InternetConnectivityStatus.NatOrFirewalledInternetConnection:
                                            if (!DoWebCheckIncomingConnection(_localPort))
                                                _connectivityCheckExternalEP = null;

                                            break;

                                        case InternetConnectivityStatus.NatInternetConnectionViaUPnPRouter:
                                            break;

                                        default:
                                            _localLiveIP = null;
                                            _upnpExternalIP = null;
                                            _connectivityCheckExternalEP = null;
                                            break;
                                    }
                                }
                                else
                                {
                                    newInternetStatus = InternetConnectivityStatus.NoInternetConnection;
                                    _localLiveIP = null;
                                    _upnpExternalIP = null;
                                    _connectivityCheckExternalEP = null;
                                }
                                break;
                        }
                    }

                    if ((newInternetStatus == InternetConnectivityStatus.NatInternetConnectionViaUPnPRouter) && (_upnpDeviceStatus != newUPnPStatus) && (newUPnPStatus == UPnPDeviceStatus.PortForwarded))
                    {
                        if (_upnpDeviceStatus == UPnPDeviceStatus.PortForwardedNotAccessible)
                        {
                            newUPnPStatus = UPnPDeviceStatus.PortForwardedNotAccessible;
                        }
                        else if (!DoWebCheckIncomingConnection(_localPort))
                        {
                            newUPnPStatus = UPnPDeviceStatus.PortForwardedNotAccessible;
                        }
                    }

                    if ((_internetStatus != newInternetStatus) || (_upnpDeviceStatus != newUPnPStatus))
                    {
                        _internetStatus = newInternetStatus;
                        _upnpDeviceStatus = newUPnPStatus;

                        if (this.ExternalEndPoint == null)
                        {
                            //if no incoming connection possible

                            if (_dhtClient.GetTotalNodes() < DhtClient.KADEMLIA_K)
                            {
                                _dhtSeedingTracker.LookupOnly = true;
                                _dhtSeedingTracker.ScheduleUpdateNow(); //start finding dht nodes immediately
                            }
                            else
                            {
                                _dhtSeedingTracker.StopTracking(); //we have enough dht nodes and can stop seeding tracker
                            }
                        }
                        else
                        {
                            _dhtSeedingTracker.LookupOnly = false;

                            if (!_dhtSeedingTracker.IsTrackerRunning)
                                _dhtSeedingTracker.StartTracking(); //keep seeding tracker running for other peers to find dht bootstrap nodes
                        }

                        InternetConnectivityStatusChanged?.Invoke(this, EventArgs.Empty);
                    }

                    //schedule next check
                    if (_connectivityCheckTimer != null)
                    {
                        if ((_internetStatus == InternetConnectivityStatus.NoInternetConnection) || (_upnpDeviceStatus == UPnPDeviceStatus.DeviceNotFound) || (_upnpDeviceStatus == UPnPDeviceStatus.PortForwardingFailed))
                            _connectivityCheckTimer.Change(10000, Timeout.Infinite);
                        else
                            _connectivityCheckTimer.Change(CONNECTIVITY_CHECK_TIMER_INTERVAL, Timeout.Infinite);
                    }
                }
                catch
                { }
            }
        }

        private bool DoWebCheckIncomingConnection(int externalPort)
        {
            bool _webCheckError = false;
            bool _webCheckSuccess = false;

            try
            {
                using (WebClientEx client = new WebClientEx())
                {
                    client.Proxy = _profile.Proxy;
                    client.QueryString.Add("port", externalPort.ToString());
                    client.Timeout = 30000;

                    using (MemoryStream mS = new MemoryStream(client.DownloadData(CONNECTIVITY_CHECK_WEB_SERVICE)))
                    {
                        _webCheckError = false;
                        _webCheckSuccess = (mS.ReadByte() == 1);

                        switch (mS.ReadByte())
                        {
                            case 1: //ipv4
                                {
                                    byte[] ipv4 = new byte[4];
                                    byte[] port = new byte[2];

                                    mS.Read(ipv4, 0, 4);
                                    mS.Read(port, 0, 2);

                                    _connectivityCheckExternalEP = new IPEndPoint(new IPAddress(ipv4), BitConverter.ToUInt16(port, 0));
                                }
                                break;

                            case 2: //ipv6
                                {
                                    byte[] ipv6 = new byte[16];
                                    byte[] port = new byte[2];

                                    mS.Read(ipv6, 0, 16);
                                    mS.Read(port, 0, 2);

                                    _connectivityCheckExternalEP = new IPEndPoint(new IPAddress(ipv6), BitConverter.ToUInt16(port, 0));
                                }
                                break;

                            default:
                                _connectivityCheckExternalEP = null;
                                break;
                        }
                    }
                }
            }
            catch
            {
                _webCheckError = true;
                _webCheckSuccess = false;
                _connectivityCheckExternalEP = null;
            }

            return _webCheckSuccess || _webCheckError;
        }

        public void ReCheckConnectivity()
        {
            if (_internetStatus != InternetConnectivityStatus.Identifying)
            {
                _internetStatus = InternetConnectivityStatus.Identifying;
                _upnpDeviceStatus = UPnPDeviceStatus.Identifying;

                _connectivityCheckTimer.Change(1000, Timeout.Infinite);
            }
        }

        #endregion

        #region public

        public Connection MakeConnection(IPEndPoint remotePeerEP)
        {
            if (NetUtilities.IsIPv4MappedIPv6Address(remotePeerEP.Address))
                remotePeerEP = new IPEndPoint(NetUtilities.ConvertFromIPv4MappedIPv6Address(remotePeerEP.Address), remotePeerEP.Port);

            //prevent multiple connection requests to same remote end-point
            lock (_makeConnectionList)
            {
                if (_makeConnectionList.ContainsKey(remotePeerEP))
                    throw new BitChatException("Connection attempt for end-point already in progress.");

                _makeConnectionList.Add(remotePeerEP, null);
            }

            try
            {
                //check if self
                if (remotePeerEP.Equals(this.ExternalEndPoint))
                    throw new IOException("Cannot connect to remote port: self connection.");

                //check existing connection
                Connection existingConnection = GetExistingConnection(remotePeerEP);
                if (existingConnection != null)
                    return existingConnection;

                try
                {
                    //try new tcp connection

                    Socket client;

                    if (_profile.Proxy != null)
                    {
                        switch (_profile.Proxy.Type)
                        {
                            case NetProxyType.Http:
                                client = _profile.Proxy.HttpProxy.Connect(remotePeerEP);
                                break;

                            case NetProxyType.Socks5:
                                using (SocksConnectRequestHandler requestHandler = _profile.Proxy.SocksProxy.Connect(remotePeerEP))
                                {
                                    client = requestHandler.GetSocket();
                                }
                                break;

                            default:
                                throw new NotSupportedException("Proxy type not supported.");
                        }
                    }
                    else
                    {
                        client = new Socket(remotePeerEP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        client.Connect(remotePeerEP);
                    }

                    client.NoDelay = true;
                    client.SendTimeout = SOCKET_SEND_TIMEOUT;
                    client.ReceiveTimeout = SOCKET_RECV_TIMEOUT;

                    NetworkStream networkStream = new NetworkStream(client, true);

                    MakeDecoyHttpConnection(networkStream, remotePeerEP);

                    return MakeConnectionInitiateProtocol(networkStream, remotePeerEP);
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
                    _makeConnectionList.Remove(remotePeerEP);
                }
            }
        }

        public Connection MakeVirtualConnection(Connection viaConnection, IPEndPoint remotePeerEP)
        {
            if (NetUtilities.IsIPv4MappedIPv6Address(remotePeerEP.Address))
                remotePeerEP = new IPEndPoint(NetUtilities.ConvertFromIPv4MappedIPv6Address(remotePeerEP.Address), remotePeerEP.Port);

            //prevent multiple virtual connection requests to same remote end-point
            lock (_makeVirtualConnectionList)
            {
                if (_makeVirtualConnectionList.ContainsKey(remotePeerEP))
                    throw new BitChatException("Connection attempt for end-point already in progress.");

                _makeVirtualConnectionList.Add(remotePeerEP, null);
            }

            try
            {
                //check if self
                if (remotePeerEP.Equals(this.ExternalEndPoint))
                    throw new IOException("Cannot connect to remote port: self connection.");

                //check existing connection
                Connection existingConnection = GetExistingConnection(remotePeerEP);
                if (existingConnection != null)
                    return existingConnection;

                //create tunnel via proxy peer
                Stream proxyNetworkStream = viaConnection.RequestProxyTunnel(remotePeerEP);

                //make new connection protocol begins
                return MakeConnectionInitiateProtocol(proxyNetworkStream, remotePeerEP);
            }
            finally
            {
                lock (_makeVirtualConnectionList)
                {
                    _makeVirtualConnectionList.Remove(remotePeerEP);
                }
            }
        }

        public void SendDhtPacket(IPEndPoint remoteNodeEP, byte[] buffer, int offset, int size)
        {
            Connection connection = MakeConnection(remoteNodeEP);

            if (!connection.IsProxyTunnelConnection)
                connection.SendDhtPacket(buffer, offset, size);
        }

        #endregion

        #region properties

        public BinaryID LocalPeerID
        { get { return _localPeerID; } }

        public int LocalPort
        { get { return _localPort; } }

        public InternetConnectivityStatus InternetStatus
        { get { return _internetStatus; } }

        public UPnPDeviceStatus UPnPStatus
        { get { return _upnpDeviceStatus; } }

        public IPAddress UPnPDeviceIP
        {
            get
            {
                if (_upnpDevice == null)
                    return null;
                else
                    return _upnpDevice.DeviceIP;
            }
        }

        public IPAddress UPnPExternalIP
        {
            get
            {
                if (_internetStatus == InternetConnectivityStatus.NatInternetConnectionViaUPnPRouter)
                    return _upnpExternalIP;
                else
                    return null;
            }
        }

        public IPEndPoint ExternalEndPoint
        {
            get
            {
                switch (_internetStatus)
                {
                    case InternetConnectivityStatus.DirectInternetConnection:
                        if (_localLiveIP == null)
                            return null;
                        else
                            return new IPEndPoint(_localLiveIP, _localPort);

                    case InternetConnectivityStatus.NatInternetConnectionViaUPnPRouter:
                        switch (_upnpDeviceStatus)
                        {
                            case UPnPDeviceStatus.PortForwarded:
                                if (_upnpExternalIP == null)
                                    return null;
                                else
                                    return new IPEndPoint(_upnpExternalIP, _localPort);

                            default:
                                return null;
                        }

                    case InternetConnectivityStatus.Identifying:
                        return null;

                    default:
                        if (_connectivityCheckExternalEP == null)
                            return null;
                        else
                            return _connectivityCheckExternalEP;
                }
            }
        }

        public DhtClient DhtClient
        { get { return _dhtClient; } }

        public BitChatProfile Profile
        { get { return _profile; } }

        #endregion
    }
}
