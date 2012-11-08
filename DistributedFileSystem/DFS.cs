//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using NetworkCommsDotNet;
using System.Threading;
using Common.Logging;
using System.Net;
using DPSBase;

namespace DistributedFileSystem
{
    /// <summary>
    /// Provides functionality to distribute large files across a cluster and get from multiple sources at once.
    /// </summary>
    public static class DFS
    {
        public const int ChunkRequestTimeoutMS = 10000;
        public const int MinChunkSizeInBytes = 2097152;

        public const int NumConcurrentRequests = 2;
        public const int NumTotalGlobalRequests = 8;

        public const int PeerBusyTimeoutMS = 500;

        /// <summary>
        /// While the peer network load goes above this value it will always reply with a busy response 
        /// </summary>
        public const double PeerBusyNetworkLoadThreshold = 0.8;

        public const int ItemBuildTimeoutSecsPerMB = 6;

        static object globalDFSLocker = new object();
        /// <summary>
        /// Dictionary which contains a cache of the distributed items
        /// </summary>
        static Dictionary<string, DistributedItem> swarmedItemsDict = new Dictionary<string, DistributedItem>();

        static int ChunkCacheDataTimeoutSecs = 300;
        static int ChunkCacheDataCleanupIntervalSecs = 310;
        static DateTime lastChunkCacheCleanup = DateTime.Now;
        static object chunkDataCacheLocker = new object();
        /// <summary>
        /// Temporary storage for chunk data which is awaiting info
        /// </summary>
        static Dictionary<ShortGuid, Dictionary<long, ChunkDataWrapper>> chunkDataCache = new Dictionary<ShortGuid, Dictionary<long, ChunkDataWrapper>>();

        internal static List<string> allowedPeerIPs = new List<string>();
        internal static List<string> disallowedPeerIPs = new List<string>();

        internal static bool DFSShutdownRequested { get; private set; }
        public static bool DFSInitialised { get; private set; }

        public static int MinTargetLocalPort { get; set; }
        public static int MaxTargetLocalPort { get; set; }

        static Thread linkWorkerThread;
        static string linkTargetIP;
        static int linkTargetPort;

        /// <summary>
        /// We keep a reference to sendReceiveOptions which use no data compression in the DFS
        /// </summary>
        static SendReceiveOptions nullCompressionSRO;

        /// <summary>
        /// We keep a reference to sendReceiveOptions which use a high recieve priority
        /// </summary>
        static SendReceiveOptions highPrioRecieveSRO;

        public static bool IsLinked { get; private set; }
        public static DFSLinkMode LinkMode { get; private set; }
        static int linkRequestTimeoutSecs = 10;
        static int linkRequestIntervalSecs = 5;
        /// <summary>
        /// The number of link items to build concurrently
        /// </summary>
        static int concurrentNumLinkItems = 2;

        internal static TaskFactory DFSTaskFactory;

        public static int TotalNumCompletedChunkRequests { get; private set; }
        private static object TotalNumCompletedChunkRequestsLocker = new object();

        static DFS()
        {
            MinTargetLocalPort = 10001;
            MaxTargetLocalPort = 10999;

            DFSTaskFactory = new TaskFactory(new LimitedParallelismTaskScheduler(Environment.ProcessorCount));

            nullCompressionSRO = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                            new List<DataProcessor>(),
                            new Dictionary<string, string>());

            highPrioRecieveSRO = (SendReceiveOptions)NetworkComms.DefaultSendReceiveOptions.Clone();
            //highPrioRecieveSRO.Options.Add("ReceiveHandlePriority", Enum.GetName(typeof(ThreadPriority), ThreadPriority.AboveNormal));
        }

        /// <summary>
        /// Initialises the DFS to run on the current local IP and default comms port.
        /// </summary>
        public static void InitialiseDFS(int initialPort, bool rangeRandomPortFailover = true)
        {
            try
            {
                if (TCPConnection.ExistingLocalListenEndPoints().Count > 0)
                    throw new CommsSetupShutdownException("Unable to initialise DFS if already listening for incoming connections.");

                //Load the allowed ip addresses
                LoadAllowedDisallowedPeerIPs();

                DFSShutdownRequested = false;

                NetworkComms.IgnoreUnknownPacketTypes = true;

                #region Add Packet Handlers
                NetworkComms.AppendGlobalIncomingPacketHandler<ItemAssemblyConfig>("DFS_IncomingLocalItemBuild", IncomingLocalItemBuild);
                NetworkComms.AppendGlobalIncomingPacketHandler<string[]>("DFS_RequestLocalItemBuild", RequestLocalItemBuilds);

                NetworkComms.AppendGlobalIncomingPacketHandler<ChunkAvailabilityRequest>("DFS_ChunkAvailabilityInterestRequest", IncomingChunkInterestRequest, highPrioRecieveSRO);

                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("DFS_ChunkAvailabilityInterestReplyData", IncomingChunkInterestReplyData);
                NetworkComms.AppendGlobalIncomingPacketHandler<ChunkAvailabilityReply>("DFS_ChunkAvailabilityInterestReplyInfo", IncomingChunkInterestReplyInfo);

                NetworkComms.AppendGlobalIncomingPacketHandler<string>("DFS_ChunkAvailabilityRequest", IncomingChunkAvailabilityRequest, highPrioRecieveSRO);
                NetworkComms.AppendGlobalIncomingPacketHandler<PeerChunkAvailabilityUpdate>("DFS_PeerChunkAvailabilityUpdate", IncomingPeerChunkAvailabilityUpdate, highPrioRecieveSRO);

                NetworkComms.AppendGlobalIncomingPacketHandler<ItemRemovalUpdate>("DFS_ItemRemovalUpdate", IncomingItemRemovalUpdate, highPrioRecieveSRO);

                NetworkComms.AppendGlobalIncomingPacketHandler<string>("DFS_KnownPeersRequest", KnownPeersRequest, highPrioRecieveSRO);
                NetworkComms.AppendGlobalIncomingPacketHandler<DFSLinkRequest>("DFS_ItemLinkRequest", IncomingRemoteItemLinkRequest);

                NetworkComms.AppendGlobalConnectionCloseHandler(DFSConnectionShutdown);
                #endregion

                #region OpenIncomingPorts
                List<IPAddress> availableIPAddresses = NetworkComms.AllAllowedIPs();
                List<IPEndPoint> localEndPointAttempts;
                try
                {
                    localEndPointAttempts = (from current in availableIPAddresses select new IPEndPoint(current, initialPort)).ToList();
                    TCPConnection.StartListening(localEndPointAttempts, false);
                }
                catch (Exception)
                {
                    if (rangeRandomPortFailover)
                    {
                        for (int tryPort = MinTargetLocalPort; tryPort <= MaxTargetLocalPort; tryPort++)
                        {
                            try
                            {
                                localEndPointAttempts = (from current in availableIPAddresses select new IPEndPoint(current, tryPort)).ToList();
                                TCPConnection.StartListening(localEndPointAttempts, false);
                                break;
                            }
                            catch (Exception) { }

                            if (tryPort == MaxTargetLocalPort)
                                throw new CommsSetupShutdownException("Failed to find local available listen port while trying to initialise DFS.");
                        }
                    }
                    else
                        throw;
                }
                #endregion

                if (DFS.loggingEnabled) DFS.logger.Info("Initialised DFS");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_DFSIntialise");
            }

            DFSInitialised = true;
        }

