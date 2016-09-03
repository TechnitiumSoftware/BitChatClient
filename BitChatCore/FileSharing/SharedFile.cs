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

using BitChatCore.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using TechnitiumLibrary.IO;
using TechnitiumLibrary.Net;

namespace BitChatCore.FileSharing
{
    public enum SharedFileState : byte
    {
        Advertisement = 0,
        Sharing = 1,
        Downloading = 2,
        Paused = 3
    }

    public enum FileBlockState : byte
    {
        NotAvailable = 0,
        Downloading = 1,
        Available = 2
    }

    public class SharedFile : IDisposable
    {
        #region events

        public event EventHandler FileDownloadStarted;
        public event EventHandler FileSharingStarted;
        public event EventHandler FilePaused;
        public event EventHandler FileDownloaded;
        public event EventHandler FileBlockDownloaded;
        public event EventHandler FileTransferSpeedUpdate;
        public event EventHandler PeerCountUpdate;

        #endregion

        #region variables

        readonly static Dictionary<BinaryID, SharedFile> _sharedFiles = new Dictionary<BinaryID, SharedFile>();

        const int MAX_DOWNLOADING_BLOCKS = 5;
        const int DOWNLOAD_MONITOR_INTERVAL = 10000; //10 seconds
        const int DOWNLOAD_INACTIVE_INTERVAL_SECONDS = 30; //30 seconds

        const ushort MIN_BLOCK_SIZE = 57344; //56kb min block size

        readonly SynchronizationContext _syncCxt;

        //shared file input
        FileStream _fileStream;
        readonly SharedFileMetaData _metaData;

        SharedFileState _state;
        FileBlockState[] _blockAvailable;
        int _availableBlocksCount;
        bool _isComplete;

        //downloading task variables
        List<int> _pendingBlocks;
        Random _rndPendingBlock;
        List<FileBlockDownloadManager> _downloadingBlocks;
        Timer _downloadMonitor;

        //transfer speed
        Timer _fileTransferSpeedCalculator;
        int _bytesDownloadedLastSecond = 0;
        int _bytesUploadedLastSecond = 0;

        //peers
        readonly List<BitChat.Peer> _seeders = new List<BitChat.Peer>();
        readonly List<BitChat.Peer> _peers = new List<BitChat.Peer>();

        //chats
        readonly List<BitChat> _chats = new List<BitChat>();

        #endregion

        #region constructor

        private SharedFile(FileStream fS, SharedFileMetaData metaData, FileBlockState[] blockAvailable, int availableBlocksCount, SynchronizationContext syncCxt)
        {
            _fileStream = fS;
            _metaData = metaData;
            _blockAvailable = blockAvailable;
            _availableBlocksCount = availableBlocksCount;
            _syncCxt = syncCxt;

            _isComplete = (_blockAvailable.Length == _availableBlocksCount);
        }

        #endregion

        #region IDisposable

        ~SharedFile()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool _disposed = false;

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                //stop timers
                StopTimers();

                if (_fileStream != null)
                    _fileStream.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region static

        internal static SharedFile LoadFile(string filePath, SharedFileMetaData metaData, FileBlockState[] blockAvailable, SharedFileState state, BitChat chat, SynchronizationContext syncCxt)
        {
            //check if file already shared
            lock (_sharedFiles)
            {
                SharedFile sharedFile;

                if (_sharedFiles.ContainsKey(metaData.FileID))
                {
                    sharedFile = _sharedFiles[metaData.FileID];
                }
                else
                {
                    if (state == SharedFileState.Advertisement)
                    {
                        sharedFile = new SharedFile(null, metaData, new FileBlockState[metaData.BlockHash.Length], 0, syncCxt);
                        _sharedFiles.Add(metaData.FileID, sharedFile);

                        sharedFile._state = SharedFileState.Advertisement;
                    }
                    else
                    {
                        int availableBlocksCount = 0;

                        for (int i = 0; i < blockAvailable.Length; i++)
                        {
                            if (blockAvailable[i] == FileBlockState.Available)
                                availableBlocksCount++;
                        }

                        FileStream fS;

                        if (blockAvailable.Length == availableBlocksCount)
                            fS = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        else
                            fS = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);

                        sharedFile = new SharedFile(fS, metaData, blockAvailable, availableBlocksCount, syncCxt);
                        _sharedFiles.Add(metaData.FileID, sharedFile);

                        sharedFile._state = SharedFileState.Paused;

                        if (state != SharedFileState.Paused)
                            sharedFile.Start();
                    }
                }

                sharedFile.AddChat(chat);

                return sharedFile;
            }
        }

