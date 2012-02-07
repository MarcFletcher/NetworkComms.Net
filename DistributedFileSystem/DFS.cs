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

#if logging
using log4net;
using log4net.Repository;
using log4net.Layout;
using log4net.Appender;
using System.Reflection;
#endif

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
        //public const int NumConcurrentRequests = 1;
        public const int NumTotalGlobalRequests = 8;

        public const int PeerBusyTimeoutMS = 250;

        public const int ItemBuildTimeoutSecs = 300;

        static object globalDFSLocker = new object();
        //Dictionary which contains a cache of the distributed items
        static Dictionary<long, DistributedItem> swarmedItemsDict = new Dictionary<long, DistributedItem>();

        internal static List<string> allowedPeerIPs = new List<string>();
        internal static List<string> disallowedPeerIPs = new List<string>();

        internal static bool DFSShutdownRequested { get; private set; }

        private static Dictionary<string, List<int>> setupPortHandOutDict = new Dictionary<string, List<int>>();
        private static int maxHandOutPeerPort = 9700;
        private static int minHandOutPeerPort = 9600;

#if logging
        internal static readonly ILog logger = LogManager.GetLogger(typeof(NetworkComms));
        internal static volatile bool loggerConfigured = false;
#endif

#if logging
        private static void ConfigureLogger()
        {
            lock (globalDFSLocker)
            {
                if (!loggerConfigured)
                {
                    loggerConfigured = true;
                    //If we are logging configure the logger
                    ILoggerRepository repository = LogManager.GetRepository(Assembly.GetCallingAssembly());
                    IBasicRepositoryConfigurator configurableRepository = repository as IBasicRepositoryConfigurator;

                    PatternLayout layout = new PatternLayout();
                    layout.ConversionPattern = "%timestamp% - %level% [%thread%] - %message%newline";
                    layout.ActivateOptions();

                    FileAppender appender = new FileAppender();
                    appender.Layout = layout;
                    appender.File = "DFSLog.txt";
                    appender.AppendToFile = false;
                    appender.ActivateOptions();
                    configurableRepository.Configure(appender);
                }
            }
        }
#endif
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
                int newCommsPort = NetworkComms.SendRecieveObject<int>("DFS_Setup", startupServerIP, startupServerPort, false, "DFS_Setup", 30000, 0);

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
                NetworkComms.AppendIncomingPacketHandler <ItemAssemblyConfig>("DFS_RequestLocalItemBuild", IncomingLocalItemBuild, false);

                NetworkComms.AppendIncomingPacketHandler <ChunkAvailabilityRequest>("DFS_ChunkAvailabilityInterestRequest", IncomingChunkInterestRequest, false);
                NetworkComms.AppendIncomingPacketHandler < ChunkAvailabilityReply>("DFS_ChunkAvailabilityInterestReply", IncomingChunkInterestReply, ProtobufSerializer.Instance, NullCompressor.Instance, false);

                NetworkComms.AppendIncomingPacketHandler <long>("DFS_ChunkAvailabilityRequest", IncomingChunkAvailabilityRequest, false);
                NetworkComms.AppendIncomingPacketHandler < PeerChunkAvailabilityUpdate>("DFS_PeerChunkAvailabilityUpdate", IncomingPeerChunkAvailabilityUpdate, false);

                NetworkComms.AppendIncomingPacketHandler <long>("DFS_ItemRemovedLocallyUpdate", IncomingPeerItemRemovalUpdate, false);

                NetworkComms.AppendIncomingPacketHandler <string>("DFS_ChunkAvailabilityInterestReplyComplete", IncomingChunkRequestReplyComplete, false);

                NetworkComms.AppendGlobalConnectionCloseHandler(DFSConnectionShutdown);

#if logging
            ConfigureLogger();
            logger.Debug("Initialised DFS.");
#endif

                NetworkComms.IgnoreUnknownPacketTypes = true;
                NetworkComms.StartListening();

                Console.WriteLine(" ... initialised DFS on {0}:{1}", NetworkComms.LocalIP, NetworkComms.CommsPort);
            }
            catch (Exception e)
            {
                NetworkComms.LogError(e, "Error_DFSIntialise");
            }
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

