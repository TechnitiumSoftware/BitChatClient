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

namespace BitChatClient.Network.KademliaDHT
{
    class KBucket
    {
        #region variables

        int _k;
        BinaryID _bucketPrefixID;
        int _bucketDepth;
        bool _bucketContainsCurrentNode;
        Dictionary<BinaryID, NodeContact> _contacts;
        Dictionary<BinaryID, NodeContact> _replacementContacts;
        KBucket _parentBucket = null;
        KBucket _leftBucket = null;
        KBucket _rightBucket = null;
        int _totalBucketContacts = 0;
        DateTime _lastChanged;

        #endregion

        #region constructor

        public KBucket(int k, NodeContact currentNode)
        {
            _k = k;
            _bucketDepth = 0;

            _contacts = new Dictionary<BinaryID, NodeContact>();
            _replacementContacts = new Dictionary<BinaryID, NodeContact>();

            _contacts.Add(currentNode.NodeID, currentNode);
            _bucketContainsCurrentNode = true;
            _totalBucketContacts++;
            _lastChanged = DateTime.UtcNow;
        }

        private KBucket(KBucket parentBucket, bool left, int k)
        {
            _k = k;
            _parentBucket = parentBucket;
            _bucketDepth = parentBucket._bucketDepth + 1;

            if (parentBucket._bucketPrefixID == null)
            {
                _bucketPrefixID = new BinaryID(new byte[20]);

                if (left)
                    _bucketPrefixID.ID[0] = 0x80;
            }
            else
            {
                if (left)
                {
                    _bucketPrefixID = new BinaryID(new byte[20]);
                    _bucketPrefixID.ID[0] |= 0x80;

                    _bucketPrefixID = parentBucket._bucketPrefixID | (_bucketPrefixID >> _bucketDepth);
                }
                else
                {
                    _bucketPrefixID = parentBucket._bucketPrefixID;
                }
            }
        }

        #endregion

        #region public

        public bool AddContact(NodeContact contact)
        {
            if (_contacts == null)
            {
                bool returnValue;

                if ((_leftBucket._bucketPrefixID & contact.NodeID) == _leftBucket._bucketPrefixID)
                    returnValue = _leftBucket.AddContact(contact);
                else
                    returnValue = _rightBucket.AddContact(contact);

                if (returnValue)
                    _totalBucketContacts++;

                return returnValue;
            }
            else
            {
                if (_contacts.Count < _k)
                {
                    _contacts.Add(contact.NodeID, contact);
                    _totalBucketContacts++;
                    _lastChanged = DateTime.UtcNow;

                    if (contact.IsCurrentNode)
                        _bucketContainsCurrentNode = true;

                    return true;
                }
                else
                {
                    if (_bucketContainsCurrentNode)
                    {
                        _contacts.Add(contact.NodeID, contact);
                        _totalBucketContacts++;
                        _lastChanged = DateTime.UtcNow;

                        //split current bucket
                        _leftBucket = new KBucket(this, true, _k);
                        _rightBucket = new KBucket(this, false, _k);

                        foreach (KeyValuePair<BinaryID, NodeContact> nodeItem in _contacts)
                        {
                            if ((_leftBucket._bucketPrefixID & nodeItem.Key) == _leftBucket._bucketPrefixID)
                                _leftBucket.AddContact(nodeItem.Value);
                            else
                                _rightBucket.AddContact(nodeItem.Value);
                        }

                        //demote current object as bucket
                        _contacts = null;
                        _bucketContainsCurrentNode = false;
                        return true;
                    }
                    else
                    {
                        //never split buckets that arent on the same side of the tree as the current node
                        //keep the node contact in replacement contacts list

                        _replacementContacts.Add(contact.NodeID, contact);

                        return false;
                    }
                }
            }
        }

        public bool RemoveContact(NodeContact contact)
        {
            bool returnValue;

            if (_contacts == null)
            {
                if ((_leftBucket._bucketPrefixID & contact.NodeID) == _leftBucket._bucketPrefixID)
                    returnValue = _leftBucket.RemoveContact(contact);
                else
                    returnValue = _rightBucket.RemoveContact(contact);

                if (returnValue)
                    _totalBucketContacts--;

                //check child buckets total for k

                if (_totalBucketContacts <= _k)
                {
                    //combine buckets
                    _contacts = new Dictionary<BinaryID, NodeContact>(_totalBucketContacts);

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
            }
            else
            {
                returnValue = _contacts.Remove(contact.NodeID);

                if (returnValue)
                    _totalBucketContacts--;
            }

            return returnValue;
        }

        public KBucket FindClosestBucket(BinaryID nodeID)
        {
            if (_contacts == null)
            {
                if ((_leftBucket._bucketPrefixID & nodeID) == _leftBucket._bucketPrefixID)
                    return _leftBucket.FindClosestBucket(nodeID);
                else
                    return _rightBucket.FindClosestBucket(nodeID);
            }
            else
            {
                _lastChanged = DateTime.UtcNow;
                return this;
            }
        }

        public NodeContact[] GetContacts()
        {
            NodeContact[] contacts;

            if (_contacts == null)
            {
                NodeContact[] leftContacts = _leftBucket.GetContacts();
                NodeContact[] rightContacts = _leftBucket.GetContacts();

                contacts = new NodeContact[leftContacts.Length + rightContacts.Length];

                Array.Copy(leftContacts, contacts, leftContacts.Length);
                Array.Copy(rightContacts, 0, contacts, leftContacts.Length, rightContacts.Length);
            }
            else
            {
                contacts = new NodeContact[_contacts.Count];
                _contacts.Values.CopyTo(contacts, 0);
            }

            return contacts;
        }

        #endregion
    }
}