        internal static SharedFile ShareFile(string filePath, string hashAlgo, BitChat chat, SynchronizationContext syncCxt)
        {
            FileStream fS = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            HashAlgorithm hash = HashAlgorithm.Create(hashAlgo);
            int hashSize = hash.HashSize / 8;

            //calculate block size
            int blockSize;
            {
                //FileAdvertisement header
                int AdvtHeaderSize = 1 + 1 + 255 + 1 + 255 + 8 + 8 + 4 + 1 + 255 + 1;
                int payloadSize = BitChatNetwork.MAX_MESSAGE_SIZE - AdvtHeaderSize; //max secure channel packet paload size - header
                int totalBlocksPossible = payloadSize / hashSize;
                blockSize = Convert.ToInt32(fS.Length / totalBlocksPossible);

                if (blockSize <= short.MaxValue)
                    blockSize = short.MaxValue + 1;
                else
                {
                    //align to 16 bytes
                    int remainder = blockSize % 16;
                    if (remainder > 0)
                        blockSize = blockSize - remainder + 16;
                }
            }

            //compute block hashes and file info hash
            int totalBlocks = Convert.ToInt32(Math.Ceiling(Convert.ToDouble((double)fS.Length / blockSize)));
            byte[][] blockHash = new byte[totalBlocks][];
            FileBlockState[] blockAvailable = new FileBlockState[totalBlocks];

            //init
            for (int i = 0; i < totalBlocks; i++)
            {
                long offset = i * blockSize;
                long length = blockSize;

                if ((offset + length) > fS.Length)
                    length = fS.Length - offset;

                blockHash[i] = hash.ComputeHash(new OffsetStream(fS, offset, length));
                blockAvailable[i] = FileBlockState.Available;
            }

            //get file meta data
            SharedFileMetaData metaData = new SharedFileMetaData(Path.GetFileName(fS.Name), WebUtilities.GetContentType(fS.Name), File.GetLastWriteTimeUtc(fS.Name), fS.Length, blockSize, hashAlgo, blockHash);

            //check if file already shared
            lock (_sharedFiles)
            {
                SharedFile sharedFile;

                if (_sharedFiles.ContainsKey(metaData.FileID))
                {
                    sharedFile = _sharedFiles[metaData.FileID];

                    if (sharedFile._isComplete)
                    {
                        //close the file and do nothing
                        fS.Dispose();
                    }
                    else
                    {
                        //since the current shared file is incomplete and we are adding a complete shared file copy

                        //stop current download process
                        sharedFile.Pause();
                        sharedFile._fileStream.Dispose();

                        //update the current object with new file data
                        sharedFile._fileStream = fS;
                        sharedFile._blockAvailable = blockAvailable;
                        sharedFile._availableBlocksCount = blockAvailable.Length;
                        sharedFile._isComplete = true;

                        //start sharing new file
                        sharedFile.StartSharing();
                    }
                }
                else
                {
                    sharedFile = new SharedFile(fS, metaData, blockAvailable, blockAvailable.Length, syncCxt);
                    sharedFile.StartSharing();

                    _sharedFiles.Add(metaData.FileID, sharedFile);
                }

                sharedFile.AddChat(chat);

                return sharedFile;
            }
        }