        /// <summary>
        /// Initialies this DFS peer to repeat all items made available by some other peer
        /// </summary>
        /// <param name="linkTargetIP"></param>
        /// <param name="linkTargetPort"></param>
        public static void InitialiseDFSLink(string linkTargetIP, int linkTargetPort, DFSLinkMode linkMode)
        {
            if (!DFSInitialised)
                throw new Exception("Attempted to initialise DFS link before DFS had been initialised.");

            if (linkTargetIP == NetworkComms.AllAllowedIPs()[0].ToString() && linkTargetPort == NetworkComms.DefaultListenPort)
                throw new Exception("Attempted to initialise DFS link with local peer.");

            lock (globalDFSLocker)
            {
                if (IsLinked) throw new Exception("Attempted to initialise DFS link once already initialised.");

                DFS.linkTargetIP = linkTargetIP;
                DFS.linkTargetPort = linkTargetPort;
                DFS.LinkMode = linkMode;

                linkWorkerThread = new Thread(LinkModeWorker);
                linkWorkerThread.Name = "DFSLinkWorkerThread";
                linkWorkerThread.Start();
                IsLinked = true;
            }
        }

        /// <summary>
        /// Background worker thread which maintains the link depending on the selected link mode
        /// </summary>
        private static void LinkModeWorker()
        {
            do
            {
                try
                {
                    //This links any existing local items and retrieves a list of all remote items
                    TCPConnection primaryServer = TCPConnection.GetConnection(new ConnectionInfo(linkTargetIP, linkTargetPort));

                    DFSLinkRequest availableLinkTargetItems = primaryServer.SendReceiveObject<DFSLinkRequest>("DFS_ItemLinkRequest", "DFS_ItemLinkRequest", linkRequestTimeoutSecs * 1000, new DFSLinkRequest(AllLocalDFSItemsWithBuildTime(), false));
                    if (DFS.loggingEnabled) DFS.logger.Trace("LinkModeWorker could link " + availableLinkTargetItems.AvailableItems.Count+ " items from target.");

                    if (LinkMode == DFSLinkMode.LinkAndRepeat)
                    {
                        //We get a list of items we don't have
                        string[] allLocalItems = AllLocalDFSItemKeys(false);

                        //We only begin a new link cycle if all local items are complete
                        if (allLocalItems.Length == AllLocalDFSItemKeys(true).Length)
                        {
                            //Pull out the items we want to request
                            //We order the items by item creation time starting with the newest
                            string[] itemsToRequest = (from current in availableLinkTargetItems.AvailableItems
                                                     where !allLocalItems.Contains(current.Key)
                                                     orderby current.Value descending
                                                     select current.Key).ToArray();

                            //Make the request for items we do not have
                            if (itemsToRequest.Length > 0)
                            {
                                primaryServer.SendObject("DFS_RequestLocalItemBuild", itemsToRequest.Take(concurrentNumLinkItems).ToArray());
                                if (DFS.loggingEnabled) DFS.logger.Trace("LinkModeWorker made a request to link " + itemsToRequest.Take(concurrentNumLinkItems).Count() + " items.");
                            }
                        }
                    }
                }
                catch (CommsException)
                {
                    //We were unable to talk with our link peer, just keep trying until they hopefully respond
                }
                catch (Exception e)
                {
                    NetworkComms.LogError(e, "RepeaterWorkerError");
                }

                Thread.Sleep(linkRequestIntervalSecs * 1000);

            } while (!DFSShutdownRequested);

            IsLinked = false;
        }

        private static void LoadAllowedDisallowedPeerIPs()
        {
            string allowedFileName = "DFSAllowedPeerIPs.txt";
            string disallowedFilename = "DFSDisallowedPeerIPs.txt";

            //DFSAllowedPeerIPs.txt
            //Allowed takes precedence
            //We have to check a directory up as well incase this is running in the win client manager
            if (File.Exists(allowedFileName) || File.Exists("..\\" + allowedFileName))
            {
                string[] lines;
                if (File.Exists(allowedFileName))
                    lines = File.ReadAllLines(allowedFileName);
                else
                    lines = File.ReadAllLines("..\\" + allowedFileName);

                lines = (from current in lines
                         where !current.StartsWith("#") && current != ""
                         select current).ToArray();

                for (int i = 0; i < lines.Length; i++)
                    allowedPeerIPs.Add(lines[i]);
            }
            else if (File.Exists(disallowedFilename) || File.Exists("..\\" + disallowedFilename))
            {
                string[] lines;

                if (File.Exists(disallowedFilename))
                    lines = File.ReadAllLines(disallowedFilename);
                else
                    lines = File.ReadAllLines("..\\" + disallowedFilename);

                lines = (from current in lines
                         where !current.StartsWith("#") && current != ""
                         select current).ToArray();

                for (int i = 0; i < lines.Length; i++)
                    disallowedPeerIPs.Add(lines[i]);
            }

            if (disallowedPeerIPs.Count > 0 && allowedPeerIPs.Count > 0)
                throw new Exception("Can not set both allowed and disallowed peers.");
        }

