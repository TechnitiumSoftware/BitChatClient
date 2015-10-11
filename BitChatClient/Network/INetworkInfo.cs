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
using System.Net;

namespace BitChatClient.Network
{
    public interface INetworkInfo
    {
        BinaryID LocalPeerID { get; }

        int LocalPort { get; }

        BinaryID DhtNodeID { get; }

        int DhtLocalPort { get; }

        int DhtTotalNodes { get; }

        InternetConnectivityStatus InternetStatus { get; }

        UPnPDeviceStatus UPnPStatus { get; }

        IPAddress UPnPExternalIP { get; }

        IPEndPoint ExternalEP { get; }

        IPEndPoint[] ProxyNodes { get; }
    }
}
