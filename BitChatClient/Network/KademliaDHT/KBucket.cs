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
using System.Net;
using System.Threading;

namespace BitChatClient.Network.KademliaDHT
{
    class KBucket
    {
        #region variables

        const int HEALTH_PING_MAX_RETRIES = 2;
        const int BUCKET_STALE_TIMEOUT_SECONDS = 900; //15mins timeout before declaring node stale

        int _k;
        BinaryID _bucketID;
        int _bucketDepth;
        bool _bucketContainsCurrentNode;
        DateTime _lastChanged;

        KBucket _parentBucket = null;

        Dictionary<BinaryID, NodeContact> _contacts;
        Dictionary<BinaryID, NodeContact> _replacementContacts;

        KBucket _leftBucket = null;
        KBucket _rightBucket = null;

        DhtRpcQueryManager _queryManager;

        object _lockObj;

        #endregion

        #region constructor

        public KBucket(int k, NodeContact currentNode, DhtRpcQueryManager queryManager)
        {
            _k = k;
            _bucketDepth = 0;

            _contacts = new Dictionary<BinaryID, NodeContact>();
            _replacementContacts = new Dictionary<BinaryID, NodeContact>();

            _contacts.Add(currentNode.NodeID, currentNode);
            _bucketContainsCurrentNode = true;
            _lastChanged = DateTime.UtcNow;

            _queryManager = queryManager;
            _lockObj = new object();
        }