        internal static SharedFile PrepareDownloadFile(SharedFileMetaData metaData, BitChat chat, SynchronizationContext syncCxt, BitChat.Peer seeder = null)
        {
            //check if file already exists
            lock (_sharedFiles)
            {
                SharedFile sharedFile;

                if (_sharedFiles.ContainsKey(metaData.FileID))
                {
                    sharedFile = _sharedFiles[metaData.FileID];
                }
                else
                {
                    sharedFile = new SharedFile(null, metaData, new FileBlockState[metaData.BlockHash.Length], 0, syncCxt);
                    _sharedFiles.Add(metaData.FileID, sharedFile);
                }

                sharedFile.AddChat(chat);

                if (seeder != null)
                    sharedFile.AddSeeder(seeder);

                return sharedFile;
            }
        }

        #endregion

        #region private event functions

        private void RaiseEventFileDownloadStarted()
        {
            _syncCxt.Send(new SendOrPostCallback(FileDownloadStartedCallback), null);
        }

        private void FileDownloadStartedCallback(object obj)
        {
            try
            {
                FileDownloadStarted(this, EventArgs.Empty);
            }
            catch { }
        }

        private void RaiseEventFileSharingStarted()
        {
            _syncCxt.Send(new SendOrPostCallback(FileSharingStartedCallback), null);
        }

        private void FileSharingStartedCallback(object obj)
        {
            try
            {
                FileSharingStarted(this, EventArgs.Empty);
            }
            catch { }
        }

        private void RaiseEventFilePaused()
        {
            _syncCxt.Send(new SendOrPostCallback(FilePausedCallback), null);
        }

        private void FilePausedCallback(object obj)
        {
            try
            {
                FilePaused(this, EventArgs.Empty);
            }
            catch { }
        }

        private void RaiseEventFileDownloaded()
        {
            _syncCxt.Send(new SendOrPostCallback(FileDownloadedCallback), null);
        }

        private void FileDownloadedCallback(object obj)
        {
            try
            {
                FileDownloaded(this, EventArgs.Empty);
            }
            catch { }
        }

        private void RaiseEventFileBlockDownloaded()
        {
            if (_isComplete)
                return;

            _syncCxt.Send(new SendOrPostCallback(FileBlockDownloadedCallback), null);
        }

        private void FileBlockDownloadedCallback(object obj)
        {
            try
            {
                FileBlockDownloaded(this, EventArgs.Empty);
            }
            catch { }
        }

        private void RaiseEventFileTransferSpeedUpdate()
        {
            _syncCxt.Send(new SendOrPostCallback(FileTransferSpeedUpdateCallback), null);
        }

        private void FileTransferSpeedUpdateCallback(object obj)
        {
            try
            {
                FileTransferSpeedUpdate(this, EventArgs.Empty);
            }
            catch { }
        }

        private void RaiseEventPeerCountUpdate()
        {
            _syncCxt.Send(new SendOrPostCallback(PeerCountUpdateCallback), null);
        }

        private void PeerCountUpdateCallback(object obj)
        {
            try
            {
                PeerCountUpdate(this, EventArgs.Empty);
            }
            catch { }
        }

        #endregion

        #region private

        internal BitChatProfile.SharedFileInfo GetSharedFileInfo()
        {
            return new BitChatProfile.SharedFileInfo((_fileStream == null ? null : _fileStream.Name), _metaData, _blockAvailable, _state);
        }

        internal void AddChat(BitChat chat)
        {
            lock (_chats)
            {
                if (!_chats.Contains(chat))
                    _chats.Add(chat);
            }
        }

        internal void AddPeer(BitChat.Peer peer)
        {
            lock (_peers)
            {
                if (!_peers.Contains(peer))
                {
                    _peers.Add(peer);

                    if (PeerCountUpdate != null)
                        RaiseEventPeerCountUpdate();
                }
            }
        }