        public static void ShutdownDFS()
        {
            DFSShutdownRequested = true;
            RemoveAllItemsFromLocalOnly();
            NetworkComms.Shutdown();

            DFSInitialised = false;

            if (loggingEnabled) DFS.logger.Debug("DFS Shutdown.");
        }

        #region Logging
        internal static object loggingLocker = new object();
        internal static bool loggingEnabled = false;
        internal static ILog logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Access the networkComms logger externally. Allows logging from external sources
        /// </summary>
        public static ILog Logger
        {
            get { return logger; }
        }

        /// <summary>
        /// Enable logging in networkComms using the provided logging adaptor
        /// </summary>
        /// <param name="loggingAdaptor"></param>
        public static void EnableLogging(ILoggerFactoryAdapter loggingAdaptor)
        {
            lock (loggingLocker)
            {
                loggingEnabled = true;
                Common.Logging.LogManager.Adapter = loggingAdaptor;
                logger = LogManager.GetCurrentClassLogger();
            }
        }

        /// <summary>
        /// Disable logging in networkComms
        /// </summary>
        public static void DisableLogging()
        {
            lock (loggingLocker)
            {
                loggingEnabled = false;
                Common.Logging.LogManager.Adapter = new Common.Logging.Simple.NoOpLoggerFactoryAdapter();
            }
        }
        #endregion

        /// <summary>
        /// Returns true if the provided item is already present within the swarm
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static bool ItemAlreadyInLocalCache(DistributedItem item)
        {
            lock (globalDFSLocker)
            {
                if (swarmedItemsDict.ContainsKey(item.ItemCheckSum))
                {
                    if (swarmedItemsDict[item.ItemCheckSum].ItemBytesLength == item.ItemBytesLength)
                        return true;
                    else
                        throw new Exception("Potential Md5 conflict detected in DFS.");
                }

                return false;
            }
        }

        /// <summary>
        /// Returns true if the provided itemCheckSum is present within the local cache
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static bool ItemAlreadyInLocalCache(string itemCheckSum)
        {
            lock (globalDFSLocker)
            {
                return swarmedItemsDict.ContainsKey(itemCheckSum);
            }
        }

        /// <summary>
        /// Returns the most recently completed item from the swarmedItemsDict. Returns null if there are no DFS items.
        /// </summary>
        /// <returns></returns>
        public static DistributedItem MostRecentlyCompletedItem()
        {
            lock (globalDFSLocker)
            {
                if (swarmedItemsDict.Count > 0)
                    return (from current in swarmedItemsDict.Values orderby current.ItemBuildCompleted descending select current).First();
                else
                    return null;
            }
        }

        /// <summary>
        /// Returns the distributed item with the provided itemCheckSum. Returns null if item is not found.
        /// </summary>
        /// <param name="itemCheckSum"></param>
        /// <returns></returns>
        public static DistributedItem GetDistributedItemByChecksum(string itemCheckSum)
        {
            lock (globalDFSLocker)
            {
                if (swarmedItemsDict.ContainsKey(itemCheckSum))
                    return swarmedItemsDict[itemCheckSum];
                else
                    return null;
            }
        }

        /// <summary>
        /// Returns the distributed item with the provided identifier. Returns null if item is not found.
        /// </summary>
        /// <param name="itemIdentifier"></param>
        /// <returns></returns>
        public static DistributedItem GetDistributedItemByIdentifier(string itemIdentifier)
        {
            lock (globalDFSLocker)
            {
                foreach (DistributedItem item in swarmedItemsDict.Values)
                {
                    if (item.ItemIdentifier == itemIdentifier)
                        return item;
                }
            }

            return null;
        }

        /// <summary>
        /// Remove an item from the DFS. Possibly swarmWide and with or without a removal broadcast
        /// </summary>
        /// <param name="itemCheckSum"></param>
        /// <param name="removeSwarmWide"></param>
        /// <param name="broadcastRemoval"></param>
        public static void RemoveItem(string itemCheckSum, bool broadcastRemoval = true, bool removeSwarmWide = false)
        {
            if (!broadcastRemoval && removeSwarmWide)
                throw new Exception("BroadcastRemoval must be true if RemoveSwarmWide is also true.");

            DistributedItem itemToRemove = null;

            lock (globalDFSLocker)
            {
                if (swarmedItemsDict.ContainsKey(itemCheckSum))
                {
                    itemToRemove = swarmedItemsDict[itemCheckSum];

                    //Remove the item locally
                    swarmedItemsDict.Remove(itemCheckSum);
                }
            }

            //This BroadcastItemRemoval has to be outside lock (globalDFSLocker) otherwise it can deadlock
            if (itemToRemove != null)
            {
                if(broadcastRemoval)
                //Broadcast to the swarm we are removing this file
                    itemToRemove.SwarmChunkAvailability.BroadcastItemRemoval(itemCheckSum, removeSwarmWide);

                //Dispose of the distributed item incase it has any open file handles
                itemToRemove.Dispose();
            }

            //try { GC.Collect(); }
            //catch (Exception) { }
        }

        public static void RemoveAllItemsFromLocalOnly(bool broadcastRemoval = false)
        {
            string[] keysToRemove;
            lock (globalDFSLocker)
                keysToRemove = swarmedItemsDict.Keys.ToArray();

            foreach (string key in keysToRemove)
                RemoveItem(key, broadcastRemoval);
        }

