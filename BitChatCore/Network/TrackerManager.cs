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

using BitChatCore.Network.KademliaDHT;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Net.Proxy;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore.Network
{
    delegate void TrackerManagerDiscoveredPeers(TrackerManager sender, IEnumerable<IPEndPoint> peerEPs);

    class TrackerManager : IDisposable
    {
        #region events

        public event TrackerManagerDiscoveredPeers DiscoveredPeers;

        #endregion

        #region variables

        const int DHT_DEFAULT_UPDATE_INTERVAL_SECONDS = 5 * 60; //5 mins
        const int TRACKER_TIMER_CHECK_INTERVAL = 10000;
        const int TRACKER_MAX_RETRIES = 3;
        const int TRACKER_RETRY_UPDATE_INTERVAL_SECONDS = 30; //30 sec
        const int TRACKER_FAILED_UPDATE_INTERVAL_SECONDS = 30 * 60; //30 mins

        readonly BinaryNumber _networkID;
        readonly int _servicePort;
        readonly DhtNode _ipv4DhtNode;
        readonly DhtNode _ipv6DhtNode;
        int _customUpdateInterval;
        bool _lookupOnly;

        NetProxy _proxy;

        readonly List<IPEndPoint> _dhtPeers = new List<IPEndPoint>();
        DateTime _dhtLastUpdated;
        Exception _dhtLastException;

        readonly List<TrackerClient> _trackers = new List<TrackerClient>();
        Timer _trackerUpdateTimer;

        #endregion

        #region constructor

        public TrackerManager(BinaryNumber networkID, int servicePort, DhtNode ipv4DhtNode, DhtNode ipv6DhtNode, int customUpdateInterval, bool lookupOnly = false)
        {
            _networkID = networkID;
            _servicePort = servicePort;
            _ipv4DhtNode = ipv4DhtNode;
            _ipv6DhtNode = ipv6DhtNode;
            _customUpdateInterval = customUpdateInterval;
            _lookupOnly = lookupOnly;
        }

        #endregion

        #region IDisposable

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
                if (disposing)
                    StopTracking();

                _disposed = true;
            }
        }

        #endregion

        #region private

        private int GetDhtUpdateInterval()
        {
            if (_customUpdateInterval > 0)
                return _customUpdateInterval;

            return DHT_DEFAULT_UPDATE_INTERVAL_SECONDS;
        }

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
                if (forceUpdate || (DateTime.UtcNow - _dhtLastUpdated).TotalSeconds > GetDhtUpdateInterval())
                {
                    _dhtPeers.Clear();

                    if (_ipv4DhtNode != null)
                        ThreadPool.QueueUserWorkItem(UpdateDhtAsync, new object[] { _ipv4DhtNode, localEP });

                    if (_ipv6DhtNode != null)
                        ThreadPool.QueueUserWorkItem(UpdateDhtAsync, new object[] { _ipv6DhtNode, localEP });
                }
            }
            catch
            { }
            finally
            {
                if (_trackerUpdateTimer != null)
                    _trackerUpdateTimer.Change(TRACKER_TIMER_CHECK_INTERVAL, Timeout.Infinite);
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
                            if (_ipv4DhtNode != null)
                                _ipv4DhtNode.AddNode(tracker.Peers);

                            if (_ipv6DhtNode != null)
                                _ipv6DhtNode.AddNode(tracker.Peers);

                            DiscoveredPeers?.Invoke(this, tracker.Peers);
                        }

                        break;
                }

                tracker.CustomUpdateInterval = _customUpdateInterval;
            }
            catch
            {
                if (tracker.RetriesDone < TRACKER_MAX_RETRIES)
                    tracker.CustomUpdateInterval = TRACKER_RETRY_UPDATE_INTERVAL_SECONDS;
                else
                    tracker.CustomUpdateInterval = TRACKER_FAILED_UPDATE_INTERVAL_SECONDS;
            }
        }

        private void UpdateDhtAsync(object state)
        {
            object[] parameters = state as object[];

            DhtNode dhtNode = parameters[0] as DhtNode;
            IPEndPoint localEP = parameters[1] as IPEndPoint;

            try
            {
                IPEndPoint[] peers;

                if (_lookupOnly)
                    peers = dhtNode.FindPeers(_networkID);
                else
                    peers = dhtNode.Announce(_networkID, localEP.Port);

                _dhtLastUpdated = DateTime.UtcNow;
                _dhtLastException = null; //reset last error

                if ((peers != null) && (peers.Length > 0))
                {
                    _dhtPeers.AddRange(peers);
                    DiscoveredPeers?.Invoke(this, peers);
                }
            }
            catch (Exception ex)
            {
                _dhtLastException = ex;
            }
        }

        #endregion

        #region public

        public void StartTracking(IEnumerable<Uri> trackerURIs = null)
        {
            lock (_trackers)
            {
                if (trackerURIs != null)
                {
                    _trackers.Clear();

                    foreach (Uri trackerURI in trackerURIs)
                    {
                        TrackerClient tracker = TrackerClient.Create(trackerURI, _networkID.Number, TrackerClientID.CreateDefaultID(), _customUpdateInterval);
                        tracker.Proxy = _proxy;

                        _trackers.Add(tracker);
                    }
                }

                if (_trackerUpdateTimer == null)
                {
                    if ((_trackers.Count > 0) || (_ipv4DhtNode != null) || (_ipv6DhtNode != null))
                        _trackerUpdateTimer = new Timer(TrackerUpdateTimerCallBack, TrackerClientEvent.Started, 1000, Timeout.Infinite);
                }
            }
        }

        public void StopTracking()
        {
            lock (_trackers)
            {
                if (_trackerUpdateTimer != null)
                {
                    _trackerUpdateTimer.Dispose();
                    _trackerUpdateTimer = null;

                    //update trackers
                    IPEndPoint localEP = new IPEndPoint(IPAddress.Any, _servicePort);

                    foreach (TrackerClient tracker in _trackers)
                    {
                        ThreadPool.QueueUserWorkItem(UpdateTrackerAsync, new object[] { tracker, TrackerClientEvent.Stopped, localEP });
                    }
                }
            }
        }

        public void ScheduleUpdateNow()
        {
            DhtUpdate();

            lock (_trackers)
            {
                foreach (TrackerClient tracker in _trackers)
                {
                    if (tracker.RetriesDone == 0)
                        tracker.ScheduleUpdateNow();
                }
            }
        }

        public TrackerClient[] GetTrackers()
        {
            lock (_trackers)
            {
                return _trackers.ToArray();
            }
        }

        public Uri[] GetTrackerURIs()
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
                    TrackerClient tracker = TrackerClient.Create(trackerURI, _networkID.Number, TrackerClientID.CreateDefaultID(), _customUpdateInterval);
                    tracker.Proxy = _proxy;

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
                        TrackerClient tracker = TrackerClient.Create(trackerURI, _networkID.Number, TrackerClientID.CreateDefaultID(), _customUpdateInterval);
                        tracker.Proxy = _proxy;

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

        public int DhtGetTotalPeers()
        {
            return _dhtPeers.Count;
        }

        public IPEndPoint[] DhtGetPeers()
        {
            return _dhtPeers.ToArray();
        }

        public void DhtUpdate()
        {
            _dhtLastUpdated = DateTime.UtcNow.AddSeconds(GetDhtUpdateInterval() * -1);
        }

        public TimeSpan DhtNextUpdateIn()
        {
            return _dhtLastUpdated.AddSeconds(GetDhtUpdateInterval()) - DateTime.UtcNow;
        }

        public Exception DhtLastException()
        {
            return _dhtLastException;
        }

        #endregion

        #region properties

        public BinaryNumber NetworkID
        { get { return _networkID; } }

        public bool IsTrackerRunning
        { get { return (_trackerUpdateTimer != null); } }

        public int CustomUpdateInterval
        {
            get { return _customUpdateInterval; }
            set
            {
                _customUpdateInterval = value;

                lock (_trackers)
                {
                    foreach (TrackerClient tracker in _trackers)
                    {
                        if (tracker.RetriesDone == 0)
                            tracker.CustomUpdateInterval = _customUpdateInterval;
                    }
                }
            }
        }

        public bool LookupOnly
        {
            get { return _lookupOnly; }
            set { _lookupOnly = value; }
        }

        public NetProxy Proxy
        {
            get { return _proxy; }
            set
            {
                _proxy = value;

                lock (_trackers)
                {
                    foreach (TrackerClient tracker in _trackers)
                    {
                        tracker.Proxy = _proxy;
                    }
                }
            }
        }

        #endregion
    }
}
