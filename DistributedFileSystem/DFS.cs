//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using NetworkCommsDotNet;
using SerializerBase.Protobuf;
using SerializerBase;
using System.Threading;
using Common.Logging;

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
        public const double PeerBusyNetworkLoadThreshold = 0.95;

        public const int ItemBuildTimeoutSecs = 300;

        static object globalDFSLocker = new object();
        //Dictionary which contains a cache of the distributed items
        static Dictionary<long, DistributedItem> swarmedItemsDict = new Dictionary<long, DistributedItem>();

        internal static List<string> allowedPeerIPs = new List<string>();
        internal static List<string> disallowedPeerIPs = new List<string>();

        internal static bool DFSShutdownRequested { get; private set; }
        public static bool DFSInitialised { get; private set; }

        static Dictionary<string, List<int>> setupPortHandOutDict = new Dictionary<string, List<int>>();
        static int maxHandOutPeerPort = 9700;
        static int minHandOutPeerPort = 9600;

        static Thread linkWorkerThread;
        static string linkTargetIP;
        static int linkTargetPort;

        public static bool IsLinked { get; private set; }
        public static DFSLinkMode LinkMode { get; private set; }
        static int linkRequestTimeoutSecs = 10;
        static int linkRequestIntervalSecs = 5;
        /// <summary>
        /// The number of link items to build concurrently
        /// </summary>
        static int concurrentNumLinkItems = 2;

        public static int TotalNumCompletedChunkRequests { get; private set; }
        private static object TotalNumCompletedChunkRequestsLocker = new object();

        /// <summary>
        /// Initialises the DFS to run on the current local IP and default comms port.
        /// </summary>
        public static void InitialiseDFS(bool initialiseDFSStartupServer = false)
        {
            CompleteInitialise();

            //If we need a startup server then we do that here
            if (initialiseDFSStartupServer)
                NetworkComms.AppendIncomingPacketHandler<int>("DFS_Setup", IncomingPeerStartup);
        }

        /// <summary>
        /// Initialises the DFS but first contacts the startup server to retrieve startup information.
        /// </summary>
        public static void InitialiseDFS(string startupServerIP, int startupServerPort)
        {
            try
            {
                //Contact server here
                int newCommsPort = NetworkComms.SendReceiveObject<int>("DFS_Setup", startupServerIP, startupServerPort, false, "DFS_Setup", 30000, 0);

                //We need to shutdown comms otherwise we can't change the comms port
                NetworkComms.ShutdownComms();
                NetworkComms.CommsPort = newCommsPort;

                Console.WriteLine(" ... data server suggested DFS listening port of {0}", newCommsPort);
            }
            catch (CommsException)
            {

            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_DFSInitialise", "startupServerIP - " + startupServerIP + ", startupServerPort - " + startupServerPort);
            }

            //Once we have the startup data we can finish the initialisation
            CompleteInitialise();
        }

        private static void CompleteInitialise()
        {
            try
            {
                //Load the allowed ip addresses
                LoadAllowedDisallowedPeerIPs();

                DFSShutdownRequested = false;

                //Add to network comms
                NetworkComms.AppendIncomingPacketHandler<ItemAssemblyConfig>("DFS_IncomingLocalItemBuild", IncomingLocalItemBuild, false);
                NetworkComms.AppendIncomingPacketHandler<long[]>("DFS_RequestLocalItemBuild", RequestLocalItemBuilds, false);

                NetworkComms.AppendIncomingPacketHandler<ChunkAvailabilityRequest>("DFS_ChunkAvailabilityInterestRequest", IncomingChunkInterestRequest, false);
                NetworkComms.AppendIncomingPacketHandler<ChunkAvailabilityReply>("DFS_ChunkAvailabilityInterestReply", IncomingChunkInterestReply, ProtobufSerializer.Instance, NullCompressor.Instance, false);

                NetworkComms.AppendIncomingPacketHandler<long>("DFS_ChunkAvailabilityRequest", IncomingChunkAvailabilityRequest, false);
                NetworkComms.AppendIncomingPacketHandler<PeerChunkAvailabilityUpdate>("DFS_PeerChunkAvailabilityUpdate", IncomingPeerChunkAvailabilityUpdate, false);

                NetworkComms.AppendIncomingPacketHandler<ItemRemovalUpdate>("DFS_ItemRemovalUpdate", IncomingItemRemovalUpdate, false);

                NetworkComms.AppendIncomingPacketHandler<string>("DFS_ChunkAvailabilityInterestReplyComplete", IncomingChunkRequestReplyComplete, false);

                NetworkComms.AppendIncomingPacketHandler<long>("DFS_KnownPeersRequest", KnownPeersRequest, false);
                NetworkComms.AppendIncomingPacketHandler<DFSLinkRequest>("DFS_ItemLinkRequest", IncomingRemoteItemLinkRequest, false);

                NetworkComms.AppendGlobalConnectionCloseHandler(DFSConnectionShutdown);

                if (DFS.loggingEnabled) DFS.logger.Debug("DFS Initialised.");

                NetworkComms.IgnoreUnknownPacketTypes = true;
                NetworkComms.StartListening();

                Console.WriteLine(" ... initialised DFS on {0}:{1}", NetworkComms.LocalIP, NetworkComms.CommsPort);
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
        /// <param name="targetIP"></param>
        /// <param name="targetPort"></param>
        public static void InitialiseDFSLink(string targetIP, int targetPort, DFSLinkMode linkMode)
        {
            if (!DFSInitialised)
                throw new Exception("Attempted to initialise DFS link before DFS had been initialised.");

            if (targetIP == NetworkComms.LocalIP && targetPort == NetworkComms.CommsPort)
                throw new Exception("Attempted to initialise DFS link with local peer.");

            lock (globalDFSLocker)
            {
                if (IsLinked) throw new Exception("Attempted to initialise DFS link once already initialised.");

                DFS.linkTargetIP = targetIP;
                DFS.linkTargetPort = targetPort;
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
                    DFSLinkRequest availableLinkTargetItems = NetworkComms.SendReceiveObject<DFSLinkRequest>("DFS_ItemLinkRequest", linkTargetIP, linkTargetPort, false, "DFS_ItemLinkRequest", linkRequestTimeoutSecs * 1000, new DFSLinkRequest(AllLocalDFSItemsWithBuildTime(), false));
                    if (DFS.loggingEnabled) DFS.logger.Trace("LinkModeWorker could link " + availableLinkTargetItems.AvailableItems.Count+ " items from target.");

                    if (LinkMode == DFSLinkMode.LinkAndRepeat)
                    {
                        //We get a list of items we don't have
                        long[] allLocalItems = AllLocalDFSItemKeys(false);

                        //We only begin a new link cycle if all local items are complete
                        if (allLocalItems.Length == AllLocalDFSItemKeys(true).Length)
                        {
                            //Pull out the items we want to request
                            //We order the items by item creation time starting with the newest
                            long[] itemsToRequest = (from current in availableLinkTargetItems.AvailableItems
                                                     where !allLocalItems.Contains(current.Key)
                                                     orderby current.Value descending
                                                     select current.Key).ToArray();

                            //Make the request for items we do not have
                            if (itemsToRequest.Length > 0)
                            {
                                NetworkComms.SendObject("DFS_RequestLocalItemBuild", linkTargetIP, linkTargetPort, false, itemsToRequest.Take(concurrentNumLinkItems).ToArray());
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
            NetworkComms.ShutdownComms();

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
        public static bool ItemAlreadyInLocalCache(long itemCheckSum)
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
        public static DistributedItem GetDistributedItem(long itemCheckSum)
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
        /// Remove an item from the DFS. Possibly swarmWide and with or without a removal broadcast
        /// </summary>
        /// <param name="itemCheckSum"></param>
        /// <param name="removeSwarmWide"></param>
        /// <param name="broadcastRemoval"></param>
        public static void RemoveItem(long itemCheckSum, bool broadcastRemoval = true, bool removeSwarmWide = false)
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
            if (itemToRemove != null && broadcastRemoval)
                //Broadcast to the swarm we are removing this file
                itemToRemove.SwarmChunkAvailability.BroadcastItemRemoval(itemCheckSum, removeSwarmWide);

            try { GC.Collect(); }
            catch (Exception) { }
        }

        public static void RemoveAllItemsFromLocalOnly()
        {
            long[] keysToRemove;
            lock (globalDFSLocker)
                keysToRemove = swarmedItemsDict.Keys.ToArray();

            foreach (long key in keysToRemove)
                RemoveItem(key, false);
        }

        /// <summary>
        /// Pings all clients for a tracked item to make sure they are still alive. 
        /// Any clients which fail to respond within a sensible time are removed for the item swarm.
        /// </summary>
        /// <param name="itemCheckSum"></param>
        public static void UpdateItemSwarmStatus(long itemCheckSum, int responseTimeMS)
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
        /// <param name="requestOriginConnectionId"></param>
        /// <param name="itemToDistribute"></param>
        public static void PushItemToPeer(ShortGuid requestOriginConnectionId, DistributedItem itemToDistribute, string completedPacketType)
        {
            try
            {
                lock (globalDFSLocker)
                {
                    //First double check to see if it's already in the swarm
                    if (!ItemAlreadyInLocalCache(itemToDistribute))
                    {
                        swarmedItemsDict.Add(itemToDistribute.ItemCheckSum, itemToDistribute);
                    }
                    else
                    {
                        itemToDistribute = swarmedItemsDict[itemToDistribute.ItemCheckSum];
                    }

                    //We add the requester to the item swarm at this point
                    itemToDistribute.SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(requestOriginConnectionId, new ChunkFlags(0));
                    itemToDistribute.IncrementPushCount();
                }

                //We could contact other known super peers to see if they also have this file

                //Send the config information to the client that wanted the file
                NetworkComms.SendObject("DFS_IncomingLocalItemBuild", requestOriginConnectionId, false, new ItemAssemblyConfig(itemToDistribute, completedPacketType));

                if (DFS.loggingEnabled) DFS.logger.Debug("Pushed DFS item " + itemToDistribute.ItemCheckSum + " to peer " + requestOriginConnectionId + ".");
            }
            catch (CommsException)
            {
                //NetworkComms.LogError(ex, "CommsError_AddItemToSwarm");
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_AddItemToSwarm");
            }

            try { GC.Collect(); }
            catch (Exception) { }
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

            try { GC.Collect(); }
            catch (Exception) { }

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
                NetworkComms.SendObject("DFS_ItemLinkRequest", peerIP, peerPort, false, new DFSLinkRequest(AllLocalDFSItemsWithBuildTime(), false));
            }
            catch (CommsException ex)
            {

            }
        }

        public static long[] AllLocalDFSItemKeys(bool completeItemsOnly = true)
        {
            long[] returnArray;

            lock (globalDFSLocker)
            {
                //returnArray = swarmedItemsDict.Keys.ToArray();
                returnArray = (from current in swarmedItemsDict where (completeItemsOnly ? current.Value.LocalItemComplete() : true) select current.Key).ToArray();
            }

            return returnArray;
        }

        public static Dictionary<long, DateTime> AllLocalDFSItemsWithBuildTime(bool completeItemsOnly = true)
        {
            long[] itemCheckSums = AllLocalDFSItemKeys(completeItemsOnly);

            Dictionary<long, DateTime> returnDict = new Dictionary<long, DateTime>();

            lock (globalDFSLocker)
            {
                foreach (long item in itemCheckSums)
                {
                    if (swarmedItemsDict.ContainsKey(item))
                        returnDict.Add(item, swarmedItemsDict[item].ItemBuildCompleted);
                }
            }

            return returnDict;
        }

        #region NetworkCommsDelegates
        /// <summary>
        /// If a connection is disconnected we want to make sure we handle it within the DFS
        /// </summary>
        /// <param name="disconnectedConnectionIdentifier"></param>
        private static void DFSConnectionShutdown(ShortGuid disconnectedConnectionIdentifier)
        {
            //We want to run this as a task as we want the shutdown to return ASAP
            Task.Factory.StartNew(new Action(() =>
            {
                try
                {
                    lock (globalDFSLocker)
                    {
                        //Remove peer from any items
                        foreach (var item in swarmedItemsDict)
                            item.Value.SwarmChunkAvailability.RemovePeerFromSwarm(disconnectedConnectionIdentifier);

                        ConnectionInfo peerConnInfo = NetworkComms.ConnectionIdToConnectionInfo(disconnectedConnectionIdentifier);

                        if (setupPortHandOutDict.ContainsKey(peerConnInfo.ClientIP))
                            setupPortHandOutDict[peerConnInfo.ClientIP].Remove(peerConnInfo.ClientPort);
                    }

                    if (loggingEnabled) DFS.logger.Debug("DFSConnectionShutdown triggered for peer " + disconnectedConnectionIdentifier + ".");
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

        private static void IncomingPeerStartup(PacketHeader packetHeader, ShortGuid sourceConnectionId, int incomingObject)
        {
            try
            {
                //We need to provide a port between max and min
                //We could just start with the max and go down
                int portToReturn = maxHandOutPeerPort;

                ConnectionInfo peerConnInfo = NetworkComms.ConnectionIdToConnectionInfo(sourceConnectionId);

                lock (globalDFSLocker)
                {
                    //For each item we go through all the peers to see if we already have an existing peer with that ip address
                    for (int i = maxHandOutPeerPort; i > 0; i--)
                    {
                        //This first 'if' alone is NOT sufficient to decide a port number
                        //i.e. we may end up returning this port number to several peers at the same time if neither quickly reconnects
                        if (!NetworkComms.ConnectionExists(peerConnInfo.ClientIP, i))
                        {
                            //This later check will make sure we don't hand the same port out in quick succession
                            if (!setupPortHandOutDict.ContainsKey(peerConnInfo.ClientIP)) setupPortHandOutDict.Add(peerConnInfo.ClientIP, new List<int>());

                            if (!setupPortHandOutDict[peerConnInfo.ClientIP].Contains(i))
                            {
                                setupPortHandOutDict[peerConnInfo.ClientIP].Add(i);
                                portToReturn = i;
                                break;
                            }
                        }
                    }

                    //We only contain a list of the last half ports to be handed out
                    if (setupPortHandOutDict[peerConnInfo.ClientIP].Count > (maxHandOutPeerPort - minHandOutPeerPort)/2)
                    {
                        setupPortHandOutDict[peerConnInfo.ClientIP] = (from current in setupPortHandOutDict[peerConnInfo.ClientIP].Select((item, index) => new { index, item }) 
                                                where current.index > (maxHandOutPeerPort - minHandOutPeerPort) / 2 
                                                select current.item).ToList();
                    }

                    if (portToReturn < minHandOutPeerPort)
                        throw new Exception("Unable to choose an appropriate port to return. Consider increasing available range. Attempted to assign port " + portToReturn +
                            " to " + sourceConnectionId + " at " + peerConnInfo.ClientIP + ". Starting at port " + maxHandOutPeerPort + ". " +
                            NetworkComms.TotalNumConnections(peerConnInfo.ClientIP) + " total existing connections from IP.");
                }

                //Return the selected port
                NetworkComms.SendObject("DFS_Setup", sourceConnectionId, false, portToReturn);
            }
            catch (CommsException e)
            {

            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_DFSPeerStartup");
            }
        }

        /// <summary>
        /// Used by a client when requesting a list of known peers
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void KnownPeersRequest(PacketHeader packetHeader, ShortGuid sourceConnectionId, long itemCheckSum)
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
                    NetworkComms.SendObject("DFS_KnownPeersUpdate", sourceConnectionId, false, new string[] { "" });
                else
                    NetworkComms.SendObject("DFS_KnownPeersUpdate", sourceConnectionId, false, selectedItem.SwarmChunkAvailability.AllPeerEndPoints());
            }
            catch (CommsException e)
            {
                //NetworkComms.LogError(e, "CommsError_IncomingChunkAvailabilityRequest");
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_KnownPeersRequest");
            }
        }

        /// <summary>
        /// Blank delegate for complete replies which return late.
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingChunkRequestReplyComplete(PacketHeader packetHeader, ShortGuid sourceConnectionId, object incomingObject)
        {
            //No need to do anything here
        }

        /// <summary>
        /// Received by this DFS if a server is telling this instance to build a local file
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingLocalItemBuild(PacketHeader packetHeader, ShortGuid sourceConnectionId, ItemAssemblyConfig assemblyConfig)
        {
            try
            {
                if (DFS.loggingEnabled) DFS.logger.Debug("IncomingLocalItemBuild from " + sourceConnectionId + " for item " + assemblyConfig.ItemCheckSum + ".");

                DistributedItem newItem = null;
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
                newItem.AssembleItem(ItemBuildTimeoutSecs);

                //Once complete we pass the item bytes back into network comms
                //If an exception is thrown we will probably not call this method, timeouts in other areas should then handle and can restart the build.
                if (newItem.LocalItemComplete() && assemblyConfig.CompletedPacketType != "") NetworkComms.TriggerPacketHandler(new PacketHeader(assemblyConfig.CompletedPacketType, false, 0, newItem.ItemBytesLength, true), sourceConnectionId, newItem.AccessItemBytes(), NullSerializer.Instance, NullCompressor.Instance);

                //Close any connections which are no longer required
                newItem.SwarmChunkAvailability.CloseConnectionToCompletedPeers(newItem.TotalNumChunks);

                if (DFS.loggingEnabled) DFS.logger.Debug("IncomingLocalItemBuild completed for item with MD5 " + assemblyConfig.ItemCheckSum + ".");
            }
            catch (CommsException e)
            {
                //Crap an error has happened, let people know we probably don't have a good file
                RemoveItem(assemblyConfig.ItemCheckSum);
                //NetworkComms.LogError(e, "CommsError_IncomingLocalItemBuild");
            }
            catch (Exception e)
            {
                //Crap an error has happened, let people know we probably don't have a good file
                RemoveItem(assemblyConfig.ItemCheckSum);
                NetworkComms.LogError(e, "Error_IncomingLocalItemBuild");
            }
        }

        /// <summary>
        /// A remote peer has request a push of the provided itemCheckSums. This method is used primiarly when in repeater mode
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="itemCheckSum"></param>
        private static void RequestLocalItemBuilds(PacketHeader packetHeader, ShortGuid sourceConnectionId, long[] itemCheckSums)
        {
            try
            {
                DistributedItem[] selectedItems = null;
                lock (globalDFSLocker)
                    selectedItems = (from current in swarmedItemsDict where itemCheckSums.Contains(current.Key) select current.Value).ToArray();

                if (selectedItems !=null && selectedItems.Length > 0)
                    foreach(DistributedItem item in selectedItems)
                        DFS.PushItemToPeer(sourceConnectionId, item, "");
            }
            catch (CommsException e)
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
        private static void IncomingChunkInterestRequest(PacketHeader packetHeader, ShortGuid sourceConnectionId, ChunkAvailabilityRequest incomingRequest)
        {
            try
            {
                //A peer has requested a specific chunk of data, we will only provide it if we are not already providing it to someone else

                //Console.WriteLine("... ({0}) received request for chunk {1} from {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingChunkInterestRequest from " + sourceConnectionId + " for " + incomingRequest.ItemCheckSum + ", chunkIndex " + incomingRequest.ChunkIndex + ".");

                DistributedItem selectedItem = null;
                lock (globalDFSLocker)
                    if (swarmedItemsDict.ContainsKey(incomingRequest.ItemCheckSum))
                        selectedItem = swarmedItemsDict[incomingRequest.ItemCheckSum];

                if (selectedItem == null)
                {
                    //First reply and say the peer can't have the requested data. This prevents a request timing out
                    NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), ProtobufSerializer.Instance, NullCompressor.Instance);

                    //Inform peer that we don't actually have the requested item
                    NetworkComms.SendObject("DFS_ItemRemovalUpdate", sourceConnectionId, false, incomingRequest.ItemCheckSum);
                }
                else
                {
                    if (!selectedItem.ChunkAvailableLocally(incomingRequest.ChunkIndex))
                    {
                        //First reply and say the peer can't have the requested data. This prevents a request timing out
                        NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), ProtobufSerializer.Instance, NullCompressor.Instance);

                        //If the peer thinks we have a chunk we dont we send them an update so that they are corrected
                        NetworkComms.SendObject("DFS_PeerChunkAvailabilityUpdate", sourceConnectionId, false, new PeerChunkAvailabilityUpdate(incomingRequest.ItemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkNodeIdentifier)));
                    }
                    else
                    {
                        if (NetworkComms.AverageNetworkLoad(10) > DFS.PeerBusyNetworkLoadThreshold)
                        {
                            //We can return a busy reply if we are currently experiencing high demand
                            NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.PeerBusy), ProtobufSerializer.Instance, NullCompressor.Instance);
                        }
                        else
                        {
                            //try
                            //{
                                //We get the data here
                                byte[] chunkData = selectedItem.GetChunkBytes(incomingRequest.ChunkIndex);

                                //Console.WriteLine("   ... ({0}) begin push of chunk {1} to {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
                                NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, chunkData), ProtobufSerializer.Instance, NullCompressor.Instance);

                                lock (TotalNumCompletedChunkRequestsLocker)
                                    TotalNumCompletedChunkRequests++;
                            
                                //Console.WriteLine("         ... ({0}) pushed chunk {1} to {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
                                //If we have sent data there is a good chance we have used up alot of memory
                                //This seems to be an efficient place for a garbage collection
                                try { GC.Collect(); }
                                catch (Exception) { }
                            //}
                            //finally
                            //{
                            //    //We must guarantee we leave the bytes if we had succesfully entered them.
                            //    selectedItem.LeaveChunkBytes(incomingRequest.ChunkIndex);
                            //}
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
        /// Received when a peer sends us a chunk possibly following a request
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingChunkInterestReply(PacketHeader packetHeader, ShortGuid sourceConnectionId, ChunkAvailabilityReply incomingReply)
        {
            try
            {
                //Console.WriteLine(". {0} chunk {1} incoming from {2}. state={3}", DateTime.Now.ToString("HH:mm:ss.fff"), incomingReply.ChunkIndex, sourceConnectionId, incomingReply.ReplyState);
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingChunkInterestReply from " + sourceConnectionId + " for item " + incomingReply.ItemCheckSum + ", chunkIndex " + incomingReply.ChunkIndex + ".");

                //We no longer lock on chunks so no need for a reply
                //Thank the peer for the chunk as soon as it is received which allows it to forward the chunk to others
                //                if (incomingReply.DataIncluded)
                //                {
                //                    //Use a task here so that it does not slow later code
                //                    Task.Factory.StartNew(new Action(() =>
                //                        {
                //                            NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReplyComplete, sourceConnectionId, false, true);
                //#if logging
                //                    logger.Debug("... confirmed chunk receipt to " + sourceConnectionId + " for data (" + incomingReply.ItemMD5 + " : " + incomingReply.ChunkIndex + ").");
                //#endif
                //                        }));
                //                }

                DistributedItem item = null;
                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(incomingReply.ItemCheckSum))
                        item = swarmedItemsDict[incomingReply.ItemCheckSum];
                    //else
                    //    NetworkComms.LogError(new Exception("Incoming interest reply for an item which is not tracked by this node."), "IncomingChunkInterestReplyNote");
                }

                if (item != null)
                    item.HandleIncomingChunkReply(incomingReply, sourceConnectionId);
            }
            catch (CommsException)
            {
                //Whoever we got the data from is probably no longer connected.
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_IncomingChunkInterestReply");
            }

            //try { GC.Collect(); }
            //catch (Exception) { }
        }

        /// <summary>
        /// A remote peer is announcing that it has an updated availability of chunks
        /// </summary>
        /// <param name="packetHeader"></param>
        /// <param name="sourceConnectionId"></param>
        /// <param name="incomingObjectBytes"></param>
        private static void IncomingPeerChunkAvailabilityUpdate(PacketHeader packetHeader, ShortGuid sourceConnectionId, PeerChunkAvailabilityUpdate updateDetails)
        {
            try
            {
                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingPeerChunkAvailabilityUpdate from " + sourceConnectionId + " for item " + updateDetails.ItemCheckSum + "("+updateDetails.ChunkFlags.NumCompletedChunks()+").");

                DistributedItem selectedItem = null;
                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(updateDetails.ItemCheckSum))
                        selectedItem = swarmedItemsDict[updateDetails.ItemCheckSum];
                }

                if (selectedItem != null)
                    selectedItem.SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(sourceConnectionId, updateDetails.ChunkFlags);
                else
                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    NetworkComms.SendObject("DFS_ItemRemovalUpdate", sourceConnectionId, false, updateDetails.ItemCheckSum);
            }
            catch (CommsException e)
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
        private static void IncomingChunkAvailabilityRequest(PacketHeader packetHeader, ShortGuid sourceConnectionId, long itemCheckSum)
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
                    NetworkComms.SendObject("DFS_ItemRemovalUpdate", sourceConnectionId, false, itemCheckSum);
                else
                    NetworkComms.SendObject("DFS_PeerChunkAvailabilityUpdate", sourceConnectionId, false, new PeerChunkAvailabilityUpdate(itemCheckSum, selectedItem.SwarmChunkAvailability.PeerChunkAvailability(NetworkComms.NetworkNodeIdentifier)));

                if (DFS.loggingEnabled) DFS.logger.Trace(" ... replied to IncomingChunkAvailabilityRequest (" + itemCheckSum + ").");
            }
            catch (CommsException e)
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
        private static void IncomingItemRemovalUpdate(PacketHeader packetHeader, ShortGuid sourceConnectionId, ItemRemovalUpdate itemRemovalUpdate)
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
                            swarmedItemsDict[itemRemovalUpdate.ItemCheckSum].SwarmChunkAvailability.RemovePeerFromSwarm(sourceConnectionId, true);
                    }
                     
                }

                if (DFS.loggingEnabled) DFS.logger.Trace("IncomingItemRemovalUpdate from " + sourceConnectionId + " for " + itemRemovalUpdate.ItemCheckSum + ". " + (itemRemovalUpdate.RemoveSwarmWide ? "SwamWide" : "Local Only") + ".");
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
        private static void IncomingRemoteItemLinkRequest(PacketHeader packetHeader, ShortGuid sourceConnectionId, DFSLinkRequest linkRequestData)
        {
            try
            {
                var localItemKeys = AllLocalDFSItemsWithBuildTime();

                //We only check for potential links if the remote end has provided us with some items to link
                if (linkRequestData.AvailableItems.Count > 0)
                {
                    //Get the item matches using linq. Could also use localItemKeys.Intersect<long>(linkRequestData.AvailableItemCheckSums);
                    long[] itemsToLink = (from current in localItemKeys.Keys
                                          join remote in linkRequestData.AvailableItems.Keys on current equals remote
                                          select current).ToArray();

                    lock (globalDFSLocker)
                    {
                        for (int i = 0; i < itemsToLink.Length; i++)
                        {
                            //If we still have the item then we add the remote end as a new super peer
                            if (swarmedItemsDict.ContainsKey(itemsToLink[i]))
                                swarmedItemsDict[itemsToLink[i]].SwarmChunkAvailability.AddOrUpdateCachedPeerChunkFlags(sourceConnectionId, new ChunkFlags(swarmedItemsDict[itemsToLink[i]].TotalNumChunks), true);
                        }
                    }
                }

                //If this link request is from the original requester then we reply with our own items list
                if (!linkRequestData.LinkRequestReply)
                    NetworkComms.SendObject("DFS_ItemLinkRequest", sourceConnectionId, false, new DFSLinkRequest(localItemKeys, true));
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