#if logging
            logger.Debug("DFS Shutdown.");
#endif
        }

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
                        throw new Exception("Potential Md5 conflict detected in dfs.");
                }

                return false;
            }
        }

        /// <summary>
        /// Contacts all nodes and requests that an item be removed from the swarm
        /// </summary>
        /// <param name="itemMD5"></param>
        public static void RemoveItemFromSwarm(DistributedItem item)
        {
            throw new NotImplementedException();
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

        public static void RemoveItemFromLocalOnly(long itemCheckSum)
        {
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
                //Broadcast to the swarm we are removing this file
                itemToRemove.SwarmChunkAvailability.BroadcastItemRemoval(itemCheckSum);

            try { GC.Collect(); }
            catch (Exception) { }
        }

        public static void RemoveAllItemsFromLocalOnly()
        {
            long[] keysToRemove;
            lock (globalDFSLocker)
                keysToRemove = swarmedItemsDict.Keys.ToArray();

            foreach (long key in keysToRemove)
                RemoveItemFromLocalOnly(key);
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
        public static void PushItemToSwarm(ShortGuid requestOriginConnectionId, DistributedItem itemToDistribute, string completedPacketType)
        {
            try
            {
                lock (globalDFSLocker)
                {
                    //First double check to see if it's already in the swarm
                    if (!ItemAlreadyInLocalCache(itemToDistribute))
                    {
#if logging
                        logger.Debug("... adding new item to swarm with MD5 of " + itemToDistribute.ItemMd5 + ".");
#endif
                        swarmedItemsDict.Add(itemToDistribute.ItemCheckSum, itemToDistribute);
                    }
                    else
                    {
#if logging
                        logger.Debug("... existing item with MD5 of " + itemToDistribute.ItemMd5 + " already in swarm. Continuing with existing item.");
#endif
                        itemToDistribute = swarmedItemsDict[itemToDistribute.ItemCheckSum];
                    }

#if logging
                    logger.Debug("... adding requester ("+requestOriginConnectionId+") of item to itemSwarm (" + itemToDistribute.ItemMd5 + ").");
#endif

                    //We add the requester to the item swarm at this point
                    itemToDistribute.UpdateCachedPeerChunkFlags(requestOriginConnectionId, new ChunkFlags(0));
                    itemToDistribute.IncrementPushCount();
                }

                //Send the config information to the client that wanted the file
                NetworkComms.SendObject("DFS_RequestLocalItemBuild", requestOriginConnectionId, false, new ItemAssemblyConfig(itemToDistribute, completedPacketType));

#if logging
                logger.Debug("... ended by sending DFS_RequestLocalItemBuild to " + requestOriginConnectionId + " for item " + itemToDistribute.ItemMd5 + ".");
#endif
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
        /// Adds a distributed item to the local cache only and informs and known peers of the item availability
        /// </summary>
        /// <param name="handRankArrayItem"></param>
        public static void AddItemToLocalOnly(DistributedItem itemToAdd)
        {
            try
            {
                lock (globalDFSLocker)
                {
                    //First double check to see if it's already in the swarm
                    if (!ItemAlreadyInLocalCache(itemToAdd))
                    {
#if logging
                        logger.Debug("... adding new item to swarm with MD5 of " + itemToDistribute.ItemMd5 + ".");
#endif
                        swarmedItemsDict.Add(itemToAdd.ItemCheckSum, itemToAdd);
                    }
                    else
                    {
#if logging
                        logger.Debug("... existing item with MD5 of " + itemToDistribute.ItemMd5 + " already in swarm. Continuing with existing item.");
#endif
                        itemToAdd = swarmedItemsDict[itemToAdd.ItemCheckSum];
                    }

#if logging
                    logger.Debug("... adding requester ("+requestOriginConnectionId+") of item to itemSwarm (" + itemToDistribute.ItemMd5 + ").");
#endif
                }

                //Send the config information to the client that wanted the file
                //NetworkComms.SendObject("DFS_RequestLocalItemBuild, requestOriginConnectionId, false, new ItemAssemblyConfig(itemToDistribute, completedPacketType));
                itemToAdd.SwarmChunkAvailability.BroadcastLocalAvailability(itemToAdd.ItemCheckSum);

#if logging
                logger.Debug("... ended by sending DFS_RequestLocalItemBuild to " + requestOriginConnectionId + " for item " + itemToDistribute.ItemMd5 + ".");
#endif
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
                            item.Value.RemovePeer(disconnectedConnectionIdentifier);

                        ConnectionInfo peerConnInfo = NetworkComms.ConnectionIdToConnectionInfo(disconnectedConnectionIdentifier);

                        if (setupPortHandOutDict.ContainsKey(peerConnInfo.ClientIP))
                            setupPortHandOutDict[peerConnInfo.ClientIP].Remove(peerConnInfo.ClientPort);
                    }

#if logging
                logger.Debug("DFSConnectionShutdown triggered for peer with networkIdentifier of " + disconnectedConnectionIdentifier + ".");
#endif
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
#if logging
                logger.Debug("RequestLocalItemBuild triggered by "+sourceConnectionId+" for item " + assemblyConfig.ItemMD5 + ".");
#endif

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
                newItem.AssembleItem(ItemBuildTimeoutSecs);

#if logging
                logger.Debug("RequestLocalItemBuild completed for item with MD5 " + assemblyConfig.ItemMD5 + ", passing back to network comms for completion.");
#endif

                //Once complete we pass the item bytes back into network comms
                //If an exception is thrown we will probably not call this method, timeouts in other areas should then handle and can restart the build.
                if (newItem.LocalItemComplete())
                    NetworkComms.TriggerPacketHandler(new PacketHeader(assemblyConfig.CompletedPacketType, false, 0, newItem.ItemBytesLength, true), sourceConnectionId, newItem.AccessItemBytes(), NullSerializer.Instance, NullCompressor.Instance);
            }
            catch (CommsException e)
            {
                //Crap an error has happened, let people know we probably don't have a good file
                RemoveItemFromLocalOnly(assemblyConfig.ItemCheckSum);
                //NetworkComms.LogError(e, "CommsError_IncomingLocalItemBuild");
            }
            catch (Exception e)
            {
                //Crap an error has happened, let people know we probably don't have a good file
                RemoveItemFromLocalOnly(assemblyConfig.ItemCheckSum);
                NetworkComms.LogError(e, "Error_IncomingLocalItemBuild");
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

                //Console.WriteLine("... ({0}) recieved request for chunk {1} from {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
#if logging
                logger.Debug("IncomingChunkInterestRequest triggered by " + sourceConnectionId + " for item " + incomingRequest.ItemMD5 + ", chunkIndex " + incomingRequest.ChunkIndex + ".");
#endif

                DistributedItem selectedItem = null;
                lock (globalDFSLocker)
                    if (swarmedItemsDict.ContainsKey(incomingRequest.ItemCheckSum))
                        selectedItem = swarmedItemsDict[incomingRequest.ItemCheckSum];

                if (selectedItem == null)
                {
                    //First reply and say the peer can't have the requested data. This prevents a request timing out
                    NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), ProtobufSerializer.Instance, NullCompressor.Instance);

                    //Inform peer that we don't actually have the requested item
                    NetworkComms.SendObject("DFS_ItemRemovedLocallyUpdate", sourceConnectionId, false, incomingRequest.ItemCheckSum);
                }
                else
                {
                    if (!selectedItem.ChunkAvailableLocally(incomingRequest.ChunkIndex))
                    {
                        //First reply and say the peer can't have the requested data. This prevents a request timing out
                        NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.ItemOrChunkNotAvailable), ProtobufSerializer.Instance, NullCompressor.Instance);

                        //If the peer thinks we have a chunk we dont we send them an update so that they are corrected
                        NetworkComms.SendObject("DFS_PeerChunkAvailabilityUpdate", sourceConnectionId, false, new PeerChunkAvailabilityUpdate(incomingRequest.ItemCheckSum, selectedItem.PeerChunkAvailability(NetworkComms.NetworkNodeIdentifier)));
                    }
                    else
                    {
                        //We try to enter the chunk here
                        byte[] chunkData = selectedItem.EnterChunkBytes(incomingRequest.ChunkIndex);

                        if (chunkData == null)
                            //We can return a busy reply if we were unsuccesfull in accessing the bytes.
                            NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, ChunkReplyState.PeerBusy), ProtobufSerializer.Instance, NullCompressor.Instance);
                        else
                        {
                            try
                            {
                                //Console.WriteLine("   ... ({0}) begin push of chunk {1} to {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
                                NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReply", sourceConnectionId, false, new ChunkAvailabilityReply(incomingRequest.ItemCheckSum, incomingRequest.ChunkIndex, chunkData), ProtobufSerializer.Instance, NullCompressor.Instance);
                                //Console.WriteLine("         ... ({0}) pushed chunk {1} to {2}.", DateTime.Now.ToString("HH:mm:ss.fff"), incomingRequest.ChunkIndex, sourceConnectionId);
#if logging
                                    logger.Debug("... ended IncomingChunkInterestRequest and provided requested chunk data to " + sourceConnectionId + " (" + incomingRequest.ItemMD5 + " : " + incomingRequest.ChunkIndex + ").");
#endif
                                //If we have sent data there is a good chance we have used up alot of memory
                                //This seems to be an efficient place for a garbage collection
                                try { GC.Collect(); }
                                catch (Exception) { }
                            }
                            finally
                            {
                                //We must guarantee we leave the bytes if we had succesfully entered them.
                                selectedItem.LeaveChunkBytes(incomingRequest.ChunkIndex);
                            }
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
#if logging
                logger.Debug("IncomingChunkInterestReply triggered by " + sourceConnectionId + " for item " + incomingReply.ItemMD5 + ", chunkIndex " + incomingReply.ChunkIndex + ".");
#endif

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
#if logging
                logger.Debug("IncomingPeerChunkAvailabilityUpdate triggered by " + sourceConnectionId + " for item " + updateDetails.ItemMD5 + ".");
#endif

                DistributedItem selectedItem = null;
                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(updateDetails.ItemCheckSum))
                        selectedItem = swarmedItemsDict[updateDetails.ItemCheckSum];
                }

                if (selectedItem != null)
                    selectedItem.UpdateCachedPeerChunkFlags(sourceConnectionId, updateDetails.ChunkFlags);
                else
                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    NetworkComms.SendObject("DFS_ItemRemovedLocallyUpdate", sourceConnectionId, false, updateDetails.ItemCheckSum);
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

