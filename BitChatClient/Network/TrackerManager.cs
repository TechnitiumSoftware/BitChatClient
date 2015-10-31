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

using BitChatClient.Network.KademliaDHT;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Net.Proxy;

namespace BitChatClient.Network
{
    delegate void TrackerManagerDiscoveredPeers(TrackerManager sender, IEnumerable<IPEndPoint> peerEPs);

    class TrackerManager : IDisposable
    {
        #region events

        public event TrackerManagerDiscoveredPeers DiscoveredPeers;

        #endregion

        #region variables

        const int DHT_UPDATE_INTERVAL_SECONDS = 5 * 60; //5 min

        BinaryID _networkID;
        int _servicePort;
        DhtClient _dhtClient;
        bool _lookupOnly;

        SocksClient _proxy;

        List<IPEndPoint> _dhtPeers = new List<IPEndPoint>();
        DateTime _dhtLastUpdated;

        const int _TRACKER_TIMER_CHECK_INTERVAL = 10000;
        List<TrackerClient> _trackers = new List<TrackerClient>();
        Timer _trackerUpdateTimer;

        #endregion

        #region constructor

        public TrackerManager(BinaryID networkID, int servicePort, DhtClient dhtClient = null, bool lookupOnly = false)
        {
            _networkID = networkID;
            _servicePort = servicePort;
            _dhtClient = dhtClient;
            _lookupOnly = lookupOnly;
        }

        #endregion

        #region IDisposable

        ~TrackerManager()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                StopTracking();

