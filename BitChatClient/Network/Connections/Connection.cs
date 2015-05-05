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
using TechnitiumLibrary.IO;

namespace BitChatClient.Network.Connections
{
    delegate void ChannelRequest(Connection connection, BinaryID channelName, ChannelType type, Stream channel);

    enum ChannelType : byte
    {
        BitChatNetwork = 1,
        ProxyTunnel = 2,
        VirtualConnection = 3
    }

    class Connection : IDisposable
    {
        #region variables

        public const byte SIGNAL_DATA = 0;
        public const byte SIGNAL_PEER_STATUS = 1;
        public const byte SIGNAL_PEER_STATUS_AVAILABLE = 2;
        public const byte SIGNAL_CONNECT_BIT_CHAT_NETWORK = 3;
        public const byte SIGNAL_DISCONNECT_BIT_CHAT_NETWORK = 4;
        public const byte SIGNAL_CONNECT_PROXY_TUNNEL = 5;
        public const byte SIGNAL_DISCONNECT_PROXY_TUNNEL = 6;
        public const byte SIGNAL_CONNECT_VIRTUAL_CONNECTION = 7;
        public const byte SIGNAL_DISCONNECT_VIRTUAL_CONNECTION = 8;

        const int BUFFER_SIZE = 65536;

        Stream _baseStream;
        BinaryID _remotePeerID;
        IPEndPoint _remotePeerEP;

        ConnectionManager _connectionManager;
        ChannelRequest _requestHandler;

        Dictionary<BinaryID, ChannelStream> _bitChatNetworkChannels = new Dictionary<BinaryID, ChannelStream>();
        Dictionary<BinaryID, ChannelStream> _proxyTunnelChannels = new Dictionary<BinaryID, ChannelStream>();
        Dictionary<BinaryID, ChannelStream> _virtualConnectionChannels = new Dictionary<BinaryID, ChannelStream>();

        Thread _readThread;

        Dictionary<BinaryID, object> _peerStatusLockList = new Dictionary<BinaryID, object>();
        List<Joint> _tunnelJointList = new List<Joint>();

        int _channelWriteTimeout = 30000;

        byte[] _writeBufferData = new byte[BUFFER_SIZE];

        #endregion

        #region constructor

        public Connection(Stream baseStream, BinaryID remotePeerID, IPEndPoint remotePeerEP, ConnectionManager connectionManager, ChannelRequest requestHandler)
        {
            _baseStream = baseStream;
            _remotePeerID = remotePeerID;
            _remotePeerEP = remotePeerEP;
            _connectionManager = connectionManager;
            _requestHandler = requestHandler;
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
                //Debug.Write("Connection.Dispose", _remotePeerEP.ToString());

                if (!_disposed)
                {
                    //stop read thread
                    try
                    {
                        _readThread.Abort();
                    }
                    catch
                    { }

                    //dispose all channels
                    List<ChannelStream> streamList = new List<ChannelStream>();

                    lock (_bitChatNetworkChannels)
                    {
                        foreach (KeyValuePair<BinaryID, ChannelStream> channel in _bitChatNetworkChannels)
                            streamList.Add(channel.Value);
                    }

                    lock (_proxyTunnelChannels)
                    {
                        foreach (KeyValuePair<BinaryID, ChannelStream> channel in _proxyTunnelChannels)
                            streamList.Add(channel.Value);
                    }

                    lock (_virtualConnectionChannels)
                    {
                        foreach (KeyValuePair<BinaryID, ChannelStream> channel in _virtualConnectionChannels)
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

                    //dispose base stream
                    lock (_baseStream)
                    {
                        try
                        {
                            _baseStream.Dispose();
                        }
                        catch
                        { }
                    }

                    //remote channel from connection manager
                    _connectionManager.RemoveConnection(this);

                    _disposed = true;
                }
            }
        }

        #endregion

        #region private

        private void WriteSignalFrame(BinaryID channelName, byte signal)
        {
            Debug.Write("Connection.WriteSignalFrame", _remotePeerEP.ToString() + "; channel:" + channelName.ToString() + "; signal:" + signal);

            lock (_baseStream)
            {
                //write frame signal
                _writeBufferData[0] = signal;

                //write channel name
                Buffer.BlockCopy(channelName.ID, 0, _writeBufferData, 1, 20);

                //output to base stream
                _baseStream.Write(_writeBufferData, 0, 21);
                _baseStream.Flush();
            }
        }

