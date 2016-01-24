/*
Technitium Bit Chat
Copyright (C) 2016  Shreyas Zare (shreyas@technitium.com)

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
using System.IO;
using TechnitiumLibrary.IO;

namespace BitChatClient
{
    public class MessageItem
    {
        #region variables

        int _messageNumber;
        string _sender;
        string _message;

        #endregion

        #region constructor

        public MessageItem(string sender, string message)
        {
            _messageNumber = -1;
            _sender = sender;
            _message = message;
        }

        public MessageItem(MessageStore store, int messageNumber)
        {
            _messageNumber = messageNumber;
            byte[] messageData = store.ReadMessage(messageNumber);

            using (MemoryStream mS = new MemoryStream(messageData))
            {
                BincodingDecoder decoder = new BincodingDecoder(mS);

                _sender = decoder.DecodeNext().GetStringValue();
                _message = decoder.DecodeNext().GetStringValue();
            }
        }

        #endregion

        #region static

        public static MessagePage GetPage(MessageStore store, int pageNumber, int itemsPerPage)
        {
            int totalMessages = store.TotalMessages();
            int pageCount = (int)Math.Ceiling(totalMessages / (double)itemsPerPage);

            if (pageNumber > pageCount)
                return new MessagePage(0, pageCount, new MessageItem[] { });

            int firstItemNumber = (pageNumber - 1) * itemsPerPage;
            int itemCount = totalMessages - firstItemNumber;

            if (itemCount > itemsPerPage)
                itemCount = itemsPerPage;

            MessageItem[] items = new MessageItem[itemCount];

            int lastItemNumber = firstItemNumber + itemCount;

            for (int i = firstItemNumber, x = 0; i < lastItemNumber; i++, x++)
                items[x] = new MessageItem(store, i);

            return new MessagePage(pageNumber, pageCount, items);
        }

        public static int GetPageCount(MessageStore store, int itemsPerPage)
        {
            return (int)Math.Ceiling(store.TotalMessages() / (double)itemsPerPage);
        }

        #endregion

        #region public

        public void WriteTo(MessageStore store)
        {
            if (_messageNumber != -1)
                return;

            using (MemoryStream mS = new MemoryStream(128))
            {
                BincodingEncoder encoder = new BincodingEncoder(mS);

                encoder.Encode(_sender);
                encoder.Encode(_message);

                byte[] messageData = mS.ToArray();
                _messageNumber = store.WriteMessage(messageData, 0, messageData.Length);
            }
        }

        #endregion

        #region properties

        public int MessageNumber
        { get { return _messageNumber; } }

        public string Sender
        { get { return _sender; } }

        public string Message
        { get { return _message; } }

        #endregion
    }

    public class MessagePage
    {
        #region variables

        int _pageNumber;
        int _pageCount;
        MessageItem[] _items;

        #endregion

        #region constructor

        public MessagePage(int pageNumber, int pageCount, MessageItem[] items)
        {
            _pageNumber = pageNumber;
            _pageCount = pageCount;
            _items = items;
        }

        #endregion

        #region properties

        public int PageNumber
        { get { return _pageNumber; } }

        public int PageCount
        { get { return _pageCount; } }

        public MessageItem[] Items
        { get { return _items; } }

        #endregion
    }
}
