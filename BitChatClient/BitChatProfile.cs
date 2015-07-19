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
using System.IO;
using System.Net;
using System.Text;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net.BitTorrent;
using TechnitiumLibrary.Security.Cryptography;

namespace BitChatClient
{
    public class BitChatProfile : CryptoContainer
    {
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
                new Uri("http://announce.torrentsmd.com:8080/announce.php"),
                new Uri("http://announce.torrentsmd.com:6969/announce"),
                new Uri("http://bt.careland.com.cn:6969/announce"),
                new Uri("http://i.bandito.org/announce"),
                new Uri("http://tracker.ex.ua/announce"),
                new Uri("http://tracker.ilibr.org:6969/announce")
            };

        CertificateStore _localCertStore;
        IPEndPoint _localEP;
        string _downloadFolder;
        BitChatInfo[] _bitChatInfoList;
        Uri[] _trackerURIs;
        bool _checkCertificateRevocationList;

        byte[] _clientData;

        #endregion

        #region constructor

        public BitChatProfile(CertificateStore localCertStore, IPEndPoint localEP, string downloadFolder, Uri[] trackerURIs)
        {
            _localCertStore = localCertStore;
            _localEP = localEP;
            _downloadFolder = downloadFolder;
            _bitChatInfoList = new BitChatInfo[] { };
            _trackerURIs = trackerURIs;
            _checkCertificateRevocationList = true;
        }

        public BitChatProfile(CertificateStore localCertStore, IPEndPoint localEP, string downloadFolder, Uri[] trackerURIs, string password)
            : base(SymmetricEncryptionAlgorithm.Rijndael, 256, password)
        {
            _localCertStore = localCertStore;
            _localEP = localEP;
            _downloadFolder = downloadFolder;
            _bitChatInfoList = new BitChatInfo[] { };
            _trackerURIs = trackerURIs;
            _checkCertificateRevocationList = true;
        }

        public BitChatProfile(Stream s, string password)
            : base(s, password)
        { }

        public BitChatProfile(Stream s)
            : base(s, null)
        { }

        #endregion

        #region private

        public void UpdateBitChatInfo(BitChatInfo[] bitChatInfoList)
        {
            _bitChatInfoList = bitChatInfoList;
        }

        protected override void ReadPlainTextFrom(BinaryReader bR)
        {
            if (Encoding.ASCII.GetString(bR.ReadBytes(2)) != "BP")
                throw new BitChatException("Invalid BitChatProfile data format.");

            byte version = bR.ReadByte();

            switch (version)
            {
                case 1:
                    #region version 1

                    //tracker client id
                    TrackerClientID localClientID = new TrackerClientID(bR);

                    //local cert store
                    if (bR.ReadByte() == 1)
                        _localCertStore = new CertificateStore(bR);

                    //bitchat local service end point
                    _localEP = new IPEndPoint(new IPAddress(bR.ReadBytes(bR.ReadByte())), bR.ReadInt32());

                    //default tracker urls
                    _trackerURIs = DefaultTrackerURIs;

                    break;

                    #endregion

                case 2:
                case 3:
                    #region version 2 & 3

                    //local cert store
                    if (bR.ReadByte() == 1)
                        _localCertStore = new CertificateStore(bR);

                    //bitchat local service end point
                    _localEP = new IPEndPoint(new IPAddress(bR.ReadBytes(bR.ReadByte())), bR.ReadInt32());

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

                    break;

                    #endregion

                default:
                    throw new BitChatException("BitChatProfile data version not supported.");
            }
        }

        protected override void WritePlainTextTo(BinaryWriter bW)
        {
            bW.Write(Encoding.ASCII.GetBytes("BP"));
            bW.Write((byte)3);

            //local cert store
            if (_localCertStore == null)
                bW.Write((byte)0);
            else
            {
                bW.Write((byte)1);
                _localCertStore.WriteTo(bW);
            }

            //bitchat local service end point
            byte[] localIP = _localEP.Address.GetAddressBytes();
            bW.Write(Convert.ToByte(localIP.Length));
            bW.Write(localIP);
            bW.Write(_localEP.Port);

            //download folder
            if (_downloadFolder == null)
                bW.Write((ushort)0);
            else
            {
                byte[] buffer = Encoding.UTF8.GetBytes(_downloadFolder);
                bW.Write((ushort)buffer.Length);
                bW.Write(buffer, 0, buffer.Length);
            }

            //tracker urls
            bW.Write(Convert.ToByte(_trackerURIs.Length));
            foreach (Uri trackerURI in _trackerURIs)
            {
                byte[] buffer = Encoding.UTF8.GetBytes(trackerURI.AbsoluteUri);
                bW.Write((byte)buffer.Length);
                bW.Write(buffer);
            }

            //bitchat info
            bW.Write(Convert.ToByte(_bitChatInfoList.Length));
            foreach (BitChatInfo info in _bitChatInfoList)
                info.WriteTo(bW);

            //check CertificateRevocationList
            bW.Write(_checkCertificateRevocationList);

            //generic client data
            if ((_clientData == null) || (_clientData.Length == 0))
            {
                bW.Write(0);
            }
            else
            {
                bW.Write(_clientData.Length);
                bW.Write(_clientData);
            }
        }

