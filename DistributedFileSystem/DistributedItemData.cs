using NetworkCommsDotNet;
using NetworkCommsDotNet.Tools;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DistributedFileSystem
{
    /// <summary>
    /// Wrapper used to segment a DFS item data into chunks
    /// </summary>
    public struct PositionLength
    {
        /// <summary>
        /// The start position in bytes of this chunk
        /// </summary>
        public int Position;

        /// <summary>
        /// The number of bytes of this chunk
        /// </summary>
        public int Length;

        /// <summary>
        /// Initialise a new PositionLength struct
        /// </summary>
        /// <param name="position">The start position in bytes of this chunk</param>
        /// <param name="length">The number of bytes of this chunk</param>
        public PositionLength(int position, int length)
        {
            Position = position;
            Length = length;
        }
    }

    /// <summary>
    /// Manages the respective data for a distributed item
    /// </summary>
    [ProtoContract]
    public class DistributedItemData
    {
        /// <summary>
        /// The MD5 checksum for the completed data. Used to validate a completed build.
        /// </summary>
        [ProtoMember(1)]
        public string CompleteDataCheckSum { get; private set; } 

        /// <summary>
        /// Optional MD5 checksums for individual chunks. Useful for debugging build issues.
        /// </summary>
        [ProtoMember(2)]
        public string[] ChunkCheckSums { get; private set; } 

        /// <summary>
        /// Total number of chunks for this item
        /// </summary>
        [ProtoMember(3)]
        public byte TotalNumChunks { get; private set; } 

        /// <summary>
        /// Maximum size of each chunk in bytes. The final chunk may be less than this value.
        /// </summary>
        [ProtoMember(4)]
        public int ChunkSizeInBytes { get; private set; } 

        /// <summary>
        /// Total item size in bytes.
        /// </summary>
        [ProtoMember(5)]
        public long ItemBytesLength { get; private set; }

        /// <summary>
        /// The build mode describing how the item should be built, i.e. memory or disk, as a single stream of multiple blocks
        /// </summary>
        [ProtoMember(6)]
        public DataBuildMode DataBuildMode { get; private set; }

        /// <summary>
        /// The chunk positions and lengths. Key is chunkIndex.
        /// </summary>
        public Dictionary<int, PositionLength> ChunkPositionLengthDict { get; private set; }

        /// <summary>
        /// The stream containing the item chunk data
        /// </summary>
        StreamTools.ThreadSafeStream CompleteDataStream { get; set; }

        /// <summary>
        /// An array of streams that contain the item chunk data. Each index is a matches the 
        /// corresponding chunk index.
        /// </summary>
        StreamTools.ThreadSafeStream[] ChunkDataStreams { get; set; }

        /// <summary>
        /// Private constructor for deserialisation
        /// </summary>
        private DistributedItemData() 
        {
            InitialiseChunkPositionLengthDict();
        }

        /// <summary>
        /// Initialise the item data using an existing stream. If the build mode is to blocks the itemDataStream is broken into chunks.
        /// </summary>
        /// <param name="itemIdentifier"></param>
        /// <param name="dataBuildMode"></param>
        /// <param name="itemDataStream"></param>
        /// <param name="enableChunkChecksum"></param>
        public DistributedItemData(string itemIdentifier, DataBuildMode dataBuildMode, Stream itemDataStream, bool enableChunkChecksum = false)
        {
            this.DataBuildMode = dataBuildMode;
            if (itemDataStream == null) throw new ArgumentNullException("itemDataStream", "itemDataStream cannot be null.");
            this.ItemBytesLength = itemDataStream.Length;

            //Calculate the exactChunkSize if we split everything up into 255 pieces
            double exactChunkSize = (double)ItemBytesLength / 255.0;

            //If the item is too small we just use the minimumChunkSize
            //If we need something larger than MinChunkSizeInBytes we select appropriately
            this.ChunkSizeInBytes = (exactChunkSize <= DFS.MinChunkSizeInBytes ? DFS.MinChunkSizeInBytes : (int)Math.Ceiling(exactChunkSize));
            this.TotalNumChunks = (byte)(Math.Ceiling((double)ItemBytesLength / (double)ChunkSizeInBytes));

            InitialiseChunkPositionLengthDict();

            SetData(itemIdentifier, itemDataStream);

            if (enableChunkChecksum) BuildChunkCheckSums();
        }

        /// <summary>
        /// Initialise the item data using existing chunk streams. Build mode must be to blocks.
        /// </summary>
        /// <param name="dataBuildMode"></param>
        /// <param name="itemDataStreams"></param>
        /// <param name="enableChunkChecksum"></param>
        public DistributedItemData(DataBuildMode dataBuildMode, Dictionary<int, Stream> itemDataStreams, bool enableChunkChecksum = false)
        {
            this.DataBuildMode = DataBuildMode;
            
            if (itemDataStreams == null) throw new ArgumentNullException("itemDataStreams", "itemDataStreams cannot be null.");

            if (dataBuildMode == DataBuildMode.Disk_Single ||
                //itemBuildMode == ItemBuildMode.Both_Single ||
                dataBuildMode == DataBuildMode.Memory_Single)
                throw new ArgumentException("Please use other constructor that takes a single input data stream.");

            this.ItemBytesLength = itemDataStreams.Select(i => i.Value.Length).Sum();

            //Calculate the exactChunkSize if we split everything up into 255 pieces
            double exactChunkSize = (double)ItemBytesLength / 255.0;

            //If the item is too small we just use the minimumChunkSize
            //If we need something larger than MinChunkSizeInBytes we select appropriately
            this.ChunkSizeInBytes = (exactChunkSize <= DFS.MinChunkSizeInBytes ? DFS.MinChunkSizeInBytes : (int)Math.Ceiling(exactChunkSize));
            this.TotalNumChunks = (byte)(Math.Ceiling((double)ItemBytesLength / (double)ChunkSizeInBytes));

            InitialiseChunkPositionLengthDict();

            if (itemDataStreams.Count != ChunkPositionLengthDict.Count)
                throw new ArgumentException("Number of streams should equal the number of chunks");

            //Initialise the data streams
            ChunkDataStreams = new StreamTools.ThreadSafeStream[ChunkPositionLengthDict.Count];
            foreach (int chunkIndex in ChunkPositionLengthDict.Keys)
                ChunkDataStreams[chunkIndex] = new StreamTools.ThreadSafeStream(itemDataStreams[chunkIndex]);

            this.CompleteDataCheckSum = MD5();

            if (enableChunkChecksum) BuildChunkCheckSums();
        }

        /// <summary>
        /// Initialise the item data from an assembly config
        /// </summary>
        /// <param name="assemblyConfig"></param>
        public DistributedItemData(ItemAssemblyConfig assemblyConfig)
        {
            this.DataBuildMode = assemblyConfig.ItemBuildMode;
            this.TotalNumChunks = assemblyConfig.TotalNumChunks;
            this.ChunkSizeInBytes = assemblyConfig.ChunkSizeInBytes;
            this.CompleteDataCheckSum = assemblyConfig.CompleteDataCheckSum;
            this.ChunkCheckSums = assemblyConfig.ChunkCheckSums;
            this.ItemBytesLength = assemblyConfig.TotalItemSizeInBytes;

            InitialiseChunkPositionLengthDict();

            #region Build Internal Data Structure
            //Create the data stores as per the assembly config
            if (DataBuildMode == DataBuildMode.Memory_Single)
                //ItemBuildMode == ItemBuildMode.Both_Single ||)
            {
                MemoryStream itemStream = new MemoryStream(0);
                itemStream.SetLength(ItemBytesLength);
                CompleteDataStream = new StreamTools.ThreadSafeStream(itemStream);
            }
            else if (DataBuildMode == DataBuildMode.Disk_Single)
            {
                #region DiskSingle
                string folderLocation = "DFS_" + NetworkComms.NetworkIdentifier;
                string fileName = Path.Combine(folderLocation, assemblyConfig.ItemIdentifier + ".DFSItemData");
                FileStream fileStream;
                if (File.Exists(fileName))
                {
                    //If the file already exists the MD5 had better match otherwise we have a problem
                    try
                    {
                        fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                        if (StreamTools.MD5(fileStream) != assemblyConfig.CompleteDataCheckSum)
                            throw new Exception("Wrong place, wrong time, wrong file!");
                    }
                    catch (Exception)
                    {
                        try
                        {
                            File.Delete(fileName);
                            fileStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                        }
                        catch (Exception)
                        {
                            throw new Exception("File with name '" + fileName + "' already exists. Unfortunately the MD5 does match the expected DFS item. Unable to delete in order to continue.");
                        }
                    }
                }
                else
                {
                    lock (DFS.globalDFSLocker)
                    {
                        if (!Directory.Exists(folderLocation))
                            Directory.CreateDirectory(folderLocation);
                    }

                    fileStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                    fileStream.SetLength(ItemBytesLength);
                    fileStream.Flush();
                }

                CompleteDataStream = new StreamTools.ThreadSafeStream(fileStream);

                if (!File.Exists(fileName)) throw new Exception("At this point the item data file should have been created. This exception should not really be possible.");
                #endregion
            }
            else
            {
                #region Data Chunks
                //Break the itemDataStream into blocks
                ChunkDataStreams = new StreamTools.ThreadSafeStream[ChunkPositionLengthDict.Count];

                for (int i = 0; i < ChunkPositionLengthDict.Count; i++)
                {
                    //We now need to available the respective data into blocks
                    Stream destinationStream = null;
                    if (DataBuildMode == DataBuildMode.Disk_Blocks)
                    {
                        string folderLocation = "DFS_" + NetworkComms.NetworkIdentifier;
                        string fileName = Path.Combine(folderLocation, assemblyConfig.ItemIdentifier + ".DFSItemData_" + i.ToString());

                        if (File.Exists(fileName))
                        {
                            try
                            {
                                destinationStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                                if (assemblyConfig.ChunkCheckSums != null && StreamTools.MD5(destinationStream) != assemblyConfig.ChunkCheckSums[i])
                                    throw new Exception("Wrong place, wrong time, wrong file!");
                            }
                            catch (Exception)
                            {
                                try
                                {
                                    File.Delete(fileName);
                                    destinationStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 8192, FileOptions.DeleteOnClose);
                                }
                                catch (Exception)
                                {
                                    throw new Exception("File with name '" + fileName + "' already exists. Unfortunately the MD5 does match the expected DFS item. Unable to delete in order to continue.");
                                }
                            }
                        }
                        else
                        {
                            //Create the folder if it does not exist yet
                            lock (DFS.globalDFSLocker)
                            {
                                if (!Directory.Exists(folderLocation))
                                    Directory.CreateDirectory(folderLocation);
                            }

                            destinationStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 8192, FileOptions.DeleteOnClose);
                        }

                        if (!File.Exists(fileName)) throw new Exception("At this point the item data file should have been created. This exception should not really be possible.");
                    }
                    else
                        //If we are not exclusively building to the disk we just use a memory stream at this stage
                        destinationStream = new MemoryStream(ChunkPositionLengthDict[i].Length);

                    //Ensure we start at the beginning of the stream
                    destinationStream.SetLength(ChunkPositionLengthDict[i].Length);
                    destinationStream.Flush();
                    destinationStream.Seek(0, SeekOrigin.Begin);
                    ChunkDataStreams[i] = new StreamTools.ThreadSafeStream(destinationStream);
                }
                #endregion
            }
            #endregion
        }

        /// <summary>
        /// Sets the item data using the provided data stream. Useful for setting data after deserialisation
        /// </summary>
        /// <param name="itemIdentifier"></param>
        /// <param name="itemDataStream"></param>
        public void SetData(string itemIdentifier, Stream itemDataStream)
        {
            if (itemIdentifier == null) throw new ArgumentNullException("itemIdentifier");
            if (itemDataStream == null) throw new ArgumentNullException("itemDataStream");

            #region Build Internal Data Structure
            if (DataBuildMode == DataBuildMode.Disk_Single ||
                //ItemBuildMode == ItemBuildMode.Both_Single ||
                DataBuildMode == DataBuildMode.Memory_Single)
            {
                CompleteDataStream = new StreamTools.ThreadSafeStream(itemDataStream);
            }
            else
            {
                //Break the itemDataStream into blocks
                ChunkDataStreams = new StreamTools.ThreadSafeStream[ChunkPositionLengthDict.Count];

                //If the itemDataStream is a memory stream we can try to access the buffer as it makes creating the streams more efficient
                byte[] itemDataStreamBuffer = null;
                if (itemDataStream is MemoryStream)
                {
                    try
                    {
                        itemDataStreamBuffer = ((MemoryStream)itemDataStream).GetBuffer();
                    }
                    catch (UnauthorizedAccessException) { /* Ignore */ }
                }

                for (int i = 0; i < ChunkPositionLengthDict.Count; i++)
                {
                    if (itemDataStreamBuffer != null)
                        //This is the fastest way to create a block of data streams
                        ChunkDataStreams[i] = new StreamTools.ThreadSafeStream(new MemoryStream(itemDataStreamBuffer, ChunkPositionLengthDict[i].Position, ChunkPositionLengthDict[i].Length));
                    else
                    {
                        //We now need to available the respective data into blocks
                        Stream destinationStream = null;
                        if (DataBuildMode == DataBuildMode.Disk_Blocks)
                        {
                            string folderLocation = "DFS_" + NetworkComms.NetworkIdentifier;
                            string fileName = Path.Combine(folderLocation, itemIdentifier + ".DFSItemData_" + i.ToString());

                            if (File.Exists(fileName))
                            {
                                destinationStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                                //if (StreamTools.MD5(destinationStream) != checksum)
                                //    throw new Exception("Wrong place, wrong time, wrong file!");
                            }
                            else
                            {
                                //Create the folder if it does not exist yet
                                lock (DFS.globalDFSLocker)
                                {
                                    if (!Directory.Exists(folderLocation))
                                        Directory.CreateDirectory(folderLocation);
                                }

                                destinationStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 8192, FileOptions.DeleteOnClose);
                                destinationStream.SetLength(ChunkPositionLengthDict[i].Length);
                                destinationStream.Flush();
                            }

                            if (!File.Exists(fileName)) throw new Exception("At this point the item data file should have been created. This exception should not really be possible.");
                        }
                        else
                            //If we are not exclusively building to the disk we just use a memory stream at this stage
                            destinationStream = new MemoryStream(ChunkPositionLengthDict[i].Length);

                        //Ensure we start at the beginning of the stream
                        destinationStream.Seek(0, SeekOrigin.Begin);

                        //Copy over the correct part of the itemDataStream to the chunks
                        StreamTools.Write(itemDataStream, ChunkPositionLengthDict[i].Position, ChunkPositionLengthDict[i].Length, destinationStream, 8192, double.MaxValue, int.MaxValue);
                        ChunkDataStreams[i] = new StreamTools.ThreadSafeStream(destinationStream);
                    }
                }
            }
            #endregion

            this.CompleteDataCheckSum = StreamTools.MD5(itemDataStream);
        }

        /// <summary>
        /// Calculates the corresponding chunk positions and lengths when this item is deserialised
        /// </summary>
        private void InitialiseChunkPositionLengthDict()
        {
            int currentPosition = 0;
            ChunkPositionLengthDict = new Dictionary<int, PositionLength>();
            for (int i = 0; i < TotalNumChunks; i++)
            {
                int chunkSize = (int)(i == TotalNumChunks - 1 ? ItemBytesLength - (i * ChunkSizeInBytes) : ChunkSizeInBytes);
                ChunkPositionLengthDict.Add(i, new PositionLength(currentPosition, chunkSize));
                currentPosition += chunkSize;
            }

            //Validate what we have just creating
            int expectedStreamLength = ChunkPositionLengthDict[TotalNumChunks - 1].Position + ChunkPositionLengthDict[TotalNumChunks - 1].Length;
            //if (expectedStreamLength != ItemData.Length)
            //    throw new Exception("Error initialising ChunkPositionLengthDict. Last entry puts expected stream length at " + expectedStreamLength + ", but stream length is actually " + ItemData.Length +". ItemBytesLength=" + ItemBytesLength);
        }

        /// <summary>
        /// Uses the loaded stream and builds individual chunk checksums
        /// </summary>
        /// <returns></returns>
        private void BuildChunkCheckSums()
        {
            if (ChunkPositionLengthDict == null)
                throw new InvalidOperationException("ChunkPositionLengthDict must be set before building chunk checksums");

            ChunkCheckSums = new string[TotalNumChunks];
            for (int i = 0; i < TotalNumChunks; i++)
                ChunkCheckSums[i] = MD5(i);
        }

        /// <summary>
        /// Return the MD5 of the whole item data
        /// </summary>
        /// <returns></returns>
        public string MD5()
        {
            if (CompleteDataStream != null)
                return CompleteDataStream.MD5();
            else
            {
                using (System.Security.Cryptography.HashAlgorithm md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] chunkBytes;
                    for (int i = 0; i < ChunkDataStreams.Length; i++)
                    {
                        chunkBytes = ChunkDataStreams[i].ToArray();

                        if (i < ChunkDataStreams.Length - 1)
                            md5.TransformBlock(chunkBytes, 0, chunkBytes.Length, chunkBytes, 0);
                        else
                            md5.TransformFinalBlock(chunkBytes, 0, chunkBytes.Length);
                    }

                    return BitConverter.ToString(md5.Hash).Replace("-", "");
                }
            }
        }

        /// <summary>
        /// Return the MD5 of the specified chunk
        /// </summary>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public string MD5(int chunkIndex)
        {
            if (chunkIndex < 0) throw new ArgumentException("ChunkIndex cannot be less than 0.");
            if (chunkIndex >= ChunkPositionLengthDict.Count) throw new ArgumentException("ChunkIndex was greater than the number of chunks");

            if (CompleteDataStream != null)
            {
                long start = ChunkPositionLengthDict[chunkIndex].Position;
                int length = ChunkPositionLengthDict[chunkIndex].Length;

                return CompleteDataStream.MD5(start, length);
            }
            else
                return ChunkDataStreams[chunkIndex].MD5();
        }

        /// <summary>
        /// Writes the provided buffer to the data starting at the provided position within the item data
        /// </summary>
        /// <param name="chunkIndex"></param>
        /// <param name="chunkData"></param>
        public void Write(int chunkIndex, byte[] chunkData)
        {
            if (chunkIndex < 0) throw new ArgumentException("ChunkIndex cannot be less than 0.");
            if (chunkIndex >= ChunkPositionLengthDict.Count) throw new ArgumentException("ChunkIndex was greater than the number of chunks");

            if (CompleteDataStream != null)
            {
                long startPosition = ChunkPositionLengthDict[chunkIndex].Position;

                CompleteDataStream.Write(chunkData, startPosition);
            }
            else
                ChunkDataStreams[chunkIndex].Write(chunkData, 0);
        }

        /// <summary>
        /// Copies data specified by start and length properties from internal stream to the provided stream.
        /// </summary>
        /// <param name="destinationStream">The destination stream for the item data</param>
        public void CopyTo(Stream destinationStream)
        {
            if (CompleteDataStream != null)
                CompleteDataStream.CopyTo(destinationStream);
            else
            {
                for (int i = 0; i < ChunkDataStreams.Length; i++)
                    ChunkDataStreams[i].CopyTo(destinationStream);
            }
        }

        /// <summary>
        /// Return a StreamSendWrapper corresponding with the desired chunk
        /// </summary>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public StreamTools.StreamSendWrapper GetChunkStream(int chunkIndex)
        {
            if (chunkIndex < 0) throw new ArgumentException("ChunkIndex cannot be less than 0.");
            if (chunkIndex >= ChunkPositionLengthDict.Count) throw new ArgumentException("ChunkIndex was greater than the number of chunks");

            long startPosition = ChunkPositionLengthDict[chunkIndex].Position;
            int length = ChunkPositionLengthDict[chunkIndex].Length;

            if (CompleteDataStream != null)
                return new StreamTools.StreamSendWrapper(CompleteDataStream, startPosition, length);
            else
            {
                long currentBytesPassed = 0;
                for (int i = 0; i < ChunkDataStreams.Length; i++)
                {
                    if (currentBytesPassed < startPosition)
                    {
                        //While we are not in the correct chunk
                        currentBytesPassed += ChunkDataStreams[i].Length;
                        continue;
                    }
                    else if (currentBytesPassed == startPosition && length == ChunkDataStreams[i].Length)
                    {
                        return new StreamTools.StreamSendWrapper(ChunkDataStreams[i]);
                    }
                    else
                        throw new NotImplementedException("Method not implemented when start is mid chunk.");
                }

                throw new Exception("The requested data is not available.");
            }            
        }

        /// <summary>
        /// Updates the ItemBuildTarget
        /// </summary>
        /// <param name="newDataBuildMode">The new DataBuildMode to use</param>
        public void UpdateBuildTarget(DataBuildMode newDataBuildMode)
        {
            if (DFS.GetDistributedItemByChecksum(CompleteDataCheckSum) == null)
                this.DataBuildMode = newDataBuildMode;
            else
                throw new Exception("Unable to update build target once item has been added to DFS. Future version of the DFS may be more flexible in this regard.");
        }

        /// <summary>
        /// Returns data for the entire item as byte[]
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            return GetDataAsSingleStream().ToArray();
        }

        /// <summary>
        /// Get a single threadsafe stream containing all item data
        /// </summary>
        /// <returns></returns>
        public StreamTools.ThreadSafeStream GetDataAsSingleStream()
        {
            if (CompleteDataStream != null)
                return CompleteDataStream;
            else
            {
                Stream destinationStream = new MemoryStream();
                destinationStream.SetLength(ItemBytesLength);

                for (int i = 0; i < ChunkDataStreams.Length; i++)
                    ChunkDataStreams[i].CopyTo(destinationStream, 8192);

                return new StreamTools.ThreadSafeStream(destinationStream);
            }    
        }

        /// <summary>
        /// Disposes the internal stream. If <see cref="StreamTools.ThreadSafeStream.DiposeInnerStreamOnDispose"/> is false, forceDispose
        /// must be true to dispose of the internal stream.
        /// </summary>
        /// <param name="forceDispose">If true the internal stream will be disposed regardless of <see cref="StreamTools.ThreadSafeStream.DiposeInnerStreamOnDispose"/> value.</param>
        public void Dispose(bool forceDispose)
        {
            if (CompleteDataStream != null)
                CompleteDataStream.Dispose(forceDispose);
            else
            {
                for (int i = 0; i < ChunkDataStreams.Length; i++)
                    ChunkDataStreams[i].Dispose(forceDispose);
            }       
        }
    }
}