        internal void AddSeeder(BitChat.Peer seeder)
        {
            //remove from peers
            lock (_peers)
            {
                _peers.Remove(seeder);
            }

            //add seeder
            lock (_seeders)
            {
                if (!_seeders.Contains(seeder))
                {
                    _seeders.Add(seeder);

                    if (PeerCountUpdate != null)
                        RaiseEventPeerCountUpdate();
                }
            }
        }

        internal void RemoveChat(BitChat chat)
        {
            //announce no participation in chat
            byte[] packetData = BitChatMessage.CreateFileUnparticipate(_metaData.FileID);
            chat.WriteMessageBroadcast(packetData, 0, packetData.Length);

            //remove chat from list
            lock (_chats)
            {
                _chats.Remove(chat);

                if (_chats.Count == 0)
                {
                    lock (_sharedFiles)
                    {
                        _sharedFiles.Remove(_metaData.FileID);
                    }

                    this.Dispose();
                }
            }
        }

        internal void RemovePeerOrSeeder(BitChat.Peer peer)
        {
            bool peerRemoved = false;

            //remove from peers
            lock (_peers)
            {
                if (_peers.Remove(peer))
                    peerRemoved = true;
            }

            //remove from seeds
            lock (_seeders)
            {
                if (_seeders.Remove(peer))
                    peerRemoved = true;
            }

            //remove from downloading blocks
            if (_downloadingBlocks != null)
            {
                lock (_downloadingBlocks)
                {
                    foreach (FileBlockDownloadManager download in _downloadingBlocks)
                    {
                        download.RemoveDownloadPeer(peer);
                    }
                }
            }

            if (peerRemoved)
            {
                if (PeerCountUpdate != null)
                    RaiseEventPeerCountUpdate();
            }
        }

        internal bool PeerExists(BitChat.Peer peer)
        {
            lock (_seeders)
            {
                if (_seeders.Contains(peer))
                    return true;
            }

            lock (_peers)
            {
                return _peers.Contains(peer);
            }
        }

        private void StartSharing()
        {
            if (_state != SharedFileState.Sharing)
            {
                _state = SharedFileState.Sharing;

                //send advertisement
                SendFileAdvertisement();

                //start file transfer speed calculator
                _fileTransferSpeedCalculator = new Timer(FileTransferSpeedCalculatorAsync, null, 1000, 1000);

                if (FileSharingStarted != null)
                    RaiseEventFileSharingStarted();
            }
        }

        private void StartDownload()
        {
            if (_state != SharedFileState.Downloading)
            {
                _state = SharedFileState.Downloading;

                //get file
                if (_fileStream == null)
                {
                    BitChatProfile profile;

                    lock (_chats)
                    {
                        profile = _chats[0].Profile;
                    }

                    //find file name
                    string filePath = Path.Combine(profile.DownloadFolder, _metaData.FileName);

                    int nameCount = 0;

                    while (File.Exists(filePath))
                    {
                        if (nameCount > 10000)
                            throw new BitChatException("Cannot download file as local file name not available.");

                        nameCount++;
                        filePath = Path.Combine(profile.DownloadFolder, Path.GetFileNameWithoutExtension(_metaData.FileName) + " [" + nameCount + "]" + Path.GetExtension(_metaData.FileName));
                    }

                    //create file
                    _fileStream = new FileStream(filePath + ".downloading", FileMode.Create, FileAccess.ReadWrite);

                    //pre-allocate file
                    _fileStream.SetLength(_metaData.FileSize);

                    //init block available
                    _blockAvailable = new FileBlockState[_blockAvailable.Length];
                    _availableBlocksCount = 0;
                }

                //list all pending blocks
                if (_pendingBlocks == null)
                {
                    _pendingBlocks = new List<int>(_blockAvailable.Length);
                    _rndPendingBlock = new Random(DateTime.UtcNow.Second);

                    for (int i = 0; i < _blockAvailable.Length; i++)
                    {
                        if (_blockAvailable[i] != FileBlockState.Available)
                            _pendingBlocks.Add(i);
                    }
                }

                //announce file sharing participation in chats
                SendFileShareParticipate();

                //start block download
                _downloadingBlocks = new List<FileBlockDownloadManager>(MAX_DOWNLOADING_BLOCKS);

                lock (_downloadingBlocks)
                {
                    for (int i = 0; i < MAX_DOWNLOADING_BLOCKS; i++)
                    {
                        FileBlockDownloadManager download = GetRandomDownloadBlock();

                        if (download == null)
                            break;

                        _downloadingBlocks.Add(download);
                    }
                }

                //start file transfer speed calculator
                _fileTransferSpeedCalculator = new Timer(FileTransferSpeedCalculatorAsync, null, 1000, 1000);

                //start download monitor
                _downloadMonitor = new Timer(DownloadMonitorAsync, null, DOWNLOAD_MONITOR_INTERVAL, Timeout.Infinite);

                if (FileDownloadStarted != null)
                    RaiseEventFileDownloadStarted();
            }
        }

