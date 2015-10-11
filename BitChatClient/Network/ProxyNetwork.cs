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
using System;
using System.Collections.Generic;
using System.Net;

namespace BitChatClient.Network
{
    class ProxyNetwork
    {
        #region variables

        static Dictionary<BinaryID, ProxyNetwork> _proxyNetworks = new Dictionary<BinaryID, ProxyNetwork>(2);

        TrackerManager _trackerManager;

        Dictionary<BinaryID, Connection> _proxyConnections = new Dictionary<BinaryID, Connection>(2);

        #endregion

        #region constructor

        private ProxyNetwork(BinaryID networkID, int servicePort)
        {
            _trackerManager = new TrackerManager(networkID, servicePort);
        }

        #endregion

        #region static

        public static ProxyNetwork JoinProxyNetwork(BinaryID networkID, int servicePort, Connection connection)
        {
            lock (_proxyNetworks)
            {
                if (_proxyNetworks.ContainsKey(networkID))
                {
                    ProxyNetwork proxyNetwork = _proxyNetworks[networkID];

                    proxyNetwork._proxyConnections.Add(connection.RemotePeerID, connection);

                    return proxyNetwork;
                }
                else
                {
                    ProxyNetwork proxyNetwork = new ProxyNetwork(networkID, servicePort);
                    _proxyNetworks.Add(networkID, proxyNetwork);

                    proxyNetwork._proxyConnections.Add(connection.RemotePeerID, connection);

                    return proxyNetwork;
                }
            }
        }

        public static List<IPEndPoint> GetProxyNetworkPeers(BinaryID channelName, Connection requestingConnection)
        {
            BinaryID localPeerID = requestingConnection.LocalPeerID;
            BinaryID remotePeerID = requestingConnection.RemotePeerID;

            lock (_proxyNetworks)
            {
                foreach (KeyValuePair<BinaryID, ProxyNetwork> itemProxyNetwork in _proxyNetworks)
                {
                    BinaryID computedChannelName = Connection.GetChannelName(localPeerID, remotePeerID, itemProxyNetwork.Key);

                    if (computedChannelName.Equals(channelName))
                    {
                        Dictionary<BinaryID, Connection> proxyConnections = itemProxyNetwork.Value._proxyConnections;
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
            lock (_proxyNetworks)
            {
                _proxyConnections.Remove(connection.RemotePeerID);

                if (_proxyConnections.Count < 1)
                {
                    _proxyNetworks.Remove(_trackerManager.NetworkID);
                    _trackerManager.StopTracking();
                }
            }
        }

        #endregion

        #region properties

        public BinaryID NetworkID
        { get { return _trackerManager.NetworkID; } }

        #endregion
    }
}
