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

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace BitChatCore.Network.KademliaDHT
{
    class KBucket : IDisposable
    {
        #region variables

        const int BUCKET_STALE_TIMEOUT_SECONDS = 900; //15mins timeout before declaring node stale

        BinaryID _bucketID;
        int _bucketDepth;
        bool _bucketContainsCurrentNode;
        DateTime _lastChanged;

        KBucket _parentBucket = null;

        Dictionary<BinaryID, NodeContact> _contacts;
        Dictionary<BinaryID, NodeContact> _replacementContacts;

        KBucket _leftBucket = null;
        KBucket _rightBucket = null;

        int _totalContacts = 0;
        int _totalReplacementContacts = 0;

        const int LOCK_TIMEOUT = 5000;
        ReaderWriterLockSlim _lock;

        #endregion

        #region constructor

        public KBucket(NodeContact currentNode)
        {
            _bucketDepth = 0;

            _contacts = new Dictionary<BinaryID, NodeContact>();
            _replacementContacts = new Dictionary<BinaryID, NodeContact>();

            _contacts.Add(currentNode.NodeID, currentNode);
            _bucketContainsCurrentNode = true;
            _lastChanged = DateTime.UtcNow;

            _lock = new ReaderWriterLockSlim();
        }

        private KBucket(KBucket parentBucket, bool left)
        {
            _bucketDepth = parentBucket._bucketDepth + 1;

            _parentBucket = parentBucket;

            _contacts = new Dictionary<BinaryID, NodeContact>();
            _replacementContacts = new Dictionary<BinaryID, NodeContact>();

            if (parentBucket._bucketID == null)
            {
                _bucketID = new BinaryID(new byte[20]);

                if (left)
                    _bucketID.ID[0] = 0x80;
            }
            else
            {
                if (left)
                {
                    _bucketID = new BinaryID(new byte[20]);
                    _bucketID.ID[0] |= 0x80;

                    _bucketID = parentBucket._bucketID | (_bucketID >> (_bucketDepth - 1));
                }
                else
                {
                    _bucketID = parentBucket._bucketID;
                }
            }

            _lastChanged = DateTime.UtcNow;

            _lock = new ReaderWriterLockSlim();
        }

        #endregion

        #region IDisposable

        ~KBucket()
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
                if (_lock != null)
                {
                    try
                    {
                        if (!_lock.TryEnterWriteLock(LOCK_TIMEOUT))
                            throw new Exception("Could not enter write lock.");

                        try
                        {
                            if (_leftBucket != null)
                            {
                                _leftBucket.Dispose();
                                _leftBucket = null;
                            }

                            if (_rightBucket != null)
                            {
                                _rightBucket.Dispose();
                                _rightBucket = null;
                            }
                        }
                        finally
                        {
                            _lock.ExitWriteLock();
                        }
                    }
                    catch
                    { }

                    _lock.Dispose();
                    _lock = null;
                }

                _disposed = true;
            }
        }

        #endregion

        #region static

        public static NodeContact[] GetClosestContacts(IEnumerable<NodeContact> contacts, BinaryID nodeID, int count)
        {
            NodeContact[] closestContacts = new NodeContact[count];
            BinaryID[] min = new BinaryID[count];
            BinaryID distance;
            int ubound = count - 1;
            int i;
            int j;

            foreach (NodeContact contact in contacts)
            {
                distance = nodeID ^ contact.NodeID;

                for (i = 0; i < count; i++)
                {
                    if ((min[i] == null) || (distance < min[i]))
                    {
                        //demote existing values
                        for (j = ubound; j > i; j--)
                        {
                            min[j] = min[j - 1];
                            closestContacts[j] = closestContacts[j - 1];
                        }

                        //place current on top
                        min[i] = distance;
                        closestContacts[i] = contact;
                        break;
                    }
                }
            }

            return closestContacts;
        }

        private static void SplitBucket(KBucket bucket)
        {
            KBucket currentBucket = bucket;

            while (true)
            {
                KBucket leftBucket = new KBucket(currentBucket, true);
                KBucket rightBucket = new KBucket(currentBucket, false);

                foreach (KeyValuePair<BinaryID, NodeContact> nodeItem in currentBucket._contacts)
                {
                    if ((leftBucket._bucketID & nodeItem.Key) == leftBucket._bucketID)
                    {
                        leftBucket._contacts.Add(nodeItem.Key, nodeItem.Value);

                        if (nodeItem.Value.IsCurrentNode)
                            leftBucket._bucketContainsCurrentNode = true;
                    }
                    else
                    {
                        rightBucket._contacts.Add(nodeItem.Key, nodeItem.Value);

                        if (nodeItem.Value.IsCurrentNode)
                            rightBucket._bucketContainsCurrentNode = true;
                    }
                }

                leftBucket._totalContacts = leftBucket._contacts.Count;
                rightBucket._totalContacts = rightBucket._contacts.Count;

                //demote current bucket to tree node
                currentBucket._contacts = null;
                currentBucket._replacementContacts = null;
                currentBucket._bucketContainsCurrentNode = false;
                currentBucket._leftBucket = leftBucket;
                currentBucket._rightBucket = rightBucket;

                if (leftBucket._contacts.Count > DhtClient.KADEMLIA_K)
                {
                    currentBucket = leftBucket;
                }
                else if (rightBucket._contacts.Count > DhtClient.KADEMLIA_K)
                {
                    currentBucket = rightBucket;
                }
                else
                {
                    break;
                }
            }
        }

        private static void JoinBucket(KBucket parentBucket)
        {
            if ((parentBucket._leftBucket == null) || (parentBucket._rightBucket == null))
                return;

            parentBucket._contacts = new Dictionary<BinaryID, NodeContact>(parentBucket._totalContacts);
            parentBucket._replacementContacts = new Dictionary<BinaryID, NodeContact>(parentBucket._totalReplacementContacts);

            //move contacts to this bucket
            foreach (KeyValuePair<BinaryID, NodeContact> nodeItem in parentBucket._leftBucket._contacts)
            {
                parentBucket._contacts.Add(nodeItem.Key, nodeItem.Value);

                if (nodeItem.Value.IsCurrentNode)
                    parentBucket._bucketContainsCurrentNode = true;
            }

            foreach (KeyValuePair<BinaryID, NodeContact> nodeItem in parentBucket._rightBucket._contacts)
            {
                parentBucket._contacts.Add(nodeItem.Key, nodeItem.Value);

                if (nodeItem.Value.IsCurrentNode)
                    parentBucket._bucketContainsCurrentNode = true;
            }

            parentBucket._leftBucket._parentBucket = null;
            parentBucket._rightBucket._parentBucket = null;

            parentBucket._leftBucket.Dispose();
            parentBucket._rightBucket.Dispose();

            parentBucket._leftBucket = null;
            parentBucket._rightBucket = null;
        }

        #endregion

        #region private

        private void IncrementContactCount()
        {
            Interlocked.Increment(ref _totalContacts);

            KBucket currentBucket = this;

            while (currentBucket._parentBucket != null)
            {
                Interlocked.Increment(ref currentBucket._parentBucket._totalContacts);
                currentBucket = currentBucket._parentBucket;
            }
        }

        private void DecrementContactCount()
        {
            Interlocked.Decrement(ref _totalContacts);

            KBucket currentBucket = this;

            while (currentBucket._parentBucket != null)
            {
                Interlocked.Decrement(ref currentBucket._parentBucket._totalContacts);
                currentBucket = currentBucket._parentBucket;
            }
        }

        private void IncrementReplacementContactCount()
        {
            Interlocked.Increment(ref _totalReplacementContacts);

            KBucket currentBucket = this;

            while (currentBucket._parentBucket != null)
            {
                Interlocked.Increment(ref currentBucket._parentBucket._totalReplacementContacts);
                currentBucket = currentBucket._parentBucket;
            }
        }

        private void DecrementReplacementContactCount()
        {
            Interlocked.Decrement(ref _totalReplacementContacts);

            KBucket currentBucket = this;

            while (currentBucket._parentBucket != null)
            {
                Interlocked.Decrement(ref currentBucket._parentBucket._totalReplacementContacts);
                currentBucket = currentBucket._parentBucket;
            }
        }

        private void CheckContactHealthAsync(object state)
        {
            object[] param = state as object[];

            DhtClient dhtClient = param[0] as DhtClient;
            NodeContact contact = param[1] as NodeContact;

            if (dhtClient.Ping(contact))
                return; //contact replied; do nothing.

            try
            {
                //remove stale node
                RemoveContactFromCurrentBucket(contact);
            }
            catch
            { }
        }

        private void RefreshBucketAsync(object state)
        {
            DhtClient dhtClient = state as DhtClient;

            //get random node ID in the bucket range
            BinaryID randomNodeID = (BinaryID.GenerateRandomID160() << _bucketDepth) | _bucketID;

            //find closest contacts for current node id
            NodeContact[] initialContacts = GetKClosestContacts(randomNodeID);

            if (initialContacts.Length > 0)
                dhtClient.QueryFindNode(initialContacts, randomNodeID);
        }

        #endregion

        #region public

        public bool AddContactInCurrentBucket(NodeContact contact)
        {
            if (!_lock.TryEnterWriteLock(LOCK_TIMEOUT))
                throw new Exception("Could not enter write lock.");

            try
            {
                if (_contacts == null)
                    return false;

                if (_contacts.ContainsKey(contact.NodeID))
                {
                    _contacts[contact.NodeID].UpdateLastSeenTime();
                    return true;
                }

                if (_contacts.Count < DhtClient.KADEMLIA_K)
                {
                    _contacts.Add(contact.NodeID, contact);
                    IncrementContactCount();
                    _lastChanged = DateTime.UtcNow;

                    if (contact.IsCurrentNode)
                        _bucketContainsCurrentNode = true;

                    return true;
                }

                if (_bucketContainsCurrentNode)
                {
                    _contacts.Add(contact.NodeID, contact);
                    _lastChanged = DateTime.UtcNow;

                    //remove any stale node contact
                    NodeContact staleContact = null;

                    foreach (NodeContact existingContact in _contacts.Values)
                    {
                        if (existingContact.IsStale())
                        {
                            staleContact = existingContact;
                            break;
                        }
                    }

                    if (staleContact != null)
                    {
                        //remove stale contact
                        _contacts.Remove(staleContact.NodeID);
                        return true;
                    }

                    //no stale contact found to replace with current contact
                    IncrementContactCount();

                    //split current bucket since count > k
                    SplitBucket(this);

                    return true;
                }

                //never split buckets that arent on the same side of the tree as the current node

                if (_replacementContacts.ContainsKey(contact.NodeID))
                {
                    _replacementContacts[contact.NodeID].UpdateLastSeenTime();
                    return true;
                }

                if (_replacementContacts.Count < DhtClient.KADEMLIA_K)
                {
                    //keep the current node contact in replacement cache
                    _replacementContacts.Add(contact.NodeID, contact);
                    IncrementReplacementContactCount();
                    return true;
                }

                //find stale contact from replacement cache and replace with current contact
                NodeContact staleReplacementContact = null;

                foreach (NodeContact replacementContact in _replacementContacts.Values)
                {
                    if (replacementContact.IsStale())
                    {
                        staleReplacementContact = replacementContact;
                        break;
                    }
                }

                if (staleReplacementContact == null)
                    return false;

                //remove bad contact & keep the current node contact in replacement cache
                _replacementContacts.Remove(staleReplacementContact.NodeID);
                _replacementContacts.Add(contact.NodeID, contact);
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool RemoveContactFromCurrentBucket(NodeContact contact)
        {
            if (!_lock.TryEnterWriteLock(LOCK_TIMEOUT))
                throw new Exception("Could not enter write lock.");

            try
            {
                if (_contacts == null)
                    return false;

                if (_replacementContacts.Count < 1)
                    return false;

                if (_contacts.Remove(contact.NodeID))
                {
                    if (_replacementContacts.Count > 0)
                    {
                        //add good replacement contact to main contacts
                        NodeContact goodContact = null;

                        foreach (NodeContact replacementContact in _replacementContacts.Values)
                        {
                            if (!replacementContact.IsStale())
                            {
                                if ((goodContact == null) || (replacementContact.LastSeen > goodContact.LastSeen))
                                    goodContact = replacementContact;
                            }
                        }

                        if (goodContact == null)
                        {
                            //no good replacement contact available
                            DecrementContactCount();
                        }
                        else
                        {
                            //move good replacement contact to main contacts
                            _replacementContacts.Remove(goodContact.NodeID);
                            _contacts.Add(goodContact.NodeID, goodContact);

                            DecrementReplacementContactCount();
                            _lastChanged = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        //no replacement contacts available
                        DecrementContactCount();
                    }

                    //check parent bucket contact count and join the parent buckets
                    KBucket parentBucket = this._parentBucket;
                    ReaderWriterLockSlim currentLock;

                    while ((parentBucket != null) && (parentBucket._totalContacts <= DhtClient.KADEMLIA_K))
                    {
                        currentLock = parentBucket._lock;

                        if (!currentLock.TryEnterWriteLock(LOCK_TIMEOUT))
                            throw new Exception("Could not enter write lock.");

                        try
                        {
                            JoinBucket(parentBucket);

                            parentBucket = parentBucket._parentBucket;
                        }
                        finally
                        {
                            currentLock.ExitWriteLock();
                        }
                    }

                    return true;
                }
                else
                {
                    if (_replacementContacts.Remove(contact.NodeID))
                    {
                        DecrementReplacementContactCount();
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public KBucket FindClosestBucket(BinaryID nodeID)
        {
            KBucket currentBucket = this;
            ReaderWriterLockSlim currentLock = this._lock;

            while (true)
            {
                if (!currentLock.TryEnterReadLock(LOCK_TIMEOUT))
                    throw new Exception("Could not enter read lock.");

                try
                {
                    if (currentBucket._contacts != null)
                        break;

                    if ((currentBucket._leftBucket._bucketID & nodeID) == currentBucket._leftBucket._bucketID)
                        currentBucket = currentBucket._leftBucket;
                    else
                        currentBucket = currentBucket._rightBucket;
                }
                finally
                {
                    currentLock.ExitReadLock();
                }

                currentLock = currentBucket._lock;
            }

            currentBucket._lastChanged = DateTime.UtcNow;
            return currentBucket;
        }

        public NodeContact[] GetKClosestContacts(BinaryID networkID)
        {
            NodeContact[] contacts = null;
            KBucket closestBucket = FindClosestBucket(networkID);

            if (closestBucket._totalContacts >= DhtClient.KADEMLIA_K)
            {
                contacts = closestBucket.GetAllContacts(false);

                if (contacts.Length > DhtClient.KADEMLIA_K)
                    return GetClosestContacts(contacts, networkID, DhtClient.KADEMLIA_K);
                else if (contacts.Length == DhtClient.KADEMLIA_K)
                    return contacts;
                else if (closestBucket._parentBucket == null)
                    return contacts;
                else
                    contacts = null;
            }

            while (closestBucket._parentBucket != null)
            {
                KBucket parentBucket = closestBucket._parentBucket;

                if (parentBucket._totalContacts >= DhtClient.KADEMLIA_K)
                {
                    contacts = parentBucket.GetAllContacts(false);

                    if (contacts.Length > DhtClient.KADEMLIA_K)
                        return GetClosestContacts(contacts, networkID, DhtClient.KADEMLIA_K);
                    else if (contacts.Length == DhtClient.KADEMLIA_K)
                        return contacts;
                }

                closestBucket = parentBucket;
            }

            if (contacts == null)
                return closestBucket.GetAllContacts(false);
            else
                return contacts;
        }

        public NodeContact[] GetAllContacts(bool includeReplacementCache)
        {
            if (!_lock.TryEnterReadLock(LOCK_TIMEOUT))
                throw new Exception("Could not enter read lock.");

            try
            {
                NodeContact[] contacts;

                if (_contacts == null)
                {
                    NodeContact[] leftContacts = _leftBucket.GetAllContacts(includeReplacementCache);
                    NodeContact[] rightContacts = _rightBucket.GetAllContacts(includeReplacementCache);

                    contacts = new NodeContact[leftContacts.Length + rightContacts.Length];

                    Array.Copy(leftContacts, contacts, leftContacts.Length);
                    Array.Copy(rightContacts, 0, contacts, leftContacts.Length, rightContacts.Length);
                }
                else
                {
                    int cacheCount;

                    if (includeReplacementCache)
                        cacheCount = _totalReplacementContacts;
                    else
                        cacheCount = 0;

                    List<NodeContact> contactsList = new List<NodeContact>(_totalContacts + cacheCount);

                    foreach (NodeContact contact in _contacts.Values)
                    {
                        if (!contact.IsStale() && !contact.IsCurrentNode)
                            contactsList.Add(contact);
                    }

                    if (includeReplacementCache)
                    {
                        foreach (NodeContact contact in _replacementContacts.Values)
                        {
                            if (!contact.IsStale())
                                contactsList.Add(contact);
                        }
                    }

                    contacts = contactsList.ToArray();
                }

                return contacts;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public bool IsCurrentBucketFull(bool includeReplacementCache = false)
        {
            if (_contacts == null)
                return false; //node bucket doesnt know if its full

            if (_bucketContainsCurrentNode)
                return false; //bucket containing current node is never full

            if (includeReplacementCache)
                return (_totalContacts + _totalReplacementContacts) >= (DhtClient.KADEMLIA_K * 2);

            return _totalContacts >= DhtClient.KADEMLIA_K;
        }

        public NodeContact FindContactInCurrentBucket(BinaryID nodeID)
        {
            if (!_lock.TryEnterReadLock(LOCK_TIMEOUT))
                throw new Exception("Could not enter read lock.");

            try
            {
                if (_contacts == null)
                    return null;

                if (_contacts.ContainsKey(nodeID))
                    return _contacts[nodeID];

                if (_replacementContacts.ContainsKey(nodeID))
                    return _replacementContacts[nodeID];

                return null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public bool ContactExists(IPEndPoint contactEP)
        {
            if (!_lock.TryEnterReadLock(LOCK_TIMEOUT))
                throw new Exception("Could not enter read lock.");

            try
            {
                if (_contacts == null)
                {
                    if (_leftBucket.ContactExists(contactEP))
                        return true;
                    else
                        return _rightBucket.ContactExists(contactEP);
                }
                else
                {
                    foreach (NodeContact contact in _contacts.Values)
                    {
                        if (contact.NodeEP.Equals(contactEP))
                            return true;
                    }

                    foreach (NodeContact contact in _replacementContacts.Values)
                    {
                        if (contact.NodeEP.Equals(contactEP))
                            return true;
                    }

                    return false;
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void CheckContactHealth(DhtClient dhtClient)
        {
            if (!_lock.TryEnterReadLock(LOCK_TIMEOUT))
                throw new Exception("Could not enter read lock.");

            try
            {
                if (_contacts == null)
                {
                    _leftBucket.CheckContactHealth(dhtClient);
                    _rightBucket.CheckContactHealth(dhtClient);
                }
                else
                {
                    foreach (NodeContact contact in _contacts.Values)
                    {
                        if (contact.IsStale())
                            ThreadPool.QueueUserWorkItem(CheckContactHealthAsync, new object[] { dhtClient, contact });
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void RefreshBucket(DhtClient dhtClient)
        {
            if (!_lock.TryEnterReadLock(LOCK_TIMEOUT))
                throw new Exception("Could not enter read lock.");

            try
            {
                if (_contacts == null)
                {
                    _leftBucket.RefreshBucket(dhtClient);
                    _rightBucket.RefreshBucket(dhtClient);
                }
                else
                {
                    if ((DateTime.UtcNow - _lastChanged).TotalSeconds > BUCKET_STALE_TIMEOUT_SECONDS)
                        ThreadPool.QueueUserWorkItem(RefreshBucketAsync, dhtClient);
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        #endregion

        #region properties

        public int TotalContacts
        { get { return _totalContacts; } }

        public int TotalReplacementContacts
        { get { return _totalReplacementContacts; } }

        #endregion
    }
}
