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

using BitChatCore.Network.Connections;
using BitChatCore.Network.KademliaDHT;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore.Network
{
    class TcpRelayService : IDisposable
    {
        #region variables

        const int BIT_CHAT_TRACKER_UPDATE_INTERVAL = 120;
        const int TCP_RELAY_KEEP_ALIVE_INTERVAL = 30000; //30 sec

        static Dictionary<BinaryNumber, TcpRelayService> _relays = new Dictionary<BinaryNumber, TcpRelayService>(2);

        TrackerManager _trackerManager;
        Dictionary<BinaryNumber, Connection> _relayConnections = new Dictionary<BinaryNumber, Connection>(2);

        Timer _tcpRelayConnectionKeepAliveTimer;

        #endregion

        #region constructor

        private TcpRelayService(BinaryNumber networkID, int servicePort, DhtNode ipv4DhtNode)
        {
            _trackerManager = new TrackerManager(networkID, servicePort, ipv4DhtNode, null, BIT_CHAT_TRACKER_UPDATE_INTERVAL);

            //start keep alive timer
            _tcpRelayConnectionKeepAliveTimer = new Timer(RelayConnectionKeepAliveTimerCallback, null, TCP_RELAY_KEEP_ALIVE_INTERVAL, Timeout.Infinite);
        }

        #endregion

        #region IDisposable

        ~TcpRelayService()
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
                    _tcpRelayConnectionKeepAliveTimer.Dispose();

                //remove all connections
                lock (_relayConnections)
                {
                    _relayConnections.Clear();
                }

                //stop tracking
                if (_trackerManager != null)
                    _trackerManager.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region private

        private void RelayConnectionKeepAliveTimerCallback(object state)
        {
            try
            {
                //send noop to all connections to keep them alive
                lock (_relayConnections)
                {
                    foreach (Connection connection in _relayConnections.Values)
                    {
                        try
                        {
                            connection.SendNOOP();
                        }
                        catch
                        { }
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

        #endregion

        #region static

        public static TcpRelayService StartTcpRelay(BinaryNumber networkID, Connection connection, int servicePort, DhtNode dhtNode, Uri[] tracketURIs)
        {
            TcpRelayService relay;

            lock (_relays)
            {
                if (_relays.ContainsKey(networkID))
                {
                    relay = _relays[networkID];
                }
                else
                {
                    relay = new TcpRelayService(networkID, servicePort, dhtNode);
                    _relays.Add(networkID, relay);
                }

                lock (relay._relayConnections)
                {
                    relay._relayConnections.Add(connection.RemotePeerID, connection);
                }
            }

            relay._trackerManager.AddTracker(tracketURIs);
            relay._trackerManager.StartTracking();

            return relay;
        }

        public static List<IPEndPoint> GetPeerEPs(BinaryNumber channelName, Connection requestingConnection)
        {
            BinaryNumber localPeerID = requestingConnection.LocalPeerID;
            BinaryNumber remotePeerID = requestingConnection.RemotePeerID;

            lock (_relays)
            {
                foreach (KeyValuePair<BinaryNumber, TcpRelayService> itemRelay in _relays)
                {
                    BinaryNumber computedChannelName = Connection.GetChannelName(localPeerID, remotePeerID, itemRelay.Key);

                    if (computedChannelName.Equals(channelName))
                    {
                        Dictionary<BinaryNumber, Connection> relayConnections = itemRelay.Value._relayConnections;
                        List<IPEndPoint> peerEPs = new List<IPEndPoint>(relayConnections.Count);

                        lock (relayConnections)
                        {
                            foreach (KeyValuePair<BinaryNumber, Connection> itemProxyConnection in relayConnections)
                            {
                                peerEPs.Add(itemProxyConnection.Value.RemotePeerEP);
                            }
                        }

                        return peerEPs;
                    }
                }
            }

            return null;
        }

        public static void StopAllTcpRelays()
        {
            lock (_relays)
            {
                foreach (TcpRelayService relay in _relays.Values)
                {
                    relay.Dispose();
                }

                _relays.Clear();
            }
        }

        #endregion

        #region public

        public void StopTcpRelay(Connection connection)
        {
            bool removeSelf = false;

            lock (_relayConnections)
            {
                if (_relayConnections.Remove(connection.RemotePeerID))
                {
                    removeSelf = (_relayConnections.Count < 1);
                }
            }

            if (removeSelf)
            {
                lock (_relays)
                {
                    lock (_relayConnections) //lock to avoid race condition
                    {
                        if (_relayConnections.Count < 1) //recheck again
                        {
                            //stop tracking
                            _trackerManager.StopTracking();

                            //remove self from list
                            _relays.Remove(_trackerManager.NetworkID);
                        }
                    }
                }
            }
        }

        #endregion

        #region properties

        public BinaryNumber NetworkID
        { get { return _trackerManager.NetworkID; } }

        #endregion
    }
}
