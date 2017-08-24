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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;

/*
 
 FEATURE: discover local peer on ipv4 network for given network_id without disclosing network_id in the process.

 BROADCAST QUERY
 ===============
 * version = 5
 * query_challenge
 * HMAC(query_challenge, network_id)
 
 UNICAST RESPONSE
 ================
 * version = 6
 * service_port
 * query_challenge
 * HMAC(query_challenge + sender_ip_address + service_port, network_id)

*/

namespace BitChatCore.Network
{
    delegate void DiscoveredPeerInfo(LocalPeerDiscovery sender, IPEndPoint peerEP, BinaryNumber networkID);

    class LocalPeerDiscovery : IDisposable
    {
        #region events

        private delegate void ReceivedPacket(DiscoveryPacket packet, IPAddress peerIP);
        private delegate void DiscoveredBroadcastNetwork(NetworkInfo[] networks);

        public event DiscoveredPeerInfo PeerDiscovered;

        #endregion

        #region variables

        const int ANNOUNCEMENT_INTERVAL = 30000;
        const int ANNOUNCEMENT_RETRY_INTERVAL = 2000;
        const int ANNOUNCEMENT_RETRY_COUNT = 3;
        const int NETWORK_WATCHER_INTERVAL = 15000;
        const int BUFFER_MAX_SIZE = 128;

        static Listener _listener;

        ushort _announcePort;

        Timer _announcementTimer;

        List<BinaryNumber> _trackedNetworkIDs = new List<BinaryNumber>(5);
        List<BinaryNumber> _announceNetworkIDs = new List<BinaryNumber>(5);

        List<DiscoveryPacket> _queryPacketCache = new List<DiscoveryPacket>(5);
        List<PeerInfo> _peerInfoCache = new List<PeerInfo>(5);

        #endregion

        #region constructor

        public LocalPeerDiscovery(int announcePort)
        {
            _announcePort = Convert.ToUInt16(announcePort);

            _listener.ReceivedPacket += listener_ReceivedPacket;
            _listener.BroadcastNetworkDiscovered += listener_BroadcastNetworkDiscovered;

            _announcementTimer = new Timer(AnnouncementTimerCallBack, null, ANNOUNCEMENT_INTERVAL, Timeout.Infinite);
        }

        #endregion

        #region IDisposable

        ~LocalPeerDiscovery()
        {
            Dispose(false);
        }

