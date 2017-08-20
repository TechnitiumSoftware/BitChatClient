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

using BitChatCore.Network.Connections;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using TechnitiumLibrary.Net;

namespace BitChatCore.Network
{
    class TcpRelayClient : IDisposable
    {
        #region variables

        const int TCP_RELAY_MAX_CONNECTIONS = 3;
        const int TCP_RELAY_KEEP_ALIVE_INTERVAL = 30000;
        const int TCP_RELAY_REQUEST_TIMEOUT = 10000;

        ConnectionManager _connectionManager;

        Dictionary<BinaryID, Uri[]> _networks = new Dictionary<BinaryID, Uri[]>(10);
        Dictionary<IPEndPoint, Connection> _tcpRelayConnections = new Dictionary<IPEndPoint, Connection>(TCP_RELAY_MAX_CONNECTIONS);
        Timer _tcpRelayConnectionKeepAliveTimer;

        #endregion

        #region constructor

        public TcpRelayClient(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;

            _tcpRelayConnectionKeepAliveTimer = new Timer(RelayConnectionKeepAliveTimerCallback, null, 0, Timeout.Infinite);
        }

        #endregion

        #region IDisposable

        ~TcpRelayClient()
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
                if (_tcpRelayConnectionKeepAliveTimer != null)
                {
                    _tcpRelayConnectionKeepAliveTimer.Dispose();
                    _tcpRelayConnectionKeepAliveTimer = null;

                    //remove all networks from all tcp relay connections
                    BinaryID[] networkIDs;

                    lock (_networks)
                    {
                        networkIDs = new BinaryID[_networks.Keys.Count];
                        _networks.Keys.CopyTo(networkIDs, 0);
                    }

                    lock (_tcpRelayConnections)
                    {
                        foreach (Connection relayConnection in _tcpRelayConnections.Values)
                            ThreadPool.QueueUserWorkItem(RemoveTcpRelayFromConnectionAsync, new object[] { relayConnection, networkIDs });

                        _tcpRelayConnections.Clear();
                    }
                }

