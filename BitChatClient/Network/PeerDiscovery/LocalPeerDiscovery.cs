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
using TechnitiumLibrary.Net;

namespace BitChatClient.Network.PeerDiscovery
{
    public delegate void DiscoveredPeerInfo(LocalPeerDiscovery sender, IPEndPoint peerEP, BinaryID networkID);

    public class LocalPeerDiscovery
    {
        #region events

        private delegate void ReceivedPeerInfo(IPEndPoint peerEP, byte[] challenge, BinaryID recvHMAC, bool isReply);
        private delegate void DiscoveredBroadcastNetwork(IPAddress[] broadcastIPs);

        public event DiscoveredPeerInfo PeerDiscovered;

        #endregion

        #region variables

        const int ANNOUNCEMENT_INTERVAL = 5000;
        const int ANNOUNCEMENT_COUNT = 5;
        const int NETWORK_WATCHER_INTERVAL = 30000;
        const int BUFFER_MAX_SIZE = 128;

        static Listener _listener;

        ushort _announcePort;
        List<BinaryID> _trackedNetworkIDs = new List<BinaryID>(5);
        List<PeerInfo> _peerInfoCache = new List<PeerInfo>(5);

        #endregion

        #region constructor

        public LocalPeerDiscovery(int announcePort)
        {
            _announcePort = Convert.ToUInt16(announcePort);

            _listener.ReceivedPeerInfo += _listener_ReceivedPeerInfo;
            _listener.BroadcastNetworkDiscovered += _listener_BroadcastNetworkDiscovered;
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

        public void StartTracking(BinaryID networkID)
        {
            lock (_trackedNetworkIDs)
            {
                int i = FindChatHashIndex(networkID);
                if (i < 0)
                    _trackedNetworkIDs.Add(networkID);

                ThreadPool.QueueUserWorkItem(AnnounceAsync, new object[] { networkID, null, ANNOUNCEMENT_COUNT, false });
            }
        }

        public void StopTracking(BinaryID networkID)
        {
            lock (_trackedNetworkIDs)
            {
                int i = FindChatHashIndex(networkID);
                if (i > -1)
                    _trackedNetworkIDs.RemoveAt(i);
            }
        }

        #endregion

        #region private

        private int FindChatHashIndex(BinaryID networkID)
        {
            for (int i = 0; i < _trackedNetworkIDs.Count; i++)
            {
                if (_trackedNetworkIDs[i].Equals(networkID))
                    return i;
            }

            return -1;
        }

        private void AnnounceAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                BinaryID networkID = parameters[0] as BinaryID;
                IPAddress[] remoteIPs = parameters[1] as IPAddress[];
                int times = (int)parameters[2];
                bool isReply = (bool)parameters[3];

                Announce(networkID, remoteIPs, times, isReply);
            }
            catch
            { }
        }

        private void Announce(BinaryID networkID, IPAddress[] remoteIPs, int times, bool isReply)
        {
            //CREATE ADVERTISEMENT
            byte[] challenge = BinaryID.GenerateRandomID256().ID;
            byte[] buffer = new byte[BUFFER_MAX_SIZE];

            using (MemoryStream mS = new MemoryStream(buffer))
            {
                //version 1 byte
                if (isReply)
                    mS.WriteByte(4);
                else
                    mS.WriteByte(3);

                mS.Write(BitConverter.GetBytes(_announcePort), 0, 2); //service port
                mS.Write(challenge, 0, 32); //challenge

                using (HMACSHA256 hmacSHA256 = new HMACSHA256(networkID.ID))
                {
                    byte[] hmac = hmacSHA256.ComputeHash(challenge);
                    mS.Write(hmac, 0, 32);
                }

                int length = Convert.ToInt32(mS.Position);

                //SEND ADVERTISEMENT
                for (int i = 0; i < times; i++)
                {
                    if (remoteIPs == null)
                    {
                        _listener.Broadcast(buffer, 0, length);
                    }
                    else
                    {
                        foreach (IPAddress remoteIP in remoteIPs)
                        {
                            _listener.SendTo(buffer, 0, length, remoteIP);
                        }
                    }

                    if (i < times - 1)
                        Thread.Sleep(ANNOUNCEMENT_INTERVAL);
                }
            }
        }

        private void _listener_BroadcastNetworkDiscovered(IPAddress[] broadcastIPs)
        {
            try
            {
                lock (_trackedNetworkIDs)
                {
                    foreach (BinaryID networkID in _trackedNetworkIDs)
                    {
                        ThreadPool.QueueUserWorkItem(AnnounceAsync, new object[] { networkID, broadcastIPs, ANNOUNCEMENT_COUNT, false });
                    }
                }
            }
            catch
            { }
        }

        private void _listener_ReceivedPeerInfo(IPEndPoint peerEP, byte[] challenge, BinaryID recvHMAC, bool isReply)
        {
            if (PeerDiscovered != null)
            {
                BinaryID foundNetworkID = null;

                lock (_trackedNetworkIDs)
                {
                    foreach (BinaryID networkID in _trackedNetworkIDs)
                    {
                        using (HMACSHA256 hmacSHA256 = new HMACSHA256(networkID.ID))
                        {
                            BinaryID computedHmac = new BinaryID(hmacSHA256.ComputeHash(challenge));

                            if (computedHmac.Equals(recvHMAC))
                            {
                                foundNetworkID = networkID;
                                break;
                            }
                        }
                    }
                }

                if (foundNetworkID != null)
                {
                    PeerInfo peerInfo = new PeerInfo(peerEP, foundNetworkID);

                    bool peerInCache;

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

                    if (!peerInCache)
                    {
                        _peerInfoCache.Add(peerInfo);

                        PeerDiscovered(this, peerEP, foundNetworkID);
                    }

                    if (!isReply)
                        ThreadPool.QueueUserWorkItem(AnnounceAsync, new object[] { foundNetworkID, new IPAddress[] { peerEP.Address }, 1, true });
                }
            }
        }