        private void StopTimers()
        {
            //stop timers
            if (_fileTransferSpeedCalculator != null)
            {
                _fileTransferSpeedCalculator.Dispose();
                _fileTransferSpeedCalculator = null;
            }

            //stop download monitor
            if (_downloadMonitor != null)
            {
                _downloadMonitor.Dispose();
                _downloadMonitor = null;
            }
        }

        private void FileTransferSpeedCalculatorAsync(object state)
        {
            try
            {
                if (_state == SharedFileState.Paused)
                {
                    _fileTransferSpeedCalculator.Dispose();
                    _fileTransferSpeedCalculator = null;
                    return;
                }

                if (FileTransferSpeedUpdate != null)
                    RaiseEventFileTransferSpeedUpdate();

                Interlocked.Exchange(ref _bytesDownloadedLastSecond, 0);
                Interlocked.Exchange(ref _bytesUploadedLastSecond, 0);
            }
            catch (ThreadAbortException)
            { }
            catch
            { }
        }

        private void DownloadMonitorAsync(object state)
        {
            try
            {
                if (_state == SharedFileState.Paused)
                {
                    _downloadMonitor = null;
                    return;
                }

                lock (_downloadingBlocks)
                {
                    bool addAnotherBlockForDownload = false;

                    List<FileBlockDownloadManager> inactiveDownloadBlocks = new List<FileBlockDownloadManager>();

                    foreach (FileBlockDownloadManager download in _downloadingBlocks)
                    {
                        if ((DateTime.UtcNow - download.LastResponse).TotalSeconds > DOWNLOAD_INACTIVE_INTERVAL_SECONDS)
                        {
                            if (!download.IsDownloadPeerSet())
                            {
                                //add to inactive list
                                inactiveDownloadBlocks.Add(download);

                                //add another random block if available to mitigate case when all current blocks peer are not responding
                                addAnotherBlockForDownload = true;
                            }

                            //remove current peer as he is not responding and set last response time
                            download.SetDownloadPeer(null);

                            //re-announce block requirement
                            AnnounceBlockWanted(download.BlockNumber);
                        }
                    }

                    //add another random block if available to mitigate case when all current blocks peer are not responding
                    //this will allow downloading other data blocks from other peers when current block peers not responding
                    if (addAnotherBlockForDownload)
                    {
                        FileBlockDownloadManager download = GetRandomDownloadBlock();

                        if (download != null)
                            _downloadingBlocks.Add(download);
                    }

                    //remove extra inactive blocks to maintain total x 2 numbe of blocks
                    //this will prevent memory getting filled with large number of inactive blocks

                    if (_downloadingBlocks.Count > MAX_DOWNLOADING_BLOCKS * 2)
                    {
                        foreach (FileBlockDownloadManager downloadBlock in inactiveDownloadBlocks)
                        {
                            _downloadingBlocks.Remove(downloadBlock);

                            if (_downloadingBlocks.Count <= MAX_DOWNLOADING_BLOCKS * 2)
                                break;
                        }
                    }
                }
            }
            catch (ThreadAbortException)
            {
                _downloadMonitor = null;
            }
            catch
            { }
            finally
            {
                if (_downloadMonitor != null)
                    _downloadMonitor.Change(DOWNLOAD_MONITOR_INTERVAL, Timeout.Infinite);
            }
        }

