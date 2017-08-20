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
using System.Threading;

namespace BitChatCore.Network.KademliaDHT
{
    class KBucket
    {
        #region variables

        const int BUCKET_STALE_TIMEOUT_SECONDS = 900; //15 mins timeout before declaring node stale

        readonly BinaryID _bucketID;
        readonly int _bucketDepth;
        DateTime _lastChanged;

        readonly KBucket _parentBucket;

        volatile NodeContact[] _contacts;
        volatile int _contactCount;

        volatile KBucket _leftBucket;
        volatile KBucket _rightBucket;

        #endregion

        #region constructor

        public KBucket(NodeContact currentNode)
        {
            _bucketDepth = 0;

            _contacts = new NodeContact[DhtNode.KADEMLIA_K * 2];

            _contacts[0] = currentNode;
            _contactCount = 1;
            _lastChanged = DateTime.UtcNow;
        }

        private KBucket(KBucket parentBucket, bool left)
        {
            _bucketDepth = parentBucket._bucketDepth + 1;

            _parentBucket = parentBucket;

            _contacts = new NodeContact[DhtNode.KADEMLIA_K * 2];

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
                    _bucketID.ID[0] = 0x80;

                    _bucketID = parentBucket._bucketID | (_bucketID >> (_bucketDepth - 1));
                }
                else
                {
                    _bucketID = parentBucket._bucketID;
                }
            }