        #endregion

        class Listener : IDisposable
        {
            #region events

            public event DiscoveredBroadcastNetwork BroadcastNetworkDiscovered;
            public event ReceivedPeerInfo ReceivedPeerInfo;

            #endregion

            #region variables

            int _listenerPort;

            Socket _udpListener;
            Thread _listeningThread;

            List<IPAddress> _broadcastIPs;
            Timer _networkWatcher;

            #endregion

            #region constructor

            public Listener(int listenerPort)
            {
                _listenerPort = listenerPort;

                _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _udpListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
                _udpListener.Bind(new IPEndPoint(0, listenerPort));

                _listeningThread = new Thread(RecvDataAsync);
                _listeningThread.IsBackground = true;
                _listeningThread.Start();

                _broadcastIPs = NetUtilities.GetBroadcastIPList();
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
                    if (disposing)
                    {
                        _udpListener.Close();
                        _listeningThread.Abort();
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

            #region private

            private void NetworkWatcher(object state)
            {
                try
                {
                    List<IPAddress> currentBroadcastIPs = NetUtilities.GetBroadcastIPList();
                    List<IPAddress> newIPs = new List<IPAddress>();

                    lock (_broadcastIPs)
                    {
                        foreach (IPAddress currentIP in currentBroadcastIPs)
                        {
                            if (!_broadcastIPs.Contains(currentIP))
                            {
                                newIPs.Add(currentIP);
                            }
                        }

                        _broadcastIPs.Clear();
                        _broadcastIPs.AddRange(currentBroadcastIPs);
                    }

                    BroadcastNetworkDiscovered(newIPs.ToArray());
                }
                catch
                { }
                finally
                {
                    if (_networkWatcher != null)
                    {
                        _networkWatcher.Change(NETWORK_WATCHER_INTERVAL, Timeout.Infinite);
                    }
                }
            }

            private void RecvDataAsync()
            {
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] bufferRecv = new byte[BUFFER_MAX_SIZE];
                MemoryStream dataRecv = new MemoryStream(bufferRecv);
                int bytesRecv;

                while (true)
                {
                    try
                    {
                        //receive message from remote
                        bytesRecv = _udpListener.ReceiveFrom(bufferRecv, ref remoteEP);

                        IPEndPoint peerEP = remoteEP as IPEndPoint;

                        bool isSelf = false;

                        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if ((ni.OperationalStatus == OperationalStatus.Up) && (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                            {
                                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                                {
                                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                    {
                                        if (peerEP.Address.Equals(ip.Address))
                                        {
                                            isSelf = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (isSelf)
                                break;
                        }

                        if (isSelf)
                            continue;

                        if (bytesRecv > 0)
                        {
                            #region parse data

                            dataRecv.Position = 0;
                            dataRecv.SetLength(bytesRecv);

                            int version = dataRecv.ReadByte();

                            switch (version) //version
                            {
                                case 3:
                                case 4:
                                    //read remote service port
                                    byte[] data = new byte[2];
                                    dataRecv.Read(data, 0, 2);
                                    ushort servicePort = BitConverter.ToUInt16(data, 0);
                                    IPEndPoint remoteServiceEP = new IPEndPoint(peerEP.Address, servicePort);

                                    //read challenge
                                    byte[] challenge = new byte[32];
                                    dataRecv.Read(challenge, 0, 32);

                                    //read hmac
                                    BinaryID recvHMAC = new BinaryID(new byte[32]);
                                    dataRecv.Read(recvHMAC.ID, 0, 32);

                                    ReceivedPeerInfo(remoteServiceEP, challenge, recvHMAC, (version == 4));
                                    break;

                                default:
                                    continue;
                            }

                            #endregion
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        break;
                    }
                    catch
                    { }
                }
            }

            #endregion

            #region public

            public void SendTo(byte[] buffer, int offset, int count, IPAddress remoteIP)
            {
                _udpListener.SendTo(buffer, offset, count, SocketFlags.None, new IPEndPoint(remoteIP, _listenerPort));
            }

            public void Broadcast(byte[] buffer, int offset, int count)
            {
                lock (_broadcastIPs)
                {
                    foreach (IPAddress remoteIP in _broadcastIPs)
                    {
                        _udpListener.SendTo(buffer, offset, count, SocketFlags.None, new IPEndPoint(remoteIP, _listenerPort));
                    }
                }
            }

            #endregion
        }

        class PeerInfo
        {
            #region variables

            IPEndPoint _peerEP;
            BinaryID _networkID;

            DateTime _dateAdded;

            #endregion

            #region constructor

            public PeerInfo(IPEndPoint peerEP, BinaryID networkID)
            {
                _peerEP = peerEP;
                _networkID = networkID;

                _dateAdded = DateTime.UtcNow;
            }

            #endregion

            #region public

            public bool IsExpired()
            {
                return (DateTime.UtcNow > _dateAdded.AddMinutes(1));
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
    }
}
