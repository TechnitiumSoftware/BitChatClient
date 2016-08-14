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

using System.IO;
using TechnitiumLibrary.IO;

namespace BitChatClient
{
    public enum MessageRecipientStatus : byte
    {
        Undelivered = 0,
        Delivered = 1
    }

    public class MessageRecipient : IWriteStream
    {
        #region variables

        readonly string _name;

        MessageRecipientStatus _status = MessageRecipientStatus.Undelivered;

        #endregion

        #region constructor

        public MessageRecipient(string name)
        {
            _name = name;
        }

        public MessageRecipient(Stream s)
        {
            BincodingDecoder decoder = new BincodingDecoder(s);

            switch (decoder.DecodeNext().GetByteValue()) //version
            {
                case 1:
                    _name = decoder.DecodeNext().GetStringValue();
                    _status = (MessageRecipientStatus)decoder.DecodeNext().GetByteValue();
                    break;

                default:
                    throw new InvalidDataException("Cannot decode data format: version not supported.");
            }

        }

        #endregion

        #region public

        public void SetDeliveredStatus()
        {
            _status = MessageRecipientStatus.Delivered;
        }

        public void WriteTo(Stream s)
        {
            BincodingEncoder encoder = new BincodingEncoder(s);

            encoder.Encode((byte)1); //version
            encoder.Encode(_name);
            encoder.Encode((byte)_status);
        }

        public byte[] ToArray()
        {
            using (MemoryStream mS = new MemoryStream())
            {
                WriteTo(mS);
                return mS.ToArray();
            }
        }

        public Stream ToStream()
        {
            MemoryStream mS = new MemoryStream();
            WriteTo(mS);
            mS.Position = 0;
            return mS;
        }

        #endregion

        #region properties

        public string Name
        { get { return _name; } }

        public MessageRecipientStatus Status
        { get { return _status; } }

        #endregion
    }
}
