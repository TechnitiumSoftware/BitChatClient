/*
Technitium Bit Chat
Copyright (C) 2017  Shreyas Zare (shreyas@technitium.com)

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

/*  Connection Frame
*   0                8                                 168                               184
*  +----------------+---------------//----------------+----------------+----------------+---------------//----------------+
*  | signal (1 byte)| channel name  (20 bytes)        |     data length (uint16)        |              data               |
*  +----------------+---------------//----------------+----------------+----------------+---------------//----------------+
*  
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TechnitiumLibrary.IO;

namespace BitChatCore.Network.Connections
{
    delegate void BitChatNetworkInvitation(BinaryID hashedPeerEmailAddress, IPEndPoint peerEP, string message);
    delegate void BitChatNetworkChannelRequest(Connection connection, BinaryID channelName, Stream channel);
    delegate void TcpRelayPeersAvailable(Connection viaConnection, BinaryID channelName, List<IPEndPoint> peerEPs);

    enum SignalType : byte
    {
        NOOP = 0,

        ConnectChannelBitChatNetwork = 1,
        ChannelData = 2,
        DisconnectChannel = 3,
        ConnectChannelProxyTunnel = 4,
        ConnectChannelProxyConnection = 5,

        PeerStatusQuery = 8,
        PeerStatusAvailable = 9,

        StartTcpRelay = 10,
        StopTcpRelay = 11,
        TcpRelayResponseSuccess = 12,
        TcpRelayResponsePeerList = 13,

        BitChatNetworkInvitation = 15
    }

    class Connection : IDisposable
    {
        #region events

        public event BitChatNetworkInvitation BitChatNetworkInvitation;
        public event BitChatNetworkChannelRequest BitChatNetworkChannelRequest;
        public event TcpRelayPeersAvailable TcpRelayPeersAvailable;
        public event EventHandler Disposed;

        #endregion

        #region variables

        readonly Stream _baseStream;
        readonly BinaryID _remotePeerID;
        readonly IPEndPoint _remotePeerEP;
        readonly ConnectionManager _connectionManager;

        readonly Dictionary<BinaryID, ChannelStream> _channels = new Dictionary<BinaryID, ChannelStream>();

        Thread _readThread;

        readonly Dictionary<BinaryID, object> _peerStatusLockList = new Dictionary<BinaryID, object>();
        readonly List<Joint> _proxyTunnelJointList = new List<Joint>();

        readonly Dictionary<BinaryID, object> _tcpRelayRequestLockList = new Dictionary<BinaryID, object>();
        readonly Dictionary<BinaryID, TcpRelayService> _tcpRelays = new Dictionary<BinaryID, TcpRelayService>();

        int _channelWriteTimeout = 30000;

        #endregion

        #region constructor

        public Connection(Stream baseStream, BinaryID remotePeerID, IPEndPoint remotePeerEP, ConnectionManager connectionManager)
        {
            _baseStream = baseStream;
            _remotePeerID = remotePeerID;
            _remotePeerEP = remotePeerEP;
            _connectionManager = connectionManager;
        }

        #endregion

        #region IDisposable support

        ~Connection()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            lock (this)
            {
                if (!_disposed)
                {
                    //dispose all channels
                    List<ChannelStream> streamList = new List<ChannelStream>();

                    lock (_channels)
                    {
                        foreach (KeyValuePair<BinaryID, ChannelStream> channel in _channels)
                            streamList.Add(channel.Value);
                    }

                    foreach (ChannelStream stream in streamList)
                    {
                        try
                        {
                            stream.Dispose();
                        }
                        catch
                        { }
                    }

                    //remove this connection from tcp relays
                    lock (_tcpRelays)
                    {
                        foreach (TcpRelayService relay in _tcpRelays.Values)
                        {
                            relay.StopTcpRelay(this);
                        }

                        _tcpRelays.Clear();
                    }

                    //dispose base stream
                    Monitor.Enter(_baseStream);
                    try
                    {
                        _baseStream.Dispose();
                    }
                    catch
                    { }
                    finally
                    {
                        Monitor.Exit(_baseStream);
                    }

                    _disposed = true;

                    Disposed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        #endregion

        #region private

        private void WriteFrame(SignalType signalType, BinaryID channelName, byte[] buffer, int offset, int count)
        {
            int frameCount = ConnectionManager.BUFFER_SIZE;

            lock (_baseStream)
            {
                do
                {
                    if (count < frameCount)
                        frameCount = count;

                    _baseStream.WriteByte((byte)signalType); //write frame signal
                    _baseStream.Write(channelName.ID, 0, 20); //write channel name
                    _baseStream.Write(BitConverter.GetBytes(Convert.ToUInt16(frameCount)), 0, 2); //write data length

                    if (frameCount > 0)
                        _baseStream.Write(buffer, offset, frameCount); //write data

                    //flush base stream
                    _baseStream.Flush();

                    offset += frameCount;
                    count -= frameCount;
                }
                while (count > 0);
            }
        }

        private void ReadFrameAsync()
        {
            try
            {
                //frame parameters
                int signalType;
                BinaryID channelName = new BinaryID(new byte[20]);
                ushort dataLength;
                byte[] dataLengthBuffer = new byte[2];

                while (true)
                {
                    #region Read frame from base stream

                    //read frame signal
                    signalType = _baseStream.ReadByte();
                    if (signalType == -1)
                        return; //End of stream

                    //read channel name
                    OffsetStream.StreamRead(_baseStream, channelName.ID, 0, 20);

                    //read data length
                    OffsetStream.StreamRead(_baseStream, dataLengthBuffer, 0, 2);
                    dataLength = BitConverter.ToUInt16(dataLengthBuffer, 0);

                    //read data stream
                    OffsetStream dataStream = null;

                    if (dataLength > 0)
                        dataStream = new OffsetStream(_baseStream, 0, dataLength, true, false);

                    #endregion

                    switch ((SignalType)signalType)
                    {
                        case SignalType.NOOP:
                            break;

                        case SignalType.ConnectChannelBitChatNetwork:
                            #region ConnectChannelBitChatNetwork

                            lock (_channels)
                            {
                                if (_channels.ContainsKey(channelName))
                                {
                                    WriteFrame(SignalType.DisconnectChannel, channelName, null, 0, 0);
                                }
                                else
                                {
                                    ChannelStream channel = new ChannelStream(this, channelName.Clone());
                                    _channels.Add(channel.ChannelName, channel);

                                    BitChatNetworkChannelRequest.BeginInvoke(this, channel.ChannelName, channel, null, null);
                                }
                            }

                            //check if tcp relay is hosted for the channel. reply back tcp relay peers list if available
                            try
                            {
                                List<IPEndPoint> peerEPs = TcpRelayService.GetPeerEPs(channelName, this);

                                if ((peerEPs != null) && (peerEPs.Count > 0))
                                {
                                    using (MemoryStream mS = new MemoryStream(128))
                                    {
                                        mS.WriteByte(Convert.ToByte(peerEPs.Count));

                                        foreach (IPEndPoint peerEP in peerEPs)
                                        {
                                            IPEndPointParser.WriteTo(peerEP, mS);
                                        }

                                        byte[] data = mS.ToArray();

                                        WriteFrame(SignalType.TcpRelayResponsePeerList, channelName, data, 0, data.Length);
                                    }
                                }
                            }
                            catch
                            { }

                            #endregion
                            break;

                        case SignalType.ChannelData:
                            #region ChannelData

                            try
                            {
                                ChannelStream channel = null;

                                lock (_channels)
                                {
                                    channel = _channels[channelName];
                                }

                                channel.FeedReadBuffer(dataStream, _channelWriteTimeout);
                            }
                            catch
                            { }

                            #endregion
                            break;

                        case SignalType.DisconnectChannel:
                            #region DisconnectChannel

                            try
                            {
                                ChannelStream channel;

                                lock (_channels)
                                {
                                    channel = _channels[channelName];
                                    _channels.Remove(channelName);
                                }

                                channel.SetDisconnected();
                                channel.Dispose();
                            }
                            catch
                            { }

                            #endregion
                            break;

                        case SignalType.ConnectChannelProxyTunnel:
                            #region ConnectChannelProxyTunnel
                            {
                                ChannelStream remoteChannel1 = null;

                                lock (_channels)
                                {
                                    if (_channels.ContainsKey(channelName))
                                    {
                                        WriteFrame(SignalType.DisconnectChannel, channelName, null, 0, 0);
                                    }
                                    else
                                    {
                                        //add first stream into list
                                        remoteChannel1 = new ChannelStream(this, channelName.Clone());
                                        _channels.Add(remoteChannel1.ChannelName, remoteChannel1);
                                    }
                                }

                                if (remoteChannel1 != null)
                                {
                                    IPEndPoint tunnelToRemotePeerEP = ConvertChannelNameToEp(channelName); //get remote peer ep                                    
                                    Connection remotePeerConnection = _connectionManager.GetExistingConnection(tunnelToRemotePeerEP); //get remote channel service

                                    if (remotePeerConnection == null)
                                    {
                                        remoteChannel1.Dispose();
                                    }
                                    else
                                    {
                                        try
                                        {
                                            //get remote proxy connection channel stream
                                            ChannelStream remoteChannel2 = remotePeerConnection.RequestProxyConnection(_remotePeerEP);

                                            //join current and remote stream
                                            Joint joint = new Joint(remoteChannel1, remoteChannel2);
                                            joint.Disposed += joint_Disposed;

                                            lock (_proxyTunnelJointList)
                                            {
                                                _proxyTunnelJointList.Add(joint);
                                            }

                                            joint.Start();
                                        }
                                        catch
                                        {
                                            remoteChannel1.Dispose();
                                        }
                                    }
                                }
                            }
                            #endregion
                            break;

                        case SignalType.ConnectChannelProxyConnection:
                            #region ConnectChannelProxyConnection

                            lock (_channels)
                            {
                                if (_channels.ContainsKey(channelName))
                                {
                                    WriteFrame(SignalType.DisconnectChannel, channelName, null, 0, 0);
                                }
                                else
                                {
                                    //add proxy channel stream into list
                                    ChannelStream channel = new ChannelStream(this, channelName.Clone());
                                    _channels.Add(channel.ChannelName, channel);

                                    //pass channel as connection async
                                    ThreadPool.QueueUserWorkItem(AcceptProxyConnectionAsync, channel);
                                }
                            }

                            #endregion
                            break;

                        case SignalType.PeerStatusQuery:
                            #region PeerStatusQuery

                            try
                            {
                                if (_connectionManager.IsPeerConnectionAvailable(ConvertChannelNameToEp(channelName)))
                                    WriteFrame(SignalType.PeerStatusAvailable, channelName, null, 0, 0);
                            }
                            catch
                            { }

                            #endregion
                            break;

                        case SignalType.PeerStatusAvailable:
                            #region PeerStatusAvailable

                            try
                            {
                                lock (_peerStatusLockList)
                                {
                                    object lockObject = _peerStatusLockList[channelName];

                                    lock (lockObject)
                                    {
                                        Monitor.Pulse(lockObject);
                                    }
                                }
                            }
                            catch
                            { }

                            #endregion
                            break;

                        case SignalType.StartTcpRelay:
                            #region StartTcpRelay
                            {
                                BinaryID[] networkIDs;
                                Uri[] trackerURIs;

                                //read network id list
                                networkIDs = new BinaryID[dataStream.ReadByte()];
                                byte[] XORnetworkID = new byte[20];

                                for (int i = 0; i < networkIDs.Length; i++)
                                {
                                    OffsetStream.StreamRead(dataStream, XORnetworkID, 0, 20);

                                    byte[] networkID = new byte[20];

                                    for (int j = 0; j < 20; j++)
                                    {
                                        networkID[j] = (byte)(channelName.ID[j] ^ XORnetworkID[j]);
                                    }

                                    networkIDs[i] = new BinaryID(networkID);
                                }

                                //read tracker uri list
                                trackerURIs = new Uri[dataStream.ReadByte()];
                                byte[] data = new byte[255];

                                for (int i = 0; i < trackerURIs.Length; i++)
                                {
                                    int length = dataStream.ReadByte();
                                    OffsetStream.StreamRead(dataStream, data, 0, length);

                                    trackerURIs[i] = new Uri(Encoding.UTF8.GetString(data, 0, length));
                                }

                                lock (_tcpRelays)
                                {
                                    foreach (BinaryID networkID in networkIDs)
                                    {
                                        if (!_tcpRelays.ContainsKey(networkID))
                                        {
                                            TcpRelayService relay = TcpRelayService.StartTcpRelay(networkID, this, _connectionManager.LocalPort, _connectionManager.DhtClient, trackerURIs);
                                            _tcpRelays.Add(networkID, relay);
                                        }
                                    }
                                }

                                WriteFrame(SignalType.TcpRelayResponseSuccess, channelName, null, 0, 0);
                            }
                            #endregion
                            break;

                        case SignalType.StopTcpRelay:
                            #region StopTcpRelay
                            {
                                BinaryID[] networkIDs;

                                //read network id list
                                networkIDs = new BinaryID[dataStream.ReadByte()];
                                byte[] XORnetworkID = new byte[20];

                                for (int i = 0; i < networkIDs.Length; i++)
                                {
                                    OffsetStream.StreamRead(dataStream, XORnetworkID, 0, 20);

                                    byte[] networkID = new byte[20];

                                    for (int j = 0; j < 20; j++)
                                    {
                                        networkID[j] = (byte)(channelName.ID[j] ^ XORnetworkID[j]);
                                    }

                                    networkIDs[i] = new BinaryID(networkID);
                                }

                                lock (_tcpRelays)
                                {
                                    foreach (BinaryID networkID in networkIDs)
                                    {
                                        if (_tcpRelays.ContainsKey(networkID))
                                        {
                                            _tcpRelays[networkID].StopTcpRelay(this);
                                            _tcpRelays.Remove(networkID);
                                        }
                                    }
                                }

                                WriteFrame(SignalType.TcpRelayResponseSuccess, channelName, null, 0, 0);
                            }
                            #endregion
                            break;

                        case SignalType.TcpRelayResponseSuccess:
                            #region TcpRelayResponseSuccess

                            try
                            {
                                lock (_tcpRelayRequestLockList)
                                {
                                    object lockObject = _tcpRelayRequestLockList[channelName];

                                    lock (lockObject)
                                    {
                                        Monitor.Pulse(lockObject);
                                    }
                                }
                            }
                            catch
                            { }

                            #endregion
                            break;

                        case SignalType.TcpRelayResponsePeerList:
                            #region TcpRelayResponsePeerList
                            {
                                int count = dataStream.ReadByte();
                                List<IPEndPoint> peerEPs = new List<IPEndPoint>(count);

                                for (int i = 0; i < count; i++)
                                {
                                    peerEPs.Add(IPEndPointParser.Parse(dataStream));
                                }

                                TcpRelayPeersAvailable.BeginInvoke(this, channelName.Clone(), peerEPs, null, null);
                            }
                            #endregion
                            break;

                        case SignalType.BitChatNetworkInvitation:
                            #region ChannelInvitationBitChatNetwork

                            if (_connectionManager.Profile.AllowInboundInvitations)
                            {
                                byte[] buffer = new byte[dataStream.Length];
                                OffsetStream.StreamRead(dataStream, buffer, 0, buffer.Length);

                                BitChatNetworkInvitation.BeginInvoke(channelName.Clone(), _remotePeerEP, Encoding.UTF8.GetString(buffer, 0, buffer.Length), null, null);
                            }

                            #endregion
                            break;

                        default:
                            throw new IOException("Invalid frame signal type.");
                    }

                    if (dataStream != null)
                    {
                        //discard any unread data
                        if (dataStream.Length > dataStream.Position)
                            OffsetStream.StreamCopy(dataStream, Stream.Null, 4096);
                    }
                }
            }
            catch
            { }
            finally
            {
                Dispose();
            }
        }

        private void AcceptProxyConnectionAsync(object state)
        {
            ChannelStream channel = state as ChannelStream;

            try
            {
                _connectionManager.AcceptConnectionInitiateProtocol(channel, ConvertChannelNameToEp(channel.ChannelName));
            }
            catch
            {
                try
                {
                    channel.Dispose();
                }
                catch
                { }
            }
        }

        private void joint_Disposed(object sender, EventArgs e)
        {
            lock (_proxyTunnelJointList)
            {
                _proxyTunnelJointList.Remove(sender as Joint);
            }
        }

        private BinaryID ConvertEpToChannelName(IPEndPoint ep)
        {
            byte[] channelName = new byte[20];

            byte[] address = ep.Address.GetAddressBytes();
            byte[] port = BitConverter.GetBytes(Convert.ToUInt16(ep.Port));

            switch (ep.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    channelName[0] = 0;
                    break;

                case AddressFamily.InterNetworkV6:
                    channelName[0] = 1;
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            Buffer.BlockCopy(address, 0, channelName, 1, address.Length);
            Buffer.BlockCopy(port, 0, channelName, 1 + address.Length, 2);

            return new BinaryID(channelName);
        }

        private IPEndPoint ConvertChannelNameToEp(BinaryID channelName)
        {
            byte[] address;
            byte[] port;

            switch (channelName.ID[0])
            {
                case 0:
                    address = new byte[4];
                    port = new byte[2];
                    Buffer.BlockCopy(channelName.ID, 1, address, 0, 4);
                    Buffer.BlockCopy(channelName.ID, 1 + 4, port, 0, 2);
                    break;

                case 1:
                    address = new byte[16];
                    port = new byte[2];
                    Buffer.BlockCopy(channelName.ID, 1, address, 0, 16);
                    Buffer.BlockCopy(channelName.ID, 1 + 16, port, 0, 2);
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            return new IPEndPoint(new IPAddress(address), BitConverter.ToUInt16(port, 0));
        }

        private ChannelStream RequestProxyConnection(IPEndPoint forPeerEP)
        {
            BinaryID channelName = ConvertEpToChannelName(forPeerEP);
            ChannelStream channel;

            lock (_channels)
            {
                if (_channels.ContainsKey(channelName))
                    throw new ArgumentException("Channel already exists.");

                channel = new ChannelStream(this, channelName);
                _channels.Add(channelName, channel);
            }

            //send signal
            WriteFrame(SignalType.ConnectChannelProxyConnection, channelName, null, 0, 0);

            return channel;
        }

        #endregion

        #region static

        public static BinaryID GetChannelName(BinaryID localPeerID, BinaryID remotePeerID, BinaryID networkID)
        {
            // this is done to avoid disclosing networkID to passive network sniffing
            // channelName = hmac( localPeerID XOR remotePeerID, networkID)

            using (HMACSHA1 hmacSHA1 = new HMACSHA1(networkID.ID))
            {
                return new BinaryID(hmacSHA1.ComputeHash((localPeerID ^ remotePeerID).ID));
            }
        }

        public static bool IsStreamProxyTunnelConnection(Stream stream)
        {
            return (stream.GetType() == typeof(ChannelStream));
        }

        #endregion

        #region public

        public void Start()
        {
            if (_readThread == null)
            {
                _readThread = new Thread(ReadFrameAsync);
                _readThread.IsBackground = true;
                _readThread.Start();
            }
        }

        public Stream RequestBitChatNetworkChannel(BinaryID channelName)
        {
            ChannelStream channel;

            lock (_channels)
            {
                if (_channels.ContainsKey(channelName))
                    throw new ArgumentException("Channel already exists.");

                channel = new ChannelStream(this, channelName.Clone());
                _channels.Add(channel.ChannelName, channel);
            }

            //send connect signal
            WriteFrame(SignalType.ConnectChannelBitChatNetwork, channelName, null, 0, 0);

            return channel;
        }

        public bool BitChatNetworkChannelExists(BinaryID channelName)
        {
            lock (_channels)
            {
                return _channels.ContainsKey(channelName);
            }
        }

        public bool RequestPeerStatus(IPEndPoint remotePeerEP)
        {
            BinaryID channelName = ConvertEpToChannelName(remotePeerEP);
            object lockObject = new object();

            lock (_peerStatusLockList)
            {
                _peerStatusLockList.Add(channelName, lockObject);
            }

            try
            {
                lock (lockObject)
                {
                    WriteFrame(SignalType.PeerStatusQuery, channelName, null, 0, 0);

                    return Monitor.Wait(lockObject, 10000);
                }
            }
            finally
            {
                lock (_peerStatusLockList)
                {
                    _peerStatusLockList.Remove(channelName);
                }
            }
        }

        public Stream RequestProxyTunnel(IPEndPoint remotePeerEP)
        {
            BinaryID channelName = ConvertEpToChannelName(remotePeerEP);
            ChannelStream channel;

            lock (_channels)
            {
                if (_channels.ContainsKey(channelName))
                    throw new ArgumentException("Channel already exists.");

                channel = new ChannelStream(this, channelName);
                _channels.Add(channelName, channel);
            }

            //send signal
            WriteFrame(SignalType.ConnectChannelProxyTunnel, channelName, null, 0, 0);

            return channel;
        }

        public bool RequestStartTcpRelay(BinaryID[] networkIDs, Uri[] trackerURIs, int timeout)
        {
            BinaryID channelName = BinaryID.GenerateRandomID160();
            object lockObject = new object();

            lock (_tcpRelayRequestLockList)
            {
                _tcpRelayRequestLockList.Add(channelName, lockObject);
            }

            try
            {
                lock (lockObject)
                {
                    using (MemoryStream mS = new MemoryStream(1024))
                    {
                        byte[] XORnetworkID = new byte[20];
                        byte[] randomChannelID = channelName.ID;

                        //write networkid list
                        mS.WriteByte(Convert.ToByte(networkIDs.Length));

                        foreach (BinaryID networkID in networkIDs)
                        {
                            byte[] network = networkID.ID;

                            for (int i = 0; i < 20; i++)
                            {
                                XORnetworkID[i] = (byte)(randomChannelID[i] ^ network[i]);
                            }

                            mS.Write(XORnetworkID, 0, 20);
                        }

                        //write tracker uri list
                        mS.WriteByte(Convert.ToByte(trackerURIs.Length));

                        foreach (Uri trackerURI in trackerURIs)
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes(trackerURI.AbsoluteUri);
                            mS.WriteByte(Convert.ToByte(buffer.Length));
                            mS.Write(buffer, 0, buffer.Length);
                        }

                        byte[] data = mS.ToArray();

                        WriteFrame(SignalType.StartTcpRelay, channelName, data, 0, data.Length);
                    }

                    return Monitor.Wait(lockObject, timeout);
                }
            }
            finally
            {
                lock (_tcpRelayRequestLockList)
                {
                    _tcpRelayRequestLockList.Remove(channelName);
                }
            }
        }

        public bool RequestStopTcpRelay(BinaryID[] networkIDs, int timeout)
        {
            BinaryID channelName = BinaryID.GenerateRandomID160();
            object lockObject = new object();

            lock (_tcpRelayRequestLockList)
            {
                _tcpRelayRequestLockList.Add(channelName, lockObject);
            }

            try
            {
                lock (lockObject)
                {
                    using (MemoryStream mS = new MemoryStream(1024))
                    {
                        byte[] XORnetworkID = new byte[20];
                        byte[] randomChannelID = channelName.ID;

                        //write networkid list
                        mS.WriteByte(Convert.ToByte(networkIDs.Length));

                        foreach (BinaryID networkID in networkIDs)
                        {
                            byte[] network = networkID.ID;

                            for (int i = 0; i < 20; i++)
                            {
                                XORnetworkID[i] = (byte)(randomChannelID[i] ^ network[i]);
                            }

                            mS.Write(XORnetworkID, 0, 20);
                        }

                        byte[] data = mS.ToArray();

                        WriteFrame(SignalType.StopTcpRelay, channelName, data, 0, data.Length);
                    }

                    return Monitor.Wait(lockObject, timeout);
                }
            }
            finally
            {
                lock (_tcpRelayRequestLockList)
                {
                    _tcpRelayRequestLockList.Remove(channelName);
                }
            }
        }

        public void SendNOOP()
        {
            WriteFrame(SignalType.NOOP, BinaryID.GenerateRandomID160(), null, 0, 0);
        }

        public void SendBitChatNetworkInvitation(string message)
        {
            BinaryID hashedEmailAddress = BitChatNetwork.GetHashedEmailAddress(_connectionManager.Profile.LocalCertificateStore.Certificate.IssuedTo.EmailAddress);
            byte[] buffer = Encoding.UTF8.GetBytes(message);

            //send invitation signal with message
            WriteFrame(SignalType.BitChatNetworkInvitation, hashedEmailAddress, buffer, 0, buffer.Length);
        }

        #endregion

        #region properties

        public BinaryID LocalPeerID
        { get { return _connectionManager.LocalPeerID; } }

        public BinaryID RemotePeerID
        { get { return _remotePeerID; } }

        public IPEndPoint RemotePeerEP
        { get { return _remotePeerEP; } }

        public int ChannelWriteTimeout
        {
            get { return _channelWriteTimeout; }
            set { _channelWriteTimeout = value; }
        }

        public bool IsProxyTunnelConnection
        { get { return (_baseStream.GetType() == typeof(ChannelStream)); } }

        #endregion

        private class ChannelStream : Stream
        {
            #region variables

            const int CHANNEL_READ_TIMEOUT = 60000; //channel read timeout; application must NOOP
            const int CHANNEL_WRITE_TIMEOUT = 30000; //dummy timeout for write since base channel write timeout will be used

            readonly Connection _connection;
            readonly BinaryID _channelName;

            readonly byte[] _readBuffer = new byte[ConnectionManager.BUFFER_SIZE];
            int _readBufferPosition;
            int _readBufferCount;

            int _readTimeout = CHANNEL_READ_TIMEOUT;
            int _writeTimeout = CHANNEL_WRITE_TIMEOUT;

            bool _disconnected = false;

            #endregion

            #region constructor

            public ChannelStream(Connection connection, BinaryID channelName)
            {
                _connection = connection;
                _channelName = channelName;
            }

            #endregion

            #region IDisposable

            ~ChannelStream()
            {
                Dispose(false);
            }

            bool _disposed = false;

            protected override void Dispose(bool disposing)
            {
                lock (this)
                {
                    if (!_disposed)
                    {
                        if (!_disconnected)
                        {
                            lock (_connection._channels)
                            {
                                _connection._channels.Remove(_channelName);
                            }

                            try
                            {
                                //send disconnect signal
                                _connection.WriteFrame(SignalType.DisconnectChannel, _channelName, null, 0, 0);
                            }
                            catch
                            { }
                        }

                        _disposed = true;
                        Monitor.PulseAll(this);
                    }
                }
            }

            #endregion

            #region stream support

            public override bool CanRead
            {
                get { return _connection._baseStream.CanRead; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return _connection._baseStream.CanWrite; }
            }

            public override bool CanTimeout
            {
                get { return true; }
            }

            public override int ReadTimeout
            {
                get { return _readTimeout; }
                set { _readTimeout = value; }
            }

            public override int WriteTimeout
            {
                get { return _writeTimeout; }
                set { _writeTimeout = value; }
            }

            public override void Flush()
            {
                //do nothing
            }

            public override long Length
            {
                get { throw new NotSupportedException("ChannelStream stream does not support seeking."); }
            }

            public override long Position
            {
                get
                {
                    throw new NotSupportedException("ChannelStream stream does not support seeking.");
                }
                set
                {
                    throw new NotSupportedException("ChannelStream stream does not support seeking.");
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException("ChannelStream stream does not support seeking.");
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException("ChannelStream stream does not support seeking.");
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count < 1)
                    throw new ArgumentOutOfRangeException("Count must be atleast 1 byte.");

                lock (this)
                {
                    if (_readBufferCount < 1)
                    {
                        if (_disposed)
                            return 0;

                        if (!Monitor.Wait(this, _readTimeout))
                            throw new IOException("Read timed out.");

                        if (_readBufferCount < 1)
                            return 0;
                    }

                    int bytesToCopy = count;

                    if (bytesToCopy > _readBufferCount)
                        bytesToCopy = _readBufferCount;

                    Buffer.BlockCopy(_readBuffer, _readBufferPosition, buffer, offset, bytesToCopy);

                    _readBufferPosition += bytesToCopy;
                    _readBufferCount -= bytesToCopy;

                    if (_readBufferCount < 1)
                        Monitor.Pulse(this);

                    return bytesToCopy;
                }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_disposed)
                    throw new ObjectDisposedException("ChannelStream");

                _connection.WriteFrame(SignalType.ChannelData, _channelName, buffer, offset, count);
            }

            #endregion

            #region private

            internal void FeedReadBuffer(Stream s, int timeout)
            {
                int count = Convert.ToInt32(s.Length - s.Position);
                if (count > 0)
                {
                    lock (this)
                    {
                        if (_disposed)
                            throw new ObjectDisposedException("ChannelStream");

                        if (_readBufferCount > 0)
                        {
                            if (!Monitor.Wait(this, timeout))
                                throw new IOException("Channel FeedReadBuffer timed out.");

                            if (_readBufferCount > 0)
                                throw new IOException("Channel FeedReadBuffer failed. Buffer not empty.");
                        }

                        OffsetStream.StreamRead(s, _readBuffer, 0, count);
                        _readBufferPosition = 0;
                        _readBufferCount = count;

                        Monitor.Pulse(this);
                    }
                }
            }

            internal void SetDisconnected()
            {
                _disconnected = true;
            }

            #endregion

            #region properties

            public BinaryID ChannelName
            { get { return _channelName; } }

            #endregion
        }
    }
}