        private KBucket(KBucket parentBucket, bool left, int k)
        {
            _k = k;
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

            _queryManager = _parentBucket._queryManager;
            _lockObj = _parentBucket._lockObj;
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

        #endregion

        #region private

        private bool IsBucketStale()
        {
            return (DateTime.UtcNow - _lastChanged).TotalSeconds > BUCKET_STALE_TIMEOUT_SECONDS;
        }

        private void CheckContactHealthAsync(object state)
        {
            int retries = 0;
            NodeContact contact = state as NodeContact;

            do
            {
                try
                {
                    if (_queryManager.Ping(contact))
                        return; //contact replied; do nothing.
                }
                catch
                { }

                retries++;
            }
            while (retries < HEALTH_PING_MAX_RETRIES);

            lock (_lockObj)
            {
                if (_contacts != null) //in case if bucket got split
                {
                    //remove node contact if there exists any replacement so that replacement node contact takes its place
                    if (_replacementContacts.Count > 0)
                        RemoveContact(contact);
                }
            }
        }

        private void RefreshBucketAsync(object state)
        {
            try
            {
                //get random node ID in the bucket range
                BinaryID randomNodeID = (BinaryID.GenerateRandomID160() << _bucketDepth) | _bucketID;

                //find closest contacts for current node id
                NodeContact[] initialContacts = GetKClosestContacts(randomNodeID);

                if (initialContacts.Length < 1)
                    return;

                NodeContact[] closestContacts = _queryManager.QueryFindNode(initialContacts, randomNodeID);

                if (closestContacts != null)
                {
                    foreach (NodeContact contact in closestContacts)
                    {
                        if (FindContact(contact.NodeID) == null)
                            AddContact(contact);
                    }
                }

            }
            catch
            { }
        }

        private NodeContact[] GetAllContacts()
        {
            lock (_lockObj)
            {
                NodeContact[] contacts;

                if (_contacts == null)
                {
                    NodeContact[] leftContacts = _leftBucket.GetAllContacts();
                    NodeContact[] rightContacts = _rightBucket.GetAllContacts();

                    contacts = new NodeContact[leftContacts.Length + rightContacts.Length];

                    Array.Copy(leftContacts, contacts, leftContacts.Length);
                    Array.Copy(rightContacts, 0, contacts, leftContacts.Length, rightContacts.Length);
                }
                else
                {
                    if (_bucketContainsCurrentNode)
                    {
                        contacts = new NodeContact[_contacts.Count - 1];
                        int i = 0;

                        foreach (NodeContact contact in _contacts.Values)
                        {
                            if (!contact.IsCurrentNode)
                            {
                                contacts[i] = contact;
                                i++;
                            }
                        }
                    }
                    else
                    {
                        contacts = new NodeContact[_contacts.Count];
                        _contacts.Values.CopyTo(contacts, 0);
                    }
                }

                return contacts;
            }
        }

        #endregion

        #region public

        public bool AddContact(NodeContact contact)
        {
            lock (_lockObj)
            {
                if (_contacts == null)
                {
                    if ((_leftBucket._bucketID & contact.NodeID) == _leftBucket._bucketID)
                        return _leftBucket.AddContact(contact);
                    else
                        return _rightBucket.AddContact(contact);
                }
                else
                {
                    if (_contacts.Count < _k)
                    {
                        if (!_contacts.ContainsKey(contact.NodeID))
                        {
                            _contacts.Add(contact.NodeID, contact);
                            _lastChanged = DateTime.UtcNow;

                            if (contact.IsCurrentNode)
                                _bucketContainsCurrentNode = true;
                        }

                        return true;
                    }
                    else
                    {
                        if (_bucketContainsCurrentNode)
                        {
                            if (!_contacts.ContainsKey(contact.NodeID))
                            {
                                _contacts.Add(contact.NodeID, contact);
                                _lastChanged = DateTime.UtcNow;
                            }
                            else
                            {
                                return true;
                            }

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

                            if (staleContact == null)
                            {
                                //no stale contact found to replace with current contact
                                //split current bucket since count > k
                                _leftBucket = new KBucket(this, true, _k);
                                _rightBucket = new KBucket(this, false, _k);

                                foreach (KeyValuePair<BinaryID, NodeContact> nodeItem in _contacts)
                                {
                                    if ((_leftBucket._bucketID & nodeItem.Key) == _leftBucket._bucketID)
                                        _leftBucket.AddContact(nodeItem.Value);
                                    else
                                        _rightBucket.AddContact(nodeItem.Value);
                                }

                                //demote current object as bucket
                                _contacts = null;
                                _replacementContacts = null;
                                _bucketContainsCurrentNode = false;
                            }
                            else
                            {
                                //remove stale contact
                                _contacts.Remove(staleContact.NodeID);
                            }

                            return true;
                        }
                        else
                        {
                            //never split buckets that arent on the same side of the tree as the current node

                            if (_replacementContacts.Count < _k)
                            {
                                //keep the current node contact in replacement cache
                                if (!_replacementContacts.ContainsKey(contact.NodeID))
                                {
                                    _replacementContacts.Add(contact.NodeID, contact);
                                }

                                return true;
                            }
                            else
                            {
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
                                {
                                    return false;
                                }
                                else
                                {
                                    //remove bad contact & keep the current node contact in replacement cache
                                    _replacementContacts.Remove(staleReplacementContact.NodeID);

                                    if (!_replacementContacts.ContainsKey(contact.NodeID))
                                    {
                                        _replacementContacts.Add(contact.NodeID, contact);
                                    }

                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        public bool RemoveContact(NodeContact contact)
        {
            lock (_lockObj)
            {
                if (_contacts == null)
                {
                    bool contactWasRemoved;

                    if ((_leftBucket._bucketID & contact.NodeID) == _leftBucket._bucketID)
                        contactWasRemoved = _leftBucket.RemoveContact(contact);
                    else
                        contactWasRemoved = _rightBucket.RemoveContact(contact);

                    //check child buckets total for k
                    int totalBucketContacts = _leftBucket._contacts.Count + _rightBucket._contacts.Count;

                    if (totalBucketContacts <= _k)
                    {
                        //combine buckets
                        _contacts = new Dictionary<BinaryID, NodeContact>(totalBucketContacts);
                        _replacementContacts = new Dictionary<BinaryID, NodeContact>();

                        //move contacts to this bucket
                        foreach (KeyValuePair<BinaryID, NodeContact> nodeItem in _leftBucket._contacts)
                        {
                            if (nodeItem.Value.IsCurrentNode)
                                _bucketContainsCurrentNode = true;

                            _contacts.Add(nodeItem.Key, nodeItem.Value);
                        }

                        foreach (KeyValuePair<BinaryID, NodeContact> nodeItem in _rightBucket._contacts)
                        {
                            if (nodeItem.Value.IsCurrentNode)
                                _bucketContainsCurrentNode = true;

                            _contacts.Add(nodeItem.Key, nodeItem.Value);
                        }

                        _leftBucket = null;
                        _rightBucket = null;
                    }

                    return contactWasRemoved;
                }
                else
                {
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

                            if (goodContact != null)
                            {
                                _replacementContacts.Remove(goodContact.NodeID);
                                _contacts.Add(goodContact.NodeID, goodContact);
                                _lastChanged = DateTime.UtcNow;
                            }
                        }

                        return true;
                    }
                    else
                    {
                        return _replacementContacts.Remove(contact.NodeID);
                    }
                }
            }
        }

        public KBucket FindClosestBucket(BinaryID nodeID)
        {
            lock (_lockObj)
            {
                if (_contacts == null)
                {
                    if ((_leftBucket._bucketID & nodeID) == _leftBucket._bucketID)
                    {
                        KBucket bucket = _leftBucket.FindClosestBucket(nodeID);

                        if (bucket._contacts.Count < 1)
                            return _rightBucket.FindClosestBucket(nodeID);

                        return bucket;
                    }
                    else
                    {
                        KBucket bucket = _rightBucket.FindClosestBucket(nodeID);

                        if (bucket._contacts.Count < 1)
                            return _leftBucket.FindClosestBucket(nodeID);

                        return bucket;
                    }
                }
                else
                {
                    _lastChanged = DateTime.UtcNow;
                    return this;
                }
            }
        }

        public NodeContact[] GetKClosestContacts(BinaryID networkID)
        {
            KBucket closestBucket = FindClosestBucket(networkID);
            NodeContact[] contacts = closestBucket.GetAllContacts();

            if (contacts.Length < _k)
            {
                while (closestBucket._parentBucket != null)
                {
                    contacts = closestBucket._parentBucket.GetAllContacts();

                    if (contacts.Length >= _k)
                        return GetClosestContacts(contacts, networkID, _k);

                    closestBucket = closestBucket._parentBucket;
                }

                return contacts;
            }
            else
            {
                return GetClosestContacts(contacts, networkID, _k);
            }
        }

        public int TotalContacts()
        {
            lock (_lockObj)
            {
                if (_contacts == null)
                    return _leftBucket.TotalContacts() + _rightBucket.TotalContacts();
                else
                    return _contacts.Count;
            }
        }

        public NodeContact FindContact(BinaryID nodeID)
        {
            lock (_lockObj)
            {
                if (_contacts == null)
                {
                    NodeContact contact = _leftBucket.FindContact(nodeID);

                    if (contact != null)
                        return contact;
                    else
                        return _rightBucket.FindContact(nodeID);
                }
                else
                {
                    if (_contacts.ContainsKey(nodeID))
                        return _contacts[nodeID];

                    if (_replacementContacts.ContainsKey(nodeID))
                        return _replacementContacts[nodeID];

                    return null;
                }
            }
        }

        public bool ContactExists(IPEndPoint contactEP)
        {
            lock (_lockObj)
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
        }

        public void CheckContactHealth()
        {
            lock (_lockObj)
            {
                if (_contacts == null)
                {
                    _leftBucket.CheckContactHealth();
                    _rightBucket.CheckContactHealth();
                }
                else
                {
                    foreach (NodeContact contact in _contacts.Values)
                    {
                        if (contact.IsStale())
                            ThreadPool.QueueUserWorkItem(CheckContactHealthAsync, contact);
                    }
                }
            }
        }

        public void RefreshBucket()
        {
            lock (_lockObj)
            {
                if (_contacts == null)
                {
                    _leftBucket.RefreshBucket();
                    _rightBucket.RefreshBucket();
                }
                else
                {
                    if (IsBucketStale())
                        ThreadPool.QueueUserWorkItem(RefreshBucketAsync, null);
                }
            }
        }

        #endregion
    }
}
