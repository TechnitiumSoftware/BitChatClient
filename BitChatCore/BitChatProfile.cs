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

using BitChatCore.FileSharing;
using BitChatCore.Network;
using BitChatCore.Network.SecureChannel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.Proxy;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatCore
{
    public class BitChatProfile : CryptoContainer, ISecureChannelSecurityManager
    {
        #region event

        internal event EventHandler ProxyUpdated;
        internal event EventHandler ProfileImageChanged;

        #endregion

        #region variables

        public readonly static Uri[] DefaultTrackerURIs
            = new Uri[]
            {
                new Uri("udp://tracker1.bitchat.im:6969"),
                new Uri("udp://tracker2.bitchat.im:6969"),
                new Uri("udp://tracker3.bitchat.im:6969"),
                new Uri("udp://tracker4.bitchat.im:1337"),
                new Uri("http://9.rarbg.com:2710/announce"),
                new Uri("http://opensharing.org:2710/announce"),
                new Uri("http://bt.careland.com.cn:6969/announce"),
                new Uri("http://tracker.ex.ua/announce"),
                new Uri("udp://ipv6.tracker.harry.lu:80/announce"),
                new Uri("http://ipv6.tracker.harry.lu:80/announce")
            };

        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        readonly bool _isPortableApp;
        readonly string _profileFolder;

        readonly string _portableDownloadFolder;

        CertificateStore _localCertStore;

        long _profileImageDateModified;
        byte[] _profileImage = null;

        int _localPort;
        string _downloadFolder;
        Uri[] _trackerURIs;

        BitChatInfo[] _bitChatInfoList = new BitChatInfo[] { };
        bool _checkCertificateRevocationList = true;
        IPEndPoint[] _bootstrapDhtNodes = new IPEndPoint[] { };
        bool _enableUPnP = true;
        bool _allowInboundInvitations = true;
        bool _allowOnlyLocalInboundInvitations = false;

        string _proxyAddress = "127.0.0.1";
        int _proxyPort = 0;
        NetworkCredential _proxyCredentials;
        NetProxy _proxy;

        byte[] _clientData;

        #endregion

        #region constructor

        public BitChatProfile(int localPort, string downloadFolder, Uri[] trackerURIs, bool isPortableApp, string profileFolder)
        {
            _localPort = localPort;
            _downloadFolder = downloadFolder;
            _trackerURIs = trackerURIs;

            _isPortableApp = isPortableApp;
            _profileFolder = profileFolder;

            if (_isPortableApp)
            {
                _portableDownloadFolder = Path.Combine(_profileFolder, "Downloads");

                if (!Directory.Exists(_portableDownloadFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(_portableDownloadFolder);
                    }
                    catch
                    { }
                }
            }
        }

        public BitChatProfile(Stream s, string password, bool isPortableApp, string profileFolder)
            : base(s, password)
        {
            _isPortableApp = isPortableApp;
            _profileFolder = profileFolder;

            if (_isPortableApp)
            {
                _portableDownloadFolder = Path.Combine(_profileFolder, "Downloads");

                if (!Directory.Exists(_portableDownloadFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(_portableDownloadFolder);
                    }
                    catch
                    { }
                }
            }
        }

        #endregion

        #region registration

        private static string GetUserAgent()
        {
            OperatingSystem OS = Environment.OSVersion;

            string operatingSystem;

            switch (OS.Platform)
            {
                case PlatformID.Win32NT:
                    operatingSystem = "Windows NT";
                    break;

                default:
                    operatingSystem = OS.Platform.ToString();
                    break;
            }

            operatingSystem += " " + OS.Version.Major + "." + OS.Version.Minor;

            return "Mozilla/5.0 (" + operatingSystem + ")";
        }

        public void Register(Uri apiUri, CertificateStore localCertStore)
        {
            _localCertStore = localCertStore;

            //check if email address domain exists
            {
                try
                {
                    DnsClient dns = new DnsClient();
                    dns.Proxy = _proxy;

                    dns.ResolveMX(_localCertStore.Certificate.IssuedTo.EmailAddress);
                }
                catch (NameErrorDnsClientException)
                {
                    throw new NameErrorDnsClientException("The domain of your email address '" + _localCertStore.Certificate.IssuedTo.EmailAddress.Host + "' does not exists. Please check if you have entered correct email address.");
                }
                catch
                {
                    try
                    {
                        DnsDatagram response = DnsClient.ResolveViaRootNameServers(_localCertStore.Certificate.IssuedTo.EmailAddress.Host, DnsResourceRecordType.MX, _proxy);
                        if (response.Header.RCODE == DnsResponseCode.NameError)
                            throw new NameErrorDnsClientException("The domain of your email address '" + _localCertStore.Certificate.IssuedTo.EmailAddress.Host + "' does not exists. Please check if you have entered correct email address.");
                    }
                    catch (NameErrorDnsClientException)
                    {
                        throw;
                    }
                    catch
                    { }
                }
            }

            //verify self signed cert
            _localCertStore.Certificate.Verify(new Certificate[] { _localCertStore.Certificate });

            using (WebClientEx client = new WebClientEx())
            {
                client.Proxy = _proxy;
                client.UserAgent = GetUserAgent();

                using (Stream s = client.OpenWriteEx(apiUri.AbsoluteUri + "?cmd=reg"))
                {
                    _localCertStore.Certificate.WriteTo(s);
                }

                using (BinaryReader bR = new BinaryReader(client.GetResponseStream()))
                {
                    int errorCode = bR.ReadInt32();
                    if (errorCode != 0)
                    {
                        string message = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));
                        string remoteStackTrace = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));

                        throw new Exception(message);
                    }
                }
            }
        }

        public void DownloadSignedCertificate(Uri apiUri)
        {
            using (WebClientEx client = new WebClientEx())
            {
                client.Proxy = _proxy;
                client.UserAgent = GetUserAgent();

                using (BinaryReader bR = new BinaryReader(client.OpenRead(apiUri.AbsoluteUri + "?cmd=dlc&email=" + _localCertStore.Certificate.IssuedTo.EmailAddress.Address)))
                {
                    int errorCode = bR.ReadInt32();
                    if (errorCode != 0)
                    {
                        string message = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));
                        string remoteStackTrace = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadInt32()));

                        throw new BitChatException(message);
                    }

                    Certificate cert = new Certificate(bR.BaseStream);

                    if (!cert.IssuedTo.EmailAddress.Equals(_localCertStore.Certificate.IssuedTo.EmailAddress) || (cert.PublicKeyEncryptionAlgorithm != _localCertStore.PrivateKey.Algorithm) || (cert.PublicKeyXML != _localCertStore.PrivateKey.GetPublicKey()))
                        throw new BitChatException("Invalid signed certificate received. Please try again.");

                    _localCertStore = new CertificateStore(cert, _localCertStore.PrivateKey);
                }
            }
        }

        #endregion

        #region private

        protected override void ReadPlainTextFrom(Stream s)
        {
            BincodingDecoder decoder = new BincodingDecoder(s, "BP");

            if (decoder.Version != 7)
                throw new BitChatException("BitChatProfile data version not supported.");

            NetProxyType proxyType = NetProxyType.None;
            string proxyAddress = "127.0.0.1";
            int proxyPort = 0;
            string username = null;
            string password = "";

            while (true)
            {
                Bincoding value = decoder.DecodeNext();

                if (value.Type == BincodingType.NULL)
                    break;

                KeyValuePair<string, Bincoding> item = value.GetKeyValuePair();

                switch (item.Key)
                {
                    case "local_port":
                        _localPort = item.Value.GetIntegerValue();
                        break;

                    case "check_cert_revocation":
                        _checkCertificateRevocationList = item.Value.GetBooleanValue();
                        break;

                    case "enable_upnp":
                        _enableUPnP = item.Value.GetBooleanValue();
                        break;

                    case "allow_inbound_invitations":
                        _allowInboundInvitations = item.Value.GetBooleanValue();
                        break;

                    case "allow_only_local_inbound_invitations":
                        _allowOnlyLocalInboundInvitations = item.Value.GetBooleanValue();
                        break;

                    case "download_folder":
                        _downloadFolder = item.Value.GetStringValue();
                        break;

                    case "local_cert_store":
                        _localCertStore = new CertificateStore(item.Value.GetValueStream());
                        break;

                    case "profile_image_date_modified":
                        _profileImageDateModified = item.Value.GetLongValue();
                        break;

                    case "profile_image":
                    case "profile_image_large":
                        _profileImage = item.Value.Value;
                        break;

                    case "tracker_list":
                        {
                            List<Bincoding> trackerList = item.Value.GetList();

                            _trackerURIs = new Uri[trackerList.Count];
                            int i = 0;

                            foreach (Bincoding trackerItem in trackerList)
                                _trackerURIs[i++] = new Uri(trackerItem.GetStringValue());
                        }
                        break;

                    case "bitchat_info":
                        {
                            List<Bincoding> bitChatInfoList = item.Value.GetList();

                            _bitChatInfoList = new BitChatInfo[bitChatInfoList.Count];
                            int i = 0;

                            foreach (Bincoding infoItem in bitChatInfoList)
                                _bitChatInfoList[i++] = new BitChatInfo(infoItem.GetValueStream());
                        }
                        break;

                    case "dht_nodes":
                        {
                            try
                            {
                                List<Bincoding> dhtNodeList = item.Value.GetList();

                                _bootstrapDhtNodes = new IPEndPoint[dhtNodeList.Count];
                                int i = 0;

                                foreach (Bincoding dhtItem in dhtNodeList)
                                    _bootstrapDhtNodes[i++] = IPEndPointParser.Parse(dhtItem.GetValueStream());
                            }
                            catch (NotSupportedException)
                            {
                                _bootstrapDhtNodes = new IPEndPoint[] { };
                            }
                        }
                        break;

                    case "proxy_type":
                        proxyType = (NetProxyType)item.Value.GetByteValue();
                        break;

                    case "proxy_address":
                        proxyAddress = item.Value.GetStringValue();
                        break;

                    case "proxy_port":
                        proxyPort = item.Value.GetIntegerValue();
                        break;

                    case "proxy_user":
                        username = item.Value.GetStringValue();
                        break;

                    case "proxy_pass":
                        password = item.Value.GetStringValue();
                        break;

                    case "client_data":
                        if (item.Value.Type == BincodingType.BINARY)
                            _clientData = item.Value.Value;

                        break;
                }
            }

            if (string.IsNullOrEmpty(_downloadFolder))
            {
                _downloadFolder = Path.Combine(_profileFolder, "Downloads");

                if (!Directory.Exists(_downloadFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(_downloadFolder);
                    }
                    catch
                    { }
                }
            }

            //apply proxy settings
            NetworkCredential proxyCredentials = null;

            if (username != null)
                proxyCredentials = new NetworkCredential(username, password);

            ConfigureProxy(proxyType, proxyAddress, proxyPort, proxyCredentials);
        }

        protected override void WritePlainTextTo(Stream s)
        {
            BincodingEncoder encoder = new BincodingEncoder(s, "BP", 7);

            //main settings

            //bitchat local port
            encoder.Encode("local_port", _localPort);

            //check CertificateRevocationList
            encoder.Encode("check_cert_revocation", _checkCertificateRevocationList);

            //upnp enabled
            encoder.Encode("enable_upnp", _enableUPnP);

            //enable invitation
            encoder.Encode("allow_inbound_invitations", _allowInboundInvitations);
            encoder.Encode("allow_only_local_inbound_invitations", _allowOnlyLocalInboundInvitations);

            //download folder
            if (_downloadFolder != null)
                encoder.Encode("download_folder", _downloadFolder);

            //local cert store
            if (_localCertStore != null)
                encoder.Encode("local_cert_store", _localCertStore);

            //profile image
            encoder.Encode("profile_image_date_modified", _profileImageDateModified);

            if (_profileImage != null)
                encoder.Encode("profile_image", _profileImage);

            //tracker urls
            {
                List<Bincoding> trackerList = new List<Bincoding>(_trackerURIs.Length);

                foreach (Uri trackerURI in _trackerURIs)
                    trackerList.Add(Bincoding.GetValue(trackerURI.AbsoluteUri));

                encoder.Encode("tracker_list", trackerList);
            }

            //bitchat info
            {
                List<Bincoding> bitChatInfoList = new List<Bincoding>(_bitChatInfoList.Length);

                foreach (BitChatInfo info in _bitChatInfoList)
                    bitChatInfoList.Add(Bincoding.GetValue(info));

                encoder.Encode("bitchat_info", bitChatInfoList);
            }

            //bootstrap dht nodes
            {
                List<Bincoding> dhtNodeList = new List<Bincoding>(_bootstrapDhtNodes.Length);

                using (MemoryStream mS = new MemoryStream())
                {
                    foreach (IPEndPoint nodeEP in _bootstrapDhtNodes)
                    {
                        mS.SetLength(0);
                        IPEndPointParser.WriteTo(nodeEP, mS);

                        dhtNodeList.Add(Bincoding.GetValue(mS.ToArray()));
                    }
                }

                encoder.Encode("dht_nodes", dhtNodeList);
            }

            //proxy settings

            //proxy type
            if (_proxy != null)
                encoder.Encode("proxy_type", (byte)_proxy.Type);

            //proxy address
            if (_proxyAddress != null)
                encoder.Encode("proxy_address", _proxyAddress);

            //proxy port
            encoder.Encode("proxy_port", _proxyPort);

            //proxy credentials
            if (_proxyCredentials != null)
            {
                encoder.Encode("proxy_user", _proxyCredentials.UserName);
                encoder.Encode("proxy_pass", _proxyCredentials.Password);
            }

            //generic client data
            if ((_clientData != null) && (_clientData.Length > 0))
                encoder.Encode("client_data", _clientData);

            //signal end of settings
            encoder.EncodeNull();
        }

        #endregion

        #region public

        public void ConfigureProxy(NetProxyType proxyType, string proxyAddress, int proxyPort, NetworkCredential proxyCredentials)
        {
            _proxyAddress = proxyAddress;
            _proxyPort = proxyPort;
            _proxyCredentials = proxyCredentials;

            switch (proxyType)
            {
                case NetProxyType.Http:
                    _proxy = new NetProxy(new WebProxyEx(new Uri("http://" + _proxyAddress + ":" + _proxyPort), false, new string[] { }, _proxyCredentials));
                    break;

                case NetProxyType.Socks5:
                    _proxy = new NetProxy(new SocksClient(_proxyAddress, _proxyPort, _proxyCredentials));
                    break;

                default:
                    _proxy = null;
                    break;
            }

            ProxyUpdated?.BeginInvoke(this, EventArgs.Empty, null, null);
        }

        public void DisableProxy()
        {
            if (_proxy != null)
            {
                _proxy = null;
                ProxyUpdated?.BeginInvoke(this, EventArgs.Empty, null, null);
            }
        }

        internal bool SetProfileImage(long dateModified, byte[] image)
        {
            if (dateModified > _profileImageDateModified)
            {
                _profileImageDateModified = dateModified;
                _profileImage = image;

                return true;
            }

            return false;
        }

        public bool ProceedConnection(Certificate remoteCertificate)
        {
            return true;
        }

        #endregion

        #region properties

        public bool IsPortableApp
        { get { return _isPortableApp; } }

        public string ProfileFolder
        { get { return _profileFolder; } }

        public CertificateStore LocalCertificateStore
        { get { return _localCertStore; } }

        internal long ProfileImageDateModified
        { get { return _profileImageDateModified; } }

        public byte[] ProfileImage
        {
            get { return _profileImage; }
            set
            {
                _profileImageDateModified = Convert.ToInt64((DateTime.UtcNow - _epoch).TotalMilliseconds);
                _profileImage = value;

                ProfileImageChanged?.BeginInvoke(this, EventArgs.Empty, null, null);
            }
        }

        public int LocalPort
        {
            get { return _localPort; }
            set { _localPort = value; }
        }

        internal IPEndPoint[] BootstrapDhtNodes
        {
            get { return _bootstrapDhtNodes; }
            set { _bootstrapDhtNodes = value; }
        }

        public string DownloadFolder
        {
            get
            {
                if (_isPortableApp)
                    return _portableDownloadFolder;
                else
                    return _downloadFolder;
            }
            set { _downloadFolder = value; }
        }

        internal BitChatInfo[] BitChatInfoList
        {
            get { return _bitChatInfoList; }
            set { _bitChatInfoList = value; }
        }

        public Uri[] TrackerURIs
        {
            get { return _trackerURIs; }
            set { _trackerURIs = value; }
        }

        public bool CheckCertificateRevocationList
        {
            get { return _checkCertificateRevocationList; }
            set { _checkCertificateRevocationList = value; }
        }

        public byte[] ClientData
        {
            get { return _clientData; }
            set { _clientData = value; }
        }

        public bool EnableUPnP
        {
            get { return _enableUPnP; }
            set { _enableUPnP = value; }
        }

        public bool AllowInboundInvitations
        {
            get { return _allowInboundInvitations; }
            set { _allowInboundInvitations = value; }
        }

        public bool AllowOnlyLocalInboundInvitations
        {
            get { return _allowOnlyLocalInboundInvitations; }
            set { _allowOnlyLocalInboundInvitations = value; }
        }

        public NetProxy Proxy
        { get { return _proxy; } }

        public string ProxyAddress
        { get { return _proxyAddress; } }

        public int ProxyPort
        { get { return _proxyPort; } }

        public NetworkCredential ProxyCredentials
        { get { return _proxyCredentials; } }

        #endregion

        internal class BitChatInfo : IWriteStream
        {
            #region variables

            readonly BitChatNetworkType _type = BitChatNetworkType.GroupChat;
            readonly string _networkNameOrPeerEmailAddress;
            readonly string _sharedSecret;
            readonly BinaryID _hashedPeerEmailAddress;
            readonly BinaryID _networkID;
            readonly BinaryID _networkSecret;
            readonly string _messageStoreID;
            readonly byte[] _messageStoreKey;
            readonly long _groupImageDateModified = -1;
            readonly byte[] _groupImage;
            readonly Certificate[] _peerCerts = new Certificate[] { };
            readonly SharedFileInfo[] _sharedFiles = new SharedFileInfo[] { };
            readonly Uri[] _trackerURIs = new Uri[] { };
            readonly bool _enableTracking = true;
            readonly bool _sendInvitation = false;
            readonly string _invitationSender;
            readonly string _invitationMessage;
            readonly BitChatNetworkStatus _networkStatus = BitChatNetworkStatus.Online;
            readonly bool _mute = false;

            #endregion

            #region constructor

            public BitChatInfo(BitChatNetworkType type, string networkNameOrPeerEmailAddress, string sharedSecret, BinaryID hashedPeerEmailAddress, BinaryID networkID, BinaryID networkSecret, string messageStoreID, byte[] messageStoreKey, long groupImageDateModified, byte[] groupImage, Certificate[] peerCerts, SharedFileInfo[] sharedFiles, Uri[] trackerURIs, bool enableTracking, bool sendInvitation, string invitationSender, string invitationMessage, BitChatNetworkStatus networkStatus, bool mute)
            {
                _type = type;
                _networkNameOrPeerEmailAddress = networkNameOrPeerEmailAddress;
                _sharedSecret = sharedSecret;
                _hashedPeerEmailAddress = hashedPeerEmailAddress;
                _networkID = networkID;
                _networkSecret = networkSecret;
                _messageStoreID = messageStoreID;
                _messageStoreKey = messageStoreKey;
                _groupImageDateModified = groupImageDateModified;
                _groupImage = groupImage;
                _peerCerts = peerCerts;
                _sharedFiles = sharedFiles;
                _trackerURIs = trackerURIs;
                _enableTracking = enableTracking;
                _sendInvitation = sendInvitation;
                _invitationSender = invitationSender;
                _invitationMessage = invitationMessage;
                _networkStatus = networkStatus;
                _mute = mute;
            }

            public BitChatInfo(Stream s)
            {
                BincodingDecoder decoder = new BincodingDecoder(s, "BI");

                while (true)
                {
                    Bincoding value = decoder.DecodeNext();

                    if (value.Type == BincodingType.NULL)
                        break;

                    KeyValuePair<string, Bincoding> pair = value.GetKeyValuePair();

                    switch (pair.Key)
                    {
                        case "type":
                            _type = (BitChatNetworkType)pair.Value.GetByteValue();
                            break;

                        case "network_name":
                            _networkNameOrPeerEmailAddress = pair.Value.GetStringValue();
                            break;

                        case "shared_secret":
                            _sharedSecret = pair.Value.GetStringValue();
                            break;

                        case "enable_tracking":
                            _enableTracking = pair.Value.GetBooleanValue();
                            break;

                        case "send_invitation":
                            _sendInvitation = pair.Value.GetBooleanValue();
                            break;

                        case "invitation_sender":
                            _invitationSender = pair.Value.GetStringValue();
                            break;

                        case "invitation_message":
                            _invitationMessage = pair.Value.GetStringValue();
                            break;

                        case "network_status":
                            _networkStatus = (BitChatNetworkStatus)pair.Value.GetByteValue();
                            break;

                        case "hashed_peer_email_address":
                            _hashedPeerEmailAddress = new BinaryID(pair.Value.Value);
                            break;

                        case "network_id":
                            _networkID = new BinaryID(pair.Value.Value);
                            break;

                        case "network_secret":
                            _networkSecret = new BinaryID(pair.Value.Value);
                            break;

                        case "message_store_id":
                            _messageStoreID = pair.Value.GetStringValue();
                            break;

                        case "message_store_key":
                            _messageStoreKey = pair.Value.Value;
                            break;

                        case "group_image_date_modified":
                            _groupImageDateModified = pair.Value.GetLongValue();
                            break;

                        case "group_image":
                            _groupImage = pair.Value.Value;
                            break;

                        case "mute":
                            _mute = pair.Value.GetBooleanValue();
                            break;

                        case "peer_certs":
                            {
                                List<Bincoding> peerCerts = pair.Value.GetList();

                                _peerCerts = new Certificate[peerCerts.Count];
                                int i = 0;

                                foreach (Bincoding item in peerCerts)
                                    _peerCerts[i++] = new Certificate(item.GetValueStream());
                            }
                            break;

                        case "shared_files":
                            {
                                List<Bincoding> sharedFiles = pair.Value.GetList();

                                _sharedFiles = new SharedFileInfo[sharedFiles.Count];
                                int i = 0;

                                foreach (Bincoding item in sharedFiles)
                                    _sharedFiles[i++] = new SharedFileInfo(item.GetValueStream());
                            }
                            break;

                        case "tracker_list":
                            {
                                List<Bincoding> trackerList = pair.Value.GetList();

                                _trackerURIs = new Uri[trackerList.Count];
                                int i = 0;

                                foreach (Bincoding item in trackerList)
                                    _trackerURIs[i++] = new Uri(item.GetStringValue());
                            }
                            break;
                    }
                }
            }

            #endregion

            #region public

            public void WriteTo(Stream s)
            {
                BincodingEncoder encoder = new BincodingEncoder(s, "BI", 7);

                encoder.Encode("type", (byte)_type);

                if (_networkNameOrPeerEmailAddress != null)
                    encoder.Encode("network_name", _networkNameOrPeerEmailAddress);

                if (_sharedSecret != null)
                    encoder.Encode("shared_secret", _sharedSecret);

                encoder.Encode("enable_tracking", _enableTracking);
                encoder.Encode("send_invitation", _sendInvitation);

                if (_invitationSender != null)
                    encoder.Encode("invitation_sender", _invitationSender);

                if (_invitationMessage != null)
                    encoder.Encode("invitation_message", _invitationMessage);

                encoder.Encode("network_status", (byte)_networkStatus);

                if (_hashedPeerEmailAddress != null)
                    encoder.Encode("hashed_peer_email_address", _hashedPeerEmailAddress.ID);

                if (_networkID != null)
                    encoder.Encode("network_id", _networkID.ID);

                if (_networkSecret != null)
                    encoder.Encode("network_secret", _networkSecret.ID);

                encoder.Encode("message_store_id", _messageStoreID);
                encoder.Encode("message_store_key", _messageStoreKey);

                encoder.Encode("group_image_date_modified", _groupImageDateModified);
                if (_groupImage != null)
                    encoder.Encode("group_image", _groupImage);

                encoder.Encode("mute", _mute);

                encoder.Encode("peer_certs", _peerCerts);
                encoder.Encode("shared_files", _sharedFiles);

                {
                    List<Bincoding> trackerList = new List<Bincoding>(_sharedFiles.Length);

                    foreach (Uri trackerURI in _trackerURIs)
                        trackerList.Add(Bincoding.GetValue(trackerURI.AbsoluteUri));

                    encoder.Encode("tracker_list", trackerList);
                }

                //signal end of settings
                encoder.EncodeNull();
            }

            #endregion

            #region properties

            public BitChatNetworkType Type
            { get { return _type; } }

            public string NetworkName
            { get { return _networkNameOrPeerEmailAddress; } }

            public MailAddress PeerEmailAddress
            {
                get
                {
                    if (_networkNameOrPeerEmailAddress == null)
                        return null;
                    else
                        return new MailAddress(_networkNameOrPeerEmailAddress);
                }
            }

            public string SharedSecret
            { get { return _sharedSecret; } }

            public BinaryID HashedPeerEmailAddress
            { get { return _hashedPeerEmailAddress; } }

            public BinaryID NetworkID
            { get { return _networkID; } }

            public BinaryID NetworkSecret
            { get { return _networkSecret; } }

            public string MessageStoreID
            { get { return _messageStoreID; } }

            public byte[] MessageStoreKey
            { get { return _messageStoreKey; } }

            public long GroupImageDateModified
            { get { return _groupImageDateModified; } }

            public byte[] GroupImage
            { get { return _groupImage; } }

            public Certificate[] PeerCertificateList
            { get { return _peerCerts; } }

            public SharedFileInfo[] SharedFileList
            { get { return _sharedFiles; } }

            public Uri[] TrackerURIs
            { get { return _trackerURIs; } }

            public bool EnableTracking
            { get { return _enableTracking; } }

            public bool SendInvitation
            { get { return _sendInvitation; } }

            public string InvitationSender
            { get { return _invitationSender; } }

            public string InvitationMessage
            { get { return _invitationMessage; } }

            public BitChatNetworkStatus NetworkStatus
            { get { return _networkStatus; } }

            public bool Mute
            { get { return _mute; } }

            #endregion
        }

        internal class SharedFileInfo : IWriteStream
        {
            #region variables

            readonly string _filePath;
            readonly SharedFileMetaData _fileMetaData;
            readonly byte[] _blockAvailable;
            readonly SharedFileState _state = SharedFileState.Paused;

            #endregion

            #region constructor

            public SharedFileInfo(string filePath, SharedFileMetaData fileMetaData, byte[] blockAvailable, SharedFileState state)
            {
                _filePath = filePath;
                _fileMetaData = fileMetaData;
                _blockAvailable = blockAvailable;
                _state = state;
            }

            public SharedFileInfo(Stream s)
            {
                BincodingDecoder decoder = new BincodingDecoder(s, "FI");

                while (true)
                {
                    Bincoding value = decoder.DecodeNext();

                    if (value.Type == BincodingType.NULL)
                        break;

                    KeyValuePair<string, Bincoding> pair = value.GetKeyValuePair();

                    switch (pair.Key)
                    {
                        case "file_path":
                            _filePath = pair.Value.GetStringValue();
                            break;

                        case "file_metadata":
                            _fileMetaData = new SharedFileMetaData(pair.Value.GetValueStream());
                            break;

                        case "state":
                            _state = (SharedFileState)pair.Value.GetByteValue();
                            break;

                        case "block_available":
                            _blockAvailable = pair.Value.Value;
                            break;
                    }
                }
            }

            #endregion

            #region public

            public void WriteTo(Stream s)
            {
                BincodingEncoder encoder = new BincodingEncoder(s, "FI", 2);

                if (_filePath != null)
                    encoder.Encode("file_path", _filePath);

                encoder.Encode("file_metadata", _fileMetaData);
                encoder.Encode("state", (byte)_state);
                encoder.Encode("block_available", _blockAvailable);

                //signal end
                encoder.EncodeNull();
            }

            #endregion

            #region properties

            public string FilePath
            { get { return _filePath; } }

            public SharedFileMetaData FileMetaData
            { get { return _fileMetaData; } }

            public byte[] BlockAvailable
            { get { return _blockAvailable; } }

            public SharedFileState State
            { get { return _state; } }

            #endregion
        }
    }
}