                _disposed = true;
            }
        }

        #endregion

        #region private

        private void RelayConnectionKeepAliveTimerCallback(object state)
        {
            try
            {
                bool lessRelayNodesAvailable;

                //send noop to all connections to keep them alive
                lock (_tcpRelayConnections)
                {
                    foreach (Connection relayConnection in _tcpRelayConnections.Values)
                        ThreadPool.QueueUserWorkItem(SendNOOPAsync, relayConnection);

                    lessRelayNodesAvailable = (_tcpRelayConnections.Count < TCP_RELAY_MAX_CONNECTIONS);
                }

                //if less number of relay node connections available, try to find new relay nodes
                if (lessRelayNodesAvailable)
                {
                    IPEndPoint[] nodeEPs = _connectionManager.IPv4DhtNode.GetAllNodeEPs();

                    foreach (IPEndPoint relayNodeEP in nodeEPs)
                    {
                        lock (_tcpRelayConnections)
                        {
                            if (_tcpRelayConnections.Count >= TCP_RELAY_MAX_CONNECTIONS)
                                return;

                            if (_tcpRelayConnections.ContainsKey(relayNodeEP))
                                continue;
                        }

                        if (NetUtilities.IsPrivateIP(relayNodeEP.Address))
                            continue;

                        ThreadPool.QueueUserWorkItem(ConnectTcpRelayNodeAsync, relayNodeEP);
                    }
                }
            }
            catch
            { }
            finally
            {
                if (_tcpRelayConnectionKeepAliveTimer != null)
                    _tcpRelayConnectionKeepAliveTimer.Change(TCP_RELAY_KEEP_ALIVE_INTERVAL, Timeout.Infinite);
            }
        }

        private void ConnectTcpRelayNodeAsync(object state)
        {
            IPEndPoint relayNodeEP = state as IPEndPoint;

            try
            {
                Connection relayConnection = _connectionManager.MakeConnection(relayNodeEP);

                if (relayConnection.IsProxyTunnelConnection)
                    return;

                lock (_tcpRelayConnections)
                {
                    if (_tcpRelayConnections.Count >= TCP_RELAY_MAX_CONNECTIONS)
                        return; //have enough tcp relays

                    if (!_tcpRelayConnections.ContainsKey(relayNodeEP))
                    {
                        //new tcp relay node, add to list
                        relayConnection.Disposed += RelayConnection_Disposed;
                        _tcpRelayConnections.Add(relayNodeEP, relayConnection);
                    }
                }

                //setup all networks on tcp relay
                lock (_networks)
                {
                    foreach (KeyValuePair<BinaryID, Uri[]> network in _networks)
                        ThreadPool.QueueUserWorkItem(SetupTcpRelayOnConnectionAsync, new object[] { relayConnection, network.Key, network.Value });
                }
            }
            catch
            {
                lock (_tcpRelayConnections)
                {
                    _tcpRelayConnections.Remove(relayNodeEP);
                }
            }
        }

        private void RelayConnection_Disposed(object sender, EventArgs e)
        {
            Connection relayConnection = sender as Connection;

            lock (_tcpRelayConnections)
            {
                _tcpRelayConnections.Remove(relayConnection.RemotePeerEP);
            }
        }

        private void SendNOOPAsync(object state)
        {
            Connection relayConnection = state as Connection;

            try
            {
                relayConnection.SendNOOP();
            }
            catch
            { }
        }

        private void SetupTcpRelayOnConnectionAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                Connection relayConnection = parameters[0] as Connection;
                BinaryID networkID = parameters[1] as BinaryID;
                Uri[] trackerURIs = parameters[2] as Uri[];

                bool success = relayConnection.RequestStartTcpRelay(new BinaryID[] { networkID }, trackerURIs, TCP_RELAY_REQUEST_TIMEOUT);
                if (!success)
                {
                    //remove failed relay
                    lock (_tcpRelayConnections)
                    {
                        _tcpRelayConnections.Remove(relayConnection.RemotePeerEP);
                    }
                }
            }
            catch
            { }
        }

        private void RemoveTcpRelayFromConnectionAsync(object state)
        {
            try
            {
                object[] parameters = state as object[];

                Connection relayConnection = parameters[0] as Connection;
                BinaryID[] networkIDs = parameters[1] as BinaryID[];

                bool success = relayConnection.RequestStopTcpRelay(networkIDs, TCP_RELAY_REQUEST_TIMEOUT);
                if (!success)
                {
                    //remove failed relay
                    lock (_tcpRelayConnections)
                    {
                        _tcpRelayConnections.Remove(relayConnection.RemotePeerEP);
                    }
                }
            }
            catch
            { }
        }

        #endregion

        #region public

        public void AddNetwork(BinaryID networkID, Uri[] trackerURIs)
        {
            lock (_networks)
            {
                if (_networks.ContainsKey(networkID))
                    return;

                _networks.Add(networkID, trackerURIs);
            }

            lock (_tcpRelayConnections)
            {
                foreach (Connection relayConnection in _tcpRelayConnections.Values)
                    ThreadPool.QueueUserWorkItem(SetupTcpRelayOnConnectionAsync, new object[] { relayConnection, networkID, trackerURIs });
            }
        }

        public void RemoveNetwork(BinaryID networkID)
        {
            lock (_networks)
            {
                bool removed = _networks.Remove(networkID);
                if (!removed)
                    return;
            }

            lock (_tcpRelayConnections)
            {
                foreach (Connection relayConnection in _tcpRelayConnections.Values)
                    ThreadPool.QueueUserWorkItem(RemoveTcpRelayFromConnectionAsync, new object[] { relayConnection, new BinaryID[] { networkID } });
            }
        }

        #endregion

        #region properties

        public IPEndPoint[] Nodes
        {
            get
            {
                lock (_tcpRelayConnections)
                {
                    IPEndPoint[] relayNodeEPs = new IPEndPoint[_tcpRelayConnections.Count];
                    _tcpRelayConnections.Keys.CopyTo(relayNodeEPs, 0);

                    return relayNodeEPs;
                }
            }
        }

        #endregion
    }
}