        #endregion

        #region properties

        public CertificateStore LocalCertificateStore
        {
            get { return _localCertStore; }
            set { _localCertStore = value; }
        }

        public IPEndPoint LocalEP
        {
            get { return _localEP; }
            set { _localEP = value; }
        }

        public string DownloadFolder
        {
            get { return _downloadFolder; }
            set { _downloadFolder = value; }
        }

        public BitChatInfo[] BitChatInfoList
        { get { return _bitChatInfoList; } }

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

        #endregion

        public class BitChatInfo : WriteStream
        {
            #region variables

            BitChatNetworkType _type;
            string _networkNameOrPeerEmailAddress;
            string _sharedSecret;
            BinaryID _networkID;
            Certificate[] _peerCerts;
            SharedFileInfo[] _sharedFiles;
            Uri[] _trackerURIs;

            #endregion

            #region constructor

            public BitChatInfo(BitChatNetworkType type, string networkNameOrPeerEmailAddress, string sharedSecret, BinaryID networkID, Certificate[] peerCerts, SharedFileInfo[] sharedFiles, Uri[] trackerURIs)
            {
                _type = type;
                _networkNameOrPeerEmailAddress = networkNameOrPeerEmailAddress;
                _sharedSecret = sharedSecret;
                _networkID = networkID;
                _peerCerts = peerCerts;
                _sharedFiles = sharedFiles;
                _trackerURIs = trackerURIs;
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
                        if (version > 2)
                            _type = (BitChatNetworkType)bR.ReadByte();
                        else
                            _type = BitChatNetworkType.GroupChat;

                        _networkNameOrPeerEmailAddress = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));
                        _sharedSecret = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));

                        if (version > 3)
                            _networkID = new BinaryID(bR.ReadBytes(bR.ReadByte()));

                        _peerCerts = new Certificate[bR.ReadByte()];
                        for (int i = 0; i < _peerCerts.Length; i++)
                            _peerCerts[i] = new Certificate(bR);

                        _sharedFiles = new SharedFileInfo[bR.ReadByte()];
                        for (int i = 0; i < _sharedFiles.Length; i++)
                            _sharedFiles[i] = new SharedFileInfo(bR);

                        if (version > 1)
                        {
                            _trackerURIs = new Uri[bR.ReadByte()];

                            for (int i = 0; i < _trackerURIs.Length; i++)
                                _trackerURIs[i] = new Uri(Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte())));
                        }

                        break;

                    default:
                        throw new BitChatException("BitChatInfo data version not supported.");
                }
            }

            #endregion

            #region public

            public override void WriteTo(BinaryWriter bW)
            {
                bW.Write(Encoding.ASCII.GetBytes("BI"));
                bW.Write((byte)4);

                bW.Write((byte)_type);

                byte[] buffer;

                buffer = Encoding.UTF8.GetBytes(_networkNameOrPeerEmailAddress);
                bW.Write(Convert.ToByte(buffer.Length));
                bW.Write(buffer);

                buffer = Encoding.UTF8.GetBytes(_sharedSecret);
                bW.Write(Convert.ToByte(buffer.Length));
                bW.Write(buffer);

                bW.Write(Convert.ToByte(_networkID.ID.Length));
                bW.Write(_networkID.ID);

                bW.Write(Convert.ToByte(_peerCerts.Length));
                foreach (Certificate peerCert in _peerCerts)
                    peerCert.WriteTo(bW);

                bW.Write(Convert.ToByte(_sharedFiles.Length));
                foreach (SharedFileInfo sharedFile in _sharedFiles)
                    sharedFile.WriteTo(bW);

                bW.Write(Convert.ToByte(_trackerURIs.Length));
                foreach (Uri trackerURI in _trackerURIs)
                {
                    buffer = Encoding.UTF8.GetBytes(trackerURI.AbsoluteUri);
                    bW.Write(Convert.ToByte(buffer.Length));
                    bW.Write(buffer);
                }
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

            public Certificate[] PeerCertificateList
            { get { return _peerCerts; } }

            public SharedFileInfo[] SharedFileList
            { get { return _sharedFiles; } }

            public Uri[] TrackerURIs
            { get { return _trackerURIs; } }

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

            public SharedFileInfo(BinaryReader bR)
            {
                if (Encoding.ASCII.GetString(bR.ReadBytes(2)) != "FI")
                    throw new BitChatException("Invalid SharedFileInfo data format.");

                byte version = bR.ReadByte();

                switch (version)
                {
                    case 1:
                        _filePath = Encoding.UTF8.GetString(bR.ReadBytes(bR.ReadByte()));
                        _fileMetaData = new SharedFileMetaData(bR);

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

            public override void WriteTo(BinaryWriter bW)
            {
                bW.Write(Encoding.ASCII.GetBytes("FI"));
                bW.Write((byte)1);

                byte[] buffer;

                buffer = Encoding.UTF8.GetBytes(_filePath);
                bW.Write(Convert.ToByte(buffer.Length));
                bW.Write(buffer);

                _fileMetaData.WriteTo(bW);

                bW.Write(_blockAvailable.Length);
                foreach (FileBlockState state in _blockAvailable)
                    bW.Write((byte)state);

                bW.Write(_isPaused);
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