            _lastChanged = DateTime.UtcNow;
        }

        #endregion

        #region static

        public static NodeContact[] GetClosestContacts(ICollection<NodeContact> contacts, BinaryID nodeID, int count)
        {
            if (contacts.Count < count)
                count = contacts.Count;

            NodeContact[] closestContacts = new NodeContact[count];
            BinaryID[] closestContactDistances = new BinaryID[count];

            foreach (NodeContact contact in contacts)
            {
                BinaryID distance = nodeID ^ contact.NodeID;

                for (int i = 0; i < count; i++)
                {
                    if ((closestContactDistances[i] == null) || (distance < closestContactDistances[i]))
                    {
                        //demote existing values
                        for (int j = count - 1; j > i; j--)
                        {
                            closestContactDistances[j] = closestContactDistances[j - 1];
                            closestContacts[j] = closestContacts[j - 1];
                        }

                        //place current on top
                        closestContactDistances[i] = distance;
                        closestContacts[i] = contact;
                        break;
                    }
                }
            }

            return closestContacts;
        }

        private static void SplitBucket(KBucket bucket, NodeContact newContact)
        {
            if (bucket._contacts == null)
                return;

            if (!bucket._contacts[0].IsCurrentNode)
                throw new ArgumentException("Cannot split this k-bucket: must contain current node to split.");

            KBucket leftBucket = new KBucket(bucket, true);
            KBucket rightBucket = new KBucket(bucket, false);

            foreach (NodeContact contact in bucket._contacts)
            {
                if ((leftBucket._bucketID & contact.NodeID) == leftBucket._bucketID)
                    leftBucket._contacts[leftBucket._contactCount++] = contact;
                else
                    rightBucket._contacts[rightBucket._contactCount++] = contact;
            }

            KBucket selectedBucket;

            if ((leftBucket._bucketID & newContact.NodeID) == leftBucket._bucketID)
                selectedBucket = leftBucket;
            else
                selectedBucket = rightBucket;

            if (selectedBucket._contactCount == selectedBucket._contacts.Length)
                SplitBucket(selectedBucket, newContact);
            else
                selectedBucket._contacts[selectedBucket._contactCount++] = newContact;

            bucket._contacts = null;
            bucket._leftBucket = leftBucket;
            bucket._rightBucket = rightBucket;
        }

        private static void JoinBucket(KBucket parentBucket)
        {
            lock (parentBucket)
            {
                KBucket leftBucket = parentBucket._leftBucket;
                KBucket rightBucket = parentBucket._rightBucket;

                if ((leftBucket == null) || (rightBucket == null))
                    return;

                lock (leftBucket)
                {
                    lock (rightBucket)
                    {
                        if ((leftBucket._contactCount + rightBucket._contactCount) > DhtNode.KADEMLIA_K * 2)
                            return; //child k-buckets have more contacts then parent can accomodate

                        NodeContact[] contacts = new NodeContact[DhtNode.KADEMLIA_K * 2];
                        int contactCount = 0;

                        if ((leftBucket._contacts[0] != null) && leftBucket._contacts[0].IsCurrentNode)
                        {
                            foreach (NodeContact contact in leftBucket._contacts)
                            {
                                if (contact != null)
                                    contacts[contactCount++] = contact;
                            }

                            foreach (NodeContact contact in rightBucket._contacts)
                            {
                                if (contact != null)
                                    contacts[contactCount++] = contact;
                            }
                        }
                        else
                        {
                            foreach (NodeContact contact in rightBucket._contacts)
                            {
                                if (contact != null)
                                    contacts[contactCount++] = contact;
                            }

                            foreach (NodeContact contact in leftBucket._contacts)
                            {
                                if (contact != null)
                                    contacts[contactCount++] = contact;
                            }
                        }

                        parentBucket._contacts = contacts;
                        parentBucket._contactCount = contactCount;
                        parentBucket._leftBucket = null;
                        parentBucket._rightBucket = null;
                    }
                }
            }
        }

        #endregion

        #region private

        private List<KBucket> GetAllLeafKBuckets()
        {
            List<KBucket> allLeafKBuckets = new List<KBucket>();

            KBucket currentBucket = this;

            while (true)
            {
                NodeContact[] contacts = currentBucket._contacts;
                KBucket leftBucket = currentBucket._leftBucket;
                KBucket rightBucket = currentBucket._rightBucket;

                if ((contacts != null) || (leftBucket == null) || (rightBucket == null))
                {
                    allLeafKBuckets.Add(currentBucket);

                    while (currentBucket._parentBucket != null)
                    {
                        if (ReferenceEquals(currentBucket, currentBucket._parentBucket._leftBucket))
                        {
                            currentBucket = currentBucket._parentBucket._rightBucket;
                            break;
                        }
                        else
                        {
                            currentBucket = currentBucket._parentBucket;
                        }
                    }
                }
                else
                {
                    currentBucket = leftBucket;
                }

                if (currentBucket._parentBucket == null)
                    break;
            }

            return allLeafKBuckets;
        }

        #endregion

        #region public

        public bool AddContact(NodeContact contact)
        {
            KBucket currentBucket = this;

            while (true)
            {
                NodeContact[] contacts = currentBucket._contacts;
                KBucket leftBucket = currentBucket._leftBucket;
                KBucket rightBucket = currentBucket._rightBucket;

                lock (currentBucket)
                {
                    if ((contacts != null) || (leftBucket == null) || (rightBucket == null))
                    {
                        #region  add contact in this bucket

                        //search if contact already exists
                        for (int i = 0; i < contacts.Length; i++)
                        {
                            if (contact.Equals(contacts[i]))
                                return false; //contact already exists
                        }

                        //try add contact
                        for (int i = 0; i < contacts.Length; i++)
                        {
                            if (contacts[i] == null)
                            {
                                contacts[i] = contact;
                                currentBucket._contactCount++;
                                currentBucket._lastChanged = DateTime.UtcNow;
                                return true;
                            }
                        }

                        //k-bucket is full so contact was not added

                        //if current contact is not stale then find and replace with any existing stale contact
                        if (!contact.IsStale())
                        {
                            for (int i = 0; i < contacts.Length; i++)
                            {
                                if (contacts[i].IsStale())
                                {
                                    contacts[i] = contact;
                                    currentBucket._lastChanged = DateTime.UtcNow;
                                    return true;
                                }
                            }
                        }

                        //no stale contact in this k-bucket to replace!
                        if (contacts[0].IsCurrentNode)
                        {
                            //split current bucket and add contact!
                            SplitBucket(currentBucket, contact);
                            return true;
                        }
                        else
                        {
                            //k-bucket is full!
                            return false;
                        }
                        #endregion
                    }
                }

                if ((leftBucket._bucketID & contact.NodeID) == leftBucket._bucketID)
                    currentBucket = leftBucket;
                else
                    currentBucket = rightBucket;
            }
        }

        public bool RemoveContact(NodeContact contact)
        {
            KBucket currentBucket = this;

            while (true)
            {
                NodeContact[] contacts = currentBucket._contacts;
                KBucket leftBucket = currentBucket._leftBucket;
                KBucket rightBucket = currentBucket._rightBucket;

                lock (currentBucket)
                {
                    if ((contacts != null) || (leftBucket == null) || (rightBucket == null))
                    {
                        #region remove contact from this bucket

                        if (currentBucket._contactCount <= DhtNode.KADEMLIA_K)
                            return false; //k-bucket is not full and replacement cache is empty

                        bool contactRemoved = false;

                        for (int i = 0; i < contacts.Length; i++)
                        {
                            if (contact.Equals(contacts[i]))
                            {
                                if (contacts[i].IsStale())
                                {
                                    //remove stale contact
                                    contacts[i] = null;
                                    currentBucket._contactCount--;
                                    currentBucket._lastChanged = DateTime.UtcNow;
                                    contactRemoved = true;
                                }

                                break;
                            }
                        }

                        if (!contactRemoved)
                            return false; //contact was not found or was not stale

                        //check parent bucket contact count and join the parent bucket
                        if (currentBucket._parentBucket != null)
                            JoinBucket(currentBucket._parentBucket);

                        return true;

                        #endregion
                    }
                }

                if ((leftBucket._bucketID & contact.NodeID) == leftBucket._bucketID)
                    currentBucket = leftBucket;
                else
                    currentBucket = rightBucket;
            }
        }

        public NodeContact[] GetKClosestContacts(BinaryID nodeID)
        {
            KBucket currentBucket = this;

            while (true)
            {
                NodeContact[] contacts = currentBucket._contacts;
                KBucket leftBucket = currentBucket._leftBucket;
                KBucket rightBucket = currentBucket._rightBucket;

                if ((contacts != null) || (leftBucket == null) || (rightBucket == null))
                {
                    #region find closest contacts from this bucket

                    KBucket closestBucket = currentBucket;
                    NodeContact[] closestContacts = null;

                    if (closestBucket._contactCount >= DhtNode.KADEMLIA_K)
                    {
                        closestContacts = closestBucket.GetAllContacts(false);

                        if (closestContacts.Length > DhtNode.KADEMLIA_K)
                            return GetClosestContacts(closestContacts, nodeID, DhtNode.KADEMLIA_K);

                        if (closestContacts.Length == DhtNode.KADEMLIA_K)
                            return closestContacts;

                        if (closestBucket._parentBucket == null)
                            return closestContacts;
                    }

                    while (closestBucket._parentBucket != null)
                    {
                        KBucket parentBucket = closestBucket._parentBucket;

                        closestContacts = parentBucket.GetAllContacts(false);

                        if (closestContacts.Length > DhtNode.KADEMLIA_K)
                            return GetClosestContacts(closestContacts, nodeID, DhtNode.KADEMLIA_K);

                        if (closestContacts.Length == DhtNode.KADEMLIA_K)
                            return closestContacts;

                        closestBucket = parentBucket;
                    }

                    if (closestContacts == null)
                        closestContacts = closestBucket.GetAllContacts(false);

                    return closestContacts;

                    #endregion
                }

                if ((leftBucket._bucketID & nodeID) == leftBucket._bucketID)
                    currentBucket = leftBucket;
                else
                    currentBucket = rightBucket;
            }
        }

        public NodeContact[] GetAllContacts(bool includeStaleContacts)
        {
            List<NodeContact> allContacts = new List<NodeContact>();
            List<KBucket> allLeafKBuckets = GetAllLeafKBuckets();

            foreach (KBucket kBucket in allLeafKBuckets)
            {
                NodeContact[] contacts = kBucket._contacts;

                if (contacts != null)
                {
                    foreach (NodeContact contact in contacts)
                    {
                        if (contact != null)
                        {
                            if ((includeStaleContacts || !contact.IsStale()) && !contact.IsCurrentNode)
                                allContacts.Add(contact);
                        }
                    }
                }
            }

            return allContacts.ToArray();
        }

        public NodeContact FindContact(BinaryID nodeID)
        {
            KBucket currentBucket = this;

            while (true)
            {
                NodeContact[] contacts = currentBucket._contacts;
                KBucket leftBucket = currentBucket._leftBucket;
                KBucket rightBucket = currentBucket._rightBucket;

                if ((contacts != null) || (leftBucket == null) || (rightBucket == null))
                {
                    foreach (NodeContact contact in contacts)
                    {
                        if ((contact != null) && contact.NodeID.Equals(nodeID))
                            return contact;
                    }

                    return null; //contact not found
                }

                if ((leftBucket._bucketID & nodeID) == leftBucket._bucketID)
                    currentBucket = leftBucket;
                else
                    currentBucket = rightBucket;
            }
        }

        public int GetTotalContacts()
        {
            int count = 0;
            List<KBucket> allLeafKBuckets = GetAllLeafKBuckets();

            foreach (KBucket kBucket in allLeafKBuckets)
            {
                if (kBucket._contacts != null)
                    count += kBucket._contactCount;
            }

            return count;
        }

        public void CheckContactHealth(DhtNode dhtNode)
        {
            List<KBucket> allLeafKBuckets = GetAllLeafKBuckets();

            foreach (KBucket kBucket in allLeafKBuckets)
            {
                NodeContact[] contacts = kBucket._contacts;

                if (contacts != null)
                {
                    foreach (NodeContact contact in contacts)
                    {
                        if ((contact != null) && contact.IsStale())
                        {
                            ThreadPool.QueueUserWorkItem(delegate (object state)
                            {
                                if (!dhtNode.Ping(contact))
                                {
                                    //remove stale node contact
                                    kBucket.RemoveContact(contact);
                                }
                            });
                        }
                    }
                }
            }
        }

        public void RefreshBucket(DhtNode dhtNode)
        {
            List<KBucket> allLeafKBuckets = GetAllLeafKBuckets();

            foreach (KBucket kBucket in allLeafKBuckets)
            {
                if (kBucket._contacts != null)
                {
                    if ((DateTime.UtcNow - kBucket._lastChanged).TotalSeconds > BUCKET_STALE_TIMEOUT_SECONDS)
                    {
                        ThreadPool.QueueUserWorkItem(delegate (object state)
                        {
                            //get random node ID in the bucket range
                            BinaryID randomNodeID = BinaryID.GenerateRandomID160();

                            if (kBucket._bucketID != null)
                                randomNodeID = (randomNodeID >> kBucket._bucketDepth) | kBucket._bucketID;

                            //find closest contacts for current node id
                            NodeContact[] initialContacts = kBucket.GetKClosestContacts(randomNodeID);

                            if (initialContacts.Length > 0)
                                dhtNode.QueryFindNode(initialContacts, randomNodeID);
                        });
                    }
                }
            }
        }

        #endregion
    }
}