        /// <summary>
        /// Remove any items from the DFS with a matching itemTypeStr
        /// </summary>
        /// <param name="ItemTypeStr"></param>
        /// <param name="broadcastRemoval"></param>
        public static void RemoveAllItemsFromLocalOnly(string ItemTypeStr, bool broadcastRemoval = false)
        {
            List<string> keysToRemove = new List<string>();
            lock (globalDFSLocker)
            {
                foreach (DistributedItem item in swarmedItemsDict.Values)
                {
                    if (item.ItemTypeStr == ItemTypeStr)
                        keysToRemove.Add(item.ItemCheckSum);
                }
            }

            foreach (string key in keysToRemove)
                RemoveItem(key, broadcastRemoval);
        }

        /// <summary>
        /// Pings all clients for a tracked item to make sure they are still alive. 
        /// Any clients which fail to respond within a sensible time are removed for the item swarm.
        /// </summary>
        /// <param name="itemCheckSum"></param>
        public static void UpdateItemSwarmStatus(string itemCheckSum, int responseTimeMS)
        {
            DistributedItem itemToUpdate = null;

            lock (globalDFSLocker)
            {
                if (swarmedItemsDict.ContainsKey(itemCheckSum))
                    itemToUpdate = swarmedItemsDict[itemCheckSum];
            }

            if (itemToUpdate != null)
                itemToUpdate.UpdateItemSwarmStatus(responseTimeMS);
        }

        /// <summary>
        /// Introduces a new item into the swarm and sends a distribution command to the originating requester
        /// </summary>
        /// <param name="requestOriginNetworkIdentifier"></param>
        /// <param name="itemToDistribute"></param>
        public static void PushItemToPeer(Connection peerConnection, DistributedItem itemToDistribute, string completedPacketType)
        {
            try
            {
                lock (globalDFSLocker)
                {
                    //First double check to see if it's already in the swarm
                    if (!ItemAlreadyInLocalCache(itemToDistribute))
                        swarmedItemsDict.Add(itemToDistribute.ItemCheckSum, itemToDistribute);
                    else
                        itemToDistribute = swarmedItemsDict[itemToDistribute.ItemCheckSum];

                    //We add the requester to the item swarm at this point
                    itemToDistribute.SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(peerConnection, new ChunkFlags(0));
                    itemToDistribute.IncrementPushCount();
                }

                //We could contact other known super peers to see if they also have this file

                //Send the config information to the client that wanted the file
                peerConnection.SendObject("DFS_IncomingLocalItemBuild", new ItemAssemblyConfig(itemToDistribute, completedPacketType));

                if (DFS.loggingEnabled) DFS.logger.Debug("Pushed DFS item " + itemToDistribute.ItemCheckSum + " to peer " + peerConnection + ".");
            }
            catch (CommsException)
            {
                //NetworkComms.LogError(ex, "CommsError_AddItemToSwarm");
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_AddItemToSwarm");
            }

            //try { GC.Collect(); }
            //catch (Exception) { }
        }

        /// <summary>
        /// Adds a distributed item to the local cache and informs any known peers of the item availability
        /// </summary>
        /// <param name="itemToAdd"></param>
        /// <returns>The item added to the local cache. May not be the provided itemToAdd if an item with the same checksum already existed.</returns>
        public static DistributedItem AddItem(DistributedItem itemToAdd)
        {
            try
            {
                lock (globalDFSLocker)
                {
                    //First double check to see if it's already in the swarm
                    if (!ItemAlreadyInLocalCache(itemToAdd))
                    {
                        swarmedItemsDict.Add(itemToAdd.ItemCheckSum, itemToAdd);
                        if (DFS.loggingEnabled) DFS.logger.Debug("... added existing item to DFS (" + itemToAdd.ItemCheckSum + ").");
                    }
                    else
                    {
                        itemToAdd = swarmedItemsDict[itemToAdd.ItemCheckSum];
                        if (DFS.loggingEnabled) DFS.logger.Debug("... added new item to DFS (" + itemToAdd.ItemCheckSum + ").");
                    }
                }

                //Send the config information to the client that wanted the file
                //NetworkComms.SendObject("DFS_IncomingLocalItemBuild, requestOriginConnectionId, false, new ItemAssemblyConfig(itemToDistribute, completedPacketType));
                itemToAdd.SwarmChunkAvailability.BroadcastLocalAvailability(itemToAdd.ItemCheckSum);
            }
            catch (CommsException)
            {
                //NetworkComms.LogError(ex, "CommsError_AddItemToSwarm");
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_AddItemToSwarm");
            }

            //try { GC.Collect(); }
            //catch (Exception) { }

            return itemToAdd;
        }

        /// <summary>
        /// Communicates with the provided peer to see if any item swarms can be linked. This is a single link event, possibly use InitialiseDFSLink() for a maintained link
        /// </summary>
        /// <param name="peerIP"></param>
        /// <param name="peerPort"></param>
        public static void CheckForSharedItems(string peerIP, int peerPort)
        {
            try
            {
                //NetworkComms.SendObject("DFS_ItemLinkRequest", peerIP, peerPort, false, new DFSLinkRequest(AllLocalDFSItemsWithBuildTime(), false));
                TCPConnection.GetConnection(new ConnectionInfo(peerIP, peerPort)).SendObject("DFS_ItemLinkRequest", new DFSLinkRequest(AllLocalDFSItemsWithBuildTime(), false));
            }
            catch (CommsException)
            {

            }
        }

        public static string[] AllLocalDFSItemKeys(bool completeItemsOnly = true)
        {
            string[] returnArray;

            lock (globalDFSLocker)
            {
                //returnArray = swarmedItemsDict.Keys.ToArray();
                returnArray = (from current in swarmedItemsDict where (completeItemsOnly ? current.Value.LocalItemComplete() : true) select current.Key).ToArray();
            }

            return returnArray;
        }