        private FileBlockDownloadManager GetRandomDownloadBlock()
        {
            if (_state == SharedFileState.Paused)
                return null;

            int blockNumber;

            lock (_pendingBlocks)
            {
                if (_pendingBlocks.Count == 0)
                    return null;

                int pendingIndex = _rndPendingBlock.Next(0, _pendingBlocks.Count);
                blockNumber = _pendingBlocks[pendingIndex];
                _pendingBlocks.RemoveAt(pendingIndex);
            }

            //calculate block length
            long offset = blockNumber * _metaData.BlockSize;
            int length = _metaData.BlockSize;

            if ((offset + length) > _metaData.FileSize)
                length = Convert.ToInt32(_metaData.FileSize - offset);

            //announce block requirement
            AnnounceBlockWanted(blockNumber);

            return new FileBlockDownloadManager(this, blockNumber, length);
        }

        private void AnnounceBlockWanted(int blockNumber)
        {
            byte[] packetData = BitChatMessage.CreateFileBlockWanted(new FileBlockInfo(_metaData.FileID, blockNumber));

            //announce to all peers
            lock (_peers)
            {
                foreach (BitChat.Peer peer in _peers)
                    peer.WriteMessage(packetData);
            }

            //announce to all seeds
            lock (_seeders)
            {
                foreach (BitChat.Peer seeder in _seeders)
                    seeder.WriteMessage(packetData);
            }
        }

        internal bool IsBlockAvailable(int blockNumber)
        {
            return _blockAvailable[blockNumber] == FileBlockState.Available;
        }

        internal FileBlockDownloadManager GetDownloadingBlock(int blockNumber)
        {
            lock (_downloadingBlocks)
            {
                foreach (FileBlockDownloadManager download in _downloadingBlocks)
                {
                    if (download.BlockNumber == blockNumber)
                        return download;
                }

                return null;
            }
        }

