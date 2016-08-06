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
    public enum MessageType : byte
    {
        Info = 1,
        TextMessage = 2
    }

    public class MessageItem
    {
        #region variables

        int _messageNumber;

        MessageType _type;
        DateTime _messageDate;
        string _sender;
        string _message;

        #endregion

        #region constructor

        public MessageItem(string sender, string message)
        {
            _messageNumber = -1;

            _type = MessageType.TextMessage;
            _messageDate = DateTime.UtcNow;
            _sender = sender;
            _message = message;
        }

        public MessageItem(string info)
        {
            _messageNumber = -1;

            _type = MessageType.Info;
            _messageDate = DateTime.UtcNow;
            _message = info;
        }

        public MessageItem(string info, DateTime infoDate)
        {
            _messageNumber = -1;

            _type = MessageType.Info;
            _messageDate = infoDate;
            _message = info;
        }

        public MessageItem(MessageStore store, int messageNumber)
        {
            _messageNumber = messageNumber;
            byte[] messageData = store.ReadMessage(messageNumber);

            using (MemoryStream mS = new MemoryStream(messageData))
            {
                BincodingDecoder decoder = new BincodingDecoder(mS);

                _type = (MessageType)decoder.DecodeNext().GetByteValue();
                _messageDate = new DateTime(1970, 1, 1).AddSeconds(decoder.DecodeNext().GetULongValue());

                switch (_type)
                {
                    case MessageType.Info:
                        _message = decoder.DecodeNext().GetStringValue();
                        break;

                    case MessageType.TextMessage:
                        _sender = decoder.DecodeNext().GetStringValue();
                        _message = decoder.DecodeNext().GetStringValue();
                        break;
                }
            }
        }

        #endregion

        #region static

        public static MessageItem[] GetLastMessageItems(MessageStore store, int index, int count)
        {
            int totalMessages = store.GetMessageCount();

            if (index > totalMessages)
                index = totalMessages;
            else if (index < 1)
                return new MessageItem[] { };

            int firstMessageNumber = index - count;

            if (firstMessageNumber < 0)
                firstMessageNumber = 0;

            int itemCount = index - firstMessageNumber;

            MessageItem[] items = new MessageItem[itemCount];

            for (int i = firstMessageNumber, x = 0; i < index; i++, x++)
                items[x] = new MessageItem(store, i);

            return items;
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

                encoder.Encode((byte)_type);
                encoder.Encode(Convert.ToUInt64((_messageDate - new DateTime(1970, 1, 1)).TotalSeconds));

                switch (_type)
                {
                    case MessageType.Info:
                        encoder.Encode(_message);
                        break;

                    case MessageType.TextMessage:
                        encoder.Encode(_sender);
                        encoder.Encode(_message);
                        break;
                }

                byte[] messageData = mS.ToArray();
                _messageNumber = store.WriteMessage(messageData, 0, messageData.Length);
            }
        }

        #endregion

        #region properties

        public int MessageNumber
        { get { return _messageNumber; } }

        public MessageType Type
        { get { return _type; } }

        public DateTime MessageDate
        { get { return _messageDate; } }

        public string Sender
        { get { return _sender; } }

        public string Message
        { get { return _message; } }

        #endregion
    }
}
