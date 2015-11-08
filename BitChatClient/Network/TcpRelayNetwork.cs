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

using BitChatClient.Network.Connections;
using BitChatClient.Network.KademliaDHT;
using System;
using System.Collections.Generic;
using System.Net;
using TechnitiumLibrary.Net.Proxy;

namespace BitChatClient.Network
{
    class TcpRelayNetwork : IDisposable
    {
        #region variables

        static Dictionary<BinaryID, TcpRelayNetwork> _relayNetworks = new Dictionary<BinaryID, TcpRelayNetwork>(2);

        TrackerManager _trackerManager;
        Dictionary<BinaryID, Connection> _relayConnections = new Dictionary<BinaryID, Connection>(2);

        #endregion

        #region constructor

        private TcpRelayNetwork(BinaryID networkID, int servicePort, DhtClient dhtClient)
        {
            _trackerManager = new TrackerManager(networkID, servicePort, dhtClient);
        }

        #endregion

        #region IDisposable

        ~TcpRelayNetwork()
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
                if (_trackerManager != null)
                    _trackerManager.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region static

        public static TcpRelayNetwork JoinRelayNetwork(BinaryID networkID, int servicePort, Connection connection, DhtClient dhtClient, NetProxy proxy)
        {
            lock (_relayNetworks)
            {
                if (_relayNetworks.ContainsKey(networkID))
                {
                    TcpRelayNetwork relayNetwork = _relayNetworks[networkID];

                    relayNetwork._relayConnections.Add(connection.RemotePeerID, connection);

                    return relayNetwork;
                }
                else
                {
                    TcpRelayNetwork relayNetwork = new TcpRelayNetwork(networkID, servicePort, dhtClient);
                    relayNetwork.Proxy = proxy;

                    _relayNetworks.Add(networkID, relayNetwork);

                    relayNetwork._relayConnections.Add(connection.RemotePeerID, connection);

                    return relayNetwork;
                }
            }
        }

        public static List<IPEndPoint> GetRelayNetworkPeers(BinaryID channelName, Connection requestingConnection)
        {
            BinaryID localPeerID = requestingConnection.LocalPeerID;
            BinaryID remotePeerID = requestingConnection.RemotePeerID;

            lock (_relayNetworks)
            {
                foreach (KeyValuePair<BinaryID, TcpRelayNetwork> itemRelayNetwork in _relayNetworks)
                {
                    BinaryID computedChannelName = Connection.GetChannelName(localPeerID, remotePeerID, itemRelayNetwork.Key);

                    if (computedChannelName.Equals(channelName))
                    {
                        Dictionary<BinaryID, Connection> proxyConnections = itemRelayNetwork.Value._relayConnections;
                        List<IPEndPoint> peerEPs = new List<IPEndPoint>(proxyConnections.Count);

                        foreach (KeyValuePair<BinaryID, Connection> itemProxyConnection in proxyConnections)
                        {
                            peerEPs.Add(itemProxyConnection.Value.RemotePeerEP);
                        }

                        return peerEPs;
                    }
                }
            }

            return null;
        }

        public static void UpdateSocksProxy(NetProxy proxy)
        {
            lock (_relayNetworks)
            {
                foreach (TcpRelayNetwork relayNetwork in _relayNetworks.Values)
                    relayNetwork.Proxy = proxy;
            }
        }

        #endregion

        #region public

        public void StartTracking()
        {
            _trackerManager.StartTracking();
        }

        public void AddTrackers(Uri[] tracketURIs)
        {
            _trackerManager.AddTracker(tracketURIs);
        }

        public void StopTracking()
        {
            _trackerManager.StopTracking();
        }

        public void LeaveNetwork(Connection connection)
        {
            lock (_relayNetworks)
            {
                _relayConnections.Remove(connection.RemotePeerID);

                if (_relayConnections.Count < 1)
                {
                    _relayNetworks.Remove(_trackerManager.NetworkID);
                    _trackerManager.StopTracking();
                }
            }
        }

        #endregion

        #region properties

        public BinaryID NetworkID
        { get { return _trackerManager.NetworkID; } }

        public NetProxy Proxy
        {
            get { return _trackerManager.Proxy; }
            set { _trackerManager.Proxy = value; }
        }

        #endregion
    }
}
