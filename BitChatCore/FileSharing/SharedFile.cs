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

        readonly static Dictionary<BinaryNumber, SharedFile> _sharedFiles = new Dictionary<BinaryNumber, SharedFile>();

        const int BUFFER_SIZE = 63488; //62kb data transfer buffer size
        const int FILE_BLOCK_DOWNLOAD_THREADS = 3; //total downloading threads per file
        const int FILE_BLOCK_MINIMUM_SIZE = 524288; //512kb min block size
        const byte FILE_BLOCK_NOT_AVAILABLE = 0;
        const byte FILE_BLOCK_AVAILABLE = 1;

        readonly SynchronizationContext _syncCxt;

        //shared file input
        FileStream _fileStream;
        readonly SharedFileMetaData _metaData;

        SharedFileState _state;
        byte[] _blockAvailable;
        int _availableBlocksCount;
        bool _isComplete;

        //downloading task variables
        List<int> _pendingBlocks;
        Random _rndPendingBlock;
        List<FileBlockDownloader> _downloaders;

        //transfer speed
        Timer _fileTransferSpeedCalculator;
        int _bytesDownloadedLastSecond = 0;
        int _bytesUploadedLastSecond = 0;

        //peers
        readonly List<BitChatNetwork.VirtualPeer.VirtualSession> _peers = new List<BitChatNetwork.VirtualPeer.VirtualSession>();
        readonly ReaderWriterLockSlim _peersLock = new ReaderWriterLockSlim();

        readonly List<BitChatNetwork.VirtualPeer.VirtualSession> _seeders = new List<BitChatNetwork.VirtualPeer.VirtualSession>();
        readonly ReaderWriterLockSlim _seedersLock = new ReaderWriterLockSlim();

        //chats
        readonly List<BitChat> _chats = new List<BitChat>();

        #endregion

        #region constructor

        private SharedFile(FileStream fS, SharedFileMetaData metaData, byte[] blockAvailable, int availableBlocksCount, SynchronizationContext syncCxt)
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
                Stop();

                if (_fileStream != null)
                    _fileStream.Dispose();

                _peersLock.Dispose();
                _seedersLock.Dispose();

                _disposed = true;
            }
        }

        #endregion

        #region static

        internal static SharedFile LoadFile(string filePath, SharedFileMetaData metaData, byte[] blockAvailable, SharedFileState state, SynchronizationContext syncCxt)
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
                        sharedFile = new SharedFile(null, metaData, new byte[metaData.BlockHash.Length], 0, syncCxt);
                        _sharedFiles.Add(metaData.FileID, sharedFile);

                        sharedFile._state = SharedFileState.Advertisement;
                    }
                    else
                    {
                        int availableBlocksCount = 0;

                        for (int i = 0; i < blockAvailable.Length; i++)
                        {
                            if (blockAvailable[i] == FILE_BLOCK_AVAILABLE)
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

                return sharedFile;
            }
        }

        internal static SharedFile ShareFile(string filePath, string hashAlgo, SynchronizationContext syncCxt)
        {
            FileStream fS = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            HashAlgorithm hash = HashAlgorithm.Create(hashAlgo);
            int hashSize = hash.HashSize / 8;

            //calculate block size
            int blockSize;
            {
                int totalBlocksPossible = BUFFER_SIZE / hashSize;
                blockSize = Convert.ToInt32(fS.Length / totalBlocksPossible);

                if (blockSize < FILE_BLOCK_MINIMUM_SIZE)
                {
                    blockSize = FILE_BLOCK_MINIMUM_SIZE;
                }
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
            byte[] blockAvailable = new byte[totalBlocks];

            //init
            for (int i = 0; i < totalBlocks; i++)
            {
                long offset = i * blockSize;
                long length = blockSize;

                if ((offset + length) > fS.Length)
                    length = fS.Length - offset;

                blockHash[i] = hash.ComputeHash(new OffsetStream(fS, offset, length));
                blockAvailable[i] = FILE_BLOCK_AVAILABLE;
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

                return sharedFile;
            }
        }

        internal static SharedFile PrepareDownloadFile(SharedFileMetaData metaData, SynchronizationContext syncCxt)
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
                    sharedFile = new SharedFile(null, metaData, new byte[metaData.BlockHash.Length], 0, syncCxt);
                    _sharedFiles.Add(metaData.FileID, sharedFile);
                }

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

        internal void AddPeer(BitChatNetwork.VirtualPeer.VirtualSession peer)
        {
            bool peerWasAdded = false;

            _peersLock.EnterWriteLock();
            try
            {
                if (!_peers.Contains(peer))
                {
                    _peers.Add(peer);
                    peerWasAdded = true;
                }
            }
            finally
            {
                _peersLock.ExitWriteLock();
            }

            if (peerWasAdded)
            {
                if (PeerCountUpdate != null)
                    RaiseEventPeerCountUpdate();
            }
        }

        internal void AddSeeder(BitChatNetwork.VirtualPeer.VirtualSession seeder)
        {
            //remove from peers
            _peersLock.EnterWriteLock();
            try
            {
                _peers.Remove(seeder);
            }
            finally
            {
                _peersLock.ExitWriteLock();
            }

            //add seeder
            bool seederWasAdded = false;

            _seedersLock.EnterWriteLock();
            try
            {
                if (!_seeders.Contains(seeder))
                {
                    _seeders.Add(seeder);
                    seederWasAdded = true;
                }
            }
            finally
            {
                _seedersLock.ExitWriteLock();
            }

            if (seederWasAdded)
            {
                if (PeerCountUpdate != null)
                    RaiseEventPeerCountUpdate();
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

        internal void RemovePeerOrSeeder(BitChatNetwork.VirtualPeer.VirtualSession peer)
        {
            bool peerRemoved = false;

            //remove from peers
            _peersLock.EnterWriteLock();
            try
            {
                if (_peers.Remove(peer))
                    peerRemoved = true;
            }
            finally
            {
                _peersLock.ExitWriteLock();
            }

            //remove from seeds
            _seedersLock.EnterWriteLock();
            try
            {
                if (_seeders.Remove(peer))
                    peerRemoved = true;
            }
            finally
            {
                _seedersLock.ExitWriteLock();
            }

            if (peerRemoved)
            {
                if (PeerCountUpdate != null)
                    RaiseEventPeerCountUpdate();
            }
        }

        internal bool PeerExists(BitChatNetwork.VirtualPeer.VirtualSession peer)
        {
            _seedersLock.EnterReadLock();
            try
            {
                if (_seeders.Contains(peer))
                    return true;
            }
            finally
            {
                _seedersLock.ExitReadLock();
            }

            _peersLock.EnterReadLock();
            try
            {
                return _peers.Contains(peer);
            }
            finally
            {
                _peersLock.ExitReadLock();
            }
        }

        private void StartSharing()
        {
            if (_state != SharedFileState.Sharing)
            {
                _state = SharedFileState.Sharing;

                //send advertisement
                SendBroadcastFileAdvertisement();

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
                    _blockAvailable = new byte[_blockAvailable.Length];
                    _availableBlocksCount = 0;
                }

                //list all pending blocks
                if (_pendingBlocks == null)
                {
                    _pendingBlocks = new List<int>(_blockAvailable.Length);
                    _rndPendingBlock = new Random(DateTime.UtcNow.Second);
                }
                else
                {
                    _pendingBlocks.Clear();
                }

                for (int i = 0; i < _blockAvailable.Length; i++)
                {
                    if (_blockAvailable[i] == FILE_BLOCK_NOT_AVAILABLE)
                        _pendingBlocks.Add(i);
                }

                if (_pendingBlocks.Count > 0)
                {
                    //announce file sharing participation in chats
                    SendBroadcastFileShareParticipate();

                    //start block download
                    int totalDownloaders = FILE_BLOCK_DOWNLOAD_THREADS;

                    if (_pendingBlocks.Count < totalDownloaders)
                        totalDownloaders = _pendingBlocks.Count;

                    _downloaders = new List<FileBlockDownloader>(totalDownloaders);

                    lock (_downloaders)
                    {
                        for (int i = 0; i < totalDownloaders; i++)
                            _downloaders.Add(new FileBlockDownloader(this));
                    }

                    //start file transfer speed calculator
                    _fileTransferSpeedCalculator = new Timer(FileTransferSpeedCalculatorAsync, null, 1000, 1000);

                    if (FileDownloadStarted != null)
                        RaiseEventFileDownloadStarted();
                }
                else
                {
                    OnDownloadComplete();
                }
            }
        }

        private void Stop()
        {
            //stop timers
            if (_fileTransferSpeedCalculator != null)
            {
                _fileTransferSpeedCalculator.Dispose();
                _fileTransferSpeedCalculator = null;
            }

            if (_downloaders != null)
            {
                lock (_downloaders)
                {
                    foreach (FileBlockDownloader downloader in _downloaders)
                        downloader.Dispose();
                }
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
            catch
            { }
        }

        internal bool IsBlockAvailable(int blockNumber)
        {
            return _blockAvailable[blockNumber] == FILE_BLOCK_AVAILABLE;
        }

        private int GetNextDownloadBlockNumber()
        {
            int blockNumber;

            lock (_pendingBlocks)
            {
                if (_pendingBlocks.Count == 0)
                    return -1;

                int pendingIndex = _rndPendingBlock.Next(0, _pendingBlocks.Count);
                blockNumber = _pendingBlocks[pendingIndex];
                _pendingBlocks.RemoveAt(pendingIndex);
            }

            return blockNumber;
        }

        private void AddPendingDownloadBlockNumber(int blockNumber)
        {
            lock (_pendingBlocks)
            {
                _pendingBlocks.Add(blockNumber);
            }
        }

        internal void BlockAvailable(int blockNumber, BitChatNetwork.VirtualPeer.VirtualSession peerSession)
        {
            FileBlockDownloader downloader = null;

            lock (_downloaders)
            {
                foreach (FileBlockDownloader item in _downloaders)
                {
                    if (item.BlockNumber == blockNumber)
                    {
                        downloader = item;
                        break;
                    }
                }
            }

            if (downloader != null)
                downloader.BlockAvailable(blockNumber, peerSession);
        }

        private bool DownloadBlock(int blockNumber, Stream dataStream)
        {
            if (_blockAvailable[blockNumber] == FILE_BLOCK_AVAILABLE)
                return true;

            byte[] buffer = new byte[BUFFER_SIZE];
            HashAlgorithm hash = HashAlgorithm.Create(_metaData.HashAlgo);
            int bytesRead;
            int totalBytesRead = 0;
            long offset = blockNumber * _metaData.BlockSize;
            long length = _metaData.BlockSize;
            int bytesToRead = BUFFER_SIZE;

            if ((offset + length) > _metaData.FileSize)
                length = _metaData.FileSize - offset;

            while (length > 0)
            {
                if (length < bytesToRead)
                    bytesToRead = (int)length;

                bytesRead = dataStream.Read(buffer, 0, bytesToRead);
                if (bytesRead < 1)
                    break;

                hash.TransformBlock(buffer, 0, bytesRead, buffer, 0);

                lock (_fileStream)
                {
                    _fileStream.Position = blockNumber * _metaData.BlockSize + totalBytesRead;
                    _fileStream.Write(buffer, 0, bytesRead);
                }

                totalBytesRead += bytesRead;
                length -= bytesRead;

                Interlocked.Add(ref _bytesDownloadedLastSecond, bytesRead); //add bytes downloaded for transfer speed calc
            }

            hash.TransformFinalBlock(buffer, 0, 0);
            byte[] computedHash = hash.Hash;
            byte[] actualHash = _metaData.BlockHash[blockNumber];

            for (int i = 0; i < actualHash.Length; i++)
            {
                if (actualHash[i] != computedHash[i])
                    return false;
            }

            _blockAvailable[blockNumber] = FILE_BLOCK_AVAILABLE;
            _availableBlocksCount++;

            if (FileBlockDownloaded != null)
                RaiseEventFileBlockDownloaded();

            return true;
        }

        internal void TransferBlock(int blockNumber, Stream dataStream)
        {
            if ((blockNumber >= _blockAvailable.Length) || (blockNumber < 0))
                throw new ArgumentException("Invalid block number.");

            if (_blockAvailable[blockNumber] == FILE_BLOCK_NOT_AVAILABLE)
                throw new ArgumentException("Block is not available to read.");

            byte[] buffer = new byte[BUFFER_SIZE];
            long offset = blockNumber * _metaData.BlockSize;
            long length = _metaData.BlockSize;
            long totalBytesRead = 0;
            int bytesRead;
            int bytesToRead = BUFFER_SIZE;

            if ((offset + length) > _metaData.FileSize)
                length = _metaData.FileSize - offset;

            while (length > 0)
            {
                if (length < bytesToRead)
                    bytesToRead = (int)length;

                lock (_fileStream)
                {
                    _fileStream.Position = offset + totalBytesRead;
                    bytesRead = _fileStream.Read(buffer, 0, bytesToRead);
                }

                dataStream.Write(buffer, 0, bytesRead);

                totalBytesRead += bytesRead;
                length -= bytesRead;

                Interlocked.Add(ref _bytesUploadedLastSecond, bytesRead); //add bytes uploaded for transfer speed calc
            }
        }

        private void SendBroadcastFileAdvertisement()
        {
            byte[] packetData = BitChatMessage.CreateFileAdvertisement(_metaData);

            lock (_chats)
            {
                foreach (BitChat chat in _chats)
                    chat.WriteMessageBroadcast(packetData, 0, packetData.Length);
            }
        }

        private void SendBroadcastFileShareParticipate()
        {
            byte[] packetData = BitChatMessage.CreateFileParticipate(_metaData.FileID);

            lock (_chats)
            {
                foreach (BitChat chat in _chats)
                    chat.WriteMessageBroadcast(packetData, 0, packetData.Length);
            }
        }

        private void SendBroadcastFileShareUnparticipate()
        {
            byte[] packetData = BitChatMessage.CreateFileUnparticipate(_metaData.FileID);

            lock (_chats)
            {
                foreach (BitChat chat in _chats)
                    chat.WriteMessageBroadcast(packetData, 0, packetData.Length);
            }
        }

        private void SendAnnouncementBlockWanted(BinaryNumber fileID, int blockNumber)
        {
            byte[] packetData = BitChatMessage.CreateFileBlockWanted(fileID, blockNumber);

            //announce to all peers
            _peersLock.EnterReadLock();
            try
            {
                foreach (BitChatNetwork.VirtualPeer.VirtualSession peer in _peers)
                    peer.WriteMessage(packetData, 0, packetData.Length);
            }
            finally
            {
                _peersLock.ExitReadLock();
            }

            //announce to all seeds
            _seedersLock.EnterReadLock();
            try
            {
                foreach (BitChatNetwork.VirtualPeer.VirtualSession seeder in _seeders)
                    seeder.WriteMessage(packetData, 0, packetData.Length);
            }
            finally
            {
                _seedersLock.ExitReadLock();
            }
        }

        private void OnDownloadComplete()
        {
            //verify if all blocks are available
            for (int i = 0; i < _blockAvailable.Length; i++)
            {
                if (_blockAvailable[i] == FILE_BLOCK_NOT_AVAILABLE)
                    return; //found a pending block; do nothing
            }

            //set variables
            _isComplete = true;
            _state = SharedFileState.Sharing;
            _availableBlocksCount = _blockAvailable.Length;

            //rename and open file again in read shared mode
            string filePath = _fileStream.Name;

            _fileStream.Close();
            File.SetLastWriteTimeUtc(filePath, _metaData.LastModified);

            string newFilePath = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
            File.Move(filePath, newFilePath);

            _fileStream = new FileStream(newFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            //remove seeders
            _seedersLock.EnterWriteLock();
            try
            {
                _seeders.Clear();
            }
            finally
            {
                _seedersLock.ExitWriteLock();
            }

            //announce advertisement for the complete file to all chats available with same file
            SendBroadcastFileAdvertisement();

            //notify event to UI
            if (FileDownloaded != null)
                RaiseEventFileDownloaded();
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

                Stop();

                //announce no participation in chats
                SendBroadcastFileShareUnparticipate();

                if (FilePaused != null)
                    RaiseEventFilePaused();
            }
        }

        public BitChat[] GetChatList()
        {
            lock (_chats)
            {
                return _chats.ToArray();
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

        class FileBlockDownloader : IDisposable
        {
            #region variables

            readonly SharedFile _sharedFile;
            readonly List<FileBlockDownloader> _downloaders;

            readonly Thread _thread;
            readonly object _waitLock = new object();

            int _blockNumber;
            BitChatNetwork.VirtualPeer.VirtualSession _peerSession;

            #endregion

            #region constructor

            public FileBlockDownloader(SharedFile sharedFile)
            {
                _sharedFile = sharedFile;
                _downloaders = _sharedFile._downloaders;

                _thread = new Thread(DownloadAsync);
                _thread.IsBackground = true;
                _thread.Start();
            }

            #endregion

            #region IDisposable

            ~FileBlockDownloader()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            bool _disposed = false;

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (_thread != null)
                        _thread.Abort();

                    _disposed = true;
                }
            }

            #endregion

            #region private

            private void DownloadAsync()
            {
                try
                {
                    while (true)
                    {
                        BitChatNetwork.VirtualPeer.VirtualSession peerSession = null;

                        lock (_waitLock)
                        {
                            _blockNumber = _sharedFile.GetNextDownloadBlockNumber();
                            if (_blockNumber < 0)
                                return;

                            _sharedFile.SendAnnouncementBlockWanted(_sharedFile._metaData.FileID, _blockNumber);

                            if (Monitor.Wait(_waitLock, 30000))
                                peerSession = _peerSession;
                        }

                        if (peerSession != null)
                        {
                            BitChatNetwork.VirtualPeer.VirtualSession.DataStream dataStream = null;
                            try
                            {
                                //open data stream on peer session
                                dataStream = peerSession.OpenDataStream();

                                //send file block request to peer
                                byte[] packetData = BitChatMessage.CreateFileBlockRequest(_sharedFile._metaData.FileID, _blockNumber, dataStream.Port);
                                peerSession.WriteMessage(packetData, 0, packetData.Length);

                                //download block data via stream
                                _sharedFile.DownloadBlock(_blockNumber, dataStream);
                            }
                            catch
                            {
                                //add the block number back to pending list
                                _sharedFile.AddPendingDownloadBlockNumber(_blockNumber);
                            }
                            finally
                            {
                                if (dataStream != null)
                                    dataStream.Dispose();
                            }
                        }
                        else
                        {
                            //add the block number back to pending list and continue for next random block
                            _sharedFile.AddPendingDownloadBlockNumber(_blockNumber);
                        }
                    }
                }
                catch (ThreadAbortException)
                { }
                catch (Exception ex)
                {
                    Debug.Write("SharedFile.FileBlockDownloader", ex);
                }
                finally
                {
                    lock (_downloaders)
                    {
                        _downloaders.Remove(this);

                        if (_downloaders.Count < 1)
                        {
                            if (_sharedFile.GetNextDownloadBlockNumber() < 0)
                                _sharedFile.OnDownloadComplete();
                        }
                    }
                }
            }

            #endregion

            #region public

            public void BlockAvailable(int blockNumber, BitChatNetwork.VirtualPeer.VirtualSession peerSession)
            {
                lock (_waitLock)
                {
                    if (_blockNumber == blockNumber)
                    {
                        _peerSession = peerSession;
                        Monitor.Pulse(_waitLock);
                    }
                }
            }

            #endregion

            #region properties

            public int BlockNumber
            { get { return _blockNumber; } }

            #endregion
        }
    }
}