        protected bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_announcementTimer != null)
                    {
                        _announcementTimer.Dispose();
                        _announcementTimer = null;
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region static

        public static void StartListener(int listenerPort)
        {
            if (_listener == null)
                _listener = new Listener(listenerPort);
        }

        public static void StopListener(int listenerPort)
        {
            if (_listener != null)
            {
                _listener.Dispose();
                _listener = null;
            }
        }

        #endregion

        #region public

        public void StartTracking(BinaryNumber networkID)
        {
            lock (_trackedNetworkIDs)
            {
                if (!_trackedNetworkIDs.Contains(networkID))
                    _trackedNetworkIDs.Add(networkID);
            }
        }

        public void StopTracking(BinaryNumber networkID)
        {
            lock (_trackedNetworkIDs)
            {
                _trackedNetworkIDs.Remove(networkID);
            }
        }

        public void StartAnnouncement(BinaryNumber networkID)
        {
            lock (_announceNetworkIDs)
            {
                if (!_announceNetworkIDs.Contains(networkID))
                {
                    _announceNetworkIDs.Add(networkID);

                    AnnounceAsync(new BinaryNumber[] { networkID }, null, ANNOUNCEMENT_RETRY_COUNT);
                }
            }
        }

        public void StopAnnouncement(BinaryNumber networkID)
        {
            lock (_announceNetworkIDs)
            {
                _announceNetworkIDs.Remove(networkID);
            }
        }

        #endregion

        #region private

        private void ClearCache()
        {
            lock (_queryPacketCache)
            {
                List<DiscoveryPacket> expiredPackets = new List<DiscoveryPacket>(5);

                foreach (DiscoveryPacket packet in _queryPacketCache)
                {
                    if (packet.IsExpired())
                        expiredPackets.Add(packet);
                }

                foreach (DiscoveryPacket packet in expiredPackets)
                {
                    _queryPacketCache.Remove(packet);
                }
            }

            lock (_peerInfoCache)
            {
                List<PeerInfo> expiredPeerInfo = new List<PeerInfo>(5);

                foreach (PeerInfo peerInfo in _peerInfoCache)
                {
                    if (peerInfo.IsExpired())
                        expiredPeerInfo.Add(peerInfo);
                }

                foreach (PeerInfo peerInfo in expiredPeerInfo)
                {
                    _peerInfoCache.Remove(peerInfo);
                }
            }
        }

        private void AnnounceAsync(BinaryNumber[] networkIDs, NetworkInfo[] networks, int times)
        {
            ThreadPool.QueueUserWorkItem(AnnounceAsync, new object[] { networkIDs, networks, times });
        }

        private void AnnounceAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                BinaryNumber[] networkIDs = parameters[0] as BinaryNumber[];
                NetworkInfo[] networks = parameters[1] as NetworkInfo[];
                int times = (int)parameters[2];

                Announce(networkIDs, networks, times);
            }
            catch
            { }
        }

        private void Announce(BinaryNumber[] networkIDs, NetworkInfo[] networks, int times)
        {
            for (int i = 0; i < times; i++)
            {
                foreach (BinaryNumber networkID in networkIDs)
                {
                    DiscoveryPacket queryPacket = DiscoveryPacket.CreateQueryPacket();

                    lock (_queryPacketCache)
                    {
                        _queryPacketCache.Add(queryPacket);
                    }

                    byte[] packet = queryPacket.ToArray(networkID);

                    if (networks == null)
                    {
                        _listener.Broadcast(packet, 0, packet.Length);
                    }
                    else
                    {
                        foreach (NetworkInfo network in networks)
                        {
                            _listener.BroadcastTo(packet, 0, packet.Length, network);
                        }
                    }
                }

                if (i < times - 1)
                    Thread.Sleep(ANNOUNCEMENT_RETRY_INTERVAL);
            }
        }

        private void SendResponse(DiscoveryPacket receivedQueryPacket, BinaryNumber networkID, IPAddress remotePeerIP)
        {
            NetworkInfo network = NetUtilities.GetNetworkInfo(remotePeerIP);

            DiscoveryPacket responsePacket = DiscoveryPacket.CreateResponsePacket(_announcePort, receivedQueryPacket.Challenge);
            byte[] packet = responsePacket.ToArray(networkID, network.LocalIP);

            _listener.SendTo(packet, 0, packet.Length, remotePeerIP);
        }

        private void AnnouncementTimerCallBack(object state)
        {
            try
            {
                ClearCache(); //periodic cache clearing

                BinaryNumber[] networkIDs;

                lock (_announceNetworkIDs)
                {
                    networkIDs = _announceNetworkIDs.ToArray();
                }

                if (networkIDs.Length > 0)
                    Announce(networkIDs, null, ANNOUNCEMENT_RETRY_COUNT);
            }
            catch
            { }
            finally
            {
                if (_announcementTimer != null)
                    _announcementTimer.Change(ANNOUNCEMENT_INTERVAL, Timeout.Infinite);
            }
        }

        private void listener_BroadcastNetworkDiscovered(NetworkInfo[] networks)
        {
            try
            {
                BinaryNumber[] networkIDs;

                lock (_trackedNetworkIDs)
                {
                    networkIDs = _trackedNetworkIDs.ToArray();
                }

                if (networkIDs.Length > 0)
                    AnnounceAsync(networkIDs, networks, ANNOUNCEMENT_RETRY_COUNT);
            }
            catch
            { }
        }

        private void listener_ReceivedPacket(DiscoveryPacket packet, IPAddress remotePeerIP)
        {
            if (packet.IsResponse)
            {
                #region response process

                lock (_queryPacketCache)
                {
                    int i = _queryPacketCache.IndexOf(packet);
                    if (i < 0)
                        return;

                    DiscoveryPacket queryPacket = _queryPacketCache[i];
                    if (queryPacket.IsExpired())
                        return;
                }

                BinaryNumber foundNetworkID = null;

                lock (_announceNetworkIDs)
                {
                    foreach (BinaryNumber networkID in _announceNetworkIDs)
                    {
                        if (packet.IsResponseValid(networkID, remotePeerIP))
                        {
                            foundNetworkID = networkID;
                            break;
                        }
                    }
                }

                if (foundNetworkID != null)
                {
                    IPEndPoint peerEP = new IPEndPoint(remotePeerIP, packet.ServicePort);
                    PeerInfo peerInfo = new PeerInfo(peerEP, foundNetworkID);

                    bool peerInCache;

                    lock (_peerInfoCache)
                    {
                        if (_peerInfoCache.Contains(peerInfo))
                        {
                            PeerInfo existingPeerInfo = _peerInfoCache[_peerInfoCache.IndexOf(peerInfo)];

                            if (existingPeerInfo.IsExpired())
                            {
                                peerInCache = false;
                                _peerInfoCache.Remove(existingPeerInfo);
                            }
                            else
                            {
                                peerInCache = true;
                            }
                        }
                        else
                        {
                            peerInCache = false;
                        }
                    }

                    if (!peerInCache)
                    {
                        lock (_peerInfoCache)
                        {
                            _peerInfoCache.Add(peerInfo);
                        }

                        PeerDiscovered(this, peerEP, foundNetworkID);
                    }
                }

                #endregion
            }
            else
            {
                #region query process

                BinaryNumber foundNetworkID = null;

                lock (_trackedNetworkIDs)
                {
                    foreach (BinaryNumber networkID in _trackedNetworkIDs)
                    {
                        if (packet.IsThisNetwork(networkID))
                        {
                            foundNetworkID = networkID;
                            break;
                        }
                    }
                }

                if (foundNetworkID != null)
                    SendResponse(packet, foundNetworkID, remotePeerIP);

                #endregion
            }
        }

        #endregion

        class Listener : IDisposable
        {
            #region events

            public event DiscoveredBroadcastNetwork BroadcastNetworkDiscovered;
            public event ReceivedPacket ReceivedPacket;

            #endregion

            #region variables

            const string IPV6_MULTICAST_IP = "FF12::1";

            int _listenerPort;

            Socket _udpListener;
            Thread _udpListenerThread;

            List<NetworkInfo> _networks;
            Timer _networkWatcher;

            #endregion

            #region constructor

            public Listener(int listenerPort)
            {
                _listenerPort = listenerPort;

                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        if (Environment.OSVersion.Version.Major < 6)
                        {
                            //below vista
                            _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        }
                        else
                        {
                            //vista & above
                            _udpListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                            _udpListener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                        }
                        break;

                    case PlatformID.Unix: //mono framework
                        if (Socket.OSSupportsIPv6)
                            _udpListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                        else
                            _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                        break;

                    default: //unknown
                        _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        break;
                }

                _udpListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                if (_udpListener.AddressFamily == AddressFamily.InterNetwork)
                {
                    _udpListener.Bind(new IPEndPoint(IPAddress.Any, listenerPort));
                }
                else
                {
                    _udpListener.Bind(new IPEndPoint(IPAddress.IPv6Any, listenerPort));

                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if ((nic.OperationalStatus == OperationalStatus.Up) && (nic.Supports(NetworkInterfaceComponent.IPv6)) && nic.SupportsMulticast)
                        {
                            try
                            {
                                _udpListener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(IPAddress.Parse(IPV6_MULTICAST_IP), nic.GetIPProperties().GetIPv6Properties().Index));
                            }
                            catch
                            { }
                        }
                    }
                }

                _networks = NetUtilities.GetNetworkInfo();

                _udpListenerThread = new Thread(RecvDataAsync);
                _udpListenerThread.IsBackground = true;
                _udpListenerThread.Start(_udpListener);

                _networkWatcher = new Timer(NetworkWatcher, null, NETWORK_WATCHER_INTERVAL, Timeout.Infinite);
            }

            #endregion

            #region IDisposable

            ~Listener()
            {
                Dispose(false);
            }

            protected bool _disposed = false;

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (_networkWatcher != null)
                    {
                        _networkWatcher.Dispose();
                        _networkWatcher = null;
                    }

                    if (_udpListener != null)
                        _udpListener.Dispose();

                    _disposed = true;
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            #endregion

            #region private

            private void NetworkWatcher(object state)
            {
                try
                {
                    List<NetworkInfo> currentNetworks = NetUtilities.GetNetworkInfo();
                    List<NetworkInfo> newNetworks = new List<NetworkInfo>();

                    lock (_networks)
                    {
                        foreach (NetworkInfo currentNetwork in currentNetworks)
                        {
                            if (!_networks.Contains(currentNetwork))
                            {
                                newNetworks.Add(currentNetwork);
                            }
                        }

                        _networks.Clear();
                        _networks.AddRange(currentNetworks);
                    }

                    if (newNetworks.Count > 0)
                        BroadcastNetworkDiscovered(newNetworks.ToArray());
                }
                catch
                { }
                finally
                {
                    if (_networkWatcher != null)
                        _networkWatcher.Change(NETWORK_WATCHER_INTERVAL, Timeout.Infinite);
                }
            }

            private void RecvDataAsync(object parameter)
            {
                Socket udpListener = parameter as Socket;

                EndPoint remoteEP = null;
                FixMemoryStream dataRecv = new FixMemoryStream(BUFFER_MAX_SIZE);
                int bytesRecv;

                if (udpListener.AddressFamily == AddressFamily.InterNetwork)
                    remoteEP = new IPEndPoint(IPAddress.Any, 0);
                else
                    remoteEP = new IPEndPoint(IPAddress.IPv6Any, 0);

                try
                {
                    while (true)
                    {
                        //receive message from remote
                        bytesRecv = udpListener.ReceiveFrom(dataRecv.Buffer, ref remoteEP);

                        if (bytesRecv > 0)
                        {
                            IPAddress peerIP = (remoteEP as IPEndPoint).Address;

                            if (NetUtilities.IsIPv4MappedIPv6Address(peerIP))
                                peerIP = NetUtilities.ConvertFromIPv4MappedIPv6Address(peerIP);

                            bool isSelf = false;

                            lock (_networks)
                            {
                                foreach (NetworkInfo network in _networks)
                                {
                                    if (network.LocalIP.Equals(peerIP))
                                    {
                                        isSelf = true;
                                        break;
                                    }
                                }
                            }

                            if (isSelf)
                                continue;

                            dataRecv.Position = 0;
                            dataRecv.SetLength(bytesRecv);

                            try
                            {
                                ReceivedPacket(new DiscoveryPacket(dataRecv), peerIP);
                            }
                            catch (Exception ex)
                            {
                                Debug.Write("LocalPeerDiscovery.Listner.RecvDataAsync", ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Write("LocalPeerDiscovery.Listner.RecvDataAsync", ex);
                }
            }

            #endregion

            #region public

            public void SendTo(byte[] buffer, int offset, int count, IPAddress remoteIP)
            {
                if ((_udpListener.AddressFamily == AddressFamily.InterNetworkV6) && (remoteIP.AddressFamily == AddressFamily.InterNetwork))
                    remoteIP = NetUtilities.ConvertToIPv4MappedIPv6Address(remoteIP);

                try
                {
                    _udpListener.SendTo(buffer, offset, count, SocketFlags.None, new IPEndPoint(remoteIP, _listenerPort));
                }
                catch (Exception ex)
                {
                    Debug.Write("LocalPeerDiscovery.Listener.SendTo", ex);
                }
            }

            public void BroadcastTo(byte[] buffer, int offset, int count, NetworkInfo network)
            {
                if (network.LocalIP.AddressFamily == AddressFamily.InterNetwork)
                {
                    IPAddress broadcastIP = network.BroadcastIP;

                    if (_udpListener.AddressFamily == AddressFamily.InterNetworkV6)
                        broadcastIP = NetUtilities.ConvertToIPv4MappedIPv6Address(broadcastIP);

                    try
                    {
                        _udpListener.SendTo(buffer, offset, count, SocketFlags.None, new IPEndPoint(broadcastIP, _listenerPort));
                    }
                    catch (Exception ex)
                    {
                        Debug.Write("LocalPeerDiscovery.Listener.BroadcastTo", ex);
                    }
                }
                else
                {
                    try
                    {
                        _udpListener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, network.Interface.GetIPProperties().GetIPv6Properties().Index);
                        _udpListener.SendTo(buffer, offset, count, SocketFlags.None, new IPEndPoint(IPAddress.Parse(IPV6_MULTICAST_IP), _listenerPort));
                    }
                    catch (Exception ex)
                    {
                        Debug.Write("LocalPeerDiscovery.Listener.BroadcastTo", ex);
                    }
                }
            }

            public void Broadcast(byte[] buffer, int offset, int count)
            {
                lock (_networks)
                {
                    foreach (NetworkInfo network in _networks)
                    {
                        if (network.LocalIP.AddressFamily == AddressFamily.InterNetwork)
                        {
                            IPAddress broadcastIP = network.BroadcastIP;

                            if (_udpListener.AddressFamily == AddressFamily.InterNetworkV6)
                                broadcastIP = NetUtilities.ConvertToIPv4MappedIPv6Address(broadcastIP);

                            //do broadcast
                            try
                            {
                                _udpListener.SendTo(buffer, offset, count, SocketFlags.None, new IPEndPoint(broadcastIP, _listenerPort));
                            }
                            catch (Exception ex)
                            {
                                Debug.Write("LocalPeerDiscovery.Listener.Broadcast", ex);
                            }
                        }
                        else
                        {
                            //do multicast
                            try
                            {
                                _udpListener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, network.Interface.GetIPProperties().GetIPv6Properties().Index);
                                _udpListener.SendTo(buffer, offset, count, SocketFlags.None, new IPEndPoint(IPAddress.Parse(IPV6_MULTICAST_IP), _listenerPort));
                            }
                            catch (Exception ex)
                            {
                                Debug.Write("LocalPeerDiscovery.Listener.Broadcast", ex);
                            }
                        }
                    }
                }
            }

            #endregion
        }

        class PeerInfo
        {
            #region variables

            IPEndPoint _peerEP;
            BinaryNumber _networkID;

            DateTime _dateCreated;

            #endregion

            #region constructor

            public PeerInfo(IPEndPoint peerEP, BinaryNumber networkID)
            {
                _peerEP = peerEP;
                _networkID = networkID;

                _dateCreated = DateTime.UtcNow;
            }

            #endregion

            #region public

            public bool IsExpired()
            {
                return (DateTime.UtcNow > _dateCreated.AddMilliseconds(ANNOUNCEMENT_INTERVAL));
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as PeerInfo);
            }

            public bool Equals(PeerInfo obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;

                if (ReferenceEquals(this, obj))
                    return true;

                if (!_peerEP.Equals(obj._peerEP))
                    return false;

                if (!_networkID.Equals(obj._networkID))
                    return false;

                return true;
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            #endregion
        }

        class DiscoveryPacket
        {
            #region variables

            bool _isResponse;
            ushort _servicePort;
            BinaryNumber _challenge;
            BinaryNumber _hmac;

            DateTime _dateCreated;

            #endregion

            #region constructor

            private DiscoveryPacket(bool isResponse, ushort servicePort, BinaryNumber challenge)
            {
                _isResponse = isResponse;
                _servicePort = servicePort;
                _challenge = challenge;

                _dateCreated = DateTime.UtcNow;
            }

            public DiscoveryPacket(Stream s)
            {
                switch (s.ReadByte())
                {
                    case 5: //query
                        {
                            byte[] bufferChallenge = new byte[32];
                            byte[] bufferHmac = new byte[32];

                            OffsetStream.StreamRead(s, bufferChallenge, 0, 32);
                            OffsetStream.StreamRead(s, bufferHmac, 0, 32);

                            _isResponse = false;
                            _challenge = new BinaryNumber(bufferChallenge);
                            _hmac = new BinaryNumber(bufferHmac);
                        }
                        break;

                    case 6: //response
                        {
                            byte[] bufferServicePort = new byte[2];
                            byte[] bufferChallenge = new byte[32];
                            byte[] bufferHmac = new byte[32];

                            OffsetStream.StreamRead(s, bufferServicePort, 0, 2);
                            OffsetStream.StreamRead(s, bufferChallenge, 0, 32);
                            OffsetStream.StreamRead(s, bufferHmac, 0, 32);

                            _isResponse = true;
                            _servicePort = BitConverter.ToUInt16(bufferServicePort, 0);
                            _challenge = new BinaryNumber(bufferChallenge);
                            _hmac = new BinaryNumber(bufferHmac);
                        }
                        break;

                    case -1:
                        throw new EndOfStreamException();

                    default:
                        throw new IOException("Invalid local discovery packet.");
                }
            }

            #endregion

            #region static

            public static DiscoveryPacket CreateQueryPacket()
            {
                return new DiscoveryPacket(false, 0, BinaryNumber.GenerateRandomNumber256());
            }

            public static DiscoveryPacket CreateResponsePacket(ushort servicePort, BinaryNumber challenge)
            {
                return new DiscoveryPacket(true, servicePort, challenge);
            }

            #endregion

            #region public

            public bool IsThisNetwork(BinaryNumber networkID)
            {
                if (_isResponse)
                    throw new Exception("Packet is not a query.");

                BinaryNumber computedHmac;

                using (HMACSHA256 hmacSHA256 = new HMACSHA256(networkID.Number))
                {
                    computedHmac = new BinaryNumber(hmacSHA256.ComputeHash(_challenge.Number)); //computed hmac
                }

                return _hmac.Equals(computedHmac);
            }

            public bool IsResponseValid(BinaryNumber networkID, IPAddress remotePeerIP)
            {
                if (!_isResponse)
                    throw new Exception("Packet is not a response.");

                byte[] servicePort = BitConverter.GetBytes(_servicePort);
                BinaryNumber computedHmac;

                using (HMACSHA256 hmacSHA256 = new HMACSHA256(networkID.Number))
                {
                    using (MemoryStream mS2 = new MemoryStream(50))
                    {
                        mS2.Write(_challenge.Number, 0, 32); //query_challenge

                        byte[] ipAddr = remotePeerIP.GetAddressBytes();
                        mS2.Write(ipAddr, 0, ipAddr.Length); //peer_ip_address

                        mS2.Write(servicePort, 0, 2); //service_port

                        mS2.Position = 0;
                        computedHmac = new BinaryNumber(hmacSHA256.ComputeHash(mS2)); //computed hmac
                    }
                }

                return _hmac.Equals(computedHmac);
            }

            public byte[] ToArray(BinaryNumber networkID, IPAddress senderPeerIP = null)
            {
                using (MemoryStream mS = new MemoryStream(BUFFER_MAX_SIZE))
                {
                    if (_isResponse)
                    {
                        byte[] servicePort = BitConverter.GetBytes(_servicePort);

                        mS.WriteByte(6); //version
                        mS.Write(servicePort, 0, 2); //service_port
                        mS.Write(_challenge.Number, 0, 32); //query_challenge

                        using (HMACSHA256 hmacSHA256 = new HMACSHA256(networkID.Number))
                        {
                            using (MemoryStream mS2 = new MemoryStream(50))
                            {
                                mS2.Write(_challenge.Number, 0, 32); //query_challenge

                                byte[] ipAddr = senderPeerIP.GetAddressBytes();
                                mS2.Write(ipAddr, 0, ipAddr.Length); //peer_ip_address

                                mS2.Write(servicePort, 0, 2); //service_port

                                mS2.Position = 0;
                                mS.Write(hmacSHA256.ComputeHash(mS2), 0, 32); //hmac
                            }
                        }
                    }
                    else
                    {
                        mS.WriteByte(5); //version
                        mS.Write(_challenge.Number, 0, 32); //challenge

                        using (HMACSHA256 hmacSHA256 = new HMACSHA256(networkID.Number))
                        {
                            mS.Write(hmacSHA256.ComputeHash(_challenge.Number), 0, 32); //hmac
                        }
                    }

                    return mS.ToArray();
                }
            }

            public bool IsExpired()
            {
                return (DateTime.UtcNow > _dateCreated.AddMilliseconds(ANNOUNCEMENT_RETRY_INTERVAL));
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as DiscoveryPacket);
            }

            public bool Equals(DiscoveryPacket obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;

                if (ReferenceEquals(this, obj))
                    return true;

                if (!_challenge.Equals(obj._challenge))
                    return false;

                return true;
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            #endregion

            #region properties

            public bool IsResponse
            { get { return _isResponse; } }

            public ushort ServicePort
            { get { return _servicePort; } }

            public BinaryNumber Challenge
            { get { return _challenge; } }

            #endregion
        }
    }
}
