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

namespace BitChatClient.Network.Connections
{
    delegate void DiscoveredPeerInfo(LocalPeerDiscovery sender, IPEndPoint peerEP, List<BinaryID> chatHashes);

    class LocalPeerDiscovery : IDisposable
    {
        #region events

        public event DiscoveredPeerInfo PeerDiscovered;

        #endregion

        #region variables

        const int _BUFFER_MAX_SIZE = 1024;
        const int _ANNOUNCE_INTERVAL = 30000;

        Socket _udpListener;
        Thread _listeningThread;
        Timer _AnnouncementTimer;

        ushort _peerDiscoveryPort;
        ushort _announcePort;

        List<BinaryID> _chatHashes = new List<BinaryID>(5);

        #endregion

        #region constructor

        public LocalPeerDiscovery(int peerDiscoveryPort, int announcePort)
        {
            _peerDiscoveryPort = Convert.ToUInt16(peerDiscoveryPort);
            _announcePort = Convert.ToUInt16(announcePort);

            _udpListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            _udpListener.Bind(new IPEndPoint(0, peerDiscoveryPort));

            _listeningThread = new Thread(RecvDataAsync);
            _listeningThread.IsBackground = true;
            _listeningThread.Start();
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
                    _udpListener.Close();
                    _listeningThread.Abort();

                    if (_AnnouncementTimer != null)
                    {
                        _AnnouncementTimer.Dispose();
                        _AnnouncementTimer = null;
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

        #region public

        public void StartAnnounce(BinaryID chatHash)
        {
            lock (_chatHashes)
            {
                int i = FindChatHashIndex(chatHash);
                if (i < 0)
                    _chatHashes.Add(chatHash);

                if (_chatHashes.Count > 0)
                {
                    if (_AnnouncementTimer == null)
                    {
                        _AnnouncementTimer = new Timer(AnnounceAsync, null, 1000, Timeout.Infinite);
                    }
                    else
                    {
                        _AnnouncementTimer.Dispose();
                        _AnnouncementTimer = new Timer(AnnounceAsync, null, 1000, Timeout.Infinite);
                    }
                }
            }
        }

        public void StopAnnounce(BinaryID chatHash)
        {
            lock (_chatHashes)
            {
                int i = FindChatHashIndex(chatHash);
                if (i > -1)
                    _chatHashes.RemoveAt(i);

                if ((_chatHashes.Count == 0) && (_AnnouncementTimer != null))
                {
                    _AnnouncementTimer.Dispose();
                    _AnnouncementTimer = null;
                }
            }
        }

        #endregion

        #region private

        private int FindChatHashIndex(BinaryID chatHash)
        {
            for (int i = 0; i < _chatHashes.Count; i++)
            {
                if (_chatHashes[i].Equals(chatHash))
                    return i;
            }

            return -1;
        }

        private void AnnounceAsync(object state)
        {
            try
            {
                if (_chatHashes.Count == 0)
                {
                    _AnnouncementTimer = null;
                    return;
                }

                Debug.Write("LocalPeerDiscovery.AnnounceAsync", "");

                //CREATE ADVERTISEMENT
                byte[] challenge = BinaryID.GenerateRandomID256().ID;
                byte[] buffer = new byte[_BUFFER_MAX_SIZE];

                using (MemoryStream mS = new MemoryStream(buffer))
                {
                    mS.WriteByte(2); //version 1 byte
                    mS.Write(BitConverter.GetBytes(_announcePort), 0, 2); //bitchat port
                    mS.Write(challenge, 0, 32); //challenge

                    lock (_chatHashes)
                    {
                        foreach (BinaryID chatHash in _chatHashes)
                        {
                            using (HMACSHA256 hmacSHA256 = new HMACSHA256(chatHash.ID))
                            {
                                byte[] hmac = hmacSHA256.ComputeHash(challenge);
                                mS.Write(hmac, 0, 32);
                            }
                        }
                    }

                    //BROADCAST ADVERTISEMENT ON ALL NETWORKS

                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up)
                            continue;

                        if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                            continue;

                        switch (nic.NetworkInterfaceType)
                        {
                            case NetworkInterfaceType.Ethernet:
                            case NetworkInterfaceType.Ethernet3Megabit:
                            case NetworkInterfaceType.FastEthernetT:
                            case NetworkInterfaceType.FastEthernetFx:
                            case NetworkInterfaceType.Wireless80211:
                            case NetworkInterfaceType.GigabitEthernet:
                                //for all broadcast type network
                                foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                                {
                                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                    {
                                        byte[] addr = ip.Address.GetAddressBytes();
                                        byte[] mask;

                                        try
                                        {
                                            mask = ip.IPv4Mask.GetAddressBytes();
                                        }
                                        catch (NotImplementedException)
                                        {
                                            //method not implemented in mono framework for Linux
                                            if (addr[0] == 10)
                                            {
                                                mask = new byte[] { 255, 0, 0, 0 };
                                            }
                                            else if ((addr[0] == 192) && (addr[1] == 168))
                                            {
                                                mask = new byte[] { 255, 255, 255, 0 };
                                            }
                                            else if ((addr[0] == 169) && (addr[1] == 254))
                                            {
                                                mask = new byte[] { 255, 255, 0, 0 };
                                            }
                                            else if ((addr[0] == 172) && (addr[1] > 15) && (addr[1] < 32))
                                            {
                                                mask = new byte[] { 255, 240, 0, 0 };
                                            }
                                            else
                                            {
                                                mask = new byte[] { 255, 255, 255, 0 };
                                            }
                                        }

                                        int ip_bytes = BitConverter.ToInt32(addr, 0);
                                        int mask_bytes = BitConverter.ToInt32(mask, 0); ;

                                        IPAddress broadcastIP = new IPAddress(BitConverter.GetBytes(ip_bytes | (~mask_bytes)));
                                        IPEndPoint broadcastEP = new IPEndPoint(broadcastIP, _peerDiscoveryPort);

                                        _udpListener.SendTo(buffer, 0, Convert.ToInt32(mS.Position), SocketFlags.None, broadcastEP);

                                        Debug.Write("LocalPeerDiscovery.AnnounceAsync", "on: " + broadcastEP.ToString());
                                    }
                                }
                                break;
                        }
                    }
                }

            }
            catch (ThreadAbortException)
            {
                _AnnouncementTimer = null;
            }
            catch { }
            finally
            {
                if (_AnnouncementTimer != null)
                    _AnnouncementTimer.Change(_ANNOUNCE_INTERVAL, Timeout.Infinite);
            }
        }