#if logging
                logger.Debug("IncomingChunkAvailabilityRequest triggered by " + sourceConnectionId + " for item " + itemMD5 + ".");
#endif

                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(itemCheckSum))
                        selectedItem = swarmedItemsDict[itemCheckSum];
                }

                if (selectedItem == null)
                    //Inform peer that we don't actually have the requested item so that it won't bother us again
                    NetworkComms.SendObject("DFS_ItemRemovedLocallyUpdate", sourceConnectionId, false, itemCheckSum);
                else
                    NetworkComms.SendObject("DFS_PeerChunkAvailabilityUpdate", sourceConnectionId, false, new PeerChunkAvailabilityUpdate(itemCheckSum, selectedItem.PeerChunkAvailability(NetworkComms.NetworkNodeIdentifier)));

#if logging
                logger.Debug(".. ended IncomingChunkAvailabilityRequest by returning our availability for item "+itemMD5+".");
#endif
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
        private static void IncomingPeerItemRemovalUpdate(PacketHeader packetHeader, ShortGuid sourceConnectionId, long itemCheckSum)
        {
            try
            {
#if logging
                logger.Debug("IncomingPeerItemRemovalUpdate triggered by " + sourceConnectionId + " for item " + itemMD5 + ".");
#endif

                lock (globalDFSLocker)
                {
                    if (swarmedItemsDict.ContainsKey(itemCheckSum))
                        swarmedItemsDict[itemCheckSum].RemovePeer(sourceConnectionId, true);
                }

#if logging
                logger.Debug(".. ended IncomingPeerItemRemovalUpdate by removing " + sourceConnectionId + " from item " + itemMD5 + ".");
#endif
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
        #endregion
    }
}
