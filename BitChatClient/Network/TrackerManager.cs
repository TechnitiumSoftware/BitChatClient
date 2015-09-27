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
using System.Threading;
using TechnitiumLibrary.Net.BitTorrent;

namespace BitChatClient.Network
{
    delegate void TrackerManagerDiscoveredPeers(TrackerManager sender, List<IPEndPoint> peerEPs);

    class TrackerManager
    {
        #region events

        public event TrackerManagerDiscoveredPeers DiscoveredPeers;

        #endregion

        #region variables

        BinaryID _networkID;
        IConnectionInfo _info;
        bool _lookupOnly;

        const int _TRACKER_TIMER_CHECK_INTERVAL = 10000;
        List<TrackerClient> _trackers = new List<TrackerClient>();
        Timer _trackerUpdateTimer;

        #endregion

        #region constructor

        public TrackerManager(BinaryID networkID, IConnectionInfo info, bool lookupOnly = false)
        {
            _networkID = networkID;
            _info = info;
            _lookupOnly = lookupOnly;
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

                IPEndPoint localEP = new IPEndPoint(IPAddress.Any, _info.GetExternalPort());

                lock (_trackers)
                {
                    foreach (TrackerClient tracker in _trackers)
                    {
                        if (!tracker.IsUpdating && (forceUpdate || (tracker.NextUpdateIn().TotalSeconds < 1)))
                            ThreadPool.QueueUserWorkItem(new WaitCallback(UpdateTrackerAsync), new object[] { tracker, @event, localEP });
                    }
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
                        }

                        break;
                }
            }
            catch
            { }
        }

        #endregion

        #region public

        public void StartTracking(Uri[] trackerURIs = null)
        {
            if (trackerURIs != null)
            {
                lock (_trackers)
                {
                    _trackers.Clear();

                    foreach (Uri trackerURI in trackerURIs)
                        _trackers.Add(TrackerClient.Create(trackerURI, _networkID.ID, TrackerClientID.CreateDefaultID()));
                }
            }

            if (_trackerUpdateTimer == null)
            {
                lock (_trackers)
                {
                    if (_trackers.Count > 0)
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
                IPEndPoint localEP = new IPEndPoint(IPAddress.Any, _info.GetExternalPort());

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

        public TrackerClient AddTracker(Uri trackerURI)
        {
            lock (_trackers)
            {
                foreach (TrackerClient tracker in _trackers)
                {
                    if (tracker.TrackerUri.Equals(trackerURI))
                        return null;
                }

                TrackerClient newTracker = TrackerClient.Create(trackerURI, _networkID.ID, TrackerClientID.CreateDefaultID());

                _trackers.Add(newTracker);

                return newTracker;
            }
        }

        public void AddTracker(Uri[] trackerURIs)
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
                        _trackers.Add(TrackerClient.Create(trackerURI, _networkID.ID, TrackerClientID.CreateDefaultID()));
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

        public List<IPEndPoint> GetPeers()
        {
            List<IPEndPoint> peerEPs = new List<IPEndPoint>(10);

            foreach (TrackerClient tracker in _trackers)
            {
                foreach (IPEndPoint peerEP in tracker.Peers)
                {
                    if (!peerEPs.Contains(peerEP))
                        peerEPs.Add(peerEP);
                }
            }

            return peerEPs;
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

        #endregion
    }
}