        public static Dictionary<string, DateTime> AllLocalDFSItemsWithBuildTime(bool completeItemsOnly = true)
        {
            string[] itemCheckSums = AllLocalDFSItemKeys(completeItemsOnly);

            Dictionary<string, DateTime> returnDict = new Dictionary<string, DateTime>();

            lock (globalDFSLocker)
            {
                foreach (string item in itemCheckSums)
                {
                    if (swarmedItemsDict.ContainsKey(item))
                        returnDict.Add(item, swarmedItemsDict[item].ItemBuildCompleted);
                }
            }

            return returnDict;
        }

        /// <summary>
        /// Flick through the chunk data cache and remove any items that have timed out
        /// </summary>
        private static void CheckForChunkDataCacheTimeouts()
        {
            lock (chunkDataCacheLocker)
            {
                if ((DateTime.Now - lastChunkCacheCleanup).TotalSeconds > ChunkCacheDataCleanupIntervalSecs)
                {
                    if (DFS.loggingEnabled) DFS.logger.Trace("Starting ChunkDataCache cleanup.");

                    int removedCount = 0;

                    ShortGuid[] peerKeys = chunkDataCache.Keys.ToArray();
                    for (int i = 0; i < peerKeys.Length; i++)
                    {
                        long[] dataSequenceKeys = chunkDataCache[peerKeys[i]].Keys.ToArray();
                        for (int k = 0; k < dataSequenceKeys.Length; k++)
                        {
                            if ((DateTime.Now - chunkDataCache[peerKeys[i]][dataSequenceKeys[k]].TimeCreated).TotalSeconds > ChunkCacheDataTimeoutSecs)
                            {
                                //If we have timed out data we will remove it
                                chunkDataCache[peerKeys[i]].Remove(dataSequenceKeys[k]);
                                removedCount++;
                            }
                        }

                        //If there is no longer any data for a particular peer we remove the peer entry
                        if (chunkDataCache[peerKeys[i]].Count == 0)
                            chunkDataCache.Remove(peerKeys[i]);
                    }

                    if (DFS.loggingEnabled) DFS.logger.Trace("Completed ChunkDataCache cleanup having removed " + removedCount + " items.");

                    lastChunkCacheCleanup = DateTime.Now;
                }
            }
        }

        #region NetworkCommsDelegates
        /// <summary>
        /// If a connection is disconnected we want to make sure we handle it within the DFS
        /// </summary>
        /// <param name="disconnectedConnectionIdentifier"></param>
        private static void DFSConnectionShutdown(Connection connection)
        {
            //We want to run this as a task as we want the shutdown to return ASAP
            DFSTaskFactory.StartNew(new Action(() =>
            {
                try
                {
                    lock (globalDFSLocker)
                    {
                        //Remove peer from any items
                        foreach (var item in swarmedItemsDict)
                            item.Value.SwarmChunkAvailability.RemovePeerFromSwarm(connection.ConnectionInfo.NetworkIdentifier);
                    }

                    if (loggingEnabled) DFS.logger.Debug("DFSConnectionShutdown triggered for peer " + connection + ".");
                }
                catch (CommsException e)
                {
                    NetworkComms.LogError(e, "CommsError_DFSConnectionShutdown");
                }
                catch (Exception e)
                {
                    NetworkComms.LogError(e, "Error_DFSConnectionShutdown");
                }
            }));
        }

        /// <summary>
        /// Used by a client when requesting a list of known peers
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void KnownPeersRequest(PacketHeader packetHeader, Connection connection, string itemCheckSum)
        {
            try
            {
                DistributedItem selectedItem = null;

                if (DFS.loggingEnabled) DFS.logger.Trace(" ... known peers request for item (" + itemCheckSum + ").");

                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(itemCheckSum))
                        selectedItem = swarmedItemsDict[itemCheckSum];
                }

