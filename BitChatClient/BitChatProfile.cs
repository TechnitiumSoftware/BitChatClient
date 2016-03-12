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

using BitChatClient.FileSharing;
using BitChatClient.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Net.Proxy;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient
{
    public class BitChatProfile : CryptoContainer
    {
        #region event

        public event EventHandler ProxyUpdated;
        public event EventHandler ProfileImageChanged;

        #endregion

        #region variables

        public static Uri[] DefaultTrackerURIs
            = new Uri[] 
            { 
                new Uri("udp://tracker.publicbt.com:80"),
                new Uri("udp://tracker.openbittorrent.com:80"),
                new Uri("udp://tracker.istole.it:80"),
                new Uri("udp://open.demonii.com:1337/announce"),
                new Uri("udp://tracker.coppersurfer.tk:80"),
                new Uri("udp://coppersurfer.tk:6969/announce"),
                new Uri("udp://tracker.leechers-paradise.org:6969"),
                new Uri("udp://exodus.desync.com:6969"),
                new Uri("udp://tracker.btzoo.eu:80/announce"),
                new Uri("udp://tracker.ilibr.org:80/announce"),
                new Uri("udp://9.rarbg.com:2710/announce"),
                new Uri("udp://9.rarbg.me:2710/announce"),
                new Uri("udp://tracker4.piratux.com:6969/announce"),
                new Uri("udp://tracker.blackunicorn.xyz:6969/announce"),
                new Uri("udp://tracker.pomf.se/announce"),
                new Uri("udp://open.demonii.com:1337"),
                new Uri("udp://torrent.gresille.org:80/announce"),
                new Uri("udp://glotorrents.pw:6969/announce"),
                new Uri("udp://eddie4.nl:6969/announce"),
                new Uri("udp://9.rarbg.to:2710/announce"),
                new Uri("http://9.rarbg.com:2710/announce"),
                new Uri("http://opensharing.org:2710/announce"),
                new Uri("http://bt.careland.com.cn:6969/announce"),
                new Uri("http://i.bandito.org/announce"),
                new Uri("http://tracker.ex.ua/announce"),
                new Uri("http://tracker.ilibr.org:6969/announce")
            };

        string _profileFolder;

        CertificateStore _localCertStore;
        byte[] _profileImageSmall = null;
        byte[] _profileImageLarge = null;

        int _localPort;
        string _downloadFolder;
        Uri[] _trackerURIs;

        BitChatInfo[] _bitChatInfoList = new BitChatInfo[] { };
        bool _checkCertificateRevocationList = true;
        IPEndPoint[] _bootstrapDhtNodes = new IPEndPoint[] { };
        bool _enableUPnP = true;

        string _proxyAddress = "127.0.0.1";
        int _proxyPort = 0;
        NetworkCredential _proxyCredentials;
        NetProxy _proxy;

        byte[] _clientData;

        #endregion

        #region constructor

        public BitChatProfile(int localPort, string downloadFolder, Uri[] trackerURIs, string profileFolder)
        {
            _localPort = localPort;
            _downloadFolder = downloadFolder;
            _trackerURIs = trackerURIs;

            _profileFolder = profileFolder;
        }

        public BitChatProfile(Stream s, string password, string profileFolder)
            : base(s, password)
        {
            _profileFolder = profileFolder;
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

            //verify self signed cert
            _localCertStore.Certificate.Verify(new Certificate[] { _localCertStore.Certificate });

            using (WebClientEx client = new WebClientEx())
            {
                client.Proxy = _proxy;
                client.UserAgent = GetUserAgent();

                byte[] data = client.UploadData(apiUri.AbsoluteUri + "?cmd=reg", _localCertStore.Certificate.ToArray());

                using (BinaryReader bR = new BinaryReader(new MemoryStream(data)))
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

                byte[] data = client.DownloadData(apiUri.AbsoluteUri + "?cmd=dlc&email=" + _localCertStore.Certificate.IssuedTo.EmailAddress.Address);

                using (BinaryReader bR = new BinaryReader(new MemoryStream(data)))
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

            switch (decoder.Version)
            {
                case 1:
                    ReadSettingsVersion1(new BinaryReader(s));
                    break;

                case 2:
                case 3:
                    ReadSettingsVersion2And3(decoder.Version, new BinaryReader(s));
                    break;

                case 4:
                case 5:
                    ReadSettingsVersion4And5(decoder.Version, new BinaryReader(s));
                    break;

                case 6:
                    ReadSettingsVersion6(new BinaryReader(s));
                    break;

                case 7:
                    ReadSettingsVersion7(decoder);
                    break;

                default:
                    throw new BitChatException("BitChatProfile data version not supported.");
            }
        }

        private void ReadSettingsVersion1(BinaryReader bR)
        {
            //tracker client id
            TrackerClientID localClientID = new TrackerClientID(bR.BaseStream);

            //local cert store
            if (bR.ReadByte() == 1)
                _localCertStore = new CertificateStore(bR.BaseStream);

            //bitchat local service end point
            IPEndPoint localEP = new IPEndPoint(new IPAddress(bR.ReadBytes(bR.ReadByte())), bR.ReadInt32());
            _localPort = localEP.Port;

            _downloadFolder = @"C:\";

            //default tracker urls
            _trackerURIs = DefaultTrackerURIs;
        }

        private void ReadSettingsVersion2And3(byte version, BinaryReader bR)
        {
            //local cert store
            if (bR.ReadByte() == 1)
                _localCertStore = new CertificateStore(bR.BaseStream);

            //bitchat local service end point
            IPEndPoint localEP = new IPEndPoint(new IPAddress(bR.ReadBytes(bR.ReadByte())), bR.ReadInt32());
            _localPort = localEP.Port;

            //download folder
            _downloadFolder = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadUInt16()));
            if (_downloadFolder == null)
                _downloadFolder = @"C:\";

            //load tracker urls
            _trackerURIs = new Uri[bR.ReadByte()];
            for (int i = 0; i < _trackerURIs.Length; i++)
                _trackerURIs[i] = new Uri(Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte())));

            //load bitchat info
            _bitChatInfoList = new BitChatInfo[bR.ReadByte()];
            for (int i = 0; i < _bitChatInfoList.Length; i++)
                _bitChatInfoList[i] = new BitChatInfo(bR);

            if (version > 2)
            {
                //check CertificateRevocationList
                _checkCertificateRevocationList = bR.ReadBoolean();
            }
            else
            {
                _checkCertificateRevocationList = true;
            }

            //generic client data
            int dataCount = bR.ReadInt32();
            if (dataCount > 0)
                _clientData = bR.ReadBytes(dataCount);
        }

        private void ReadSettingsVersion4And5(byte version, BinaryReader bR)
        {
            //local cert store
            if (bR.ReadByte() == 1)
                _localCertStore = new CertificateStore(bR.BaseStream);

            //bitchat local port
            _localPort = bR.ReadInt32();

            //download folder
            _downloadFolder = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadUInt16()));
            if (_downloadFolder == null)
                _downloadFolder = @"C:\";

            //load tracker urls
            _trackerURIs = new Uri[bR.ReadByte()];
            for (int i = 0; i < _trackerURIs.Length; i++)
                _trackerURIs[i] = new Uri(Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte())));

            //load bitchat info
            _bitChatInfoList = new BitChatInfo[bR.ReadByte()];
            for (int i = 0; i < _bitChatInfoList.Length; i++)
                _bitChatInfoList[i] = new BitChatInfo(bR);

            //check CertificateRevocationList
            _checkCertificateRevocationList = bR.ReadBoolean();

            //bootstrap dht nodes
            _bootstrapDhtNodes = new IPEndPoint[bR.ReadInt32()];
            for (int i = 0; i < _bootstrapDhtNodes.Length; i++)
                _bootstrapDhtNodes[i] = IPEndPointParser.Parse(bR.BaseStream);

            //upnp enabled
            _enableUPnP = bR.ReadBoolean();

            if (version > 4)
            {
                NetProxyType proxyType;

                //socks proxy enabled
                bool proxyEnabled = bR.ReadBoolean();

                if (proxyEnabled)
                    proxyType = NetProxyType.Socks5;
                else
                    proxyType = NetProxyType.None;

                //socks proxy ep
                IPEndPoint socksEP = IPEndPointParser.Parse(bR.BaseStream);
                string proxyAddress = socksEP.Address.ToString();
                int proxyPort = socksEP.Port;

                //socks proxy credentials
                NetworkCredential proxyCredentials = null;
                bool auth = bR.ReadBoolean();
                if (auth)
                {
                    string username = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));
                    string password = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));

                    proxyCredentials = new NetworkCredential(username, password);
                }

                ConfigureProxy(proxyType, proxyAddress, proxyPort, proxyCredentials);
            }

            //generic client data
            int dataCount = bR.ReadInt32();
            if (dataCount > 0)
                _clientData = bR.ReadBytes(dataCount);
        }

        private void ReadSettingsVersion6(BinaryReader bR)
        {
            //local cert store
            if (bR.ReadByte() == 1)
                _localCertStore = new CertificateStore(bR.BaseStream);

            //bitchat local port
            _localPort = bR.ReadInt32();

            //download folder
            _downloadFolder = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadUInt16()));
            if (_downloadFolder == null)
                _downloadFolder = @"C:\";

            //load tracker urls
            _trackerURIs = new Uri[bR.ReadByte()];
            for (int i = 0; i < _trackerURIs.Length; i++)
                _trackerURIs[i] = new Uri(Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte())));

            //load bitchat info
            _bitChatInfoList = new BitChatInfo[bR.ReadByte()];
            for (int i = 0; i < _bitChatInfoList.Length; i++)
                _bitChatInfoList[i] = new BitChatInfo(bR);

            //check CertificateRevocationList
            _checkCertificateRevocationList = bR.ReadBoolean();

            //bootstrap dht nodes
            _bootstrapDhtNodes = new IPEndPoint[bR.ReadInt32()];
            for (int i = 0; i < _bootstrapDhtNodes.Length; i++)
                _bootstrapDhtNodes[i] = IPEndPointParser.Parse(bR.BaseStream);

            //upnp enabled
            _enableUPnP = bR.ReadBoolean();

            //proxy type
            NetProxyType proxyType = (NetProxyType)bR.ReadByte();

            //proxy address
            string proxyAddress = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));

            //proxy port
            int proxyPort = bR.ReadInt32();

            //proxy credentials
            NetworkCredential proxyCredentials = null;
            bool auth = bR.ReadBoolean();
            if (auth)
            {
                string username = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));
                string password = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));

                proxyCredentials = new NetworkCredential(username, password);
            }

            ConfigureProxy(proxyType, proxyAddress, proxyPort, proxyCredentials);

            //generic client data
            int dataCount = bR.ReadInt32();
            if (dataCount > 0)
                _clientData = bR.ReadBytes(dataCount);
        }

        private void ReadSettingsVersion7(BincodingDecoder decoder)
        {
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

                    case "download_folder":
                        _downloadFolder = item.Value.GetStringValue();
                        break;

                    case "local_cert_store":
                        _localCertStore = new CertificateStore(item.Value.GetValueStream());
                        break;

                    case "profile_image_small":
                        _profileImageSmall = item.Value.Value;
                        break;

                    case "profile_image_large":
                        _profileImageLarge = item.Value.Value;
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
                            List<Bincoding> dhtNodeList = item.Value.GetList();

                            _bootstrapDhtNodes = new IPEndPoint[dhtNodeList.Count];
                            int i = 0;

                            foreach (Bincoding dhtItem in dhtNodeList)
                                _bootstrapDhtNodes[i++] = IPEndPointParser.Parse(dhtItem.GetValueStream());
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

            if (_downloadFolder == null)
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        _downloadFolder = @"C:\";
                        break;

                    default:
                        _downloadFolder = @"/";
                        break;
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

            //download folder
            if (_downloadFolder != null)
                encoder.Encode("download_folder", _downloadFolder);

            //local cert store
            if (_localCertStore != null)
                encoder.Encode("local_cert_store", _localCertStore);

            //profile image
            if (_profileImageSmall != null)
                encoder.Encode("profile_image_small", _profileImageSmall);

            if (_profileImageLarge != null)
                encoder.Encode("profile_image_large", _profileImageLarge);

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

                foreach (IPEndPoint nodeEP in _bootstrapDhtNodes)
                    dhtNodeList.Add(Bincoding.GetValue(IPEndPointParser.ToArray(nodeEP)));

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
            if ((_clientData == null) || (_clientData.Length == 0))
                encoder.Encode("client_data", Bincoding.GetNullValue());
            else
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

            if (ProxyUpdated != null)
                ProxyUpdated(this, EventArgs.Empty);
        }

        public void DisableProxy()
        {
            _proxy = null;

            if (ProxyUpdated != null)
                ProxyUpdated(this, EventArgs.Empty);
        }

        public void SetProfileImage(byte[] imageSmall, byte[] imageLarge)
        {
            _profileImageSmall = imageSmall;
            _profileImageLarge = imageLarge;

            if (ProfileImageChanged != null)
                ProfileImageChanged(this, EventArgs.Empty);
        }

        #endregion

        #region properties

        public string ProfileFolder
        { get { return _profileFolder; } }

        public CertificateStore LocalCertificateStore
        {
            get { return _localCertStore; }
        }

        public byte[] ProfileImageSmall
        {
            get { return _profileImageSmall; }
            set { _profileImageSmall = value; }
        }

        public byte[] ProfileImageLarge
        {
            get { return _profileImageLarge; }
            set { _profileImageLarge = value; }
        }

        public int LocalPort
        {
            get { return _localPort; }
            set { _localPort = value; }
        }

        public IPEndPoint[] BootstrapDhtNodes
        {
            get { return _bootstrapDhtNodes; }
            set { _bootstrapDhtNodes = value; }
        }

        public string DownloadFolder
        {
            get { return _downloadFolder; }
            set { _downloadFolder = value; }
        }

        public BitChatInfo[] BitChatInfoList
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

        public NetProxy Proxy
        {
            get { return _proxy; }
        }

        public string ProxyAddress
        {
            get { return _proxyAddress; }
        }

        public int ProxyPort
        {
            get { return _proxyPort; }
        }

        public NetworkCredential ProxyCredentials
        {
            get { return _proxyCredentials; }
        }

        #endregion

        public class BitChatInfo : WriteStream
        {
            #region variables

            BitChatNetworkType _type = BitChatNetworkType.GroupChat;
            string _networkNameOrPeerEmailAddress;
            string _sharedSecret;
            BinaryID _networkID;
            string _messageStoreID;
            byte[] _messageStoreKey;
            Certificate[] _peerCerts = new Certificate[] { };
            SharedFileInfo[] _sharedFiles = new SharedFileInfo[] { };
            Uri[] _trackerURIs = new Uri[] { };
            bool _enableTracking = true;
            BitChatNetworkStatus _networkStatus = BitChatNetworkStatus.Online;

            #endregion

            #region constructor

            public BitChatInfo(BitChatNetworkType type, string networkNameOrPeerEmailAddress, string sharedSecret, BinaryID networkID, string messageStoreID, byte[] messageStoreKey, Certificate[] peerCerts, SharedFileInfo[] sharedFiles, Uri[] trackerURIs, bool enableTracking, BitChatNetworkStatus networkStatus)
            {
                _type = type;
                _networkNameOrPeerEmailAddress = networkNameOrPeerEmailAddress;
                _sharedSecret = sharedSecret;
                _networkID = networkID;
                _messageStoreID = messageStoreID;
                _messageStoreKey = messageStoreKey;
                _peerCerts = peerCerts;
                _sharedFiles = sharedFiles;
                _trackerURIs = trackerURIs;
                _enableTracking = enableTracking;
                _networkStatus = networkStatus;
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

                        case "network_status":
                            _networkStatus = (BitChatNetworkStatus)pair.Value.GetByteValue();
                            break;

                        case "network_id":
                            _networkID = new BinaryID(pair.Value.Value);
                            break;

                        case "message_store_id":
                            _messageStoreID = pair.Value.GetStringValue();
                            break;

                        case "message_store_key":
                            _messageStoreKey = pair.Value.Value;
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

            public BitChatInfo(BinaryReader bR)
            {
                if (Encoding.ASCII.GetString(bR.ReadBytes(2)) != "BI")
                    throw new BitChatException("Invalid BitChatInfo data format.");

                byte version = bR.ReadByte();

                switch (version)
                {
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                    case 6:
                        if (version > 2)
                            _type = (BitChatNetworkType)bR.ReadByte();
                        else
                            _type = BitChatNetworkType.GroupChat;

                        _networkNameOrPeerEmailAddress = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));
                        _sharedSecret = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));

                        if (version > 3)
                        {
                            int networkIDLen = bR.ReadByte();
                            if (networkIDLen > 0)
                                _networkID = new BinaryID(bR.ReadBytes(networkIDLen));
                        }

                        _peerCerts = new Certificate[bR.ReadByte()];
                        for (int i = 0; i < _peerCerts.Length; i++)
                            _peerCerts[i] = new Certificate(bR.BaseStream);

                        _sharedFiles = new SharedFileInfo[bR.ReadByte()];
                        for (int i = 0; i < _sharedFiles.Length; i++)
                            _sharedFiles[i] = new SharedFileInfo(bR);

                        if (version > 1)
                        {
                            _trackerURIs = new Uri[bR.ReadByte()];

                            for (int i = 0; i < _trackerURIs.Length; i++)
                                _trackerURIs[i] = new Uri(Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte())));
                        }

                        if (version > 4)
                            _enableTracking = bR.ReadBoolean();
                        else
                            _enableTracking = true;

                        if (version > 5)
                            _networkStatus = (BitChatNetworkStatus)bR.ReadByte();
                        else
                            _networkStatus = BitChatNetworkStatus.Online;

                        _messageStoreID = BinaryID.GenerateRandomID160().ToString();
                        _messageStoreKey = BinaryID.GenerateRandomID256().ID;

                        break;

                    default:
                        throw new BitChatException("BitChatInfo data version not supported.");
                }
            }

            #endregion

            #region public

            public override void WriteTo(Stream s)
            {
                BincodingEncoder encoder = new BincodingEncoder(s, "BI", 7);

                encoder.Encode("type", (byte)_type);
                encoder.Encode("network_name", _networkNameOrPeerEmailAddress);
                encoder.Encode("shared_secret", _sharedSecret);
                encoder.Encode("enable_tracking", _enableTracking);
                encoder.Encode("network_status", (byte)_networkStatus);

                if (_networkID != null)
                    encoder.Encode("network_id", _networkID.ID);

                encoder.Encode("message_store_id", _messageStoreID);
                encoder.Encode("message_store_key", _messageStoreKey);

                {
                    List<Bincoding> peerCerts = new List<Bincoding>(_peerCerts.Length);

                    foreach (Certificate peerCert in _peerCerts)
                        peerCerts.Add(Bincoding.GetValue(peerCert));

                    encoder.Encode("peer_certs", peerCerts);
                }

                {
                    List<Bincoding> sharedFiles = new List<Bincoding>(_sharedFiles.Length);

                    foreach (SharedFileInfo sharedFile in _sharedFiles)
                        sharedFiles.Add(Bincoding.GetValue(sharedFile));

                    encoder.Encode("shared_files", sharedFiles);
                }

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

            public string NetworkNameOrPeerEmailAddress
            { get { return _networkNameOrPeerEmailAddress; } }

            public string SharedSecret
            { get { return _sharedSecret; } }

            public BinaryID NetworkID
            { get { return _networkID; } }

            public string MessageStoreID
            { get { return _messageStoreID; } }

            public byte[] MessageStoreKey
            { get { return _messageStoreKey; } }

            public Certificate[] PeerCertificateList
            { get { return _peerCerts; } }

            public SharedFileInfo[] SharedFileList
            { get { return _sharedFiles; } }

            public Uri[] TrackerURIs
            { get { return _trackerURIs; } }

            public bool EnableTracking
            { get { return _enableTracking; } }

            public BitChatNetworkStatus NetworkStatus
            { get { return _networkStatus; } }

            #endregion
        }

        public class SharedFileInfo : WriteStream
        {
            #region variables

            string _filePath;
            SharedFileMetaData _fileMetaData;
            FileBlockState[] _blockAvailable;
            bool _isPaused;

            #endregion

            #region constructor

            public SharedFileInfo(string filePath, SharedFileMetaData fileMetaData, FileBlockState[] blockAvailable, bool isPaused)
            {
                _filePath = filePath;
                _fileMetaData = fileMetaData;
                _blockAvailable = blockAvailable;
                _isPaused = isPaused;
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

                        case "paused":
                            _isPaused = pair.Value.GetBooleanValue();
                            break;

                        case "block_available":
                            {
                                List<Bincoding> blockList = pair.Value.GetList();

                                _blockAvailable = new FileBlockState[blockList.Count];
                                int i = 0;

                                foreach (Bincoding item in blockList)
                                    _blockAvailable[i++] = (FileBlockState)item.GetByteValue();
                            }
                            break;
                    }
                }
            }

            public SharedFileInfo(BinaryReader bR)
            {
                if (Encoding.ASCII.GetString(bR.ReadBytes(2)) != "FI")
                    throw new BitChatException("Invalid SharedFileInfo data format.");

                byte version = bR.ReadByte();

                switch (version)
                {
                    case 1:
                        _filePath = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));
                        _fileMetaData = new SharedFileMetaData(bR.BaseStream);

                        _blockAvailable = new FileBlockState[bR.ReadInt32()];
                        for (int i = 0; i < _blockAvailable.Length; i++)
                            _blockAvailable[i] = (FileBlockState)bR.ReadByte();

                        _isPaused = bR.ReadBoolean();
                        break;

                    default:
                        throw new BitChatException("SharedFileInfo data version not supported.");
                }
            }

            #endregion

            #region public

            public override void WriteTo(Stream s)
            {
                BincodingEncoder encoder = new BincodingEncoder(s, "FI", 2);

                encoder.Encode("file_path", _filePath);
                encoder.Encode("file_metadata", _fileMetaData);
                encoder.Encode("paused", _isPaused);

                {
                    List<Bincoding> blockList = new List<Bincoding>(_blockAvailable.Length);

                    foreach (FileBlockState state in _blockAvailable)
                        blockList.Add(Bincoding.GetValue((byte)state));

                    encoder.Encode("block_available", blockList);
                }

                //signal end
                encoder.EncodeNull();
            }

            #endregion

            #region properties

            public string FilePath
            { get { return _filePath; } }

            public SharedFileMetaData FileMetaData
            { get { return _fileMetaData; } }

            public FileBlockState[] BlockAvailable
            { get { return _blockAvailable; } }

            public bool IsPaused
            { get { return _isPaused; } }

            #endregion
        }
    }
}
