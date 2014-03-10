//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;
using System.IO;
using ProtoBuf;
using System.Net;

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
    /// An item that is distributed using the DFS
    /// </summary>
    [ProtoContract]
    public class DistributedItem : IDisposable
    {
        /// <summary>
        /// A unique string identifier for this DFS item. Usually a filename.
        /// </summary>
        [ProtoMember(1)]
        public string ItemIdentifier { get; private set; }

        /// <summary>
        /// A category for this DFS item. Allowed items to be grouped by item type.
        /// </summary>
        [ProtoMember(2)]
        public string ItemTypeStr { get; private set; }

        /// <summary>
        /// The MD5 checksum for the completed item. Used to validate a completed build.
        /// </summary>
        [ProtoMember(3)]
        public string ItemCheckSum { get; private set; }

        /// <summary>
        /// Optional MD5 checksums for individual chunks. Useful for debugging build issues.
        /// </summary>
        [ProtoMember(11)]
        public string[] ChunkCheckSums { get; private set; }

        /// <summary>
        /// Total number of chunks for this item
        /// </summary>
        [ProtoMember(4)]
        public byte TotalNumChunks { get; private set; }

        /// <summary>
        /// Maximum size of each chunk in bytes. The final chunk may be less than this value.
        /// </summary>
        [ProtoMember(5)]
        public int ChunkSizeInBytes { get; private set; }

        /// <summary>
        /// The stream containing the item chunk data
        /// </summary>
        StreamTools.ThreadSafeStream ItemDataStream { get; set; }

        /// <summary>
        /// The chunk positions and lengths. Key is chunkIndex.
        /// </summary>
        Dictionary<int, PositionLength> ChunkPositionLengthDict { get; set; }

        // <summary>
        // Originally we stored a single array but this creates considerable inefficiencies when redistributing the data
        // We have now moved to keeping the data stored as separate chunks
        // </summary>
        //byte[][] ItemByteArray { get; set; }

        /// <summary>
        /// Total item size in bytes.
        /// </summary>
        [ProtoMember(6)]
        public int ItemBytesLength { get; private set; }

        /// <summary>
        /// The cascade depth to use when building this item. Default is 1
        /// </summary>
        [ProtoMember(7)]
        public int ItemBuildCascadeDepth { get; private set; }

        /// <summary>
        /// The DateTime this DFS item was successfully built.
        /// </summary>
        [ProtoMember(8)]
        public DateTime ItemBuildCompleted { get; private set; }

        /// <summary>
        /// The target to where the item should be built, i.e. memory or disk
        /// </summary>
        [ProtoMember(9)]
        public ItemBuildTarget ItemBuildTarget { get; private set; }

        /// <summary>
        /// Contains a record of which peers have which chunks of this DFS item
        /// </summary>
        [ProtoMember(10)]
        public SwarmChunkAvailability SwarmChunkAvailability { get; private set; }

        /// <summary>
        /// Used to track chunk requests. Key is chunkIndex and value is the request made
        /// </summary>
        Dictionary<byte, ChunkAvailabilityRequest> itemBuildTrackerDict;

        /// <summary>
        /// Contains chunk data that is waiting to be integrated
        /// </summary>
        Queue<ChunkAvailabilityReply> chunkDataToIntegrateQueue;

        AutoResetEvent itemBuildWait = new AutoResetEvent(false);
        ManualResetEvent itemBuildComplete = new ManualResetEvent(false);

        /// <summary>
        /// True if this item has been closed. Can be used externally to cancel an AssembleItem.
        /// </summary>
        public volatile bool ItemClosed;

        object itemLocker = new object();

        /// <summary>
        /// The total number of chunks pushed to peers
        /// </summary>
        public int TotalChunkSupplyCount { get; private set; }
        
        /// <summary>
        /// The total number of times this item has been pushed.
        /// </summary>
        public int PushCount { get; private set; }

        List<string> assembleLog;
        object assembleLogLocker = new object();

        private DistributedItem() { }

        /// <summary>
        /// Instantiate a new DFS item which is complete.
        /// </summary>
        /// <param name="itemTypeStr">A category string which can be used to group distributed items together.</param>
        /// <param name="itemIdentifier">A unique identifier for this item, usually a file name</param>
        /// <param name="itemData">A stream containing the data for this item</param>
        /// <param name="seedConnectionInfoList">A list of connecitonInfo corresponding to peers that will act as seeds</param>
        /// <param name="itemBuildTarget">The target to where the item should be built, i.e. memory or disk</param>
        /// <param name="enableChunkChecksum">If true checkSums will be validated for each chunk before it is integrated. Reduces the performance of the DFS.</param>
        /// <param name="itemBuildCascadeDepth">The cascade depth to use when building this item. Default is 1</param>
        public DistributedItem(string itemTypeStr, string itemIdentifier, Stream itemData, List<ConnectionInfo> seedConnectionInfoList, ItemBuildTarget itemBuildTarget, bool enableChunkChecksum=false, int itemBuildCascadeDepth = 1)
        {
            this.ItemTypeStr = itemTypeStr;
            this.ItemIdentifier = itemIdentifier;
            this.ItemDataStream = new StreamTools.ThreadSafeStream(itemData);
            this.ItemBuildTarget = itemBuildTarget;

            ItemCheckSum = ItemDataStream.MD5();
            ItemBytesLength = (int)ItemDataStream.Length;
            this.ItemBuildCascadeDepth = itemBuildCascadeDepth;

            this.ItemBuildCompleted = DateTime.Now;

            //Calculate the exactChunkSize if we split everything up into 255 pieces
            double exactChunkSize = (double)ItemBytesLength / 255.0;

            //If the item is too small we just use the minimumChunkSize
            //If we need something larger than MinChunkSizeInBytes we select appropriately
            this.ChunkSizeInBytes = (exactChunkSize <= DFS.MinChunkSizeInBytes ? DFS.MinChunkSizeInBytes : (int)Math.Ceiling(exactChunkSize));

            this.TotalNumChunks = (byte)(Math.Ceiling((double)ItemBytesLength / (double)ChunkSizeInBytes));

            //this.ItemBytes = itemBytes;
            //Break the itemBytes into chunks
            //this.ItemByteArray = new byte[TotalNumChunks][];
            InitialiseChunkPositionLengthDict();

            TotalChunkSupplyCount = 0;
            PushCount = 0;

            //Initialise the swarm availability
            SwarmChunkAvailability = new SwarmChunkAvailability(seedConnectionInfoList, TotalNumChunks);

            if (enableChunkChecksum) BuildChunkCheckSums();

            if (DFS.loggingEnabled) DFS._DFSLogger.Debug("... created new original DFS item (" + this.ItemCheckSum + ").");
        }

        /// <summary>
        /// Instantiate a new DFS item which needs to be built.
        /// </summary>
        /// <param name="assemblyConfig">An ItemAssemblyConfig containing the necessary bootstrap information.</param>
        public DistributedItem(ItemAssemblyConfig assemblyConfig)
        {
            //File.WriteAllBytes(assemblyConfig.ItemIdentifier + ".iac", DPSManager.GetDataSerializer<ProtobufSerializer>().SerialiseDataObject<ItemAssemblyConfig>(assemblyConfig).ThreadSafeStream.ToArray());

            this.TotalChunkSupplyCount = 0;
            this.PushCount = 0;

            this.ItemIdentifier = assemblyConfig.ItemIdentifier;
            this.ItemTypeStr = assemblyConfig.ItemTypeStr;
            this.TotalNumChunks = assemblyConfig.TotalNumChunks;
            this.ChunkSizeInBytes = assemblyConfig.ChunkSizeInBytes;
            this.ItemCheckSum = assemblyConfig.ItemCheckSum;
            this.ChunkCheckSums = assemblyConfig.ChunkCheckSums;
            this.ItemBytesLength = assemblyConfig.TotalItemSizeInBytes;
            this.ItemBuildCascadeDepth = assemblyConfig.ItemBuildCascadeDepth;

            //this.ItemBytes = new byte[assemblyConfig.TotalItemSizeInBytes];
            //this.ItemByteArray = new byte[TotalNumChunks][];
            if (assemblyConfig.ItemBuildTarget == ItemBuildTarget.Disk)
            {
                string folderLocation =  "DFS_" + NetworkComms.NetworkIdentifier;
                string fileName = Path.Combine(folderLocation, assemblyConfig.ItemIdentifier + ".DFSItemData");
                if (File.Exists(fileName))
                {
                    //If the file already exists the MD5 had better match otherwise we have a problem
                    FileStream file;
                    try
                    {
                        file = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                        if (StreamTools.MD5(file) != assemblyConfig.ItemCheckSum)
                            throw new Exception("Wrong place, wrong time, wrong file!");
                    }
                    catch (Exception)
                    {
                        try
                        {
                            File.Delete(fileName);
                            file = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                        }
                        catch (Exception)
                        {
                            throw new Exception("File with name '" + fileName + "' already exists. Unfortunately the MD5 does match the expected DFS item. Unable to delete in order to continue.");
                        }
                    }

                    this.ItemDataStream = new StreamTools.ThreadSafeStream(file);
                }
                else
                {
                    lock (DFS.globalDFSLocker)
                    {
                        if (!Directory.Exists(folderLocation))
                            Directory.CreateDirectory(folderLocation);
                    }

                    FileStream newStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                    newStream.SetLength(ItemBytesLength);
                    newStream.Flush();
                    this.ItemDataStream = new StreamTools.ThreadSafeStream(newStream);
                }

                if (!File.Exists(fileName)) throw new Exception("At this point the item data file should have been created. This exception should not really be possible.");
            }
            else if (assemblyConfig.ItemBuildTarget == ItemBuildTarget.Memory || assemblyConfig.ItemBuildTarget == ItemBuildTarget.Both)
            {
                MemoryStream itemStream = new MemoryStream(ItemBytesLength);
                itemStream.SetLength(ItemBytesLength);
                this.ItemDataStream = new StreamTools.ThreadSafeStream(itemStream);
            }

            InitialiseChunkPositionLengthDict();

            //this.SwarmChunkAvailability = NetworkComms.DefaultSerializer.DeserialiseDataObject<SwarmChunkAvailability>(assemblyConfig.SwarmChunkAvailabilityBytes, NetworkComms.DefaultCompressor);
            this.SwarmChunkAvailability = DPSManager.GetDataSerializer<ProtobufSerializer>().DeserialiseDataObject<SwarmChunkAvailability>(assemblyConfig.SwarmChunkAvailabilityBytes);

            //As requests are made they are added to the build dict. We never remove a completed request.
            this.itemBuildTrackerDict = new Dictionary<byte, ChunkAvailabilityRequest>();
            this.chunkDataToIntegrateQueue = new Queue<ChunkAvailabilityReply>();

            //Make sure that the original source added this node to the swarm before providing the assemblyConfig
            if (!SwarmChunkAvailability.PeerExistsInSwarm(NetworkComms.NetworkIdentifier))
                throw new Exception("The current local node should have been added by the source.");

            //Bug fix incase we have just gotten the same file twice and the super node did not know that we dropped it
            //If the SwarmChunkAvailability thinks we have everything but our local version is not correct then clear our flags which will force rebuild
            if (SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkIdentifier).NumCompletedChunks() > 0 && !LocalItemValid())
            {
                SwarmChunkAvailability.ClearAllLocalAvailabilityFlags();
                SwarmChunkAvailability.BroadcastLocalAvailability(ItemCheckSum);
                AddBuildLogLine("Created DFS item (reset local availability) - " + ItemIdentifier);

                if (DFS.loggingEnabled) DFS.Logger.Trace("Reset local chunk availability for " + ItemIdentifier + " ("+ItemCheckSum+")");
            }
            else
                AddBuildLogLine("Created DFS item - " + ItemIdentifier);

            if (DFS.loggingEnabled) DFS._DFSLogger.Debug("... created new DFS item from assembly config (" + this.ItemCheckSum + ").");
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
                int chunkSize = (i == TotalNumChunks - 1 ? ItemBytesLength - (i * ChunkSizeInBytes) : ChunkSizeInBytes);
                ChunkPositionLengthDict.Add(i, new PositionLength(currentPosition, chunkSize));
                currentPosition += chunkSize;
            }

            //Validate what we have just creating
            int expectedStreamLength = ChunkPositionLengthDict[TotalNumChunks - 1].Position + ChunkPositionLengthDict[TotalNumChunks - 1].Length;
            if (expectedStreamLength != ItemDataStream.Length)
                throw new Exception("Error initalising ChunkPositionLengthDict. Last entry puts expected stream length at " + expectedStreamLength + ", but stream length is actually " + ItemDataStream.Length +". ItemBytesLength=" + ItemBytesLength);
        }

        /// <summary>
        /// Returns ItemTypeStr + ItemIdentifier
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return ItemTypeStr + " - " + ItemIdentifier;
        }

        /// <summary>
        /// Updates the ItemBuildTarget
        /// </summary>
        /// <param name="newTarget">The new ItemBuildTarget to use</param>
        public void UpdateBuildTarget(ItemBuildTarget newTarget)
        {
            if (DFS.GetDistributedItemByChecksum(ItemCheckSum) == null)
                this.ItemBuildTarget = newTarget;
            else
                throw new Exception("Unable to update build target once item has been added to DFS. Future version of the DFS may be more flexible in this regard.");
        }

        /// <summary>
        /// Increments the item push count.
        /// </summary>
        public void IncrementPushCount()
        {
            lock (itemLocker) PushCount++;
        }

        /// <summary>
        /// Add the provided string to the build log of this item
        /// </summary>
        /// <param name="newLine"></param>
        public void AddBuildLogLine(string newLine)
        {
            lock (assembleLogLocker)
            {
                if (assembleLog == null) assembleLog = new List<string>();

                string threadId = null;
                try
                {
                    threadId = System.Threading.Thread.CurrentThread.ManagedThreadId.ToString();
                }
                catch (Exception) { }

                DateTime currentTime = DateTime.Now;
                assembleLog.Add(currentTime.Hour.ToString() + "." + currentTime.Minute.ToString() + "." + currentTime.Second.ToString() + "." + currentTime.Millisecond.ToString() + " [" + (threadId != null ? threadId.ToString() : "NA") + "] - " + newLine);
            }
        }

        /// <summary>
        /// Get the current build log for this DFS item
        /// </summary>
        /// <returns></returns>
        public string[] BuildLog()
        {
            lock (assembleLogLocker)
            {
                if (assembleLog != null)
                    return assembleLog.ToArray();
                else
                    return new string[0];
            }
        }

        /// <summary>
        /// Assemble this DFS item using the swarm
        /// </summary>
        /// <param name="assembleTimeoutSecs">The maximum time to allow to build this item before throwing a timeout exception.</param>
        public void AssembleItem(int assembleTimeoutSecs)
        {
            if (DFS.loggingEnabled) DFS._DFSLogger.Debug("Started DFS item assemble - "+ItemIdentifier+" (" + this.ItemCheckSum + ").");

            AddBuildLogLine("Started DFS item assemble - " + ItemIdentifier + " (" + this.ItemCheckSum + ").");

            //Used to load balance
            Random randGen = new Random();
            long assembleStartTime = DFS.ElapsedExecutionSeconds;

            //Start by broadcasting our start of build
            SwarmChunkAvailability.BroadcastLocalAvailability(ItemCheckSum);

            //Contact all known peers and request an update
            SwarmChunkAvailability.UpdatePeerAvailability(ItemCheckSum, ItemBuildCascadeDepth, 5000, AddBuildLogLine);

            #region Connection Close Handler
            NetworkComms.ConnectionEstablishShutdownDelegate connectionShutdownDuringBuild = new NetworkComms.ConnectionEstablishShutdownDelegate((Connection connection) =>
            {
                //On a closed connection we make sure we have no outstanding requests with that client
                if (connection.ConnectionInfo.ConnectionType == ConnectionType.TCP && connection.ConnectionInfo.NetworkIdentifier != ShortGuid.Empty)
                {
                    lock (itemLocker)
                    {
                        SwarmChunkAvailability.RemovePeerIPEndPointFromSwarm(connection.ConnectionInfo.NetworkIdentifier, (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint);
                        itemBuildTrackerDict = (from current in itemBuildTrackerDict where current.Value.PeerConnectionInfo.NetworkIdentifier != connection.ConnectionInfo.NetworkIdentifier select current).ToDictionary(dict => dict.Key, dict => dict.Value);
                        itemBuildWait.Set();
                    }

                    if (DFS.loggingEnabled) DFS.Logger.Debug("DFSConnectionShutdown Item - Removed peer from all items - " + connection + ".");
                }
                else
                    if (DFS.loggingEnabled) DFS.Logger.Trace("DFSConnectionShutdown Item - Disconnection ignored - " + connection + ".");
            });

            //If a connection is closed during the assembly we want to know
            NetworkComms.AppendGlobalConnectionCloseHandler(connectionShutdownDuringBuild);
            #endregion

            ItemClosed = false;
            bool buildTimeHalfWayPoint = false;

            //Loop until the local file is complete
            while (!LocalItemComplete() && !ItemClosed)
            {
                #region BuildItem
                //The requests we are going to make this loop
                Dictionary<ConnectionInfo, List<ChunkAvailabilityRequest>> newRequests = new Dictionary<ConnectionInfo, List<ChunkAvailabilityRequest>>();
                int newRequestCount = 0;

                ///////////////////////////////
                ///// ENTER ITEMLOCKER ////////
                ///////////////////////////////
                lock (itemLocker)
                {
                    //Check for request timeouts
                    #region ChunkRequestTimeout
                    int maxChunkTimeoutMS = Math.Min(assembleTimeoutSecs * 1000 / 2, DFS.ChunkRequestTimeoutMS);
                    byte[] currentTrackerKeys = itemBuildTrackerDict.Keys.ToArray();
                    List<ShortGuid> timedOutPeers = new List<ShortGuid>();
                    for (int i = 0; i < currentTrackerKeys.Length; i++)
                    {
                        if (!itemBuildTrackerDict[currentTrackerKeys[i]].RequestComplete)
                            AddBuildLogLine(" .... outstanding request for chunk " + itemBuildTrackerDict[currentTrackerKeys[i]].ChunkIndex + " from " + itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo);

                        if (!itemBuildTrackerDict[currentTrackerKeys[i]].RequestComplete && 
                            !itemBuildTrackerDict[currentTrackerKeys[i]].RequestIncoming && 
                            (DateTime.Now - itemBuildTrackerDict[currentTrackerKeys[i]].RequestCreationTime).TotalMilliseconds > maxChunkTimeoutMS)
                        {
                            //We are going to consider this request potentially timed out
                            if (SwarmChunkAvailability.GetNewTimeoutCount(itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo.NetworkIdentifier, (IPEndPoint)itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo.LocalEndPoint) > DFS.PeerMaxNumTimeouts)
                            {
                                if (!SwarmChunkAvailability.PeerIsSuperPeer(itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo.NetworkIdentifier))
                                {
                                    if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... removing " + itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo + " peer from AssembleItem due to potential timeout.");
                                    AddBuildLogLine("Removing " + itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo.NetworkIdentifier + " from AssembleItem due to potential timeout.");
                                    SwarmChunkAvailability.RemovePeerIPEndPointFromSwarm(itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo.NetworkIdentifier, (IPEndPoint)itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo.LocalEndPoint);
                                    timedOutPeers.Add(itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo.NetworkIdentifier);
                                }
                                else
                                {
                                    AddBuildLogLine(" ... chunk request timeout from super peer " + itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo + ".");
                                    if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... chunk request timeout from super peer " + itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo + ".");
                                }
                            }
                            else
                            {
                                if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... chunk request timeout from peer " + itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo + " which has remaining timeouts.");
                                AddBuildLogLine(" ... chunk request timeout from peer " + itemBuildTrackerDict[currentTrackerKeys[i]].PeerConnectionInfo + " which has remaining timeouts.");
                            }

                            itemBuildTrackerDict.Remove(currentTrackerKeys[i]);
                        }
                    }

                    //Remove any other remaining requests from timedOutPeers
                    itemBuildTrackerDict = (from current in itemBuildTrackerDict where !timedOutPeers.Contains(current.Value.PeerConnectionInfo.NetworkIdentifier) select current).ToDictionary(entry=> entry.Key, entry=> entry.Value);
                    #endregion

                    //Get the list of all current possible peers and chunks
                    //We get all the information we are going to need from the current swarm cache in one go
                    Dictionary<ConnectionInfo, PeerInfo> nonLocalPeerAvailability;
                    Dictionary<byte, Dictionary<ConnectionInfo, PeerInfo>> nonLocalChunkExistence = SwarmChunkAvailability.CachedNonLocalChunkExistences(TotalNumChunks, out nonLocalPeerAvailability);

                    //If over half the number of swarm peers are completed we will use them rather than uncompleted peers
                    bool useCompletedPeers = (SwarmChunkAvailability.NumCompletePeersInSwarm(TotalNumChunks) >= SwarmChunkAvailability.NumPeersInSwarm() / 2.0);

                    //We only make requests if remote chunks are available and our recieve load is below a given threshold
                    double incomingNetworkLoad = HostInfo.IP.AverageNetworkLoadIncoming(7);
                    if (nonLocalChunkExistence.Count > 0 && incomingNetworkLoad <= DFS.PeerBusyNetworkLoadThreshold)
                    {
                        AddBuildLogLine(nonLocalChunkExistence.Count + " chunks required with " + (from current in nonLocalChunkExistence select current.Value.Values.ToList()).Aggregate(new List<PeerInfo>(), (left, right) => { return left.Union(right).ToList(); }).Distinct().Count(entry=> entry.HasAtleastOneOnlineIPEndPoint()) + " unique online peers.");

                        //We will want to know how many unique peers we can potentially contact
                        int maxPeers = (from current in nonLocalChunkExistence select current.Value.Count(entry => !entry.Value.IsPeerIPEndPointBusy(entry.Key.NetworkIdentifier, (IPEndPoint)entry.Key.LocalEndPoint) && entry.Value.IsPeerIPEndPointOnline(entry.Key.NetworkIdentifier, (IPEndPoint)entry.Key.LocalEndPoint))).Max();

                        //We will want to know how many chunks we have left to request
                        int numChunksLeft = TotalNumChunks - itemBuildTrackerDict.Count;

                        //Get list of chunks we don't have and order by rarity, starting with the rarest first
                        List<byte> chunkRarity = (from current in nonLocalChunkExistence
                                                  orderby current.Value.Count, randGen.NextDouble()
                                                  select current.Key).ToList();

                        //We want an array of peer identifiers with whom we have outstanding requests
                        //In the first instance we will always go to other peers first
                        List<ChunkAvailabilityRequest> nonIncomingOutstandingRequests = (from current in itemBuildTrackerDict.Values
                                                                                         where !current.RequestIncoming
                                                                                         select current).ToList();

                        //We only consider making new requests if we are allowed to
                        if (nonIncomingOutstandingRequests.Count < DFS.MaxTotalItemRequests)
                        {
                            //Step 1 - Go through each chunk we dont have, and have not yet requested,
                            //starting with the rarest, and attempt to make a new request.
                            #region Step1
                            for (byte i = 0; i < chunkRarity.Count; i++)
                            {
                                //Make sure the chunk does exist somewhere in the swarm
                                if (nonLocalChunkExistence[chunkRarity[i]].Count == 0)
                                    throw new Exception("Every distributed item must have atleast one source for every chunk. Something bad has most likely happened to the original super peer.");

                                //If we have not yet made a request for this chunk there will be no entry
                                if (!itemBuildTrackerDict.ContainsKey(chunkRarity[i]))
                                {
                                    //We have to do this inside the for loop as the result will change once we add new requests
                                    List<ShortGuid> currentRequestIdentifiers = (nonIncomingOutstandingRequests.Select(entry => entry.PeerConnectionInfo.NetworkIdentifier).Union(newRequests.Select(entry => entry.Value[0].PeerConnectionInfo.NetworkIdentifier))).ToList();

                                    //Determine if this chunk contains non super peers, if it does we will never contact the super peers (keeps load on super peers low)
                                    //We have non super peers if the number of peers who are not us and are not super peers is greater than 0
                                    bool containsNonSuperPeers = (nonLocalChunkExistence[chunkRarity[i]].Count(entry => entry.Key.NetworkIdentifier != NetworkComms.NetworkIdentifier && !entry.Value.SuperPeer && entry.Value.HasAtleastOneOnlineIPEndPoint()) > 0);

                                    //We can now determine which peers we could contact for this chunk
                                    ConnectionInfo[] possibleChunkPeers = (from current in nonLocalChunkExistence[chunkRarity[i]]
                                                                           //We don't want to contact busy peers
                                                                           where !current.Value.IsPeerIPEndPointBusy(current.Key.NetworkIdentifier, (IPEndPoint)current.Key.LocalEndPoint) && current.Value.IsPeerIPEndPointOnline(current.Key.NetworkIdentifier, (IPEndPoint)current.Key.LocalEndPoint)
                                                                           //If we have nonSuperPeers then we only include the non super peers
                                                                           where (containsNonSuperPeers ? !current.Value.SuperPeer : true)
                                                                           //We don't want a peer from whom we currently await a response
                                                                           where !currentRequestIdentifiers.Contains(current.Key.NetworkIdentifier)
                                                                           //See comments within /**/ below for ordering notes
                                                                           orderby
                                                                               current.Value.GetCurrentTimeoutCount(current.Key.NetworkIdentifier, (IPEndPoint)current.Key.LocalEndPoint) ascending,
                                                                               (useCompletedPeers ? 0 : current.Value.PeerChunkFlags.NumCompletedChunks()) ascending,
                                                                               (useCompletedPeers ? current.Value.PeerChunkFlags.NumCompletedChunks() : 0) descending,
                                                                               randGen.NextDouble() ascending
                                                                           select current.Key).ToArray();

                                    /*
                                    *Comments on ordering the available peers in the above linq statement*
                                    We want to avoid overloading individual peers. If we always went to the peer with the most complete item
                                    there are situations (in particular if we have a single complete non super peer), where all 
                                    peers which are building and item will go to a single peer.
                                    Because of that we go to the peer with the least data in the fist instance and this should help load balance
                                    We also add a random sort at the end to make sure we always go for peers in a different order on a subsequent loop
                                
                                    10/4/12
                                    We are modifying this sorting when over half the swarm peers have already completed the item
                                     
                                    11/11/12
                                    Added an initial ordering based on timeout count so that we go to peers who do not timeout first
                                    */

                                    //We can only make a request if there are available peers
                                    if (possibleChunkPeers.Length > 0)
                                    {
                                        //We can now add the new request to the build dictionaries
                                        long chunkRequestIndex = Interlocked.Increment(ref DFS._totalNumRequestedChunks);
                                        ChunkAvailabilityRequest newChunkRequest = new ChunkAvailabilityRequest(ItemCheckSum, chunkRarity[i], possibleChunkPeers[0], chunkRequestIndex);

                                        if (newRequests.ContainsKey(possibleChunkPeers[0]))
                                            throw new Exception("We should not be choosing a peer we have already chosen in step 1");
                                        else
                                            newRequests.Add(possibleChunkPeers[0], new List<ChunkAvailabilityRequest> { newChunkRequest });

                                        newRequestCount++;

                                        AddBuildLogLine("NewChunkRequest S1 Idx:" + newChunkRequest.ChunkIndex + ", Target:" + newChunkRequest.PeerConnectionInfo.LocalEndPoint.ToString() + ", Id:" + newChunkRequest.PeerConnectionInfo.NetworkIdentifier);

                                        itemBuildTrackerDict.Add(chunkRarity[i], newChunkRequest);

                                        //Once we have added a new request we should check if we have enough
                                        if (newRequestCount >= maxPeers ||  //If we already have a number of new requests equal to the max number of peers
                                            nonIncomingOutstandingRequests.Count + newRequestCount >= maxPeers * DFS.MaxConcurrentPeerRequests || //If the total number of outstanding requests is greater than the total number of peers * our concurrency factor
                                            nonIncomingOutstandingRequests.Count + newRequestCount >= numChunksLeft || //If the total number of requests is equal the number of chunks left
                                            nonIncomingOutstandingRequests.Count + newRequestCount >= DFS.MaxTotalItemRequests) //If the total number of requests is equal to the total requests
                                            break;
                                    }
                                }
                            }
                            #endregion Step1

                            //Step 2 - Now that we've been through all chunks and peers once we can make concurrent requests if we want to
                            #region Step2
                            //We start with a list of outstanding and new requests
                            List<ConnectionInfo> currentRequestConnectionInfo = (nonIncomingOutstandingRequests.Select(entry => entry.PeerConnectionInfo).Union(newRequests.Select(entry => entry.Value[0].PeerConnectionInfo))).ToList();

                            //Update the max peers
                            maxPeers = currentRequestConnectionInfo.Count;

                            int loopSafety = 0;
                            while (nonIncomingOutstandingRequests.Count + newRequestCount < maxPeers * DFS.MaxConcurrentPeerRequests && //While the total number of requests is less than the total number of peers * our concurrency factor
                                nonIncomingOutstandingRequests.Count + newRequestCount < numChunksLeft && //While the total number of requests is less than the number of chunks left
                                nonIncomingOutstandingRequests.Count + newRequestCount < DFS.MaxTotalItemRequests //While the total number of requests is less than the total requests limit
                                )
                            {
                                if (loopSafety > 1000)
                                    throw new Exception("Loop safety triggered. outstandingRequests=" + nonIncomingOutstandingRequests.Count + ". newRequestCount=" + newRequestCount + ". maxPeers=" + maxPeers + ". numChunksLeft=" + numChunksLeft + ".");

                                //We shuffle the peer list so that we never go in the same order on successive loops
                                currentRequestConnectionInfo = ShuffleList.Shuffle(currentRequestConnectionInfo).ToList();

                                for (int i = 0; i < currentRequestConnectionInfo.Count; i++)
                                {
                                    //We want to check here and skip this peer if we already have enough requests
                                    //Or if the peer is marked as busy
                                    int numOutstandingRequestsFromCurrentPeer = nonIncomingOutstandingRequests.Count(entry => entry.PeerConnectionInfo == currentRequestConnectionInfo[i]);
                                    
                                    int newRequestsFromCurrentPeer = 0;
                                    if (newRequests.ContainsKey(currentRequestConnectionInfo[i]))
                                        newRequestsFromCurrentPeer = newRequests[currentRequestConnectionInfo[i]].Count;

                                    if (numOutstandingRequestsFromCurrentPeer + newRequestsFromCurrentPeer >= DFS.MaxConcurrentPeerRequests)
                                        continue;

                                    //Its possible we have pulled out a peer for whom we no longer have availability info for
                                    if (SwarmChunkAvailability.PeerExistsInSwarm(currentRequestConnectionInfo[i].NetworkIdentifier) && 
                                        nonLocalPeerAvailability.ContainsKey(currentRequestConnectionInfo[i]) &&
                                        !SwarmChunkAvailability.IPEndPointBusy(currentRequestConnectionInfo[i].NetworkIdentifier, (IPEndPoint)currentRequestConnectionInfo[i].LocalEndPoint) &&
                                        SwarmChunkAvailability.IPEndPointOnline(currentRequestConnectionInfo[i].NetworkIdentifier, (IPEndPoint)currentRequestConnectionInfo[i].LocalEndPoint))
                                    {
                                        //which chunks does this peer have that we could use?
                                        ChunkFlags peerAvailability = nonLocalPeerAvailability[currentRequestConnectionInfo[i]].PeerChunkFlags;

                                        //We still look in order of chunk rarity
                                        for (int j = 0; j < chunkRarity.Count; j++)
                                        {
                                            if (!itemBuildTrackerDict.ContainsKey(chunkRarity[j]) && //If we don't have an outstanding request
                                                peerAvailability.FlagSet(chunkRarity[j])) //If the selected peer has this chunk
                                            {
                                                //We can now add the new request to the build dictionaries
                                                long chunkRequestIndex = Interlocked.Increment(ref DFS._totalNumRequestedChunks);
                                                ChunkAvailabilityRequest newChunkRequest = new ChunkAvailabilityRequest(ItemCheckSum, chunkRarity[j], currentRequestConnectionInfo[i], chunkRequestIndex);

                                                AddBuildLogLine("NewChunkRequest S2 Idx:" + newChunkRequest.ChunkIndex + ", Target:" + newChunkRequest.PeerConnectionInfo.LocalEndPoint.ToString() + ", Id:" + newChunkRequest.PeerConnectionInfo.NetworkIdentifier);

                                                if (newRequests.ContainsKey(currentRequestConnectionInfo[i]))
                                                    newRequests[currentRequestConnectionInfo[i]].Add(newChunkRequest);
                                                else
                                                    newRequests.Add(currentRequestConnectionInfo[i], new List<ChunkAvailabilityRequest> { newChunkRequest });

                                                newRequestCount++;

                                                itemBuildTrackerDict.Add(chunkRarity[j], newChunkRequest);

                                                //We only add one request per peer per loop
                                                break;
                                            }

                                            if (j == chunkRarity.Count - 1)
                                                //If we have made it here then this peer has no data we can use
                                                maxPeers--;
                                        }
                                    }
                                    else
                                        //If we have come across a peer whom we have zero availability for we reduce the maxPeers
                                        maxPeers--;

                                    //Once we have added a new request we should check if we have enough
                                    if (nonIncomingOutstandingRequests.Count + newRequestCount >= maxPeers * DFS.MaxConcurrentPeerRequests || //If the total number of outstanding requests is greater than the total number of peers * our concurrency factor
                                        nonIncomingOutstandingRequests.Count + newRequestCount >= numChunksLeft || //If the total number of requests is equal the number of chunks left
                                        nonIncomingOutstandingRequests.Count + newRequestCount >= DFS.MaxTotalItemRequests) //If the total number of requests is equal to the total requests
                                        break;
                                }

                                loopSafety++;
                            }
                            #endregion Step2
                        }
                    }
                    else if (incomingNetworkLoad > DFS.PeerBusyNetworkLoadThreshold)
                        AddBuildLogLine("Unable to make further chunk requests as incoming network load is " + incomingNetworkLoad.ToString("0.000") + ". Threshold is set at " + DFS.PeerBusyNetworkLoadThreshold.ToString("0.000"));
                }
                ///////////////////////////////
                ///// LEAVE ITEMLOCKER ////////
                ///////////////////////////////

                #region PerformChunkRequests
                //Send requests to each of the peers we have added to the contact list
                if (newRequests.Count > 0)
                {
                    foreach (KeyValuePair<ConnectionInfo, List<ChunkAvailabilityRequest>> request in newRequests)
                    {
                        //We can contact every peer separately so that no single peer can hold up the build
                        try
                        {
                            if (request.Value.Count > DFS.MaxConcurrentPeerRequests)
                                throw new Exception("Number of requests, " + request.Value.Count + ", for client, " + request.Key.NetworkIdentifier + ", exceeds the maximum, " + DFS.MaxConcurrentPeerRequests + ".");

                            for (int i = 0; i < request.Value.Count; i++)
                            {
                                UDPConnection.SendObject("DFS_ChunkAvailabilityInterestRequest", request.Value[i], (IPEndPoint)request.Key.LocalEndPoint, DFS.nullCompressionSRO);
                                //Console.WriteLine("   ...({0}) chunk {1} requested from {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), request.Value[i].ChunkIndex, request.Key.NetworkIdentifier);
                            }
                        }
                        catch (CommsException)
                        {
                            //If we can't connect to a peer we assume it's dead and don't try again
                            SwarmChunkAvailability.RemovePeerIPEndPointFromSwarm(request.Key.NetworkIdentifier, (IPEndPoint)request.Key.LocalEndPoint);

                            AddBuildLogLine("Removed " + request.Key.NetworkIdentifier + " from swarm due to CommsException while requesting a chunk.");
                            //Console.WriteLine("CommsException {0} - Removing requests for peer " + request.Key.NetworkIdentifier, DateTime.Now.ToString("HH:mm:ss.fff"));
                            //LogTools.LogException(ex, "ChunkRequestError");

                            //On error remove the chunk requests
                            lock (itemLocker)
                                itemBuildTrackerDict = (from current in itemBuildTrackerDict where !request.Value.Select(entry => entry.ChunkIndex).Contains(current.Key) select current).ToDictionary(dict => dict.Key, dict => dict.Value);

                            //Trigger a loop as there has been an error
                            itemBuildWait.Set();
                        }
                        catch (Exception ex)
                        {
                            LogTools.LogException(ex, "DFSAssembleItemError");
                            //Console.WriteLine("DFSAssembleItemError");

                            //On error remove the chunk requests
                            lock (itemLocker)
                                itemBuildTrackerDict = (from current in itemBuildTrackerDict where !request.Value.Select(entry => entry.ChunkIndex).Contains(current.Key) select current).ToDictionary(dict => dict.Key, dict => dict.Value);

                            //Trigger a loop as there has been an error
                            itemBuildWait.Set();
                        }
                    }
                }
                #endregion

                if (DFS.loggingEnabled) DFS._DFSLogger.Trace("Made " + (from current in newRequests select current.Value.Count).Sum() + " new chunk requests from " + newRequests.Count + " peers for item " + ItemIdentifier + ".");
                AddBuildLogLine("Made " + (from current in newRequests select current.Value.Count).Sum() + " new chunk requests from " + newRequests.Count + " peers for item " + ItemIdentifier + ".");

                #region Integrate Chunks
                bool integratedData = false;
                while (!ItemClosed)
                {
                    ChunkAvailabilityReply incomingReply = null;
                    try
                    {
                        lock (itemLocker)
                        {
                            if (chunkDataToIntegrateQueue.Count > 0)
                                incomingReply = chunkDataToIntegrateQueue.Dequeue();
                            else
                                incomingReply = null;
                        }

                        if (incomingReply == null)
                            break;
                        else
                        {
                            integratedData = true;
                            if (DFS.loggingEnabled) DFS._DFSLogger.Trace("ChunkAvailabilityReply dequeued for chunkIndex=" + incomingReply.ChunkIndex);
                            AddBuildLogLine("ChunkAvailabilityReply dequeued for chunkIndex=" + incomingReply.ChunkIndex);

                            if (!incomingReply.ChunkDataSet)
                                throw new Exception("Dequeued ChunkAvailabilityReply does not contain data.");

                            DateTime startTime = DateTime.Now;

                            #region Write Data To Disk With Validation
                            int writeCount = 0;
                            int writeRetryCountMax = 3;
                            while (true)
                            {
                                if (incomingReply.ChunkData == null)
                                    throw new Exception("incomingReply.ChunkData was null.");

                                //Copy the received bytes into the results array
                                ItemDataStream.Write(incomingReply.ChunkData, ChunkPositionLengthDict[incomingReply.ChunkIndex].Position);

                                //The data we have received may be correct but if the disk is faulty it may not read back the same 
                                if (ItemBuildTarget == ItemBuildTarget.Disk && ChunkCheckSums != null && ChunkCheckSums[incomingReply.ChunkIndex] != "")
                                {
                                    string chunkDiskMD5 = ItemDataStream.MD5(ChunkPositionLengthDict[incomingReply.ChunkIndex].Position, ChunkPositionLengthDict[incomingReply.ChunkIndex].Length);
                                    if (chunkDiskMD5 == ChunkCheckSums[incomingReply.ChunkIndex])
                                        break;
                                    else if (chunkDiskMD5 != ChunkCheckSums[incomingReply.ChunkIndex] && writeCount >= writeRetryCountMax)
                                    {
                                        AddBuildLogLine(" ... chunk index " + incomingReply.ChunkIndex + " data from peer " + incomingReply.SourceConnectionInfo + " failed validation during integration after "+writeRetryCountMax+" write attempts.");
                                        if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... chunk index " + incomingReply.ChunkIndex + " data from peer " + incomingReply.SourceConnectionInfo + " failed validation during integration after " + writeRetryCountMax + " write attempts.");

                                        throw new InvalidDataException("Chunk index " + incomingReply.ChunkIndex + " data failed validation on read back.");
                                    }
                                }
                                else
                                    break;

                                //If there was a failure we now check the incoming data to ensure that is correct
                                if (StreamTools.MD5(incomingReply.ChunkData) != ChunkCheckSums[incomingReply.ChunkIndex])
                                {
                                        AddBuildLogLine(" ... chunk index " + incomingReply.ChunkIndex + " data from peer " + incomingReply.SourceConnectionInfo + " was corrupted before integration.");
                                        if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... chunk index " + incomingReply.ChunkIndex + " data from peer " + incomingReply.SourceConnectionInfo + " was corrupted before integration.");

                                        throw new InvalidDataException("Chunk index " + incomingReply.ChunkIndex + " data corrupted before integration.");
                                }

                                writeCount++;
                            }
                            #endregion

                            if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... chunk data integrated in " + (DateTime.Now-startTime).TotalSeconds.ToString("0.0") + " secs ["+writeCount+"].");
                            AddBuildLogLine(" ... chunk data integrated in " + (DateTime.Now-startTime).TotalSeconds.ToString("0.0") + " secs ["+writeCount+"].");

                            //Record the chunk locally as available
                            SwarmChunkAvailability.RecordLocalChunkCompletion(incomingReply.ChunkIndex);

                            lock (itemLocker)
                            {
                                if (itemBuildTrackerDict.ContainsKey(incomingReply.ChunkIndex))
                                {
                                    //Set both again incase the request was readded before we got here
                                    itemBuildTrackerDict[incomingReply.ChunkIndex].RequestIncoming = true;
                                    itemBuildTrackerDict[incomingReply.ChunkIndex].RequestComplete = true;
                                }
                                else
                                {
                                    //We pretend we made the request already
                                    long chunkRequestIndex = Interlocked.Increment(ref DFS._totalNumRequestedChunks);
                                    ChunkAvailabilityRequest request = new ChunkAvailabilityRequest(ItemCheckSum, incomingReply.ChunkIndex, incomingReply.SourceConnectionInfo, chunkRequestIndex);
                                    request.RequestComplete = true;
                                    request.RequestIncoming = true;
                                    itemBuildTrackerDict.Add(incomingReply.ChunkIndex, request);
                                }
                            }

                            //We only broadcast our availability if the health metric of this chunk is less than
                            if (SwarmChunkAvailability.ChunkHealthMetric(incomingReply.ChunkIndex, TotalNumChunks) < 1)
                                SwarmChunkAvailability.BroadcastLocalAvailability(ItemCheckSum);

                            if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... completed integration for chunk " + incomingReply.ChunkIndex + " for item " + ItemCheckSum + ".");
                        }
                    }
                    catch (Exception)
                    {
                        //We only remove a request if there was an error
                        if (incomingReply != null)
                        {
                            lock (itemLocker)
                                itemBuildTrackerDict.Remove(incomingReply.ChunkIndex);
                        }

                        throw;
                    }
                }

                //If we have just completed the build we can set the build complete signal
                if (integratedData && LocalItemComplete())
                    itemBuildComplete.Set();
                #endregion
                #endregion

                //Wait for incoming data, a complete build or a timeout.
                if (newRequests.Count > 0)
                {
                    //If we made requests we can wait with a longer timeout, incoming repies should trigger us out sooner if not
                    if (WaitHandle.WaitAny(new WaitHandle[] { itemBuildWait, itemBuildComplete }, 5000) == WaitHandle.WaitTimeout)
                    {
                        //LogTools.LogException(new Exception("Build wait timeout after 4secs. Item complete=" + SwarmChunkAvailability.PeerIsComplete(NetworkComms.NetworkNodeIdentifier, TotalNumChunks)), "AssembleWaitTimeout");
                        //Console.WriteLine("      Build wait timeout after 5secs, {0}. Item complete=" + SwarmChunkAvailability.PeerIsComplete(NetworkComms.NetworkNodeIdentifier, TotalNumChunks), DateTime.Now.ToString("HH:mm:ss.fff"));
                    }
                }
                else
                    //If we made no requests, then we have enough outstanding or all peers are busy
                    WaitHandle.WaitAny(new WaitHandle[] { itemBuildWait, itemBuildComplete }, DFS.PeerBusyTimeoutMS);

                double currentElapsedBuildSecs = DFS.ElapsedExecutionSeconds - assembleStartTime;
                if (currentElapsedBuildSecs > assembleTimeoutSecs)
                    throw new TimeoutException("AssembleItem() has taken longer than " + assembleTimeoutSecs + " secs so has been timed out. Started at " + assembleStartTime + ", it is now " + DFS.ElapsedExecutionSeconds);

                if (!buildTimeHalfWayPoint && currentElapsedBuildSecs > assembleTimeoutSecs / 2.0)
                {
                    //If we get to the half way point we trigger another round of peer updates to see if we are missing anyone
                    SwarmChunkAvailability.UpdatePeerAvailability(ItemCheckSum, 1, 5000, AddBuildLogLine);
                    buildTimeHalfWayPoint = true;
                }

                if (DFS.DFSShutdownEvent.WaitOne(0))
                    return;
            }

            NetworkComms.RemoveGlobalConnectionCloseHandler(connectionShutdownDuringBuild);

            //Once we have a complete item we can broadcast our availability
            SwarmChunkAvailability.BroadcastLocalAvailability(ItemCheckSum);
            ItemBuildCompleted = DateTime.Now;

            //Close connections to other completed clients which are not a super peer
            //SwarmChunkAvailability.CloseConnectionToCompletedPeers(TotalNumChunks);
            chunkDataToIntegrateQueue = null;

            if (ItemClosed)
            {
                if (DFS.loggingEnabled) DFS._DFSLogger.Debug(" ... aborted DFS item assemble (" + this.ItemCheckSum + ") using " + SwarmChunkAvailability.NumPeersInSwarm() + " peers.");
                AddBuildLogLine("Aborted assemble (" + this.ItemCheckSum + ") using " + SwarmChunkAvailability.NumPeersInSwarm() + " peers.");
            }
            else
            {
                if (DFS.loggingEnabled) DFS._DFSLogger.Debug(" ... completed DFS item assemble (" + this.ItemCheckSum + ") using " + SwarmChunkAvailability.NumPeersInSwarm() + " peers.");
                AddBuildLogLine("Completed assemble (" + this.ItemCheckSum + ") using " + SwarmChunkAvailability.NumPeersInSwarm() + " peers.");
            }

            //try { GC.Collect(); }
            //catch (Exception) { }
        }

        /// <summary>
        /// Handle an incoming chunk reply
        /// </summary>
        /// <param name="incomingReply">The ChunkAvailabilityReply to handle</param>
        public void HandleIncomingChunkReply(ChunkAvailabilityReply incomingReply)
        {
            try
            {
                if (incomingReply == null)
                    throw new ArgumentNullException("incomingReply");

                if (incomingReply.SourceConnectionInfo == null)
                    throw new ArgumentNullException("incomingReply.SourceConnectionInfo");

                string logString = "";
                if (incomingReply.ReplyState == ChunkReplyState.DataIncluded)
                    logString = "Incoming SUCCESS reply from " + incomingReply.SourceConnectionInfo + " chunk:" + incomingReply.ChunkIndex + " (" + incomingReply.ItemCheckSum + ")";
                else if (incomingReply.ReplyState == ChunkReplyState.ItemOrChunkNotAvailable)
                    logString = "Incoming FAILURE reply from " + incomingReply.SourceConnectionInfo + " chunk:" + incomingReply.ChunkIndex + " (" + incomingReply.ItemCheckSum + ")";
                else
                    logString = "Incoming BUSY reply from " + incomingReply.SourceConnectionInfo + " chunk:" + incomingReply.ChunkIndex + " (" + incomingReply.ItemCheckSum + ")";

                AddBuildLogLine(logString);

                if (DFS.loggingEnabled) DFS._DFSLogger.Trace(logString);

                //We want to remove the chunk from the incoming build tracker so that it no longer counts as an outstanding request
                bool integrateChunk = false;
                lock (itemLocker)
                {
                    #region Decide Action
                    //If we still have an outstanding request for this chunk
                    if (itemBuildTrackerDict.ContainsKey(incomingReply.ChunkIndex))
                    {
                        //If data was included with the reply
                        if (incomingReply.ReplyState == ChunkReplyState.DataIncluded)
                        {
                            //If we are not currently handling a reply for the same chunk
                            if (!itemBuildTrackerDict[incomingReply.ChunkIndex].RequestIncoming)
                            {
                                itemBuildTrackerDict[incomingReply.ChunkIndex].RequestIncoming = true;
                                integrateChunk = true;

                                if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... incoming data matches existing request.");
                            }
                            else
                                if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... incoming data matches existing request but request already incoming.");
                        }
                        else
                        {
                            if (incomingReply.ReplyState == ChunkReplyState.ItemOrChunkNotAvailable)
                            {
                                //Delete any old references at the same time
                                SwarmChunkAvailability.RemoveOldPeerAtEndPoint(incomingReply.SourceConnectionInfo.NetworkIdentifier, (IPEndPoint)incomingReply.SourceConnectionInfo.RemoteEndPoint);

                                //If no data was included it probably means our availability for this peer is wrong
                                //If we remove the peer here it prevents us from nailing it
                                //if the peer still has the file an availability update should be on it's way to us
                                SwarmChunkAvailability.RemovePeerIPEndPointFromSwarm(incomingReply.SourceConnectionInfo.NetworkIdentifier, new IPEndPoint(IPAddress.Any, 0), true);
                            }
                            else if (incomingReply.ReplyState == ChunkReplyState.PeerBusy)
                            {
                                SwarmChunkAvailability.SetIPEndPointBusy(incomingReply.SourceConnectionInfo.NetworkIdentifier, (IPEndPoint)incomingReply.SourceConnectionInfo.RemoteEndPoint);
                                if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... set peer " + incomingReply.SourceConnectionInfo + " as busy.");
                            }

                            //If no data was included, regardless of state, we need to remove the request and allow it to be recreated
                            if (!itemBuildTrackerDict[incomingReply.ChunkIndex].RequestIncoming)
                                itemBuildTrackerDict.Remove(incomingReply.ChunkIndex);
                        }
                    }
                    else
                    {
                        if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... incoming reply does not exist in request list.");

                        //We no longer have the requst for this reply, no worries we can still use it
                        //If the checksums match, it includes data and we don't already have it
                        if (ItemCheckSum == incomingReply.ItemCheckSum && incomingReply.ReplyState == ChunkReplyState.DataIncluded && !SwarmChunkAvailability.PeerHasChunk(NetworkComms.NetworkIdentifier, incomingReply.ChunkIndex))
                        {
                            //We pretend we made the request already
                            long chunkRequestIndex = Interlocked.Increment(ref DFS._totalNumRequestedChunks);
                            ChunkAvailabilityRequest request = new ChunkAvailabilityRequest(ItemCheckSum, incomingReply.ChunkIndex, incomingReply.SourceConnectionInfo, chunkRequestIndex);
                            request.RequestIncoming = true;
                            itemBuildTrackerDict.Add(incomingReply.ChunkIndex, request);

                            integrateChunk = true;

                            if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... required chunk so integrating regardless.");
                        }
                    }
                    #endregion
                }

                //As soon as we have decided how to proceed we should be making further requests
                itemBuildWait.Set();

                if (integrateChunk)
                {
                    if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... adding ChunkAvailabilityReply to queue for chunk " + incomingReply.ChunkIndex + " for item " + ItemCheckSum + ".");

                    if (incomingReply.ChunkData == null)
                        throw new NullReferenceException("Chunk data cannot be null.");

                    //We expect the final chunk to have a smaller length
                    if (incomingReply.ChunkData.Length != ChunkSizeInBytes && incomingReply.ChunkIndex < TotalNumChunks - 1)
                        throw new Exception("Provided bytes was " + incomingReply.ChunkData.Length + " bytes in length although " + ChunkSizeInBytes + " bytes were expected.");

                    if (incomingReply.ChunkIndex > TotalNumChunks)
                        throw new Exception("Provided chunkindex (" + incomingReply.ChunkIndex + ") is greater than the total num of the chunks for this item (" + TotalNumChunks + ").");

                    //If we have set the chunk check sums we can check it here
                    if (ChunkCheckSums != null && ChunkCheckSums[incomingReply.ChunkIndex] != "")
                    {
                        if (StreamTools.MD5(incomingReply.ChunkData) == ChunkCheckSums[incomingReply.ChunkIndex])
                        {
                            AddBuildLogLine(" ... chunk index " + incomingReply.ChunkIndex + " data was validated.");
                            if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... chunk index " + incomingReply.ChunkIndex + " data was validated.");
                        }
                        else
                        {
                            AddBuildLogLine(" ... chunk index " + incomingReply.ChunkIndex + " data from peer " + incomingReply.SourceConnectionInfo + " failed validation.");
                            if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... chunk index " + incomingReply.ChunkIndex + " data from peer " + incomingReply.SourceConnectionInfo + " failed validation.");

                            throw new InvalidDataException("Chunk index " + incomingReply.ChunkIndex + " data failed validation.");
                        }
                    }

                    lock (itemLocker)
                        chunkDataToIntegrateQueue.Enqueue(incomingReply);
                }
                else
                    if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... nothing to integrate for item " + ItemCheckSum + ".");
            }
            catch (Exception)
            {
                //We only remove a request if there was an error
                lock (itemLocker)
                    itemBuildTrackerDict.Remove(incomingReply.ChunkIndex);

                throw;
            }
        }

        /// <summary>
        /// Returns true if the requested chunk is available locally
        /// </summary>
        /// <param name="chunkIndex">The chunk index to check</param>
        /// <returns></returns>
        public bool ChunkAvailableLocally(byte chunkIndex)
        {
            return SwarmChunkAvailability.PeerHasChunk(NetworkComms.NetworkIdentifier, chunkIndex);
        }

        /// <summary>
        /// Copies the contents of the item data stream to the provided destination stream
        /// </summary>
        /// <param name="destinationStream">The destination stream</param>
        public void CopyItemDataStream(Stream destinationStream)
        {
            ItemDataStream.CopyTo(destinationStream, 0, (int)ItemDataStream.Length, 8000);
        }

        /// <summary>
        /// Returns a streamSendWrapper that contains the item
        /// </summary>
        /// <returns></returns>
        public StreamTools.StreamSendWrapper GetItemStream()
        {
            if (LocalItemComplete())
                return new StreamTools.StreamSendWrapper(ItemDataStream, 0, ItemBytesLength);
            else
                throw new Exception("Attempted to acces DFS item data stream when item was not complete.");
        }

        /// <summary>
        /// Returns a StreamSendWrapper corresponding to the requested chunkIndex.
        /// </summary>
        /// <param name="chunkIndex">The desired chunk index data</param>
        /// <returns></returns>
        public StreamTools.StreamSendWrapper GetChunkStream(byte chunkIndex)
        {
            //If we have made it this far we are returning data
            if (SwarmChunkAvailability.PeerHasChunk(NetworkComms.NetworkIdentifier, chunkIndex))
            {
                lock (itemLocker) TotalChunkSupplyCount++;

                return new StreamTools.StreamSendWrapper(ItemDataStream, ChunkPositionLengthDict[chunkIndex].Position, ChunkPositionLengthDict[chunkIndex].Length);
            }
            else
                throw new Exception("Attempted to access DFS chunk which was not available locally");
        }

        /// <summary>
        /// Once the item has been fully assembled the completed bytes can be access via this method.
        /// </summary>
        /// <returns></returns>
        public byte[] AccessItemBytes()
        {
            if (LocalItemComplete())
            {
                if (LocalItemValid())
                    return ItemDataStream.ToArray();
                else
                    throw new Exception("Attempted to access item bytes but they are corrupted.");
            }
            else
                throw new Exception("Attempted to access item bytes before all chunks had been retrieved.");
        }

        /// <summary>
        /// Returns true if the item data validates correctly
        /// </summary>
        /// <returns></returns>
        public bool LocalItemValid()
        {
            if (ItemDataStream.MD5() == ItemCheckSum)
                return true;
            else
                return false;
        }

        /// <summary>
        /// Uses the loaded stream and builds individual chunk checksums
        /// </summary>
        /// <returns></returns>
        public void BuildChunkCheckSums()
        {
            if (LocalItemValid())
            {
                ChunkCheckSums = new string[TotalNumChunks];
                for (int i = 0; i < TotalNumChunks; i++)
                    ChunkCheckSums[i] = this.ItemDataStream.MD5(i * ChunkSizeInBytes, ChunkSizeInBytes);
            }
            else
                throw new Exception("Current loaded data is not valid.");
        }

        /// <summary>
        /// Returns true once all chunks have been received and the item has been validated
        /// </summary>
        /// <returns></returns>
        public bool LocalItemComplete()
        {
            lock (itemLocker)
                return SwarmChunkAvailability.PeerIsComplete(NetworkComms.NetworkIdentifier, TotalNumChunks);
        }

        /// <summary>
        /// Dispose of this DFS item
        /// </summary>
        public void Dispose()
        {
            lock (itemLocker)
            {
                if (ItemDataStream != null)
                {
                    ItemDataStream.Dispose(true);

                    //Delete the disk file if it exists
                    if (File.Exists(ItemIdentifier + ".DFSItemData"))
                        File.Delete(ItemIdentifier + ".DFSItemData");
                }

                ItemClosed = true;
                itemBuildComplete.Set();
            }
        }

        /// <summary>
        /// Load the specified distributed item. Does not add the .DFSItem extension to the fileName
        /// </summary>
        /// <param name="fileName">The DFS item to load</param>
        /// <param name="itemDataStream">The DFS item data</param>
        /// <param name="seedConnectionInfoList">The connecitonInfo corresponding with potential seeds</param>
        /// <returns></returns>
        public static DistributedItem Load(string fileName, Stream itemDataStream, List<ConnectionInfo> seedConnectionInfoList)
        {
            DistributedItem loadedItem = DPSManager.GetDataSerializer<ProtobufSerializer>().DeserialiseDataObject<DistributedItem>(File.ReadAllBytes(fileName));
            loadedItem.ItemDataStream = new StreamTools.ThreadSafeStream(itemDataStream);
            loadedItem.InitialiseChunkPositionLengthDict();
            loadedItem.SwarmChunkAvailability = new SwarmChunkAvailability(seedConnectionInfoList, loadedItem.TotalNumChunks);
            //loadedItem.BuildChunkCheckSums();
            return loadedItem;
        }

        /// <summary>
        /// Save this distributed item (not including item data), adds .DFSItem extension, using the provided filename.
        /// </summary>
        /// <param name="fileName">The filename to use</param>
        public void Save(string fileName)
        {
            File.WriteAllBytes(fileName + ".DFSItem", DPSManager.GetDataSerializer<ProtobufSerializer>().SerialiseDataObject<DistributedItem>(this).ThreadSafeStream.ToArray());
        }
    }

    /// <summary>
    /// A utility class used to randomly shuffle a list of type T
    /// </summary>
    public static class ShuffleList
    {
        static Random randomGen = new Random();

        /// <summary>
        /// Randomly shuffle list
        /// </summary>
        /// <typeparam name="T">The type of list</typeparam>
        /// <param name="list">The list to shuffle</param>
        /// <returns>The shuffled list</returns>
        public static IList<T> Shuffle<T>(IList<T> list)
        {
            IList<T> listCopy = list.ToList();
            int n = listCopy.Count;
            while (n > 1)
            {
                n--;
                int k = randomGen.Next(n + 1);
                T value = listCopy[k];
                listCopy[k] = listCopy[n];
                listCopy[n] = value;
            }

            return listCopy;
        }
    }
}
