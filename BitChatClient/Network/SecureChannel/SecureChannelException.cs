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
using System.IO;

namespace BitChatClient.Network.SecureChannel
{
    enum SecureChannelErrorCode : byte
    {
        NoError = 0,
        RemoteError = 1,
        ProtocolVersionNotSupported = 2,
        InvalidChallengeResponse = 3,
        SecurityManagerDeclinedAccess = 4,
        NoMatchingCryptoAvailable = 5,
        InvalidRemoteCertificate = 6
    }

    [System.Serializable()]
    class SecureChannelException : IOException
    {
        #region variable

        SecureChannelErrorCode _errorCode;

        #endregion

        #region constructor

        public SecureChannelException(SecureChannelErrorCode errorCode)
        {
            _errorCode = errorCode;
        }

        public SecureChannelException(SecureChannelErrorCode errorCode, string message)
            : base(message)
        {
            _errorCode = errorCode;
        }

        public SecureChannelException(SecureChannelErrorCode errorCode, string message, Exception innerException)
            : base(message, innerException)
        {
            _errorCode = errorCode;
        }

        public SecureChannelException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        { }

        #endregion

        #region property

        public SecureChannelErrorCode ErrorCode
        { get { return _errorCode; } }

        #endregion
    }
}
