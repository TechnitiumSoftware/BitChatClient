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
using System.Collections.Generic;
using System.IO;
using System.Text;
using TechnitiumLibrary.IO;

namespace BitChatCore
{
    public enum MessageType : byte
    {
        None = 0,
        Info = 1,
        TextMessage = 2,
        InvitationMessage = 3
    }

    public enum MessageDeliveryStatus
    {
        None = 0,
        Undelivered = 1,
        PartiallyDelivered = 2,
        Delivered = 3
    }

    public class MessageItem
    {
        #region variables

        readonly static DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        int _messageNumber;

        readonly MessageType _type;
        readonly DateTime _messageDate;
        readonly string _sender;
        readonly string _message;
        readonly MessageRecipient[] _recipients;

        #endregion

        #region constructor

        public MessageItem(MessageType type, DateTime messageDate, string sender, string message)
            : this(type, messageDate, sender, message, new MessageRecipient[] { })
        { }

        public MessageItem(MessageType type, DateTime messageDate, string sender, string message, MessageRecipient[] recipients)
        {
            _messageNumber = -1;

            _type = type;
            _messageDate = messageDate;
            _sender = sender;
            _message = message;
            _recipients = recipients;
        }

        public MessageItem(string info)
        {
            _messageNumber = -1;

            _type = MessageType.Info;
            _messageDate = DateTime.UtcNow;
            _message = info;
        }

        public MessageItem(DateTime infoDate)
        {
            _messageNumber = -1;

            _type = MessageType.Info;
            _messageDate = infoDate;
            _message = "";
        }

        public MessageItem(MessageStore store, int messageNumber)
        {
            _messageNumber = messageNumber;
            byte[] messageData = store.ReadMessage(messageNumber);

            using (MemoryStream mS = new MemoryStream(messageData))
            {
                if (Encoding.ASCII.GetString(messageData, 0, 2) == "MI")
                {
                    //new format
                    BincodingDecoder decoder = new BincodingDecoder(mS, "MI");

                    switch (decoder.Version)
                    {
                        case 1:
                            _type = (MessageType)decoder.DecodeNext().GetByteValue();
                            _messageDate = decoder.DecodeNext().GetDateTimeValue();

                            switch (_type)
                            {
                                case MessageType.Info:
                                    _message = decoder.DecodeNext().GetStringValue();
                                    break;

                                case MessageType.TextMessage:
                                case MessageType.InvitationMessage:
                                    _sender = decoder.DecodeNext().GetStringValue();
                                    _message = decoder.DecodeNext().GetStringValue();

                                    {
                                        List<Bincoding> rcptDataList = decoder.DecodeNext().GetList();

                                        _recipients = new MessageRecipient[rcptDataList.Count];
                                        int i = 0;

                                        foreach (Bincoding data in rcptDataList)
                                            _recipients[i++] = new MessageRecipient(data.GetValueStream());
                                    }
                                    break;
                            }
                            break;

                        default:
                            throw new InvalidDataException("Cannot decode data format: version not supported.");
                    }
                }
                else
                {
                    //old format support
                    BincodingDecoder decoder = new BincodingDecoder(mS);

                    _type = (MessageType)decoder.DecodeNext().GetByteValue();
                    _messageDate = _epoch.AddSeconds(decoder.DecodeNext().GetLongValue());

                    switch (_type)
                    {
                        case MessageType.Info:
                            _message = decoder.DecodeNext().GetStringValue();
                            break;

                        case MessageType.TextMessage:
                            _sender = decoder.DecodeNext().GetStringValue();
                            _message = decoder.DecodeNext().GetStringValue();
                            _recipients = new MessageRecipient[] { };
                            break;
                    }
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

        public MessageDeliveryStatus GetDeliveryStatus()
        {
            if (_type != MessageType.TextMessage)
                return MessageDeliveryStatus.None;

            bool containsDelivered = false;
            bool containsUndelivered = false;

            foreach (MessageRecipient rcpt in _recipients)
            {
                if (rcpt.Status == MessageRecipientStatus.Delivered)
                    containsDelivered = true;
                else
                    containsUndelivered = true;
            }

            if (containsDelivered && containsUndelivered)
                return MessageDeliveryStatus.PartiallyDelivered;
            else if (containsDelivered)
                return MessageDeliveryStatus.Delivered;
            else if (containsUndelivered)
                return MessageDeliveryStatus.Undelivered;
            else
                return MessageDeliveryStatus.Delivered;
        }

        public void WriteTo(MessageStore store)
        {
            using (MemoryStream mS = new MemoryStream(128))
            {
                BincodingEncoder encoder = new BincodingEncoder(mS, "MI", 1);

                encoder.Encode((byte)_type);
                encoder.Encode(_messageDate);

                switch (_type)
                {
                    case MessageType.Info:
                        encoder.Encode(_message);
                        break;

                    case MessageType.TextMessage:
                    case MessageType.InvitationMessage:
                        encoder.Encode(_sender);
                        encoder.Encode(_message);
                        encoder.Encode(_recipients);
                        break;

                    default:
                        throw new NotSupportedException("MessageType not supported: " + _type);
                }

                byte[] messageData = mS.ToArray();

                if (_messageNumber == -1)
                    _messageNumber = store.WriteMessage(messageData, 0, messageData.Length);
                else
                    store.UpdateMessage(_messageNumber, messageData, 0, messageData.Length);
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

        public MessageRecipient[] Recipients
        { get { return _recipients; } }

        #endregion
    }
}