        private void RecvDataAsync()
        {
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] bufferRecv = new byte[_BUFFER_MAX_SIZE];
            MemoryStream dataRecv = new MemoryStream(bufferRecv);
            int bytesRecv;

            while (true)
            {
                try
                {
                    //receive message from remote
                    bytesRecv = _udpListener.ReceiveFrom(bufferRecv, ref remoteEP);

                    IPEndPoint fromEP = remoteEP as IPEndPoint;

                    bool isSelf = false;

                    foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if ((ni.OperationalStatus == OperationalStatus.Up) && (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                        {
                            foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                            {
                                if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                {
                                    if (fromEP.Address.Equals(ip.Address))
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
                        #region Parse & Process

                        dataRecv.Position = 0;
                        dataRecv.SetLength(bytesRecv);

                        switch (dataRecv.ReadByte()) //version
                        {
                            case 2:
                                //read remote service port
                                byte[] data = new byte[2];
                                dataRecv.Read(data, 0, 2);
                                ushort servicePort = BitConverter.ToUInt16(data, 0);
                                IPEndPoint remoteServiceEP = new IPEndPoint(fromEP.Address, servicePort);

                                //read challenge
                                byte[] challenge = new byte[32];
                                dataRecv.Read(challenge, 0, 32);

                                //read all hashed ChatHash
                                List<BinaryID> recvHMACs = new List<BinaryID>();

                                data = new byte[32];
                                while (dataRecv.Read(data, 0, 32) == 32)
                                {
                                    byte[] recvHMAC = new byte[32];
                                    Buffer.BlockCopy(data, 0, recvHMAC, 0, 32);
                                    recvHMACs.Add(new BinaryID(recvHMAC));
                                }

                                //compare and find valid chatHashes

                                List<BinaryID> validHashes = new List<BinaryID>();

                                lock (_chatHashes)
                                {
                                    byte[] currentChatHash = new byte[20];

                                    foreach (BinaryID chatHash in _chatHashes)
                                    {
                                        BinaryID computedHmac;

                                        using (HMACSHA256 hmacSHA256 = new HMACSHA256(chatHash.ID))
                                        {
                                            computedHmac = new BinaryID(hmacSHA256.ComputeHash(challenge));
                                        }

                                        //find match in received hashes
                                        foreach (BinaryID recvHMAC in recvHMACs)
                                        {
                                            if (recvHMAC.Equals(computedHmac))
                                            {
                                                validHashes.Add(chatHash);
                                                break;
                                            }
                                        }
                                    }
                                }

                                if (validHashes.Count > 0)
                                {
                                    if (PeerDiscovered != null)
                                        PeerDiscovered(this, remoteServiceEP, validHashes);
                                }

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
    }
}
