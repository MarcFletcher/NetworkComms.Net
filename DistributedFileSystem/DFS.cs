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
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using NetworkCommsDotNet;
using System.Threading;
using NLog;
using System.Net;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;
using NLog.Config;

namespace DistributedFileSystem
{
    /// <summary>
    /// Provides functionality to rapidly distribute large files across a cluster of peers.
    /// </summary>
    public static class DFS
    {
        /// <summary>
        /// The number of milliseconds after which a chunk request times out.
        /// </summary>
        public const int ChunkRequestTimeoutMS = 20000;

        /// <summary>
        /// The minimum size of DFS item chunks
        /// </summary>
        public const int MinChunkSizeInBytes = 2097152;

        /// <summary>
        /// The maximum number of concurrent chunk requests to make to the same peer
        /// </summary>
        public const int MaxConcurrentPeerRequests = 2;

        /// <summary>
        /// The total number of simultaneous chunk requests for a given item
        /// </summary>
        public const int MaxTotalItemRequests = 8;

        /// <summary>
        /// The maximum number of DFS items that can be built concurrently
        /// </summary>
        public const int MaxConcurrentLocalItemBuild = 3;

        /// <summary>
        /// The maximum number of times a chunk request can timeout from a peer before it is removed from the swarm
        /// </summary>
        public const int PeerMaxNumTimeouts = 2;

        /// <summary>
        /// The time in milliseconds after which a peer busy flag is removed
        /// </summary>
        public const int PeerBusyTimeoutMS = 1000;

        /// <summary>
        /// While the peer network load goes above this value it will always reply with a busy response 
        /// </summary>
        public const double PeerBusyNetworkLoadThreshold = 0.94;

        /// <summary>
        /// The number of seconds to allow per MB when building DFS items
        /// </summary>
        public const int ItemBuildTimeoutSecsPerMB = 12;

        internal static object globalDFSLocker = new object();
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

        internal static ManualResetEvent DFSShutdownEvent { get; private set; }

        /// <summary>
        /// True if the DFS has been initialised
        /// </summary>
        public static bool DFSInitialised { get; private set; }

        /// <summary>
        /// The minimum port number that will be used when initialising the DFS
        /// </summary>
        public static int MinTargetLocalPort { get; set; }

        /// <summary>
        /// The maximum port number that will be used when initialising the DFS
        /// </summary>
        public static int MaxTargetLocalPort { get; set; }

        /// <summary>
        /// If true ensures all DFS items include chunk MD5 list. Also on build clients will validate the chunk MD5
        /// </summary>
        public static bool ValidateEachChunkMD5 { get; set; }

        /// <summary>
        /// Runs a background timer which can be used to decide timeouts for item builds
        /// </summary>
        static Thread elapsedTimerThread;

        /// <summary>
        /// The number of seconds since the initialisation of the DFS. Used as an internal timer, rather than DateTime.Now, to 
        /// ensure builds do not time out when a suspended process is restarted.
        /// </summary>
        public static long ElapsedExecutionSeconds { get; private set; }

        /// <summary>
        /// Linking this DFS to others
        /// </summary>
        static Thread linkWorkerThread;
        static string linkTargetIP;
        static int linkTargetPort;

        /// <summary>
        /// We keep a reference to sendReceiveOptions which use no data compression in the DFS
        /// </summary>
        internal static SendReceiveOptions nullCompressionSRO;

        /// <summary>
        /// We keep a reference to sendReceiveOptions which use a high Receive priority
        /// </summary>
        static SendReceiveOptions highPrioReceiveSRO;

        /// <summary>
        /// True if this DFS is linked with another peer
        /// </summary>
        public static bool IsLinked { get; private set; }

        /// <summary>
        /// The link mode being used
        /// </summary>
        public static DFSLinkMode LinkMode { get; private set; }
        static int linkRequestTimeoutSecs = 10;
        static int linkRequestIntervalSecs = 5;
        /// <summary>
        /// The number of link items to build concurrently
        /// </summary>
        static int concurrentNumLinkItems = 2;

        /// <summary>
        /// A privte task factory for assembling new local DFS items. If we use the NetworkComms.TaskFactory we can end up deadlocking and prevent incoming packets from being handled.
        /// </summary>
        internal static TaskFactory BuildTaskFactory;

        /// <summary>
        /// The total number of completed chunk requests across all DFS items
        /// </summary>
        public static int TotalNumCompletedChunkRequests { get; private set; }
        private static object TotalNumCompletedChunkRequestsLocker = new object();

        static DFS()
        {
            MinTargetLocalPort = 10000;
            MaxTargetLocalPort = 10999;

            //BuildTaskFactory = new TaskFactory(new LimitedParallelismTaskScheduler(MaxConcurrentLocalItemBuild));
            BuildTaskFactory = new TaskFactory();

            nullCompressionSRO = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                            new List<DataProcessor>(),
                            new Dictionary<string, string>());

            highPrioReceiveSRO = (SendReceiveOptions)NetworkComms.DefaultSendReceiveOptions.Clone();
            highPrioReceiveSRO.Options.Add("ReceiveHandlePriority", Enum.GetName(typeof(ThreadPriority), ThreadPriority.AboveNormal));
        }

