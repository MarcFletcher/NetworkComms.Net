using NetworkCommsDotNet;
using NetworkCommsDotNet.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DistributedFileSystem
{
    /// <summary>
    /// Manages the respective data for a distributed item
    /// </summary>
    class DistributedItemData
    {
        /// <summary>
        /// The build mode to be used for this item data
        /// </summary>
        ItemBuildMode ItemBuildMode { get; set; }

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
        /// The total data length
        /// </summary>
        public long Length { get; private set; }

        /// <summary>
        /// Initialise the item data using an existing stream. If the build mode is to blocks the itemDataStream is broken into chunks.
        /// </summary>
        /// <param name="itemIdentifier"></param>
        /// <param name="itemBuildMode"></param>
        /// <param name="itemDataStream"></param>
        /// <param name="chunkPositionLength"></param>
        public DistributedItemData(string itemIdentifier, ItemBuildMode itemBuildMode, Stream itemDataStream, Dictionary<int, PositionLength> chunkPositionLength)
        {
            this.ItemBuildMode = itemBuildMode;
            if (itemDataStream == null) throw new ArgumentNullException("itemDataStream", "itemDataStream cannot be null.");
            this.Length = itemDataStream.Length;

            if (ItemBuildMode == ItemBuildMode.Disk_Single ||
                //ItemBuildMode == ItemBuildMode.Both_Single ||
                ItemBuildMode == ItemBuildMode.Memory_Single)
            {
                CompleteDataStream = new StreamTools.ThreadSafeStream(itemDataStream);
            }
            else
            {
                //Break the itemDataStream into blocks
                ChunkDataStreams = new StreamTools.ThreadSafeStream[chunkPositionLength.Count];

                //If the itemDataStream is a memory stream we can try to access the buffer as it makes creating the streams more efficient
                byte[] itemDataStreamBuffer = null;
                if (itemDataStream is MemoryStream)
                {
                    try
                    {
                        itemDataStreamBuffer = ((MemoryStream)itemDataStream).GetBuffer();
                    }
                    catch(UnauthorizedAccessException) { /* Ignore */ }
                }

                for (int i = 0; i < chunkPositionLength.Count; i++)
                {
                    if (itemDataStreamBuffer != null)
                        //This is the fastest way to create a block of data streams
                        ChunkDataStreams[i] = new StreamTools.ThreadSafeStream(new MemoryStream(itemDataStreamBuffer, chunkPositionLength[i].Position, chunkPositionLength[i].Length));
                    else
                    {
                        //We now need to available the respective data into blocks
                        Stream destinationStream = null;
                        if (itemBuildMode == ItemBuildMode.Disk_Blocks)
                        {
                            string folderLocation = "DFS_" + NetworkComms.NetworkIdentifier;
                            string fileName = Path.Combine(folderLocation, itemIdentifier + ".DFSItemData_"+i.ToString());

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
                                destinationStream.SetLength(chunkPositionLength[i].Length);
                                destinationStream.Flush();
                            }

                            if (!File.Exists(fileName)) throw new Exception("At this point the item data file should have been created. This exception should not really be possible.");
                        }
                        else
                            //If we are not exclusively building to the disk we just use a memory stream at this stage
                            destinationStream = new MemoryStream(chunkPositionLength[i].Length);

                        //Ensure we start at the beginning of the stream
                        destinationStream.Seek(0, SeekOrigin.Begin);

                        //Copy over the correct part of the itemDataStream to the chunks
                        StreamTools.Write(itemDataStream, chunkPositionLength[i].Position, chunkPositionLength[i].Length, destinationStream, 8192, double.MaxValue, int.MaxValue);
                        ChunkDataStreams[i] = new StreamTools.ThreadSafeStream(destinationStream);
                    }
                }
            }
        }

        /// <summary>
        /// Initialise the item data using existing chunk streams. Build mode must be to blocks.
        /// </summary>
        /// <param name="itemIdentifier"></param>
        /// <param name="itemBuildMode"></param>
        /// <param name="itemDataStreams"></param>
        /// <param name="chunkPositionLength"></param>
        public DistributedItemData(ItemBuildMode itemBuildMode, Dictionary<int, Stream> itemDataStreams, Dictionary<int, PositionLength> chunkPositionLength)
        {
            if (itemDataStreams == null) throw new ArgumentNullException("itemDataStreams", "itemDataStreams cannot be null.");

            if (itemBuildMode == ItemBuildMode.Disk_Single ||
                //itemBuildMode == ItemBuildMode.Both_Single ||
                itemBuildMode == ItemBuildMode.Memory_Single)
                throw new ArgumentException("Please use other constructor that takes a single input data stream.");

            if (itemDataStreams.Count != chunkPositionLength.Count)
                throw new ArgumentException("Number of streams should equal the number of chunks");

            this.ItemBuildMode = ItemBuildMode;
            this.Length = itemDataStreams.Select(i => i.Value.Length).Sum();

            //Initialise the data streams
            ChunkDataStreams = new StreamTools.ThreadSafeStream[chunkPositionLength.Count];
            foreach(int chunkIndex in chunkPositionLength.Keys)
                ChunkDataStreams[chunkIndex] = new StreamTools.ThreadSafeStream(itemDataStreams[chunkIndex]);
        }

        /// <summary>
        /// Initialise the item data from an assembly config
        /// </summary>
        /// <param name="assemblyConfig"></param>
        /// <param name="chunkPositionLength"></param>
        public DistributedItemData(ItemAssemblyConfig assemblyConfig, Dictionary<int, PositionLength> chunkPositionLength)
        {
            this.ItemBuildMode = assemblyConfig.ItemBuildMode;
            this.Length = assemblyConfig.TotalItemSizeInBytes;

            //Create the data stores as per the assembly config
            if (ItemBuildMode == ItemBuildMode.Memory_Single)
                //ItemBuildMode == ItemBuildMode.Both_Single ||)
            {
                MemoryStream itemStream = new MemoryStream(0);
                itemStream.SetLength(Length);
                CompleteDataStream = new StreamTools.ThreadSafeStream(itemStream);
            }
            else if (ItemBuildMode == ItemBuildMode.Disk_Single)
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
                        if (StreamTools.MD5(fileStream) != assemblyConfig.ItemCheckSum)
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
                    fileStream.SetLength(Length);
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
                ChunkDataStreams = new StreamTools.ThreadSafeStream[chunkPositionLength.Count];

                for (int i = 0; i < chunkPositionLength.Count; i++)
                {
                    //We now need to available the respective data into blocks
                    Stream destinationStream = null;
                    if (ItemBuildMode == ItemBuildMode.Disk_Blocks)
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
                        destinationStream = new MemoryStream(chunkPositionLength[i].Length);

                    //Ensure we start at the beginning of the stream
                    destinationStream.SetLength(chunkPositionLength[i].Length);
                    destinationStream.Flush();
                    destinationStream.Seek(0, SeekOrigin.Begin);
                    ChunkDataStreams[i] = new StreamTools.ThreadSafeStream(destinationStream);
                }
                #endregion
            }
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
        /// Return the MD5 of the specified part of the data
        /// </summary>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public string MD5(long start, int length)
        {
            if (CompleteDataStream != null)
                return CompleteDataStream.MD5(start, length);
            else
            {
                long currentBytesPassed = 0;
                for (int i = 0; i < ChunkDataStreams.Length; i++)
                {
                    if (currentBytesPassed < start)
                    {
                        //While we are not in the correct chunk
                        currentBytesPassed += ChunkDataStreams[i].Length;
                        continue;
                    }
                    else if (currentBytesPassed == start && ChunkDataStreams[i].Length == length)
                    {
                        return ChunkDataStreams[i].MD5();
                    }
                    else
                        throw new NotImplementedException("Method not implemented when start is mid chunk.");
                }

                throw new ArgumentException("Was not possible to MD5 the data using the provided parameters.");
            }
        }

        /// <summary>
        /// Writes the provided buffer to the data starting at the provided position within the item data
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="startPosition"></param>
        public void Write(byte[] buffer, long startPosition)
        {
            if (CompleteDataStream != null)
                CompleteDataStream.Write(buffer, startPosition);
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
                    else if (currentBytesPassed == startPosition)
                    {
                        ChunkDataStreams[i].Write(buffer, 0);
                        break;
                    }
                    else
                        throw new NotImplementedException("Method not implemented when start is mid chunk.");

                    throw new ArgumentException("Was not possible to write the provided buffer using the provided parameters.");
                }
            }
        }

        /// <summary>
        /// Copies data specified by start and length properties from internal stream to the provided stream.
        /// </summary>
        /// <param name="destinationStream">The destination stream to write to</param>
        /// <param name="startPosition"></param>
        /// <param name="length"></param>
        /// <param name="writeBufferSize">The buffer size to use for copying stream contents</param>
        /// <returns>The average time in milliseconds per byte written</returns>
        public void CopyTo(Stream destinationStream, long startPosition, long length, int writeBufferSize)
        {
            if (CompleteDataStream != null)
                CompleteDataStream.CopyTo(destinationStream, startPosition, length, writeBufferSize);
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
                    else if (currentBytesPassed == startPosition)
                    {
                        ChunkDataStreams[i].CopyTo(destinationStream, 0, length, writeBufferSize);
                        break;
                    }
                    else
                        throw new NotImplementedException("Method not implemented when start is mid chunk.");
                }
            }
        }

        /// <summary>
        /// Return a StreamSendWrapper corresponding with the desired block
        /// </summary>
        /// <param name="startPosition"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public StreamTools.StreamSendWrapper GetChunkStream(int startPosition, int length)
        {
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
                destinationStream.SetLength(Length);

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