        private void WriteDataFrame(byte[] buffer, int offset, int count, BinaryID channelName, ChannelType type)
        {
            //Debug.Write("Connection.WriteDataFrame", _remotePeerEP.ToString() + "; channel:" + channelName.ToString() + "; dataSize:" + count);

            if (count < 1)
                return;

            lock (_baseStream)
            {
                //write frame signal
                _writeBufferData[0] = SIGNAL_DATA;

                //write channel name
                Buffer.BlockCopy(channelName.ID, 0, _writeBufferData, 1, 20);

                //write channel type
                _writeBufferData[21] = (byte)type;

                //write data
                byte[] bufferCount = BitConverter.GetBytes(Convert.ToUInt16(count - 1));
                _writeBufferData[22] = bufferCount[0];
                _writeBufferData[23] = bufferCount[1];
                Buffer.BlockCopy(buffer, offset, _writeBufferData, 24, count);

                //output to base stream
                _baseStream.Write(_writeBufferData, 0, 24 + count);
                _baseStream.Flush();
            }
        }

        private void ReadDataAsync()
        {
            try
            {
                int channelDataLength;
                int frameSignal;
                byte[] channelNameBuffer = new byte[20];
                BinaryID channelName = new BinaryID(channelNameBuffer);
                ChannelType channelType;
                byte[] buffer = new byte[65536];

                while (true)
                {
                    //read frame signal
                    frameSignal = _baseStream.ReadByte();

                    //read channel name
                    OffsetStream.StreamRead(_baseStream, channelNameBuffer, 0, 20);

                    switch (frameSignal)
                    {
                        case SIGNAL_DATA:
                            {
                                //read channel type
                                channelType = (ChannelType)_baseStream.ReadByte();

                                OffsetStream.StreamRead(_baseStream, buffer, 0, 2);
                                channelDataLength = BitConverter.ToUInt16(buffer, 0) + 1;
                                OffsetStream.StreamRead(_baseStream, buffer, 0, channelDataLength);

                                //switch frame
                                ChannelStream channel = null;

                                try
                                {
                                    switch (channelType)
                                    {
                                        case ChannelType.BitChatNetwork:
                                            lock (_bitChatNetworkChannels)
                                            {
                                                channel = _bitChatNetworkChannels[channelName];
                                            }

                                            channel.WriteBuffer(buffer, 0, channelDataLength, _channelWriteTimeout);
                                            break;

                                        case ChannelType.ProxyTunnel:
                                            lock (_proxyTunnelChannels)
                                            {
                                                channel = _proxyTunnelChannels[channelName];
                                            }

                                            channel.WriteBuffer(buffer, 0, channelDataLength, _channelWriteTimeout);
                                            break;

                                        case ChannelType.VirtualConnection:
                                            lock (_virtualConnectionChannels)
                                            {
                                                channel = _virtualConnectionChannels[channelName];
                                            }

                                            channel.WriteBuffer(buffer, 0, channelDataLength, _channelWriteTimeout);
                                            break;
                                    }
                                }
                                catch
                                {
                                    if (channel != null)
                                        channel.Dispose();
                                }
                            }
                            break;

                        case SIGNAL_CONNECT_BIT_CHAT_NETWORK:
                            {
                                ChannelStream channel = null;

                                try
                                {
                                    channel = new ChannelStream(this, channelName, ChannelType.BitChatNetwork);

                                    lock (_bitChatNetworkChannels)
                                    {
                                        _bitChatNetworkChannels.Add(channelName, channel);
                                    }

                                    Debug.Write("Connection.ReadDataAsync", "SIGNAL_CONNECT_BIT_CHAT_NETWORK; channel: " + channelName.ToString());

                                    _requestHandler.BeginInvoke(this, channelName, ChannelType.BitChatNetwork, channel, null, null);
                                }
                                catch
                                {
                                    if (channel != null)
                                        channel.Dispose();
                                }
                            }
                            break;

                        case SIGNAL_DISCONNECT_BIT_CHAT_NETWORK:
                            try
                            {
                                lock (_bitChatNetworkChannels)
                                {
                                    _bitChatNetworkChannels[channelName].Dispose();
                                }

                                Debug.Write("Connection.ReadDataAsync", "SIGNAL_DISCONNECT_BIT_CHAT_NETWORK; channel: " + channelName.ToString());
                            }
                            catch
                            { }
                            break;

                        case SIGNAL_PEER_STATUS:
                            try
                            {
                                if (_connectionManager.IsPeerConnectionAvailable(ConvertToIP(channelName.ID)))
                                    WriteSignalFrame(channelName, SIGNAL_PEER_STATUS_AVAILABLE);

                                Debug.Write("Connection.ReadDataAsync", "SIGNAL_PEER_STATUS; peerIP: " + ConvertToIP(channelName.ID).ToString());
                            }
                            catch
                            { }
                            break;

                        case SIGNAL_PEER_STATUS_AVAILABLE:
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

                                Debug.Write("Connection.ReadDataAsync", "SIGNAL_PEER_STATUS_AVAILABLE; peerIP: " + ConvertToIP(channelName.ID).ToString());
                            }
                            catch
                            { }
                            break;

                        case SIGNAL_CONNECT_PROXY_TUNNEL:
                            {
                                ChannelStream remoteChannel1 = null;
                                Stream remoteChannel2 = null;

                                try
                                {
                                    //get remote peer ep
                                    IPEndPoint tunnelToPeerEP = ConvertToIP(channelName.ID);

                                    //add first stream into list
                                    remoteChannel1 = new ChannelStream(this, channelName, ChannelType.ProxyTunnel);

                                    lock (_proxyTunnelChannels)
                                    {
                                        _proxyTunnelChannels.Add(channelName, remoteChannel1);
                                    }

                                    //get remote channel service
                                    Connection remotePeerConnection = _connectionManager.GetExistingConnection(tunnelToPeerEP);

                                    //get remote stream for virtual connection
                                    remoteChannel2 = remotePeerConnection.RequestVirtualConnectionChannel(_remotePeerEP);

                                    //join current and remote stream
                                    Joint joint = new Joint(remoteChannel1, remoteChannel2);
                                    joint.Disposed += joint_Disposed;

                                    lock (_tunnelJointList)
                                    {
                                        _tunnelJointList.Add(joint);
                                    }

                                    joint.Start();

                                    Debug.Write("Connection.ReadDataAsync", "SIGNAL_CONNECT_PROXY_TUNNEL; tunnel to peerIP: " + tunnelToPeerEP.ToString());
                                }
                                catch
                                {
                                    if (remoteChannel1 != null)
                                        remoteChannel1.Dispose();

                                    if (remoteChannel2 != null)
                                        remoteChannel2.Dispose();
                                }
                            }
                            break;

                        case SIGNAL_DISCONNECT_PROXY_TUNNEL:
                            try
                            {
                                lock (_proxyTunnelChannels)
                                {
                                    _proxyTunnelChannels[channelName].Dispose();
                                }

                                Debug.Write("Connection.ReadDataAsync", "SIGNAL_DISCONNECT_PROXY_TUNNEL; channel: " + channelName.ToString());
                            }
                            catch
                            { }
                            break;

                        case SIGNAL_CONNECT_VIRTUAL_CONNECTION:
                            {
                                ChannelStream channel = null;

                                try
                                {
                                    //add current stream into list
                                    channel = new ChannelStream(this, channelName, ChannelType.VirtualConnection);

                                    lock (_virtualConnectionChannels)
                                    {
                                        _virtualConnectionChannels.Add(channelName, channel);
                                    }

                                    IPEndPoint virtualRemotePeerEP = ConvertToIP(channelName.ID);

                                    Debug.Write("Connection.ReadDataAsync", "SIGNAL_CONNECT_VIRTUAL_CONNECTION; tunnel from peerIP: " + virtualRemotePeerEP.ToString());

                                    //pass channel as virtual connection async
                                    ThreadPool.QueueUserWorkItem(AcceptVirtualConnectionAsync, new object[] { channel, virtualRemotePeerEP });
                                }
                                catch
                                {
                                    if (channel != null)
                                        channel.Dispose();
                                }
                            }
                            break;

                        case SIGNAL_DISCONNECT_VIRTUAL_CONNECTION:
                            try
                            {
                                lock (_virtualConnectionChannels)
                                {
                                    _virtualConnectionChannels[channelName].Dispose();
                                }

                                Debug.Write("Connection.ReadDataAsync", "SIGNAL_DISCONNECT_VIRTUAL_CONNECTION; channel: " + channelName.ToString());
                            }
                            catch
                            { }
                            break;

                        default:
                            throw new IOException("Invalid ChannelManager frame type.");
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

        private void AcceptVirtualConnectionAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                ChannelStream channel = parameters[0] as ChannelStream;
                IPEndPoint virtualRemotePeerEP = parameters[1] as IPEndPoint;

                try
                {
                    Connection connection = _connectionManager.AcceptConnectionInitiateProtocol(channel, virtualRemotePeerEP);
                }
                catch
                {
                    if (channel != null)
                        channel.Dispose();
                }
            }
            catch
            { }
        }

        private void joint_Disposed(object sender, EventArgs e)
        {
            lock (_tunnelJointList)
            {
                _tunnelJointList.Remove(sender as Joint);
            }
        }

        private void RemoveChannel(ChannelStream channel)
        {
            switch (channel.ChannelType)
            {
                case ChannelType.BitChatNetwork:
                    lock (_bitChatNetworkChannels)
                    {
                        _bitChatNetworkChannels.Remove(channel.ChannelName);
                    }

                    try
                    {
                        //send disconnect signal
                        WriteSignalFrame(channel.ChannelName, SIGNAL_DISCONNECT_BIT_CHAT_NETWORK);
                    }
                    catch
                    { }
                    break;

                case ChannelType.ProxyTunnel:
                    lock (_proxyTunnelChannels)
                    {
                        _proxyTunnelChannels.Remove(channel.ChannelName);
                    }

                    try
                    {
                        //send disconnect signal
                        WriteSignalFrame(channel.ChannelName, SIGNAL_DISCONNECT_PROXY_TUNNEL);
                    }
                    catch
                    { }
                    break;

                case ChannelType.VirtualConnection:
                    lock (_virtualConnectionChannels)
                    {
                        _virtualConnectionChannels.Remove(channel.ChannelName);
                    }

                    try
                    {
                        //send disconnect signal
                        WriteSignalFrame(channel.ChannelName, SIGNAL_DISCONNECT_VIRTUAL_CONNECTION);
                    }
                    catch
                    { }
                    break;
            }
        }

        private byte[] ConvertToBinary(IPEndPoint ep)
        {
            byte[] buffer = new byte[20];

            byte[] address = ep.Address.GetAddressBytes();
            byte[] port = BitConverter.GetBytes(Convert.ToUInt16(ep.Port));

            switch (ep.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    buffer[0] = 0;
                    break;

                case AddressFamily.InterNetworkV6:
                    buffer[0] = 1;
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            Buffer.BlockCopy(address, 0, buffer, 1, address.Length);
            Buffer.BlockCopy(port, 0, buffer, 1 + address.Length, port.Length);

            return buffer;
        }

        private IPEndPoint ConvertToIP(byte[] data)
        {
            byte[] address;
            byte[] port;

            switch (data[0])
            {
                case 0:
                    address = new byte[4];
                    port = new byte[2];
                    Buffer.BlockCopy(data, 1, address, 0, 4);
                    Buffer.BlockCopy(data, 1 + 4, port, 0, 2);
                    break;

                case 1:
                    address = new byte[16];
                    port = new byte[2];
                    Buffer.BlockCopy(data, 1, address, 0, 16);
                    Buffer.BlockCopy(data, 1 + 16, port, 0, 2);
                    break;

                default:
                    throw new Exception("AddressFamily not supported.");
            }

            return new IPEndPoint(new IPAddress(address), BitConverter.ToUInt16(port, 0));
        }

        private Stream RequestVirtualConnectionChannel(IPEndPoint forPeerEP)
        {
            BinaryID channelName = new BinaryID(ConvertToBinary(forPeerEP));
            ChannelStream channel = new ChannelStream(this, channelName, ChannelType.VirtualConnection);

            lock (_virtualConnectionChannels)
            {
                _virtualConnectionChannels.Add(channelName, channel);
            }

            //send signal
            WriteSignalFrame(channelName, SIGNAL_CONNECT_VIRTUAL_CONNECTION);

            return channel;
        }

        #endregion

        #region public

        public void Start()
        {
            if (_readThread == null)
            {
                _readThread = new Thread(ReadDataAsync);
                _readThread.IsBackground = true;
                _readThread.Start();
            }
        }

        public Stream RequestBitChatNetworkChannel(BinaryID channelName)
        {
            ChannelStream channel = new ChannelStream(this, channelName, ChannelType.BitChatNetwork);

            lock (_bitChatNetworkChannels)
            {
                _bitChatNetworkChannels.Add(channelName, channel);
            }

            //send connect signal
            WriteSignalFrame(channelName, SIGNAL_CONNECT_BIT_CHAT_NETWORK);

            return channel;
        }

        public bool BitChatNetworkChannelExists(BinaryID channelName)
        {
            lock (_bitChatNetworkChannels)
            {
                return _bitChatNetworkChannels.ContainsKey(channelName);
            }
        }

        public bool RequestPeerStatus(IPEndPoint remotePeerEP)
        {
            BinaryID channelName = new BinaryID(ConvertToBinary(remotePeerEP));
            object lockObject = new object();

            lock (_peerStatusLockList)
            {
                _peerStatusLockList.Add(channelName, lockObject);
            }

            lock (lockObject)
            {
                WriteSignalFrame(channelName, SIGNAL_PEER_STATUS);

                if (!Monitor.Wait(lockObject, 10000))
                {
                    lock (_peerStatusLockList)
                    {
                        _peerStatusLockList.Remove(channelName);
                    }

                    return false;
                }

                return true;
            }
        }

        public Stream RequestProxyTunnelChannel(IPEndPoint remotePeerEP)
        {
            BinaryID channelName = new BinaryID(ConvertToBinary(remotePeerEP));
            ChannelStream channel = new ChannelStream(this, channelName, ChannelType.ProxyTunnel);

            lock (_proxyTunnelChannels)
            {
                _proxyTunnelChannels.Add(channelName, channel);
            }

            //send signal
            WriteSignalFrame(channelName, SIGNAL_CONNECT_PROXY_TUNNEL);

            return channel;
        }

        #endregion

        #region properties

        public BinaryID RemotePeerID
        { get { return _remotePeerID; } }

        public IPEndPoint RemotePeerEP
        { get { return _remotePeerEP; } }

        public int ChannelWriteTimeout
        {
            get { return _channelWriteTimeout; }
            set { _channelWriteTimeout = value; }
        }

        #endregion

        private class ChannelStream : Stream
        {
            #region variables

            const int CHANNEL_READ_TIMEOUT = 30000; //channel timeout 30 sec; application must NOOP
            const int CHANNEL_WRITE_TIMEOUT = 30000; //dummy timeout for write since base channel write timeout will be used

            Connection _connection;
            BinaryID _channelName;
            ChannelType _channelType;

            byte[] _buffer;
            int _offset;
            int _count;

            int _readTimeout = CHANNEL_READ_TIMEOUT;
            int _writeTimeout = CHANNEL_WRITE_TIMEOUT;

            #endregion

            #region constructor

            public ChannelStream(Connection connection, BinaryID channelName, ChannelType channelType)
            {
                _connection = connection;
                _channelName = channelName.Clone();
                _channelType = channelType;
            }

            #endregion

            #region IDisposable

            bool _disposed = false;

            protected override void Dispose(bool disposing)
            {
                lock (this)
                {
                    if (!_disposed)
                    {
                        _buffer = null;

                        _connection.RemoveChannel(this);

                        Monitor.PulseAll(this);

                        _disposed = true;
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
                get { throw new IOException("ChannelStream stream does not support seeking."); }
            }

            public override long Position
            {
                get
                {
                    throw new IOException("ChannelStream stream does not support seeking.");
                }
                set
                {
                    throw new IOException("ChannelStream stream does not support seeking.");
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new IOException("ChannelStream stream does not support seeking.");
            }

            public override void SetLength(long value)
            {
                throw new IOException("ChannelStream stream does not support seeking.");
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count < 1)
                    throw new IOException("Count must be atleast 1 byte.");

                lock (this)
                {
                    if (_disposed)
                        throw new IOException("Cannot read from a closed stream.");

                    if (_buffer == null)
                    {
                        if (!Monitor.Wait(this, _readTimeout))
                            throw new IOException("Read timed out.");

                        if (_buffer == null)
                            return 0;
                    }

                    int bytesToCopy = count;

                    if (bytesToCopy > _count)
                        bytesToCopy = _count;

                    Buffer.BlockCopy(_buffer, _offset, buffer, offset, bytesToCopy);

                    if (bytesToCopy < _count)
                    {
                        _offset += bytesToCopy;
                        _count -= bytesToCopy;
                    }
                    else
                    {
                        _buffer = null;

                        Monitor.Pulse(this);
                    }

                    return bytesToCopy;
                }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _connection.WriteDataFrame(buffer, offset, count, _channelName, _channelType);
            }

            #endregion

            #region private

            internal void WriteBuffer(byte[] buffer, int offset, int count, int timeout)
            {
                if (count > 0)
                {
                    lock (this)
                    {
                        if (_disposed)
                            throw new IOException("Cannot write buffer to a closed stream.");

                        _buffer = buffer;
                        _offset = offset;
                        _count = count;

                        Monitor.Pulse(this);

                        if (!Monitor.Wait(this, timeout))
                            throw new IOException("Channel WriteBuffer timed out.");
                    }
                }
            }

            #endregion

            #region properties

            public BinaryID ChannelName
            { get { return _channelName; } }

            public ChannelType ChannelType
            { get { return _channelType; } }

            #endregion
        }
    }
}