        /// <summary>
        /// Initialises the DFS
        /// </summary>
        /// <param name="initialPort">The local listen port to use</param>
        /// <param name="rangeRandomPortFailover">True if a random port should be select if the initialPort is unavailable</param>
        public static void InitialiseDFS(int initialPort, bool rangeRandomPortFailover = true)
        {
            try
            {
                if (initialPort > MaxTargetLocalPort || initialPort < MinTargetLocalPort)
                    throw new CommsSetupShutdownException("Provided initial DFS port must be within the MinTargetLocalPort and MaxTargetLocalPort range.");

                if (Connection.AllExistingLocalListenEndPoints().Count > 0)
                    throw new CommsSetupShutdownException("Unable to initialise DFS if already listening for incoming connections.");

                DFSShutdownEvent = new ManualResetEvent(false);

                elapsedTimerThread = new Thread(ElapsedTimerWorker);
                elapsedTimerThread.Name = "DFSElapsedTimerThread";
                elapsedTimerThread.IsBackground = true;
                elapsedTimerThread.Start();

                //Load the allowed IP addresses
                LoadAllowedDisallowedPeerIPs();

                NetworkComms.IgnoreUnknownPacketTypes = true;

                #region Add Packet Handlers
                NetworkComms.AppendGlobalIncomingPacketHandler<ItemAssemblyConfig>("DFS_IncomingLocalItemBuild", IncomingLocalItemBuild);
                NetworkComms.AppendGlobalIncomingPacketHandler<string[]>("DFS_RequestLocalItemBuild", RequestLocalItemBuilds);

                //UDP
                //DO NOT MAKE THIS METHOD USE highPriority. If this method blocks it prevents other data coming in
                NetworkComms.AppendGlobalIncomingPacketHandler<ChunkAvailabilityRequest>("DFS_ChunkAvailabilityInterestRequest", IncomingChunkInterestRequest);

                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("DFS_ChunkAvailabilityInterestReplyData", IncomingChunkInterestReplyData);
             
                //UDP & TCP
                NetworkComms.AppendGlobalIncomingPacketHandler<ChunkAvailabilityReply>("DFS_ChunkAvailabilityInterestReplyInfo", IncomingChunkInterestReplyInfo);

                //UDP
                NetworkComms.AppendGlobalIncomingPacketHandler<string>("DFS_ChunkAvailabilityRequest", IncomingChunkAvailabilityRequest, highPrioReceiveSRO);
                //UDP
                NetworkComms.AppendGlobalIncomingPacketHandler<PeerChunkAvailabilityUpdate>("DFS_PeerChunkAvailabilityUpdate", IncomingPeerChunkAvailabilityUpdate, highPrioReceiveSRO);

                //UDP
                NetworkComms.AppendGlobalIncomingPacketHandler<ItemRemovalUpdate>("DFS_ItemRemovalUpdate", IncomingItemRemovalUpdate, highPrioReceiveSRO);

                //UDP
                NetworkComms.AppendGlobalIncomingPacketHandler<KnownPeerEndPoints>("DFS_KnownPeersUpdate", KnownPeersUpdate, highPrioReceiveSRO);
                NetworkComms.AppendGlobalIncomingPacketHandler<string>("DFS_KnownPeersRequest", KnownPeersRequest, highPrioReceiveSRO);

                //TCP
                NetworkComms.AppendGlobalIncomingPacketHandler<DFSLinkRequest>("DFS_ItemLinkRequest", IncomingRemoteItemLinkRequest);

                NetworkComms.AppendGlobalConnectionCloseHandler(DFSConnectionShutdown);
                #endregion

                #region OpenIncomingPorts
                try
                {
                    Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Any, initialPort));
                    Connection.StartListening(ConnectionType.UDP, new IPEndPoint(IPAddress.Any, initialPort));
                }
                catch (Exception)
                {
                    //If an exception occurred first reset NetworkComms.Net
                    NetworkComms.Shutdown();

                    if (rangeRandomPortFailover)
                    {
                        //Keep trying to listen on an ever increasing port number
                        for (int tryPort = MinTargetLocalPort; tryPort <= MaxTargetLocalPort; tryPort++)
                        {
                            try
                            {
                                Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Any, tryPort));
                                Connection.StartListening(ConnectionType.UDP, new IPEndPoint(IPAddress.Any, tryPort));

                                //Once we are successfully listening we can break
                                break;
                            }
                            catch (Exception) { NetworkComms.Shutdown(); }

                            if (tryPort == MaxTargetLocalPort)
                                throw new CommsSetupShutdownException("Failed to find local available listen port while trying to initialise DFS.");
                        }
                    }
                    else
                        throw;
                }

                //Do some validation
                if (Connection.ExistingLocalListenEndPoints(ConnectionType.TCP).Except(Connection.ExistingLocalListenEndPoints(ConnectionType.UDP)).Count() > 0)
                    throw new CommsSetupShutdownException("Port mismatch when comparing TCP and UDP local listen end points.");

                if ((from current in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP)
                     where ((IPEndPoint)current).Port > MaxTargetLocalPort || ((IPEndPoint)current).Port < MinTargetLocalPort
                     select current).Count() > 0)
                     throw new CommsSetupShutdownException("Local port selected that is not within the valid range.");
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
        /// Initialises this DFS peer to repeat all items available on the linkTargetIP
        /// </summary>
        /// <param name="linkTargetIP">The IPAddress corresponding with the link seed</param>
        /// <param name="linkTargetPort">The port corresponding with the link seed</param>
        /// <param name="linkMode">The link mode to be used</param>
        public static void InitialiseDFSLink(string linkTargetIP, int linkTargetPort, DFSLinkMode linkMode)
        {
            if (!DFSInitialised)
                throw new Exception("Attempted to initialise DFS link before DFS had been initialised.");

            if (linkTargetIP == HostInfo.IP.FilteredLocalAddresses()[0].ToString() &&
                Connection.ExistingLocalListenEndPoints(ConnectionType.TCP, new IPEndPoint(HostInfo.IP.FilteredLocalAddresses()[0], linkTargetPort)).Count() > 0)
                throw new Exception("Attempted to initialise DFS link with local peer.");

            lock (globalDFSLocker)
            {
                if (IsLinked) throw new Exception("Attempted to initialise DFS link once already initialised.");

                DFS.linkTargetIP = linkTargetIP;
                DFS.linkTargetPort = linkTargetPort;
                DFS.LinkMode = linkMode;

                linkWorkerThread = new Thread(LinkModeWorker);
                linkWorkerThread.Name = "DFSLinkWorkerThread";
                linkWorkerThread.IsBackground = true;
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

                    DFSLinkRequest availableLinkTargetItems = primaryServer.SendReceiveObject<DFSLinkRequest, DFSLinkRequest>("DFS_ItemLinkRequest", "DFS_ItemLinkRequest", linkRequestTimeoutSecs * 1000, new DFSLinkRequest(AllLocalDFSItemsWithBuildTime(), false));
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
                                primaryServer.SendObject("DFS_RequestLocalItemBuild", itemsToRequest.Take(concurrentNumLinkItems).ToArray(), nullCompressionSRO);
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

                if (DFSShutdownEvent.WaitOne(linkRequestIntervalSecs * 1000))
                    break;

            } while (true);

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

        /// <summary>
        /// Shutdown the DFS. All local DFS items are deleted.
        /// </summary>
        public static void ShutdownDFS()
        {
            DFSShutdownEvent.Set();
            RemoveAllItemsFromLocalOnly();
            NetworkComms.Shutdown();

            DFSInitialised = false;

            lock (globalDFSLocker)
            {
                //Cleanup the disk DFS item directory if necessary.
                if (Directory.Exists("DFS_" + NetworkComms.NetworkIdentifier))
                    Directory.Delete("DFS_" + NetworkComms.NetworkIdentifier, true);
            }

            if (elapsedTimerThread != null && !elapsedTimerThread.Join(2000))
            {
                try
                {
                    NetworkComms.LogError(new Exception("DFS elapsedTimerThread did not close after 2 seconds."), "DFSShutdownError");
                    elapsedTimerThread.Abort();
                }
                catch (Exception) { }
            }

            if (loggingEnabled) DFS.logger.Debug("DFS Shutdown.");
        }

        /// <summary>
        /// Runs in the background to estimate the elapsed time of the application.
        /// We can't use DateTime as elapsed time should not include time during which process was suspended
        /// </summary>
        private static void ElapsedTimerWorker()
        {
            ElapsedExecutionSeconds = 0;

            try
            {
                while (true)
                {
                    if (DFSShutdownEvent.WaitOne(1000))
                        break;

                    ElapsedExecutionSeconds++;
                }
            }
            catch (ThreadAbortException)
            {

            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "DFSElapsedWorkerError");
            }
        }

        #region Logging
        internal static object loggingLocker = new object();
        internal static bool loggingEnabled = false;
        internal static Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Access the networkComms logger externally. Allows logging from external sources
        /// </summary>
        public static Logger Logger
        {
            get { return logger; }
        }

        /// <summary>
        /// Enable logging in networkComms using the provided logging adaptor
        /// </summary>
        /// <param name="loggingAdaptor"></param>
        public static void EnableLogging(LoggingConfiguration loggingAdaptor)
        {
            lock (loggingLocker)
            {
                loggingEnabled = true;
                LogManager.Configuration = loggingAdaptor;
                logger = LogManager.GetCurrentClassLogger();
                LogManager.EnableLogging();
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
                LogManager.DisableLogging();
            }
        }
        #endregion

        /// <summary>
        /// Returns true if the provided item is already present within the swarm
        /// </summary>
        /// <param name="item">The relevant DFS item</param>
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
        /// Returns true if an item with a matching itemCheckSum is present within the local cache
        /// </summary>
        /// <param name="itemCheckSum">The relevant item MD5 checksum</param>
        /// <returns></returns>
        public static bool ItemAlreadyInLocalCache(string itemCheckSum)
        {
            lock (globalDFSLocker)
            {
                return swarmedItemsDict.ContainsKey(itemCheckSum);
            }
        }

        /// <summary>
        /// Returns the most recently completed item in the DFS. Returns null if there are no DFS items.
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
        /// Returns the distributed item with a matching itemCheckSum. Returns null if item is not found.
        /// </summary>
        /// <param name="itemCheckSum">The item MD5 checksum to match</param>
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
        /// Returns the distributed item with a matching itemIdentifier. Returns null if item is not found.
        /// </summary>
        /// <param name="itemIdentifier">The item identifier to match</param>
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
        /// <param name="itemCheckSum">The checksum corresponding with the item to remove</param>
        /// <param name="broadcastRemoval">If true all peers will be notified that we are removing this item.</param>
        /// <param name="removeSwarmWide">True if this item should be removed swarm wide</param>
        public static void RemoveItem(string itemCheckSum, bool broadcastRemoval = true, bool removeSwarmWide = false)
        {
            try
            {
                if (!broadcastRemoval && removeSwarmWide)
                    throw new Exception("BroadcastRemoval must be true if RemoveSwarmWide is also true.");

                DistributedItem itemToRemove = null;

                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(itemCheckSum))
                    {
                        itemToRemove = swarmedItemsDict[itemCheckSum];
                        swarmedItemsDict.Remove(itemCheckSum);

                        //Set the abort build here incase another thread tries to use the item
                        itemToRemove.ItemClosed = true;
                    }
                }

                //This BroadcastItemRemoval has to be outside lock (globalDFSLocker) otherwise it can deadlock
                if (itemToRemove != null)
                {
                    if (broadcastRemoval)
                        //Broadcast to the swarm we are removing this file
                        itemToRemove.SwarmChunkAvailability.BroadcastItemRemoval(itemCheckSum, removeSwarmWide);

                    //Dispose of the distributed item incase it has any open file handles
                    itemToRemove.Dispose();
                }

                //try { GC.Collect(); }
                //catch (Exception) { }
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "DFS_RemoveItemError");
            }
        }

        /// <summary>
        /// Removes all items from local only
        /// </summary>
        /// <param name="broadcastRemoval">If true all peers will be notified that we are removing all items.</param>
        public static void RemoveAllItemsFromLocalOnly(bool broadcastRemoval = false)
        {
            string[] keysToRemove;
            lock (globalDFSLocker)
                keysToRemove = swarmedItemsDict.Keys.ToArray();

            foreach (string key in keysToRemove)
                RemoveItem(key, broadcastRemoval);
        }

        /// <summary>
        /// Closes all connections to peers who have completed items
        /// </summary>
        public static void CloseConnectionToCompletedPeers()
        {
            lock (globalDFSLocker)
            {
                foreach(var item in swarmedItemsDict.Values)
                    item.SwarmChunkAvailability.CloseConnectionsToCompletedPeers(item.TotalNumChunks);
            }
        }

        /// <summary>
        /// Remove any items from the DFS with a matching itemTypeStr
        /// </summary>
        /// <param name="ItemTypeStr">The item type string to match</param>
        /// <param name="broadcastRemoval">If true all peers will be notified that we are removing matching items.</param>
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
        /// Introduces a new item into the swarm and sends a build command to the originating requester
        /// </summary>
        /// <param name="peerConnection">The peer which requested the DFS item</param>
        /// <param name="itemToDistribute">The item to be distributed</param>
        /// <param name="completedPacketType">The packet type to use once the item has been fully assembled</param>
        public static void PushItemToPeer(Connection peerConnection, DistributedItem itemToDistribute, string completedPacketType)
        {
            try
            {
                if (peerConnection.ConnectionInfo.ConnectionType != ConnectionType.TCP)
                    throw new Exception("Only able to push DFS item when the request is made via TCP.");

                lock (globalDFSLocker)
                {
                    //First double check to see if it's already in the swarm
                    if (!ItemAlreadyInLocalCache(itemToDistribute))
                        swarmedItemsDict.Add(itemToDistribute.ItemCheckSum, itemToDistribute);
                    else
                        itemToDistribute = swarmedItemsDict[itemToDistribute.ItemCheckSum];

                    //We add the requester to the item swarm at this point
                    itemToDistribute.SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(peerConnection.ConnectionInfo, new ChunkFlags(0));
                    itemToDistribute.IncrementPushCount();
                }

                //We could contact other known super peers to see if they also have this file

                //Send the config information to the client that wanted the file
                peerConnection.SendObject("DFS_IncomingLocalItemBuild", new ItemAssemblyConfig(itemToDistribute, completedPacketType), nullCompressionSRO);

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
        /// <param name="itemToAdd">The item to add</param>
        /// <returns>The actual item added to the local cache. May not be the provided itemToAdd if an item with the same 
        /// checksum already existed.</returns>
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
        /// Communicates with the provided peer to see if any item swarms can be linked. This is a single link event, 
        /// possibly use InitialiseDFSLink() for a maintained link.
        /// </summary>
        /// <param name="peerIP">The IPAddress of the peer</param>
        /// <param name="peerPort">The port of the peer</param>
        public static void CheckForSharedItems(string peerIP, int peerPort)
        {
            try
            {
                TCPConnection.GetConnection(new ConnectionInfo(peerIP, peerPort)).SendObject("DFS_ItemLinkRequest", new DFSLinkRequest(AllLocalDFSItemsWithBuildTime(), false), nullCompressionSRO);
            }
            catch (CommsException)
            {

            }
        }

        /// <summary>
        /// Returns all item MD5 checksums for DFS items
        /// </summary>
        /// <param name="completeItemsOnly">If true only returns checksums for completed items</param>
        /// <returns></returns>
        public static string[] AllLocalDFSItemKeys(bool completeItemsOnly = true)
        {
            string[] returnArray;

            lock (globalDFSLocker)
            {
                returnArray = (from current in swarmedItemsDict where (completeItemsOnly ? current.Value.LocalItemComplete() : true) select current.Key).ToArray();
            }

            return returnArray;
        }

        /// <summary>
        /// Returns a dictionary of DFS items along with corresponding ItemBuildCompleted times
        /// </summary>
        /// <param name="completeItemsOnly">If true only returns information for completed items</param>
        /// <returns></returns>
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
        /// <param name="connection"></param>
        private static void DFSConnectionShutdown(Connection connection)
        {
            //We want to run this as a task as we want the shutdown to return ASAP
            //GeneralTaskFactory.StartNew(new Action(() =>
            //{
                try
                {
                    //We can only rely on the network identifier if this is a TCP connection shutting down
                    if (connection.ConnectionInfo.ConnectionType == ConnectionType.TCP && connection.ConnectionInfo.NetworkIdentifier != ShortGuid.Empty)
                    {
                        lock (globalDFSLocker)
                        {
                            //Remove peer from any items
                            foreach (var item in swarmedItemsDict)
                            {
                                item.Value.SwarmChunkAvailability.RemovePeerIPEndPointFromSwarm(connection.ConnectionInfo.NetworkIdentifier, (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint);
                            }
                        }

                        lock (chunkDataCacheLocker)
                            chunkDataCache.Remove(connection.ConnectionInfo.NetworkIdentifier);

                        if (loggingEnabled) DFS.logger.Debug("DFSConnectionShutdown Global - Removed peer from all items - " + connection + ".");
                    }
                    else
                        if (loggingEnabled) DFS.logger.Trace("DFSConnectionShutdown Global - Disconnection ignored - " + connection + ".");
                }
                catch (CommsException e)
                {
                    NetworkComms.LogError(e, "CommsError_DFSConnectionShutdown");
                }
                catch (Exception e)
                {
                    NetworkComms.LogError(e, "Error_DFSConnectionShutdown");
                }
            //}));
        }

        /// <summary>
        /// UDP - Used by a client when requesting a list of known peers
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="itemCheckSum"></param>
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
                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    UDPConnection.SendObject("DFS_ItemRemovalUpdate", new ItemRemovalUpdate(NetworkComms.NetworkIdentifier, itemCheckSum, false), (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, nullCompressionSRO);
                else
                    UDPConnection.SendObject("DFS_KnownPeersUpdate", new KnownPeerEndPoints(selectedItem.ItemCheckSum, selectedItem.SwarmChunkAvailability.AllPeerEndPoints()), (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, nullCompressionSRO); 
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
        /// UDP - The response to a DFS_KnownPeersRequest
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="peerList"></param>
        private static void KnownPeersUpdate(PacketHeader packetHeader, Connection connection, KnownPeerEndPoints peerList)
        {
            try
            {
                DistributedItem currentItem = null;

                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(peerList.ItemChecksm))
                        currentItem = swarmedItemsDict[peerList.ItemChecksm];
                }

                if (currentItem != null)
                {
                    //If we have some unknown peers we can request an update from them as well
                    foreach (string peerContactInfo in peerList.PeerEndPoints)
                    {
                        try
                        {
                            IPEndPoint peerEndPoint = IPTools.ParseEndPointFromString(peerContactInfo);

                            //We don't want to contact existing peers as this has already been done by SwarmChunkAvailability.UpdatePeerAvailability
                            //We don't want to contact ourselves and for now that includes anything having the same ip as us
                            if (!currentItem.SwarmChunkAvailability.PeerExistsInSwarm(peerEndPoint) && currentItem.SwarmChunkAvailability.PeerContactAllowed(ShortGuid.Empty, peerEndPoint, false))
                            {
                                currentItem.AddBuildLogLine("Contacting " + peerContactInfo + " for a DFS_ChunkAvailabilityRequest from within KnownPeersUpdate.");
                                UDPConnection.SendObject("DFS_ChunkAvailabilityRequest", peerList.ItemChecksm, peerEndPoint, nullCompressionSRO);
                            }
                        }
                        catch (CommsException)
                        {
                            currentItem.AddBuildLogLine("Removing " + peerContactInfo + " from item swarm due to CommsException.");
                        }
                        catch (Exception ex)
                        {
                            NetworkComms.LogError(ex, "UpdatePeerChunkAvailabilityError_3");
                        }
                    }
                }
            }
            catch (CommsException)
            {
                //NetworkComms.LogError(e, "CommsError_IncomingChunkAvailabilityRequest");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_KnownPeersUpdate");
            }
        }

        /// <summary>
        /// TCP - Received by this DFS if a server is telling this instance to build a local file
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="assemblyConfig"></param>
        private static void IncomingLocalItemBuild(PacketHeader packetHeader, Connection connection, ItemAssemblyConfig assemblyConfig)
        {
            //We start the build in the DFS task factory as it will be a long lived task
            //BuildTaskFactory.StartNew(() =>
            Action assembleAction = new Action(() =>
                {
                    DistributedItem newItem = null;
                    byte[] itemBytes = null;

                    try
                    {
                        if (assemblyConfig == null)
                            throw new NullReferenceException("AssemblyConfig should not be null.");

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

                        //Ensure all possible local listeners are added here
                        List<ConnectionInfo> seedConnectionInfoList = (from current in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP) select new ConnectionInfo(ConnectionType.TCP, NetworkComms.NetworkIdentifier, current, true)).ToList();
                        foreach (ConnectionInfo info in seedConnectionInfoList)
                            newItem.SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(info, newItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkIdentifier), newItem.SwarmChunkAvailability.PeerIsSuperPeer(NetworkComms.NetworkIdentifier), false);

                        //Build the item from the swarm
                        //If the item is already complete this will return immediately
                        newItem.AssembleItem((int)(ItemBuildTimeoutSecsPerMB * (assemblyConfig.TotalItemSizeInBytes / (1024.0 * 1024.0))));

                        //Once complete we pass the item bytes back into network comms
                        //If an exception is thrown we will probably not call this method, timeouts in other areas should then handle and can restart the build.
                        if (newItem.LocalItemComplete() && assemblyConfig.CompletedPacketType != "")
                        {
                            if (DFS.loggingEnabled) DFS.logger.Debug("IncomingLocalItemBuild completed for item with MD5 " + assemblyConfig.ItemCheckSum + ". Item build target is " + assemblyConfig.ItemBuildTarget + ".");

                            //Copy the result to the disk if required by the build target
                            if (assemblyConfig.ItemBuildTarget == ItemBuildTarget.Both)
                            {
                                //All disk files go in a local temp directory
                                string tempFolderLocation = "DFS_" + NetworkComms.NetworkIdentifier;
                                lock (globalDFSLocker)
                                {
                                    if (!Directory.Exists(tempFolderLocation))
                                        Directory.CreateDirectory(tempFolderLocation);
                                }

                                using (FileStream sw = new FileStream(Path.Combine(tempFolderLocation, assemblyConfig.ItemIdentifier), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose))
                                    newItem.CopyItemDataStream(sw);
                            }

                            itemBytes = newItem.AccessItemBytes();
                        }
                        else if (assemblyConfig.CompletedPacketType != "")
                            RemoveItem(assemblyConfig.ItemCheckSum);

                        if (DFS.loggingEnabled)
                        {
                            Exception exceptionToLogWith = new Exception("Build completed succesfully. Logging was enabled so saving build log.");
                            string fileName = "DFSItemBuildLog_" + newItem.ItemIdentifier + "_" + NetworkComms.NetworkIdentifier;
                            if (newItem != null)
                                NetworkComms.LogError(exceptionToLogWith, fileName, newItem.BuildLog().Aggregate(Environment.NewLine, (p, q) => { return p + Environment.NewLine + q; }));
                            else
                                NetworkComms.LogError(exceptionToLogWith, fileName, "newItem==null so no build log was available.");
                        }
                    }
                    catch (CommsException e)
                    {
                        //Crap an error has happened, let people know we probably don't have a good file
                        RemoveItem(assemblyConfig.ItemCheckSum);
                        //connection.CloseConnection(true, 30);
                        //NetworkComms.LogError(e, "CommsError_IncomingLocalItemBuild");

                        if (newItem != null)
                            NetworkComms.LogError(e, "Error_IncomingLocalItemBuildComms", newItem.BuildLog().Aggregate(Environment.NewLine, (p, q) => { return p + Environment.NewLine + q; }));
                        else
                            NetworkComms.LogError(e, "Error_IncomingLocalItemBuildComms", "newItem==null so no build log was available.");
                    }
                    catch (Exception e)
                    {
                        //Crap an error has happened, let people know we probably don't have a good file
                        RemoveItem(assemblyConfig.ItemCheckSum);
                        //connection.CloseConnection(true, 31);

                        if (newItem != null)
                            NetworkComms.LogError(e, "Error_IncomingLocalItemBuild", newItem.BuildLog().Aggregate(Environment.NewLine, (p, q) => { return p + Environment.NewLine + q; }));
                        else
                            NetworkComms.LogError(e, "Error_IncomingLocalItemBuild", "newItem==null so no build log was available.");
                    }
                    //finally
                    //{
                    //Putting any code here appears to cause a sigsegv fault on leaving the finally in mono
                    //Just moved the code out to below as it makes no diference
                    //}

                    //Regardless of if the item completed we call the necessary packet handlers
                    //If there was a build error we just pass null data to the handlers so that the errors can get called up the relevevant stack traces.
                    try
                    {
                        PacketHeader itemPacketHeader = new PacketHeader(assemblyConfig.CompletedPacketType, newItem == null ? 0 : newItem.ItemBytesLength);
                        //We set the item checksum so that the entire distributed item can be easily retrieved later
                        itemPacketHeader.SetOption(PacketHeaderStringItems.PacketIdentifier, newItem == null ? "" : newItem.ItemCheckSum);

                        NetworkComms.TriggerGlobalPacketHandlers(itemPacketHeader, connection, (itemBytes == null ? new MemoryStream(new byte[0], 0, 0, false, true) : new MemoryStream(itemBytes, 0, itemBytes.Length, false, true)), new SendReceiveOptions<NullSerializer>(new Dictionary<string, string>()));
                    }
                    catch (Exception ex)
                    {
                        NetworkComms.LogError(ex, "Error_IncomingLocalItemBuildFinal");
                    }
                });

            if (BuildTaskFactory == null)
                NetworkComms.LogError(new NullReferenceException("BuildTaskFactory is null in IncomingLocalItemBuild"), "IncomingLocalBuildError");
            else
                //Thread buildThread = new Thread(buildAction);
                //buildThread.Name = "DFS_" + assemblyConfig.ItemIdentifier + "_Build";
                //buildThread.Start();
                BuildTaskFactory.StartNew(assembleAction);
        }

        /// <summary>
        /// TCP - A remote peer has request a push of the provided itemCheckSums. This method is used primarily when in repeater mode
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="itemCheckSums"></param>
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
        /// UDP - Received when a peer request a chunk
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="incomingRequest"></param>
        private static void IncomingChunkInterestRequest(PacketHeader packetHeader, Connection connection, ChunkAvailabilityRequest incomingRequest)
        {
            try
            {
                //A peer has requested a specific chunk of data, we will only provide it if we are not already providing it to someone else
                DateTime startTime = DateTime.Now;

                //Console.WriteLine("... ({0}) received request for chunk {1} from {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingChunkInterestRequest from " + connection + " for " + incomingRequest.ItemCheckSum + ", chunkIndex " + incomingRequest.ChunkIndex + ".");

                DistributedItem selectedItem = null;
                lock (globalDFSLocker)
                    if (swarmedItemsDict.ContainsKey(incomingRequest.ItemCheckSum))
                        selectedItem = swarmedItemsDict[incomingRequest.ItemCheckSum];

                if (selectedItem == null || (selectedItem != null && selectedItem.ItemClosed))
                {
                    //First reply and say the peer can't have the requested data. This prevents a request timing out
                    connection.SendObject("DFS_ChunkAvailabilityInterestReplyInfo", new ChunkAvailabilityReply(NetworkComms.NetworkIdentifier, incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), nullCompressionSRO);

                    //Inform peer that we don't actually have the requested item
                    UDPConnection.SendObject("DFS_ItemRemovalUpdate", new ItemRemovalUpdate(NetworkComms.NetworkIdentifier, incomingRequest.ItemCheckSum, false), (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, nullCompressionSRO);

                    if (DFS.loggingEnabled) DFS.logger.Trace(" ... item not available locally, sent DFS_ItemRemovalUpdate.");
                }
                else
                {
                    if (!selectedItem.ChunkAvailableLocally(incomingRequest.ChunkIndex))
                    {
                        //First reply and say the peer can't have the requested data. This prevents a request timing out
                        connection.SendObject("DFS_ChunkAvailabilityInterestReplyInfo", new ChunkAvailabilityReply(NetworkComms.NetworkIdentifier, incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), nullCompressionSRO);

                        //If the peer thinks we have a chunk we don't we send them an update so that they are corrected
                        UDPConnection.SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(NetworkComms.NetworkIdentifier, incomingRequest.ItemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkIdentifier)), (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, nullCompressionSRO);

                        if (DFS.loggingEnabled) DFS.logger.Trace(" ... requested chunk not available, sent DFS_PeerChunkAvailabilityUpdate.");
                    }
                    else
                    {
                        //If we are a super peer we always have to respond to the request
                        if (HostInfo.IP.AverageNetworkLoadOutgoing(10) > DFS.PeerBusyNetworkLoadThreshold && !selectedItem.SwarmChunkAvailability.PeerIsSuperPeer(NetworkComms.NetworkIdentifier))
                        {
                            //We can return a busy reply if we are currently experiencing high demand
                            connection.SendObject("DFS_ChunkAvailabilityInterestReplyInfo", new ChunkAvailabilityReply(NetworkComms.NetworkIdentifier, incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.PeerBusy), nullCompressionSRO);

                            if (DFS.loggingEnabled) DFS.logger.Trace(" ... peer busy, sent busy response.");
                        }
                        else
                        {
                            StreamTools.StreamSendWrapper chunkData = selectedItem.GetChunkStream(incomingRequest.ChunkIndex);

                            if (DFS.loggingEnabled) DFS.logger.Trace("Pushing chunkData to " + connection + " for item:" + incomingRequest.ItemCheckSum + ", chunkIndex:" + incomingRequest.ChunkIndex + ".");

                            long packetSequenceNumber;
                            
                            //This is received via UDP but we want to reply using TCP to ensure delivery of the data
                            var clientTCPConnection = TCPConnection.GetConnection(new ConnectionInfo(connection.ConnectionInfo.RemoteEndPoint));
                            clientTCPConnection.SendObject("DFS_ChunkAvailabilityInterestReplyData", chunkData, nullCompressionSRO, out packetSequenceNumber);

                            if (DFS.loggingEnabled) DFS.logger.Trace("Pushing chunkInfo to " + connection + " for item:" + incomingRequest.ItemCheckSum + ", chunkIndex:" + incomingRequest.ChunkIndex + ".");

                            clientTCPConnection.SendObject("DFS_ChunkAvailabilityInterestReplyInfo", new ChunkAvailabilityReply(NetworkComms.NetworkIdentifier, incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, packetSequenceNumber), nullCompressionSRO);

                            lock (TotalNumCompletedChunkRequestsLocker) TotalNumCompletedChunkRequests++;

                            if (DFS.loggingEnabled) DFS.logger.Trace(" ... IncomingChunkInterestRequest completed with data in " + (DateTime.Now - startTime).TotalSeconds.ToString("0.0") + " seconds.");
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                //This happens if we dispose the DFS item during this method execution
                if (loggingEnabled) Logger.Warn("Prevented ObjectDisposedException in IncomingChunkInterestRequest");
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
        /// TCP - Received when a peer sends us the data portion of a chunk possibly following a request
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="incomingData"></param>
        private static void IncomingChunkInterestReplyData(PacketHeader packetHeader, Connection connection, byte[] incomingData)
        {
            try
            {
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingChunkInterestReplyData from " + connection + " containing " + incomingData.Length + " bytes.");

                if (connection.ConnectionInfo.ConnectionType != ConnectionType.TCP)
                    throw new Exception("IncomingChunkInterestReplyData should only be received using TCP.");

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
                        //If we already have the info then we can finish this chunk off
                        if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(dataSequenceNumber))
                        {
                            if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier][dataSequenceNumber] == null)
                                throw new Exception("An entry existed for the desired dataSequenceNumber but the entry was null.");
                            else if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier][dataSequenceNumber].ChunkAvailabilityReply == null)
                                throw new Exception("An entry existed for the desired ChunkAvailabilityReply but the entry was null.");

                            //The info beat the data so we handle it here
                            existingChunkAvailabilityReply = chunkDataCache[connection.ConnectionInfo.NetworkIdentifier][dataSequenceNumber].ChunkAvailabilityReply;
                            existingChunkAvailabilityReply.SetChunkData(incomingData);

                            chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Remove(dataSequenceNumber);

                            if (chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Count == 0)
                                chunkDataCache.Remove(connection.ConnectionInfo.NetworkIdentifier);
                        }
                        else
                        {
                            //If we don't have the info we just need to log the data
                            chunkDataCache[connection.ConnectionInfo.NetworkIdentifier].Add(dataSequenceNumber, new ChunkDataWrapper(dataSequenceNumber, incomingData));
                            if (DFS.loggingEnabled) DFS.logger.Trace("Added ChunkData to chunkDataCache from " + connection + ", sequence number:" + dataSequenceNumber + " , containing " + incomingData.Length + " bytes.");
                        }
                    }

                    //Only true if we have both the data and info
                    if (existingChunkAvailabilityReply != null)
                    {
                        existingChunkAvailabilityReply.SetSourceConnectionInfo(connection.ConnectionInfo);

                        DistributedItem item = null;
                        lock (globalDFSLocker)
                        {
                            if (swarmedItemsDict.ContainsKey(existingChunkAvailabilityReply.ItemCheckSum))
                                item = swarmedItemsDict[existingChunkAvailabilityReply.ItemCheckSum];
                        }

                        if (item != null)
                            item.HandleIncomingChunkReply(existingChunkAvailabilityReply);
                    }
                }
                catch (Exception ex)
                {
                    NetworkComms.LogError(ex, "Error_IncomingChunkInterestReplyDataInner");
                }

                CheckForChunkDataCacheTimeouts();
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingChunkInterestReplyData");
            }
        }

        /// <summary>
        /// UDP and TCP - Received when a peer sends us a chunk data information possibly following a request
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="incomingReply"></param>
        private static void IncomingChunkInterestReplyInfo(PacketHeader packetHeader, Connection connection, ChunkAvailabilityReply incomingReply)
        {
            try
            {
                ConnectionInfo incomingConnectionInfo = new ConnectionInfo(connection.ConnectionInfo.ConnectionType, incomingReply.SourceNetworkIdentifier, connection.ConnectionInfo.RemoteEndPoint, true);
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
                    bool handleReply = false;
                    lock (chunkDataCacheLocker)
                    {
                        //We generally expect the data to arrive first, but we handle both situations anyway
                        //Realistic testing across a 100MB connection shows that we already have the data 90.1% of the time
                        if (chunkDataCache.ContainsKey(incomingReply.SourceNetworkIdentifier) && chunkDataCache[incomingReply.SourceNetworkIdentifier].ContainsKey(incomingReply.DataSequenceNumber))
                        {
                            incomingReply.SetChunkData(chunkDataCache[incomingReply.SourceNetworkIdentifier][incomingReply.DataSequenceNumber].Data);
                            chunkDataCache[incomingReply.SourceNetworkIdentifier].Remove(incomingReply.DataSequenceNumber);

                            if (DFS.loggingEnabled) DFS.logger.Debug("Completed ChunkAvailabilityReply using data in chunkDataCache from " + connection + ", sequence number:" + incomingReply.DataSequenceNumber + ".");

                            if (chunkDataCache[incomingReply.SourceNetworkIdentifier].Count == 0)
                                chunkDataCache.Remove(incomingReply.SourceNetworkIdentifier);
                        }
                        else if (incomingReply.ReplyState == ChunkReplyState.DataIncluded)
                        {
                            //We have beaten the data, we will add the chunkavailability reply instead and wait, letting the incoming data trigger the handle
                            if (!chunkDataCache.ContainsKey(incomingReply.SourceNetworkIdentifier))
                                chunkDataCache.Add(incomingReply.SourceNetworkIdentifier, new Dictionary<long,ChunkDataWrapper>());

                            chunkDataCache[incomingReply.SourceNetworkIdentifier].Add(incomingReply.DataSequenceNumber, new ChunkDataWrapper(incomingReply));
                            if (DFS.loggingEnabled) DFS.logger.Debug("Added ChunkAvailabilityReply to chunkDataCache (awaiting data) from " + connection + ", sequence number:" + incomingReply.DataSequenceNumber + ".");
                        }

                        //We decide if we are going to handle the data within the lock to avoid possible handle contention
                        if (incomingReply.ChunkDataSet || incomingReply.ReplyState != ChunkReplyState.DataIncluded)
                            handleReply = true;
                    }

                    if (handleReply)
                    {
                        incomingReply.SetSourceConnectionInfo(incomingConnectionInfo);
                        item.HandleIncomingChunkReply(incomingReply);
                    }
                }
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingChunkInterestReplyInfo");
            }
        }

        /// <summary>
        /// UDP - A remote peer is announcing that it has an updated availability of chunks
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="updateDetails"></param>
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
                {
                    ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, updateDetails.SourceNetworkIdentifier, connection.ConnectionInfo.RemoteEndPoint, true);
                    selectedItem.SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(connectionInfo, updateDetails.ChunkFlags);
                    selectedItem.AddBuildLogLine("Updated chunk flags for " + connectionInfo);
                }
                else
                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    //connection.SendObject("DFS_ItemRemovalUpdate", updateDetails.ItemCheckSum);
                    UDPConnection.SendObject("DFS_ItemRemovalUpdate", new ItemRemovalUpdate(NetworkComms.NetworkIdentifier, updateDetails.ItemCheckSum, false), (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, nullCompressionSRO);
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
        /// UDP - A remote peer is requesting chunk availability for this local peer
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="itemCheckSum"></param>
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
                    //connection.SendObject("DFS_ItemRemovalUpdate", itemCheckSum, nullCompressionSRO);
                    UDPConnection.SendObject("DFS_ItemRemovalUpdate", new ItemRemovalUpdate(NetworkComms.NetworkIdentifier, itemCheckSum, false), (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, nullCompressionSRO);
                else
                    //connection.SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(itemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkIdentifier)), nullCompressionSRO);
                    UDPConnection.SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(NetworkComms.NetworkIdentifier, itemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkIdentifier)), (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, nullCompressionSRO);

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
        /// UDP - A remote peer is informing us that they no longer have an item
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
        /// <param name="itemRemovalUpdate"></param>
        private static void IncomingItemRemovalUpdate(PacketHeader packetHeader, Connection connection, ItemRemovalUpdate itemRemovalUpdate)
        {
            try
            {
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingItemRemovalUpdate from " + connection + " for " + itemRemovalUpdate.ItemCheckSum + ". " + (itemRemovalUpdate.RemoveSwarmWide ? "SwamWide" : "Local Only") + ".");

                lock (globalDFSLocker)
                {
                    if (itemRemovalUpdate == null) throw new NullReferenceException("ItemRemovalUpdate was null.");
                    if (itemRemovalUpdate.SourceNetworkIdentifier == null || itemRemovalUpdate.SourceNetworkIdentifier == ShortGuid.Empty)
                        throw new NullReferenceException("itemRemovalUpdate.SourceNetworkIdentifier was null / empty. " + itemRemovalUpdate.SourceNetworkIdentifier != null ? itemRemovalUpdate.SourceNetworkIdentifier : "");

                    if (swarmedItemsDict.ContainsKey(itemRemovalUpdate.ItemCheckSum))
                    {
                        if (itemRemovalUpdate.RemoveSwarmWide)
                            //If this is a swarmwide removal then we get rid of our local copy as well
                            RemoveItem(itemRemovalUpdate.ItemCheckSum, false);
                        else
                        {
                            //Delete any old references at the same time
                            swarmedItemsDict[itemRemovalUpdate.ItemCheckSum].SwarmChunkAvailability.RemoveOldPeerAtEndPoint(itemRemovalUpdate.SourceNetworkIdentifier, (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint);

                            //If this is not a swarm wide removal we just remove this peer from our local swarm copy
                            swarmedItemsDict[itemRemovalUpdate.ItemCheckSum].SwarmChunkAvailability.RemovePeerIPEndPointFromSwarm(itemRemovalUpdate.SourceNetworkIdentifier, (IPEndPoint)connection.ConnectionInfo.RemoteEndPoint, true);
                        }
                    }
                    else
                        if (DFS.loggingEnabled) DFS.logger.Trace(" ... nothing removed as item not present locally.");
                }
            }
            catch (CommsException e)
            {
                NetworkComms.LogError(e, "CommsError_IncomingPeerItemRemovalUpdate");
            }
            catch (Exception e)
            {
                string commentStr = "";
                if (itemRemovalUpdate != null)
                    commentStr = "itemCheckSum:" + itemRemovalUpdate.ItemCheckSum + ", swarmWide:"+itemRemovalUpdate.RemoveSwarmWide + ", identifier" + itemRemovalUpdate.SourceNetworkIdentifier;

                NetworkComms.LogError(e, "Error_IncomingPeerItemRemovalUpdate", commentStr);
            }
        }

        /// <summary>
        /// TCP - A remote peer is trying to link DFS items
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="connection"></param>
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
                                swarmedItemsDict[itemsToLink[i]].SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(connection.ConnectionInfo, new ChunkFlags(swarmedItemsDict[itemsToLink[i]].TotalNumChunks), true);
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