                _disposed = true;
            }
        }

        #endregion

        #region private

        private void TrackerUpdateTimerCallBack(object state)
        {
            try
            {
                TrackerClientEvent @event;
                bool forceUpdate = false;

                if (state == null)
                {
                    forceUpdate = true;
                    @event = TrackerClientEvent.Started;
                }
                else
                {
                    @event = (TrackerClientEvent)state;
                }

                IPEndPoint localEP = new IPEndPoint(IPAddress.Any, _servicePort);

                lock (_trackers)
                {
                    foreach (TrackerClient tracker in _trackers)
                    {
                        if (!tracker.IsUpdating && (forceUpdate || (tracker.NextUpdateIn().TotalSeconds < 1)))
                            ThreadPool.QueueUserWorkItem(UpdateTrackerAsync, new object[] { tracker, @event, localEP });
                    }
                }

                //update dht
                if (_dhtClient != null)
                {
                    if (forceUpdate || (DateTime.UtcNow - _dhtLastUpdated).TotalSeconds > DHT_UPDATE_INTERVAL_SECONDS)
                        ThreadPool.QueueUserWorkItem(UpdateDhtAsync, localEP);
                }
            }
            catch
            { }
            finally
            {
                if (_trackerUpdateTimer != null)
                    _trackerUpdateTimer.Change(_TRACKER_TIMER_CHECK_INTERVAL, Timeout.Infinite);
            }
        }

        private void UpdateTrackerAsync(object state)
        {
            object[] parameters = state as object[];

            TrackerClient tracker = parameters[0] as TrackerClient;
            TrackerClientEvent @event = (TrackerClientEvent)parameters[1];
            IPEndPoint localEP = parameters[2] as IPEndPoint;

            try
            {
                tracker.Update(@event, localEP);

                switch (@event)
                {
                    case TrackerClientEvent.Started:
                    case TrackerClientEvent.Completed:
                    case TrackerClientEvent.None:
                        if (_lookupOnly)
                            tracker.Update(TrackerClientEvent.Stopped, localEP);

                        if (tracker.Peers.Count > 0)
                        {
                            if (DiscoveredPeers != null)
                                DiscoveredPeers(this, tracker.Peers);

                            if (_dhtClient != null)
                                _dhtClient.AddNode(tracker.Peers);
                        }

                        break;
                }
            }
            catch
            { }
        }

        private void UpdateDhtAsync(object state)
        {
            IPEndPoint localEP = state as IPEndPoint;

            try
            {
                IPEndPoint[] peers;

                if (_lookupOnly)
                    peers = _dhtClient.FindPeers(_networkID);
                else
                    peers = _dhtClient.Announce(_networkID, localEP.Port);

                _dhtLastUpdated = DateTime.UtcNow;

                if ((peers != null) && (peers.Length > 0))
                {
                    lock (_dhtPeers)
                    {
                        foreach (IPEndPoint peer in peers)
                        {
                            if (!_dhtPeers.Contains(peer))
                                _dhtPeers.Add(peer);
                        }
                    }

                    if (DiscoveredPeers != null)
                        DiscoveredPeers(this, peers);
                }
            }
            catch
            { }
        }

        #endregion

        #region public

        public void StartTracking(IEnumerable<Uri> trackerURIs = null)
        {
            if (trackerURIs != null)
            {
                lock (_trackers)
                {
                    _trackers.Clear();

                    foreach (Uri trackerURI in trackerURIs)
                    {
                        TrackerClient tracker = TrackerClient.Create(trackerURI, _networkID.ID, TrackerClientID.CreateDefaultID());
                        tracker.SocksProxy = _proxy;

                        _trackers.Add(tracker);
                    }
                }
            }

            if (_trackerUpdateTimer == null)
            {
                lock (_trackers)
                {
                    if ((_trackers.Count > 0) || (_dhtClient != null))
                        _trackerUpdateTimer = new Timer(TrackerUpdateTimerCallBack, TrackerClientEvent.Started, 1000, Timeout.Infinite);
                }
            }
        }

        public void StopTracking()
        {
            if (_trackerUpdateTimer != null)
            {
                _trackerUpdateTimer.Dispose();
                _trackerUpdateTimer = null;

                //update trackers
                IPEndPoint localEP = new IPEndPoint(IPAddress.Any, _servicePort);

                lock (_trackers)
                {
                    foreach (TrackerClient tracker in _trackers)
                    {
                        ThreadPool.QueueUserWorkItem(new WaitCallback(UpdateTrackerAsync), new object[] { tracker, TrackerClientEvent.Stopped, localEP });
                    }
                }
            }
        }

        public void ForceUpdate()
        {
            TrackerUpdateTimerCallBack(null);
        }

        public TrackerClient[] GetTrackers()
        {
            lock (_trackers)
            {
                return _trackers.ToArray();
            }
        }

        public Uri[] GetTracketURIs()
        {
            Uri[] trackerURIs;

            lock (_trackers)
            {
                trackerURIs = new Uri[_trackers.Count];

                for (int i = 0; i < trackerURIs.Length; i++)
                    trackerURIs[i] = _trackers[i].TrackerUri;
            }

            return trackerURIs;
        }

        public TrackerClient AddTracker(Uri trackerURI)
        {
            lock (_trackers)
            {
                foreach (TrackerClient tracker in _trackers)
                {
                    if (tracker.TrackerUri.Equals(trackerURI))
                        return null;
                }

                {
                    TrackerClient tracker = TrackerClient.Create(trackerURI, _networkID.ID, TrackerClientID.CreateDefaultID());
                    tracker.SocksProxy = _proxy;

                    _trackers.Add(tracker);
                    return tracker;
                }
            }
        }

        public void AddTracker(IEnumerable<Uri> trackerURIs)
        {
            lock (_trackers)
            {
                foreach (Uri trackerURI in trackerURIs)
                {
                    bool trackerExists = false;

                    foreach (TrackerClient tracker in _trackers)
                    {
                        if (tracker.TrackerUri.Equals(trackerURI))
                        {
                            trackerExists = true;
                            break;
                        }
                    }

                    if (!trackerExists)
                    {
                        TrackerClient tracker = TrackerClient.Create(trackerURI, _networkID.ID, TrackerClientID.CreateDefaultID());
                        tracker.SocksProxy = _proxy;

                        _trackers.Add(tracker);
                    }
                }
            }
        }

        public void RemoveTracker(TrackerClient tracker)
        {
            lock (_trackers)
            {
                _trackers.Remove(tracker);
            }
        }

        public int GetTotalDhtPeers()
        {
            lock (_dhtPeers)
            {
                return _dhtPeers.Count;
            }
        }

        public IPEndPoint[] GetDhtPeers()
        {
            lock (_dhtPeers)
            {
                return _dhtPeers.ToArray();
            }
        }

        #endregion

        #region properties

        public BinaryID NetworkID
        { get { return _networkID; } }

        public bool IsTrackerRunning
        { get { return (_trackerUpdateTimer != null); } }

        public bool LookupOnly
        {
            get { return _lookupOnly; }
            set { _lookupOnly = value; }
        }

        public SocksClient SocksClient
        {
            get { return _proxy; }
            set
            {
                _proxy = value;

                lock (_trackers)
                {
                    foreach (TrackerClient tracker in _trackers)
                    {
                        tracker.SocksProxy = _proxy;
                    }
                }
            }
        }

        #endregion
    }
}