                if (selectedItem == null)
                {
                    //Reply with an empty "DFS_KnownPeersUpdate" so that we don't hold up the peer
                    connection.SendObject("DFS_KnownPeersUpdate", new string[] { "" }, nullCompressionSRO);

                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    connection.SendObject("DFS_ItemRemovalUpdate", itemCheckSum, nullCompressionSRO);
                }
                else
                    connection.SendObject("DFS_KnownPeersUpdate", selectedItem.SwarmChunkAvailability.AllPeerEndPoints(), nullCompressionSRO);
            }
            catch (CommsException)
            {
                //NetworkComms.LogError(e, "CommsError_IncomingChunkAvailabilityRequest");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_KnownPeersRequest");
            }
        }

        /// <summary>
        /// Received by this DFS if a server is telling this instance to build a local file
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingLocalItemBuild(PacketHeader packetHeader, Connection connection, ItemAssemblyConfig assemblyConfig)
        {
            DistributedItem newItem = null;

            try
            {
                if (DFS.loggingEnabled) DFS.logger.Debug("IncomingLocalItemBuild from " + connection + " for item " + assemblyConfig.ItemCheckSum + ".");

                //We check to see if we already have the necessary file locally
                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(assemblyConfig.ItemCheckSum))
                    {
                        if (swarmedItemsDict[assemblyConfig.ItemCheckSum].ItemBytesLength != assemblyConfig.TotalItemSizeInBytes)
                            throw new Exception("Possible MD5 conflict detected.");
                        else
                            newItem = swarmedItemsDict[assemblyConfig.ItemCheckSum];
                    }
                    else
                    {
                        newItem = new DistributedItem(assemblyConfig);
                        swarmedItemsDict.Add(assemblyConfig.ItemCheckSum, newItem);
                    }
                }

                //Build the item from the swarm
                //If the item is already complete this will return immediately
                newItem.AssembleItem((int)(ItemBuildTimeoutSecsPerMB * (assemblyConfig.TotalItemSizeInBytes / 1024.0)));

                //Copy the result to the disk if required by the build target
                if (assemblyConfig.ItemBuildTarget == ItemBuildTarget.Both)
                {
                    using (FileStream sw = new FileStream(assemblyConfig.ItemIdentifier, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose))
                        newItem.CopyItemDataStream(sw);
                }

                SendReceiveOptions nullOptions = new SendRecieveOptions<NullSerializer>(new Dictionary<string,string>());

                //Once complete we pass the item bytes back into network comms
                //If an exception is thrown we will probably not call this method, timeouts in other areas should then handle and can restart the build.
                if (newItem.LocalItemComplete() && assemblyConfig.CompletedPacketType != "")
                {
                    PacketHeader itemPacketHeader = new PacketHeader(assemblyConfig.CompletedPacketType, newItem.ItemBytesLength);
                    //We set the item checksum so that the entire distributed item can be easily retrieved later
                    itemPacketHeader.SetOption(PacketHeaderStringItems.PacketIdentifier, newItem.ItemCheckSum);
                    byte[] itemBytes = newItem.AccessItemBytes();
                    NetworkComms.TriggerGlobalPacketHandlers(itemPacketHeader, connection, new MemoryStream(itemBytes, 0, itemBytes.Length, false, true), nullOptions);
                }

                //Close any connections which are no longer required
                newItem.SwarmChunkAvailability.CloseConnectionToCompletedPeers(newItem.TotalNumChunks);

                if (DFS.loggingEnabled) DFS.logger.Debug("IncomingLocalItemBuild completed for item with MD5 " + assemblyConfig.ItemCheckSum + ".");
            }
            catch (CommsException)
            {
                //Crap an error has happened, let people know we probably don't have a good file
                RemoveItem(assemblyConfig.ItemCheckSum);
                //NetworkComms.LogError(e, "CommsError_IncomingLocalItemBuild");
            }
            catch (Exception e)
            {
                //Crap an error has happened, let people know we probably don't have a good file
                RemoveItem(assemblyConfig.ItemCheckSum);

                if (newItem != null)
                    NetworkComms.LogError(e, "Error_IncomingLocalItemBuild", newItem.BuildLog().Aggregate(Environment.NewLine, (p, q) => { return p + Environment.NewLine + q; }));
                else
                    NetworkComms.LogError(e, "Error_IncomingLocalItemBuild", "newItem==null so no build log was available.");
            }
        }

        /// <summary>
        /// A remote peer has request a push of the provided itemCheckSums. This method is used primiarly when in repeater mode
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="itemCheckSum"></param>
        private static void RequestLocalItemBuilds(PacketHeader packetHeader, Connection connection, string[] itemCheckSums)
        {
            try
            {
                DistributedItem[] selectedItems = null;
                lock (globalDFSLocker)
                    selectedItems = (from current in swarmedItemsDict where itemCheckSums.Contains(current.Key) select current.Value).ToArray();

                if (selectedItems !=null && selectedItems.Length > 0)
                    foreach(DistributedItem item in selectedItems)
                        DFS.PushItemToPeer(connection, item, "");
            }
            catch (CommsException)
            {
                //NetworkComms.LogError(e, "CommsError_IncomingLocalItemBuild");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_RequestLocalItemBuild");
            }
        }

        /// <summary>
        /// Received when a peer request a chunk
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingChunkInterestRequest(PacketHeader packetHeader, Connection connection, ChunkAvailabilityRequest incomingRequest)
        {
            try
            {
                //A peer has requested a specific chunk of data, we will only provide it if we are not already providing it to someone else

                //Console.WriteLine("... ({0}) received request for chunk {1} from {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingChunkInterestRequest from " + connection + " for " + incomingRequest.ItemCheckSum + ", chunkIndex " + incomingRequest.ChunkIndex + ".");

                DistributedItem selectedItem = null;
                lock (globalDFSLocker)
                    if (swarmedItemsDict.ContainsKey(incomingRequest.ItemCheckSum))
                        selectedItem = swarmedItemsDict[incomingRequest.ItemCheckSum];

                if (selectedItem == null)
                {
                    //First reply and say the peer can't have the requested data. This prevents a request timing out
                    connection.SendObject("DFS_ChunkAvailabilityInterestReply", new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), nullCompressionSRO);

                    //Inform peer that we don't actually have the requested item
                    connection.SendObject("DFS_ItemRemovalUpdate", incomingRequest.ItemCheckSum, nullCompressionSRO);
                }
                else
                {
                    if (!selectedItem.ChunkAvailableLocally(incomingRequest.ChunkIndex))
                    {
                        //First reply and say the peer can't have the requested data. This prevents a request timing out
                        //NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), ProtobufSerializer.Instance, NullCompressor.Instance);
                        connection.SendObject("DFS_ChunkAvailabilityInterestReply", new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), nullCompressionSRO);

                        //If the peer thinks we have a chunk we dont we send them an update so that they are corrected
                        //NetworkComms.SendObject("DFS_PeerChunkAvailabilityUpdate", sourceConnectionId, false, new PeerChunkAvailabilityUpdate(incomingRequest.ItemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkNodeIdentifier)));
                        connection.SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(incomingRequest.ItemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkIdentifier)), nullCompressionSRO);
                    }
                    else
                    {
                        //If we are a super peer we always have to respond to the request
                        if (NetworkComms.AverageNetworkLoad(10) > DFS.PeerBusyNetworkLoadThreshold && !selectedItem.SwarmChunkAvailability.PeerIsSuperPeer(NetworkComms.NetworkIdentifier))
                        {
                            //We can return a busy reply if we are currently experiencing high demand
                            //NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.PeerBusy), ProtobufSerializer.Instance, NullCompressor.Instance);
                            connection.SendObject("DFS_ChunkAvailabilityInterestReply", new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.PeerBusy), nullCompressionSRO);
                        }
                        else
                        {
                            //try
                            //{
                            //We get the data here
                            StreamSendWrapper chunkData = selectedItem.GetChunkStream(incomingRequest.ChunkIndex);

                            if (DFS.loggingEnabled) DFS.logger.Trace("Pushing chunkData to " + connection + " for item:" + incomingRequest.ItemCheckSum + ", chunkIndex:" + incomingRequest.ChunkIndex + ".");

                            long packetSequenceNumber;
                            connection.SendObject("DFS_ChunkAvailabilityInterestReplyData", chunkData, nullCompressionSRO, out packetSequenceNumber);

                            if (DFS.loggingEnabled) DFS.logger.Trace("Pushing chunkInfo to " + connection + " for item:" + incomingRequest.ItemCheckSum + ", chunkIndex:" + incomingRequest.ChunkIndex + ".");
                            
                            connection.SendObject("DFS_ChunkAvailabilityInterestReplyInfo", new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, packetSequenceNumber), nullCompressionSRO);

                            lock (TotalNumCompletedChunkRequestsLocker) TotalNumCompletedChunkRequests++;
                            
                            //If we have sent data there is a good chance we have used up alot of memory
                            //This seems to be an efficient place for a garbage collection
                            //try { GC.Collect(); }
                            //catch (Exception) { }
                        }
                    }
                }
            }
            catch (CommsException)
            {
                //Something fucked happened.
                //Console.WriteLine("IncomingChunkInterestRequestError. Error logged.");
                //NetworkComms.LogError(ex, "CommsError_IncomingChunkInterestRequest");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingChunkInterestRequest");
            }
        }

        /// <summary>
        /// Received when a peer sends us the data portion of a chunk possibly following a request
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingChunkInterestReplyData(PacketHeader packetHeader, Connection connection, byte[] incomingData)
        {
            try
            {
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingChunkInterestReplyData from " + connection + " containing " + incomingData.Length + " bytes.");

                ChunkAvailabilityReply existingChunkAvailabilityReply = null;

                try
                {
                    lock (chunkDataCacheLocker)
                    {
                        if (!chunkDataCache.ContainsKey(connection.ConnectionInfo.NetworkIdentifier))
                            chunkDataCache.Add(connection.ConnectionInfo.NetworkIdentifier, new Dictionary<long, ChunkDataWrapper>());

                        if (!packetHeader.ContainsOption(PacketHeaderLongItems.PacketSequenceNumber))
                            throw new Exception("The dataSequenceNumber option appears to missing from the packetHeader. What has been changed?");

                        long dataSequenceNumber = packetHeader.GetOption(PacketHeaderLongItems.PacketSequenceNumber);
                        if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(dataSequenceNumber))
                        {
                            if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier][dataSequenceNumber] == null)
                                throw new Exception("An entry existed for the desired dataSequenceNumber but the entry was null.");
                            else if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier][dataSequenceNumber].ChunkAvailabilityReply == null)
                                throw new Exception("An entry existed for the desired ChunkAvailabilityReply but the entry was null.");

                            //The info beat the data so we handle it here
                            existingChunkAvailabilityReply = chunkDataCache[connection.ConnectionInfo.NetworkIdentifier][dataSequenceNumber].ChunkAvailabilityReply;
                            existingChunkAvailabilityReply.SetChunkData(incomingData);

                            chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Remove(existingChunkAvailabilityReply.DataSequenceNumber);
                            if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Count == 0)
                                chunkDataCache.Remove(connection.ConnectionInfo.NetworkIdentifier);
                        }
                        else
                        {
                            chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Add(dataSequenceNumber, new ChunkDataWrapper(dataSequenceNumber, incomingData));
                            if (DFS.loggingEnabled) DFS.logger.Trace("Added ChunkData to chunkDataCache from " + connection + ", sequence number:" + dataSequenceNumber + " , containing " + incomingData.Length + " bytes.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    NetworkComms.LogError(ex, "Error_IncomingChunkInterestReplyDataInner");
                }

                if (existingChunkAvailabilityReply != null)
                {
                    DistributedItem item = null;
                    lock (globalDFSLocker)
                    {
                        if (swarmedItemsDict.ContainsKey(existingChunkAvailabilityReply.ItemCheckSum))
                            item = swarmedItemsDict[existingChunkAvailabilityReply.ItemCheckSum];
                    }

                    if (item != null)
                        item.HandleIncomingChunkReply(existingChunkAvailabilityReply, connection);
                }

                CheckForChunkDataCacheTimeouts();
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingChunkInterestReplyData");
            }
        }

        /// <summary>
        /// Received when a peer sends us a chunk data information possibly following a request
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingChunkInterestReplyInfo(PacketHeader packetHeader, Connection connection, ChunkAvailabilityReply incomingReply)
        {
            try
            {
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingChunkInterestReplyInfo from " + connection + " for item " + incomingReply.ItemCheckSum + ", chunkIndex " + incomingReply.ChunkIndex + ".");

                DistributedItem item = null;
                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(incomingReply.ItemCheckSum))
                        item = swarmedItemsDict[incomingReply.ItemCheckSum];
                }

                if (item != null)
                {
                    //Do we have the data yet?
                    lock (chunkDataCacheLocker)
                    {
                        //Given we always send the data first it would be a really fucked up if the data is not where we expect it to be
                        if (chunkDataCache.ContainsKey(connection.ConnectionInfo.NetworkIdentifier) && chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(incomingReply.DataSequenceNumber))
                        {
                            incomingReply.SetChunkData(chunkDataCache[connection.ConnectionInfo.NetworkIdentifier][incomingReply.DataSequenceNumber].Data);
                            chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Remove(incomingReply.DataSequenceNumber);

                            if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Count == 0)
                                chunkDataCache.Remove(connection.ConnectionInfo.NetworkIdentifier);
                        }
                        else if (incomingReply.ReplyState == ChunkReplyState.DataIncluded)
                        {
                            //throw new Exception("Unable to locate chunk data (this info is probably really late) in cache for item:" + item.ItemCheckSum + ", chunkIndex:" + incomingReply.ChunkIndex + ", dataSequenceNumber:" + incomingReply.DataSequenceNumber);
                            //We have beaten the data, we will add the chunkavailability reply instead and wait then let the incoming data trigger the handle
                            if (!chunkDataCache.ContainsKey(connection.ConnectionInfo.NetworkIdentifier))
                                chunkDataCache.Add(connection.ConnectionInfo.NetworkIdentifier, new Dictionary<long,ChunkDataWrapper>());

                            chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Add(incomingReply.DataSequenceNumber, new ChunkDataWrapper(incomingReply));
                            if (DFS.loggingEnabled) DFS.logger.Trace("Added ChunkAvailabilityReply to chunkDataCache (awaiting data) from " + connection + ", sequence number:" + incomingReply.DataSequenceNumber + ".");
                        }
                    }

                    if (incomingReply.ChunkDataSet || incomingReply.ReplyState != ChunkReplyState.DataIncluded)
                        item.HandleIncomingChunkReply(incomingReply, connection);
                }
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingChunkInterestReplyInfo");
            }
        }

        /// <summary>
        /// A remote peer is announcing that it has an updated availability of chunks
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingPeerChunkAvailabilityUpdate(PacketHeader packetHeader, Connection connection, PeerChunkAvailabilityUpdate updateDetails)
        {
            try
            {
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingPeerChunkAvailabilityUpdate from " + connection + " for item " + updateDetails.ItemCheckSum + "(" + updateDetails.ChunkFlags.NumCompletedChunks() + ").");

                DistributedItem selectedItem = null;
                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(updateDetails.ItemCheckSum))
                        selectedItem = swarmedItemsDict[updateDetails.ItemCheckSum];
                }

                if (selectedItem != null)
                    selectedItem.SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(connection, updateDetails.ChunkFlags);
                else
                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    connection.SendObject("DFS_ItemRemovalUpdate", updateDetails.ItemCheckSum);
            }
            catch (CommsException)
            {
                //Meh some comms error happened.
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingPeerChunkAvailabilityUpdate");
            }
        }

        /// <summary>
        /// A remote peer is requesting chunk availability for this local peer
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingChunkAvailabilityRequest(PacketHeader packetHeader, Connection connection, string itemCheckSum)
        {
            try
            {
                DistributedItem selectedItem = null;

                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(itemCheckSum))
                        selectedItem = swarmedItemsDict[itemCheckSum];
                }

                if (selectedItem == null)
                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    connection.SendObject("DFS_ItemRemovalUpdate", itemCheckSum, nullCompressionSRO);
                else
                    connection.SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(itemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkIdentifier)), nullCompressionSRO);

                if (DFS.loggingEnabled) DFS.logger.Trace(" ... replied to IncomingChunkAvailabilityRequest (" + itemCheckSum + ").");
            }
            catch (CommsException)
            {
                //NetworkComms.LogError(e, "CommsError_IncomingChunkAvailabilityRequest");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingChunkAvailabilityRequest");
            }
        }

        /// <summary>
        /// A remote peer is informing us that they no longer have an item
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingItemRemovalUpdate(PacketHeader packetHeader, Connection connection, ItemRemovalUpdate itemRemovalUpdate)
        {
            try
            {
                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(itemRemovalUpdate.ItemCheckSum))
                    {
                        if (itemRemovalUpdate.RemoveSwarmWide)
                            //If this is a swarmwide removal then we get rid of our local copy as well
                            RemoveItem(itemRemovalUpdate.ItemCheckSum, false);
                        else
                            //If this is not a swarm wide removal we just remove this peer from our local swarm copy
                            swarmedItemsDict[itemRemovalUpdate.ItemCheckSum].SwarmChunkAvailability.RemovePeerFromSwarm(connection.ConnectionInfo.NetworkIdentifier, true);
                    }
                }

                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingItemRemovalUpdate from " + connection + " for " + itemRemovalUpdate.ItemCheckSum + ". " + (itemRemovalUpdate.RemoveSwarmWide ? "SwamWide" : "Local Only") + ".");
            }
            catch (CommsException e)
            {
                NetworkComms.LogError(e, "CommsError_IncomingPeerItemRemovalUpdate");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingPeerItemRemovalUpdate");
            }
        }

        /// <summary>
        /// A remote peer is trying to link dfs items
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="linkRequestData"></param>
        private static void IncomingRemoteItemLinkRequest(PacketHeader packetHeader, Connection connection, DFSLinkRequest linkRequestData)
        {
            try
            {
                var localItemKeys = AllLocalDFSItemsWithBuildTime();

                //We only check for potential links if the remote end has provided us with some items to link
                if (linkRequestData.AvailableItems.Count > 0)
                {
                    //Get the item matches using linq. Could also use localItemKeys.Intersect<long>(linkRequestData.AvailableItemCheckSums);
                    string[] itemsToLink = (from current in localItemKeys.Keys
                                          join remote in linkRequestData.AvailableItems.Keys on current equals remote
                                          select current).ToArray();

                    lock (globalDFSLocker)
                    {
                        for (int i = 0; i < itemsToLink.Length; i++)
                        {
                            //If we still have the item then we add the remote end as a new super peer
                            if (swarmedItemsDict.ContainsKey(itemsToLink[i]))
                                swarmedItemsDict[itemsToLink[i]].SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(connection, new ChunkFlags(swarmedItemsDict[itemsToLink[i]].TotalNumChunks), true);
                        }
                    }
                }

                //If this link request is from the original requester then we reply with our own items list
                if (!linkRequestData.LinkRequestReply)
                    connection.SendObject("DFS_ItemLinkRequest", new DFSLinkRequest(localItemKeys, true), nullCompressionSRO);
            }
            catch (CommsException e)
            {
                NetworkComms.LogError(e, "CommsError_IncomingRemoteItemLinkRequest");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingRemoteItemLinkRequest");
            }
        }
        #endregion
    }
}