        private void OnBlockDownloaded(FileBlockDownloadManager downloadedBlock)
        {
            lock (_downloadingBlocks)
            {
                if (_downloadingBlocks.Remove(downloadedBlock))
                {
                    if (WriteBlock(downloadedBlock))
                    {
                        //block downloaded
                        if (FileBlockDownloaded != null)
                            RaiseEventFileBlockDownloaded();
                    }
                    else
                    {
                        //block download fail/corrupt; add block again in pending list
                        lock (_pendingBlocks)
                        {
                            _pendingBlocks.Add(downloadedBlock.BlockNumber);
                        }
                    }

                    //start new block download
                    FileBlockDownloadManager newDownload = GetRandomDownloadBlock();
                    if (newDownload != null)
                        _downloadingBlocks.Add(newDownload);

                    if (_downloadingBlocks.Count == 0)
                    {
                        //download COMPLETED!

                        //set variables
                        _isComplete = true;
                        _state = SharedFileState.Sharing;
                        _availableBlocksCount = _blockAvailable.Length;

                        //stop download monitor
                        _downloadMonitor.Dispose();
                        _downloadMonitor = null;

                        //rename and open file again in read shared mode
                        string filePath = _fileStream.Name;

                        _fileStream.Close();
                        File.SetLastWriteTimeUtc(filePath, _metaData.LastModified);

                        string newFilePath = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
                        File.Move(filePath, newFilePath);

                        _fileStream = new FileStream(newFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                        //remove seeders
                        lock (_seeders)
                        {
                            _seeders.Clear();
                        }

                        //announce advertisement for the complete file to all chats available with same file
                        SendFileAdvertisement();

                        //notify event to UI
                        if (FileDownloaded != null)
                            RaiseEventFileDownloaded();
                    }
                }
            }
        }

        internal FileBlockDataPart ReadBlock(int blockNumber, int blockOffset, ushort length)
        {
            if ((blockNumber >= _blockAvailable.Length) || (blockNumber < 0))
                throw new IndexOutOfRangeException("Invalid block number.");

            if (_blockAvailable[blockNumber] != FileBlockState.Available)
                throw new ArgumentException("Block is not available to read.");


            long offset = blockNumber * _metaData.BlockSize + blockOffset;

            if ((length < 1) || (length > MIN_BLOCK_SIZE))
                length = MIN_BLOCK_SIZE;

            if (length > _metaData.BlockSize)
                length = Convert.ToUInt16(_metaData.BlockSize);

            if ((offset + length) > _metaData.FileSize)
                length = Convert.ToUInt16(_metaData.FileSize - offset);

            byte[] buffer = new byte[length];

            lock (_fileStream)
            {
                _fileStream.Position = offset;
                _fileStream.Read(buffer, 0, buffer.Length);
            }

            Interlocked.Add(ref _bytesUploadedLastSecond, length); //add bytes uploaded for transfer speed calc

            return new FileBlockDataPart(_metaData.FileID, blockNumber, blockOffset, length, buffer);
        }

        private bool WriteBlock(FileBlockDownloadManager downloadedBlock)
        {
            int blockNumber = downloadedBlock.BlockNumber;

            lock (_blockAvailable)
            {
                if (_blockAvailable[blockNumber] == FileBlockState.Available)
                    return true;

                byte[] blockData = downloadedBlock.BlockData;
                byte[] actualHash = _metaData.BlockHash[blockNumber];
                byte[] computedHash = _metaData.ComputeBlockHash(blockData);

                for (int i = 0; i < actualHash.Length; i++)
                {
                    if (actualHash[i] != computedHash[i])
                        return false;
                }

                lock (_fileStream)
                {
                    _fileStream.Position = blockNumber * _metaData.BlockSize;
                    _fileStream.Write(blockData, 0, blockData.Length);
                }

                _blockAvailable[blockNumber] = FileBlockState.Available;
                _availableBlocksCount++;
            }

            Interlocked.Add(ref _bytesDownloadedLastSecond, downloadedBlock.BlockData.Length); //add bytes downloaded for transfer speed calc

            return true;
        }

        private void SendFileAdvertisement()
        {
            byte[] packetData = BitChatMessage.CreateFileAdvertisement(_metaData);

            lock (_chats)
            {
                foreach (BitChat chat in _chats)
                    chat.WriteMessageBroadcast(packetData, 0, packetData.Length);
            }
        }

        private void SendFileShareParticipate()
        {
            byte[] packetData = BitChatMessage.CreateFileParticipate(_metaData.FileID);

            lock (_chats)
            {
                foreach (BitChat chat in _chats)
                    chat.WriteMessageBroadcast(packetData, 0, packetData.Length);
            }
        }

        private void SendFileShareUnparticipate()
        {
            byte[] packetData = BitChatMessage.CreateFileUnparticipate(_metaData.FileID);

            lock (_chats)
            {
                foreach (BitChat chat in _chats)
                    chat.WriteMessageBroadcast(packetData, 0, packetData.Length);
            }
        }

        #endregion

        #region public

        public void Start()
        {
            if ((_state == SharedFileState.Paused) || (_state == SharedFileState.Advertisement))
            {
                if (_isComplete)
                    StartSharing();
                else
                    StartDownload();
            }
        }

        public void Pause()
        {
            if (_state != SharedFileState.Paused)
            {
                _state = SharedFileState.Paused;

                StopTimers();

                //announce no participation in chats
                SendFileShareUnparticipate();

                if (FilePaused != null)
                    RaiseEventFilePaused();
            }
        }

        #endregion

        #region properties

        public string FilePath
        { get { return _fileStream.Name; } }

        public SharedFileMetaData MetaData
        { get { return _metaData; } }

        public bool IsComplete
        { get { return _isComplete; } }

        public SharedFileState State
        { get { return _state; } }

        public int TotalPeers
        { get { return _peers.Count + _seeders.Count; } }

        public int PercentComplete
        { get { return (_availableBlocksCount * 100) / _blockAvailable.Length; } }

        public int BytesDownloadedLastSecond
        { get { return _bytesDownloadedLastSecond; } }

        public int BytesUploadedLastSecond
        { get { return _bytesUploadedLastSecond; } }

        #endregion

        internal class FileBlockDownloadManager
        {
            #region variables

            SharedFile _sharedFile;
            int _blockNumber;
            byte[] _blockData;

            DateTime _lastResponse;
            BitChat.Peer _peer;
            object peerLock = new object();
            int _position = 0;

            #endregion

            #region constructor

            public FileBlockDownloadManager(SharedFile sharedFile, int blockNumber, int blockSize)
            {
                _sharedFile = sharedFile;
                _blockNumber = blockNumber;
                _blockData = new byte[blockSize];

                _lastResponse = DateTime.UtcNow;
            }

            #endregion

            #region public

            public bool SetDownloadPeer(BitChat.Peer peer)
            {
                lock (peerLock)
                {
                    if ((_peer == null) || (peer == null))
                    {
                        _peer = peer;
                        _lastResponse = DateTime.UtcNow;
                        return true;
                    }
                }

                return false;
            }

            public bool IsDownloadPeerSet()
            {
                lock (peerLock)
                {
                    return (_peer != null);
                }
            }

            public bool IsThisDownloadPeerSet(BitChat.Peer fromPeer)
            {
                lock (peerLock)
                {
                    return (_peer != null) && (fromPeer.Equals(_peer));
                }
            }

            public void RemoveDownloadPeer(BitChat.Peer peer)
            {
                lock (peerLock)
                {
                    if ((_peer != null) && (peer.Equals(_peer)))
                        _peer = null;
                }
            }

            public bool SetBlockData(FileBlockDataPart blockData)
            {
                if (_position != blockData.BlockOffset)
                    throw new BitChatException("Invalid data offset received from peer.");

                Buffer.BlockCopy(blockData.BlockDataPart, 0, _blockData, blockData.BlockOffset, blockData.Length);

                _position = blockData.BlockOffset + blockData.Length;
                _lastResponse = DateTime.UtcNow;

                if (_position == _blockData.Length)
                {
                    _sharedFile.OnBlockDownloaded(this);

                    return true;
                }

                return false;
            }

            public FileBlockRequest GetNextRequest()
            {
                int length = _blockData.Length - _position;

                if (length == 0)
                    throw new BitChatException("No block data required to request.");

                if (length > SharedFile.MIN_BLOCK_SIZE)
                    length = SharedFile.MIN_BLOCK_SIZE;

                return new FileBlockRequest(_sharedFile._metaData.FileID, _blockNumber, _position, Convert.ToUInt16(length));
            }

            public override bool Equals(object obj)
            {
                if (base.Equals(obj))
                    return true;

                FileBlockDownloadManager o = obj as FileBlockDownloadManager;
                if (o == null)
                    return false;

                return (o._blockNumber == this._blockNumber);
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            #endregion

            #region properties

            public int BlockNumber
            { get { return _blockNumber; } }

            public byte[] BlockData
            { get { return _blockData; } }

            public DateTime LastResponse
            { get { return _lastResponse; } }

            #endregion
        }
    }
}
