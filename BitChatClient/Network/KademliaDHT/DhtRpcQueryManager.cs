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

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BitChatClient.Network.KademliaDHT
{
    class DhtRpcQueryManager
    {
        #region variables

        const int BUFFER_MAX_SIZE = 1024;
        const int QUERY_TIMEOUT = 10000;

        BinaryID _currentNodeID;

        Socket _udpClient;
        Thread _readThread;

        byte[] _sendBuffer = new byte[BUFFER_MAX_SIZE];
        MemoryStream _sendBufferStream;

        Dictionary<int, Transaction> _transactions = new Dictionary<int, Transaction>(10);

        #endregion

        #region constructor

        public DhtRpcQueryManager(BinaryID currentNodeID)
        {
            _currentNodeID = currentNodeID;

            //bind udp socket to random port
            _udpClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpClient.Bind(new IPEndPoint(IPAddress.Any, 0));

            //start reading udp packets
            _readThread = new Thread(ReadPackets);
            _readThread.Start();

            _sendBufferStream = new MemoryStream(_sendBuffer);
        }

        #endregion

        #region private

        private void ReadPackets(object state)
        {
            EndPoint remoteNodeEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] bufferRecv = new byte[BUFFER_MAX_SIZE];
            MemoryStream recvStream = new MemoryStream(bufferRecv, false);
            int bytesRecv;

            try
            {
                while (true)
                {
                    bytesRecv = _udpClient.ReceiveFrom(bufferRecv, ref remoteNodeEP);

                    if (bytesRecv > 0)
                    {
                        recvStream.Position = 0;
                        recvStream.SetLength(bytesRecv);

                        DhtRpcPacket response = new DhtRpcPacket(recvStream, this);

                        //only incoming response packets handled here
                        switch (response.PacketType)
                        {
                            case RpcPacketType.Response:

                                Transaction transaction = null;

                                lock (_transactions)
                                {
                                    try
                                    {
                                        transaction = _transactions[response.TransactionID];
                                    }
                                    catch
                                    { }
                                }

                                if ((transaction != null) && transaction.RemoteNodeEP.Equals(remoteNodeEP))
                                {
                                    lock (transaction)
                                    {
                                        transaction.ResponsePacket = response;

                                        Monitor.Pulse(transaction);
                                    }
                                }

                                break;
                        }
                    }
                }
            }
            catch
            { }
        }

        #endregion

        #region public

        public DhtRpcPacket Query(DhtRpcPacket packet, IPEndPoint remoteNodeEP)
        {
            Transaction transaction = new Transaction(remoteNodeEP);

            try
            {
                lock (_transactions)
                {
                    _transactions.Add(packet.TransactionID, transaction);
                }

                lock (transaction)
                {
                    lock (_sendBufferStream)
                    {
                        _sendBufferStream.Position = 0;
                        packet.WriteTo(_sendBufferStream);
                        _udpClient.SendTo(_sendBuffer, 0, (int)_sendBufferStream.Position, SocketFlags.None, remoteNodeEP);
                    }

                    if (!Monitor.Wait(transaction, QUERY_TIMEOUT))
                        return null;

                    return transaction.ResponsePacket;
                }
            }
            finally
            {
                lock (_transactions)
                {
                    _transactions.Remove(packet.TransactionID);
                }
            }
        }

        #endregion

        #region properties

        public BinaryID CurrentNodeID
        { get { return _currentNodeID; } }

        #endregion

        class Transaction
        {
            #region variables

            public IPEndPoint RemoteNodeEP;
            public DhtRpcPacket ResponsePacket;

            #endregion

            #region constructor

            public Transaction(IPEndPoint remoteNodeEP)
            {
                this.RemoteNodeEP = remoteNodeEP;
            }

            #endregion
        }
    }
}
