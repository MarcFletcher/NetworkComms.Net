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
using NetworkCommsDotNet;
using ProtoBuf;
using System.Threading.Tasks;
using DPSBase;
using System.Threading;
using System.Net;

namespace DistributedFileSystem
{
    /// <summary>
    /// Object passed around between peers when keeping everyone updated.
    /// </summary>
    [ProtoContract]
    public class PeerChunkAvailabilityUpdate
    {
        [ProtoMember(1)]
        public string ItemCheckSum { get; private set; }
        [ProtoMember(2)]
        public ChunkFlags ChunkFlags { get; private set; }
        [ProtoMember(3)]
        public string SourceNetworkIdentifier { get; private set; }

        private PeerChunkAvailabilityUpdate() { }

        public PeerChunkAvailabilityUpdate(string sourceNetworkIdentifier, string itemCheckSum, ChunkFlags chunkFlags)
        {
            this.SourceNetworkIdentifier = sourceNetworkIdentifier;
            this.ItemCheckSum = itemCheckSum;
            this.ChunkFlags = chunkFlags;
        }
    }

    public static class LongBitCount
    {
        /// <summary>
        /// Returns the number of bits set to 1 in a ulong
        /// </summary>
        /// <param name="longToCount"></param>
        /// <returns></returns>
        public static byte CountBits(ulong inputLong)
        {
            byte currentCount = 0;
            long tmp = 0;

            int lowerLong = (int)(inputLong & 0x00000000FFFFFFFF);
            tmp = (lowerLong - ((lowerLong >> 1) & 3681400539) - ((lowerLong >> 2) & 1227133513));
            currentCount += (byte)(((tmp + (tmp >> 3)) & 3340530119) % 63);

            int upperLong = (int)(inputLong >> 32);
            tmp = (upperLong - ((upperLong >> 1) & 3681400539) - ((upperLong >> 2) & 1227133513));
            currentCount += (byte)(((tmp + (tmp >> 3)) & 3340530119) % 63);

            return currentCount;
        }
    }

    /// <summary>
    /// Provides 256 length bit flag 
    /// </summary>
    [ProtoContract]
    public class ChunkFlags
    {
        [ProtoMember(1)]
        ulong flags0;
        [ProtoMember(2)]
        ulong flags1;
        [ProtoMember(3)]
        ulong flags2;
        [ProtoMember(4)]
        ulong flags3;

        private ChunkFlags() { }

        /// <summary>
        /// Initialises the chunkflags. The initial state is generally 0 or totalNumChunks
        /// </summary>
        /// <param name="intialState"></param>
        public ChunkFlags(byte intialState)
        {
            flags0 = 0;
            flags1 = 0;
            flags2 = 0;
            flags3 = 0;

            for (byte i = 0; i < intialState; i++)
                SetFlag(i);
        }

        /// <summary>
        /// Returns true if the provided chunk is available. Zero indexed from least significant bit.
        /// </summary>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public bool FlagSet(byte chunkIndex)
        {
            if (chunkIndex > 255 || chunkIndex < 0)
                throw new Exception("Chunk index must be between 0 and 255 inclusive.");

            if (chunkIndex >= 192)
                return ((flags3 & (1UL << (chunkIndex - 192))) > 0);
            else if (chunkIndex >= 128)
                return ((flags2 & (1UL << (chunkIndex - 128))) > 0);
            else if (chunkIndex >= 64)
                return ((flags1 & (1UL << (chunkIndex - 64))) > 0);
            else
                return ((flags0 & (1UL << chunkIndex)) > 0);
        }

        /// <summary>
        /// Sets the bit flag to 1 which corresponds with the provided chunk index. Zero indexed from least significant bit.
        /// </summary>
        /// <param name="chunkIndex"></param>
        public void SetFlag(byte chunkIndex, bool state = true)
        {
            if (chunkIndex > 255 || chunkIndex < 0)
                throw new Exception("Chunk index must be between 0 and 255 inclusive.");

            //If we are setting a flag from 1 to 0
            if (state)
            {
                if (chunkIndex >= 192)
                    flags3 |= (1UL << (chunkIndex - 192));
                else if (chunkIndex >= 128)
                    flags2 |= (1UL << (chunkIndex - 128));
                else if (chunkIndex >= 64)
                    flags1 |= (1UL << (chunkIndex - 64));
                else
                    flags0 |= (1UL << chunkIndex);
            }
            else
            {
                if (chunkIndex >= 192)
                    flags3 &= ~(1UL << (chunkIndex - 192));
                else if (chunkIndex >= 128)
                    flags2 &= ~(1UL << (chunkIndex - 128));
                else if (chunkIndex >= 64)
                    flags1 &= ~(1UL << (chunkIndex - 64));
                else
                    flags0 &= ~(1UL << chunkIndex);
            }
        }

        /// <summary>
        /// Updates local chunk flags with those provided by using an OR operator. This ensures we never disable chunks which are already set available.
        /// </summary>
        /// <param name="latestChunkFlags"></param>
        public void UpdateFlags(ChunkFlags latestChunkFlags)
        {
            //flags0 |= latestChunkFlags.flags0;
            //flags1 |= latestChunkFlags.flags1;
            //flags2 |= latestChunkFlags.flags2;
            //flags3 |= latestChunkFlags.flags3;
            flags0 = latestChunkFlags.flags0;
            flags1 = latestChunkFlags.flags1;
            flags2 = latestChunkFlags.flags2;
            flags3 = latestChunkFlags.flags3;
        }

        /// <summary>
        /// Returns true if all bit flags upto the provided uptoChunkIndexInclusive are set to true
        /// </summary>
        /// <param name="uptoChunkIndexInclusive"></param>
        public bool AllFlagsSet(byte uptoChunkIndexInclusive)
        {
            for (byte i = 0; i < uptoChunkIndexInclusive; i++)
                if (!FlagSet(i))
                    return false;

            return true;
        }

        public byte NumCompletedChunks()
        {
            return (byte)(LongBitCount.CountBits(flags0) + LongBitCount.CountBits(flags1) + LongBitCount.CountBits(flags2) + LongBitCount.CountBits(flags3));
        }

        public void ClearAllFlags()
        {
            flags0 = 0;
            flags1 = 0;
            flags2 = 0;
            flags3 = 0;
        }
    }

    /// <summary>
    /// Wrapper class for ChunkFlags which allows us to include a little more information about a peer
    /// </summary>
    [ProtoContract]
    public class PeerAvailabilityInfo
    {
        /// <summary>
        /// The chunk availability for this peer.
        /// </summary>
        [ProtoMember(1)]
        public ChunkFlags PeerChunkFlags { get; private set; }

        /// <summary>
        /// For now the only extra info we want. A superPeer is generally busier network wise and should be contacted last for data.
        /// </summary>
        [ProtoMember(2)]
        public bool SuperPeer { get; private set; }

        /// <summary>
        /// Used to maintain peer busy status
        /// </summary>
        public DateTime PeerBusyAnnounce { get; private set; }
        public bool PeerBusy { get; private set; }

        bool peerOnline;
        /// <summary>
        /// A local variable which is set to true the first time a peer responds to any information/data requests
        /// </summary>
        public bool PeerOnline
        {
            get
            {
                //Super peers are always online
                return peerOnline || SuperPeer;
            }
            private set
            {
                peerOnline = value;
            }
        }

        public int timeoutCount;
        public int CurrentTimeoutCount { get { return timeoutCount; } }

        private PeerAvailabilityInfo() { }

        public PeerAvailabilityInfo(ChunkFlags peerChunkFlags, bool superPeer)
        {
            this.PeerChunkFlags = peerChunkFlags;
            this.SuperPeer = superPeer;
        }

        public void SetPeerBusy()
        {
            PeerBusyAnnounce = DateTime.Now;
            PeerBusy = true;
        }

        public void ClearBusy()
        {
            PeerBusy = false;
        }

        public void SetOnline()
        {
            PeerOnline = true;
        }

        public void SetOffline()
        {
            PeerOnline = false;
        }

        /// <summary>
        /// Returns the new timeout count value after incrementing the current count.
        /// </summary>
        /// <returns></returns>
        public int GetNewTimeoutCount()
        {
            return Interlocked.Increment(ref timeoutCount);
        }
    }

    [ProtoContract]
    public class SwarmChunkAvailability
    {
        [ProtoMember(1)]
        Dictionary<string, PeerAvailabilityInfo> peerAvailabilityByNetworkIdentifierDict;
        [ProtoMember(2)]
        Dictionary<string, ConnectionInfo> peerNetworkIdentifierToConnectionInfo;

        /// <summary>
        /// Triggered when first peer is recorded as being alive
        /// </summary>
        ManualResetEvent alivePeersReceivedEvent = new ManualResetEvent(false);

        object peerLocker = new object();

        /// <summary>
        /// Blank constructor used for serailisation
        /// </summary>
        private SwarmChunkAvailability() { }

        /// <summary>
        /// Creates a new instance of SwarmChunkAvailability for the superNode
        /// </summary>
        /// <param name="sourceNetworkIdentifier"></param>
        /// <param name="sourceConnectionInfo"></param>
        public SwarmChunkAvailability(ConnectionInfo sourceConnectionInfo, byte totalNumChunks)
        {
            //When initialising the chunk availability we add the starting source in the intialisation
            peerAvailabilityByNetworkIdentifierDict = new Dictionary<string, PeerAvailabilityInfo>() { { sourceConnectionInfo.NetworkIdentifier, new PeerAvailabilityInfo(new ChunkFlags(totalNumChunks), true) } };
            peerNetworkIdentifierToConnectionInfo = new Dictionary<string, ConnectionInfo>() { { sourceConnectionInfo.NetworkIdentifier, sourceConnectionInfo } };

            if (DFS.loggingEnabled) DFS.logger.Debug("New swarmChunkAvailability created by " + sourceConnectionInfo.NetworkIdentifier + ".");
        }

        /// <summary>
        /// Builds a dictionary of chunk availability throughout the current swarm for chunks we don't have locally. Keys are chunkIndex, peer network identifier, and peer total chunk count
        /// </summary>
        /// <param name="chunksRequired"></param>
        /// <returns></returns>
        public Dictionary<byte, Dictionary<ConnectionInfo, PeerAvailabilityInfo>> CachedNonLocalChunkExistences(byte totalChunksInItem, out Dictionary<ConnectionInfo, PeerAvailabilityInfo> nonLocalPeerAvailability)
        {
            lock (peerLocker)
            {
                Dictionary<byte, Dictionary<ConnectionInfo, PeerAvailabilityInfo>> chunkExistence = new Dictionary<byte, Dictionary<ConnectionInfo, PeerAvailabilityInfo>>();
                nonLocalPeerAvailability = new Dictionary<ConnectionInfo, PeerAvailabilityInfo>();

                //We add an entry to the dictionary for every chunk we do not yet have
                for (byte i = 0; i < totalChunksInItem; i++)
                    if (!PeerHasChunk(NetworkComms.NetworkIdentifier, i))
                        chunkExistence.Add(i, new Dictionary<ConnectionInfo, PeerAvailabilityInfo>());

                //Now for each peer we know about we add them to the list if they have a chunck of interest
                foreach (var peer in peerAvailabilityByNetworkIdentifierDict)
                {
                    //This is the only place we clear a peers busy status
                    if (peer.Value.PeerBusy && (DateTime.Now - peer.Value.PeerBusyAnnounce).TotalMilliseconds > DFS.PeerBusyTimeoutMS) peer.Value.ClearBusy();

                    if (PeerContactAllowed(peer.Key, NetworkIdentifierToConnectionInfo(peer.Key).LocalEndPoint, peer.Value.SuperPeer))
                    {
                        //For this peer for every chunk we are looking for
                        for (byte i = 0; i < totalChunksInItem; i++)
                        {
                            if (chunkExistence.ContainsKey(i) && peer.Value.PeerChunkFlags.FlagSet(i))
                            {
                                chunkExistence[i].Add(NetworkIdentifierToConnectionInfo(peer.Key), peer.Value);

                                if (!nonLocalPeerAvailability.ContainsKey(NetworkIdentifierToConnectionInfo(peer.Key)))
                                    nonLocalPeerAvailability.Add(NetworkIdentifierToConnectionInfo(peer.Key), peer.Value);
                            }
                        }
                    }
                }

                return chunkExistence;
            }
        }

        public void SetPeerBusy(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SetPeerBusy();
            }
        }

        public bool PeerBusy(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].PeerBusy;
            }

            return false;
        }

        public void SetPeerOffline(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SetOffline();
            }
        }

        public bool PeerOnline(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].PeerOnline;
            }

            return false;
        }

        /// <summary>
        /// Returns true if a peer with the provided networkIdentifier exists in the swarm for this item
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public bool PeerExistsInSwarm(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
                return peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier);
        }

        /// <summary>
        /// Returns true if the specified peer has the provided chunkIndex.
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public bool PeerHasChunk(ShortGuid networkIdentifier, byte chunkIndex)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].PeerChunkFlags.FlagSet(chunkIndex);
                else
                    throw new Exception("No peer was found in peerChunksByNetworkIdentifierDict with the provided networkIdentifier.");
            }
        }

        /// <summary>
        /// Returns true if a peer has a complete copy of a file
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public bool PeerIsComplete(ShortGuid networkIdentifier, byte totalNumChunks)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    throw new Exception("networkIdentifier provided does not exist in peerChunksByNetworkIdentifierDict. Check with PeerExistsInSwarm before calling this method.");

                return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].PeerChunkFlags.AllFlagsSet(totalNumChunks);
            }
        }

        /// <summary>
        /// Returns true if the specified peer is a super peer
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public bool PeerIsSuperPeer(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    //throw new Exception("networkIdentifier provided does not exist in peerChunksByNetworkIdentifierDict. Check with PeerExistsInSwarm before calling this method.");
                    return false;

                return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer;
            }
        }

        public int GetNewTimeoutCount(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return 0;

                return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].GetNewTimeoutCount();
            }
        }

        /// <summary>
        /// Converts a provided network identifier into an ip address. There is no guarantee the ip is connectable.
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public ConnectionInfo NetworkIdentifierToConnectionInfo(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerNetworkIdentifierToConnectionInfo.ContainsKey(networkIdentifier))
                    return peerNetworkIdentifierToConnectionInfo[networkIdentifier];
                else
                    throw new Exception("Unable to convert network identifier to ip as it's entry was not found in peerNetworkIdentifierToIP dictionary.");
            }
        }

        /// <summary>
        /// Delets the knowledge of a peer from our local swarm chunk availability
        /// </summary>
        /// <param name="networkIdentifier"></param>
        public void RemovePeerFromSwarm(ShortGuid networkIdentifier, bool forceRemove = false)
        {
            try
            {
                if (networkIdentifier == null || networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

                lock (peerLocker)
                {
                    //We only remove the peer if we have more than one and it is not a super peer
                    if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    {
                        //We can remove this peer if
                        //1. We have set force remove
                        //or
                        //2. We have more than atleast 1 other peer AND if this is a super peer we need atleast 1 other super peer in order to remove
                        if (forceRemove || (peerAvailabilityByNetworkIdentifierDict.Count > 1 && (!peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer || (from current in peerAvailabilityByNetworkIdentifierDict where current.Value.SuperPeer select current.Key).Count() > 1)))
                        {
                            peerAvailabilityByNetworkIdentifierDict.Remove(networkIdentifier);

                            if (peerNetworkIdentifierToConnectionInfo.ContainsKey(networkIdentifier))
                            {
                                if (DFS.loggingEnabled) DFS.logger.Trace(" ... removed " + peerNetworkIdentifierToConnectionInfo[networkIdentifier] + " with connectionInfo from item.");

                                peerNetworkIdentifierToConnectionInfo.Remove(networkIdentifier);
                            }
                            else
                                if (DFS.loggingEnabled) DFS.logger.Trace(" ... removed " + networkIdentifier + " from item.");
                        }
                        else
                            if (DFS.loggingEnabled) DFS.logger.Trace(" ... remove failed as forceRemove= " + forceRemove + ", peerAvailabilityByNetworkIdentifierDict.Count=" + peerAvailabilityByNetworkIdentifierDict.Count + ", isSuperPeer=" + peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer + ", superPeerCount=" + (from current in peerAvailabilityByNetworkIdentifierDict where current.Value.SuperPeer select current.Key).Count());
                    }
                    else if (DFS.loggingEnabled) DFS.logger.Trace(" ... peer not removed as does not exist in local SwarmChunkAvailability.");
                }
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_RemovePeerFromSwarm");
            }
        }

        /// <summary>
        /// Adds or updates a peer to the local availability list. Usefull for when a peer informs us of an updated availability.
        /// </summary>
        /// <param name="peerNetworkIdentifier"></param>
        /// <param name="latestChunkFlags"></param>
        public void AddOrUpdateCachedPeerChunkFlags(ConnectionInfo connectionInfo, ChunkFlags latestChunkFlags, bool superPeer = false)
        {
            if (connectionInfo.NetworkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (connectionInfo.ConnectionType != ConnectionType.TCP) throw new Exception("Only the TCP side of a DFS peer should be tracked.");

                IPEndPoint endPointToUse = null;
                if (connectionInfo.RemoteEndPoint == null)
                    endPointToUse = connectionInfo.LocalEndPoint;
                else
                    endPointToUse = connectionInfo.RemoteEndPoint;

                //We can only add a peer if it is listening
                if (endPointToUse.Port <= DFS.MaxTargetLocalPort && endPointToUse.Port >= DFS.MinTargetLocalPort)
                {
                    RemoveOldPeersAtEndPoint(connectionInfo.NetworkIdentifier, endPointToUse);

                    if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(connectionInfo.NetworkIdentifier))
                    {
                        if (peerNetworkIdentifierToConnectionInfo[connectionInfo.NetworkIdentifier].LocalEndPoint.Port != endPointToUse.Port)
                            peerNetworkIdentifierToConnectionInfo[connectionInfo.NetworkIdentifier] = connectionInfo;

                        peerAvailabilityByNetworkIdentifierDict[connectionInfo.NetworkIdentifier].PeerChunkFlags.UpdateFlags(latestChunkFlags);

                        if (DFS.loggingEnabled) DFS.Logger.Trace("Updated existing chunk flags for "+ connectionInfo);
                    }
                    else
                    {
                        //We also need to add the ip address. This should not fail as if we are calling this method locally we should have the relevant connection
                        if (!peerNetworkIdentifierToConnectionInfo.ContainsKey(connectionInfo.NetworkIdentifier))
                            peerNetworkIdentifierToConnectionInfo.Add(connectionInfo.NetworkIdentifier, new ConnectionInfo(connectionInfo.ConnectionType, connectionInfo.NetworkIdentifier, endPointToUse, connectionInfo.IsConnectable));

                        peerAvailabilityByNetworkIdentifierDict.Add(connectionInfo.NetworkIdentifier, new PeerAvailabilityInfo(latestChunkFlags, superPeer));

                        if (DFS.loggingEnabled) DFS.Logger.Trace("Added new chunk flags for " + connectionInfo);
                    }

                    //By updating cached peer chunk flags we set the peer as being online
                    peerAvailabilityByNetworkIdentifierDict[connectionInfo.NetworkIdentifier].SetOnline();

                    //We will trigger the alive peers event when we have atleast a third of the existing peers
                    if (!alivePeersReceivedEvent.WaitOne(0))
                    {
                        int numOnlinePeers = (from current in peerAvailabilityByNetworkIdentifierDict.Values where current.PeerOnline select current).Count();
                        if (numOnlinePeers >= DFS.NumTotalGlobalRequests || numOnlinePeers > peerAvailabilityByNetworkIdentifierDict.Count / 3.0)
                            alivePeersReceivedEvent.Set();
                    }
                }
                else
                    NetworkComms.LogError(new Exception("Attemped to AddOrUpdateCachedPeerChunkFlags for client which was not listening or was using port outside the valid DFS range."), "PeerChunkFlagsUpdateError", "IP:" + endPointToUse.Address.ToString() + ", Port:" + endPointToUse.Port);
            }
        }

        /// <summary>
        /// Removes any peers which have the same endPoint as the provided currentActivePeerEndPoint except one with matching currentActivePeerIdentifier
        /// </summary>
        /// <param name="currentActivePeerIdentifier">The NetworkIdenfier of the known active peer</param>
        /// <param name="currentActivePeerEndPoint">The endPoint of the known active peer</param>
        public void RemoveOldPeersAtEndPoint(ShortGuid currentActivePeerIdentifier, IPEndPoint currentActivePeerEndPoint)
        {
            lock (peerLocker)
            {
                #region Remove Any Duplicate EndPoint Matches
                //Look for endPoint matches, if there are duplicates we delete any old ones
                List<ShortGuid> deadIdentifiers = new List<ShortGuid>();
                foreach (var connectionInfoEntry in peerNetworkIdentifierToConnectionInfo)
                {
                    if (connectionInfoEntry.Value.LocalEndPoint.Equals(currentActivePeerEndPoint) && !connectionInfoEntry.Key.Equals(currentActivePeerIdentifier))
                        deadIdentifiers.Add(connectionInfoEntry.Key);
                }
                if (deadIdentifiers.Count > 0)
                {
                    if (DFS.loggingEnabled) DFS.Logger.Trace("Removed " + deadIdentifiers.Count + " dead peers from the local swarmChunkAvailability using endPoint " + currentActivePeerEndPoint);

                    peerNetworkIdentifierToConnectionInfo = (from current in peerNetworkIdentifierToConnectionInfo where !deadIdentifiers.Contains(current.Key) select current).ToDictionary(entry => entry.Key, entry => entry.Value);
                    peerAvailabilityByNetworkIdentifierDict = (from current in peerAvailabilityByNetworkIdentifierDict where !deadIdentifiers.Contains(current.Key) select current).ToDictionary(entry => entry.Key, entry => entry.Value);
                }
                #endregion
            }
        }

        /// <summary>
        /// Sets our local availability
        /// </summary>
        /// <param name="chunkIndex"></param>
        public void SetLocalChunkFlag(byte chunkIndex, bool setAvailable)
        {
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(NetworkComms.NetworkIdentifier))
                    throw new Exception("Local peer not located in peerChunkAvailabity for this item.");

                peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].PeerChunkFlags.SetFlag(chunkIndex, setAvailable);
            }
        }

        /// <summary>
        /// Update the chunk availability by contacting all existing peers. If a cascade depth greater than 1 is provided will also contact each peers peers.
        /// </summary>
        /// <param name="cascadeDepth"></param>
        public void UpdatePeerAvailability(string itemCheckSum, int cascadeDepth, int responseWaitMS = 5000, Action<string> buildLog = null)
        {
            if (buildLog != null) buildLog("Starting UpdatePeerAvailability update.");

            string[] currentPeerKeys;
            lock (peerLocker) currentPeerKeys = peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();

            if (cascadeDepth > 0)
            {
                if (cascadeDepth > 1) throw new NotImplementedException("A cascading update greater than 1 is not yet supported.");

                //If we are going to cascade peers we need to contact all our known peers and get them to send us their known peers
                #region GetAllUnknownPeers
                //Contact all known peers and request an availability update
                foreach (ShortGuid peerIdentifier in currentPeerKeys)
                {
                    try
                    {
                        IPEndPoint peerEndPoint;
                        bool superPeer;
                        lock (peerLocker)
                        {
                            peerEndPoint = peerNetworkIdentifierToConnectionInfo[peerIdentifier].LocalEndPoint;
                            superPeer = peerAvailabilityByNetworkIdentifierDict[peerIdentifier].SuperPeer;
                        }

                        //We don't want to contact ourselves plus we check the possible exception lists
                        if (PeerContactAllowed(peerIdentifier, peerEndPoint, superPeer))
                        {
                            if (buildLog != null) buildLog("Contacting " + peerNetworkIdentifierToConnectionInfo[peerIdentifier] + " for a DFS_KnownPeersRequest.");
                            
                            UDPConnection.SendObject("DFS_KnownPeersRequest", itemCheckSum, peerEndPoint, DFS.nullCompressionSRO);
                            //if (buildLog != null) buildLog(" ... completed DFS_KnownPeersRequest with " + peerNetworkIdentifierToConnectionInfo[peerIdentifier]);
                        }
                    }
                    catch (CommsException)
                    {
                        //If a peer has disconnected or fails to respond we just remove them from the list
                        RemovePeerFromSwarm(peerIdentifier);
                        if (buildLog != null) buildLog("Removing " + peerIdentifier + " from item swarm due to CommsException.");
                    }
                    catch (KeyNotFoundException)
                    {
                        //This exception will get thrown if we try to access a peers connecitonInfo from peerNetworkIdentifierToConnectionInfo 
                        //but it has been removed since we accessed the peerKeys at the start of this method
                        //We could probably be a bit more carefull with how we maintain these references but we either catch the
                        //exception here or networkcomms will throw one when we try to connect to an old peer.
                        RemovePeerFromSwarm(peerIdentifier);
                        if (buildLog != null) buildLog("Removing " + peerIdentifier + " from item swarm due to KeyNotFoundException.");
                    }
                    catch (Exception ex)
                    {
                        NetworkComms.LogError(ex, "UpdatePeerChunkAvailabilityError_1");
                    }
                }
                #endregion
            }

            //Contact all our original peers and request a chunk availability update
            #region GetAllOriginalPeerAvailabilityFlags
            //Our own current availability
            foreach (ShortGuid peerIdentifier in currentPeerKeys)
            { 
                try
                {
                    IPEndPoint peerEndPoint;
                    bool superPeer;
                    lock (peerLocker)
                    {
                        peerEndPoint = peerNetworkIdentifierToConnectionInfo[peerIdentifier].LocalEndPoint;
                        superPeer = peerAvailabilityByNetworkIdentifierDict[peerIdentifier].SuperPeer;
                    }

                    //We don't want to contact ourselves and for now that includes anything having the same ip as us
                    if (PeerContactAllowed(peerIdentifier, peerEndPoint, superPeer))
                    {
                        if (buildLog != null) buildLog("Contacting " + peerNetworkIdentifierToConnectionInfo[peerIdentifier] + " for a DFS_ChunkAvailabilityRequest from within UpdatePeerAvailability.");
                        
                        //Request a chunk update
                        UDPConnection.SendObject("DFS_ChunkAvailabilityRequest", itemCheckSum, peerEndPoint, DFS.nullCompressionSRO);

                        //if (buildLog != null) buildLog(" ... completed DFS_ChunkAvailabilityRequest with " + peerNetworkIdentifierToConnectionInfo[peerIdentifier]);
                    }
                }
                catch (CommsException)
                {
                    //If a peer has disconnected we just remove them from the list
                    RemovePeerFromSwarm(peerIdentifier);
                    if (buildLog != null) buildLog("Removing " + peerIdentifier + " from item swarm due to CommsException.");
                    //NetworkComms.LogError(ex, "UpdatePeerChunkAvailabilityCommsError");
                }
                catch (KeyNotFoundException)
                {
                    //This exception will get thrown if we try to access a peers connecitonInfo from peerNetworkIdentifierToConnectionInfo 
                    //but it has been removed since we accessed the peerKeys at the start of this method
                    //We could probably be a bit more carefull with how we maintain these references but we either catch the
                    //exception here or networkcomms will throw one when we try to connect to an old peer.
                    RemovePeerFromSwarm(peerIdentifier);
                    if (buildLog != null) buildLog("Removing " + peerIdentifier + " from item swarm due to KeyNotFoundException.");
                }
                catch (Exception ex)
                {
                    NetworkComms.LogError(ex, "UpdatePeerChunkAvailabilityError_2");
                }
            }
            #endregion

            //Thread.Sleep(responseWaitMS);
            if (alivePeersReceivedEvent.WaitOne(responseWaitMS))
            {
                if (buildLog != null)
                    buildLog("Completed SwarmChunkAvailability update succesfully.");

                //If the event was succesfully triggered we wait an additional 100ms to give other responses a chance to be handled
                Thread.Sleep(250);
            }
            else
            {
                if (buildLog != null)
                    buildLog("Completed SwarmChunkAvailability update by timeout.");
            }
        }

        /// <summary>
        /// Metric used to determine the health of a chunk and whether swarm will benefit from a broadcasted update. A value greater than 1 signifies a healthy chunk availability.
        /// </summary>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public double ChunkHealthMetric(byte chunkIndex, byte totalNumChunks)
        {
            lock (peerLocker)
            {
                //How many peers have this chunk
                int chunkExistenceCount = (from current in peerAvailabilityByNetworkIdentifierDict where current.Value.PeerChunkFlags.FlagSet(chunkIndex) select current.Key).Count();

                //How many peers are currently assembling the file
                int totalNumIncompletePeers = peerAvailabilityByNetworkIdentifierDict.Count(entry => !entry.Value.PeerChunkFlags.AllFlagsSet(totalNumChunks));

                //=((1.5*($A3-0.5))/B$2)

                if (totalNumIncompletePeers == 0)
                    return 100;
                else
                    return (1.5 * ((double)chunkExistenceCount - 0.5)) / (double)totalNumIncompletePeers;
            }
        }

        /// <summary>
        /// Records a chunk as available for the local peer
        /// </summary>
        /// <param name="chunkIndex"></param>
        public void RecordLocalChunkCompletion(byte chunkIndex)
        {
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(NetworkComms.NetworkIdentifier))
                    throw new Exception("Local peer not located in peerChunkAvailabity for this item.");

                peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].PeerChunkFlags.SetFlag(chunkIndex);
            }
        }

        /// <summary>
        /// Updates all peers in the swarm that we have updated a chunk
        /// </summary>
        /// <param name="itemCheckSum"></param>
        /// <param name="chunkIndex"></param>
        public void BroadcastLocalAvailability(string itemCheckSum)
        {
            //Console.WriteLine("Updating swarm availability.");

            string[] peerKeys;
            ChunkFlags localChunkFlags;
            lock (peerLocker)
            {
                peerKeys = peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();
                localChunkFlags = peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].PeerChunkFlags;
            }

            foreach (ShortGuid peerIdentifier in peerKeys)
            {
                try
                {
                    IPEndPoint peerEndPoint;
                    bool superPeer;
                    lock (peerLocker)
                    {
                        peerEndPoint = peerNetworkIdentifierToConnectionInfo[peerIdentifier].LocalEndPoint;
                        superPeer = peerAvailabilityByNetworkIdentifierDict[peerIdentifier].SuperPeer;
                    }

                    //We don't want to contact ourselves
                    if (PeerContactAllowed(peerIdentifier, peerEndPoint, superPeer))
                        //TCPConnection.GetConnection(new ConnectionInfo(peerEndPoint)).SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(itemCheckSum, localChunkFlags));
                        UDPConnection.SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(NetworkComms.NetworkIdentifier, itemCheckSum, localChunkFlags), peerEndPoint, DFS.nullCompressionSRO);
                }
                //We don't just want to catch comms exceptions because if a peer has potentially disconnected we may get a KeyNotFoundException here
                catch (Exception)
                {
                    //If a peer has disconnected we just remove them from the list
                    RemovePeerFromSwarm(peerIdentifier);

                    if (DFS.loggingEnabled)
                        DFS.Logger.Trace("Removed peer from swarm during BroadcastLocalAvailability due to exception - " + peerIdentifier);
                }
            }
        }

        /// <summary>
        /// Closes established connections with completed peers as they are now redundant
        /// </summary>
        public void CloseConnectionToCompletedPeers(byte totalNumChunks)
        {
            Dictionary<string, PeerAvailabilityInfo> dictCopy;
            lock (peerLocker)
                dictCopy = new Dictionary<string, PeerAvailabilityInfo>(peerAvailabilityByNetworkIdentifierDict);

            Parallel.ForEach(dictCopy, peer =>
            {
                try
                {
                    if (!peer.Value.SuperPeer && peer.Key != NetworkComms.NetworkIdentifier)
                    {
                        if (peer.Value.PeerChunkFlags.AllFlagsSet(totalNumChunks))
                        {
                            var connections = NetworkComms.GetExistingConnection(peer.Key, ConnectionType.TCP);
                            //NetworkComms.CloseConnection();
                            if (connections != null) foreach (var connection in connections) connection.CloseConnection(false);
                        }
                    }
                }
                catch (Exception) { }
            });
        }

        /// <summary>
        /// Single method for determing if contact can be made with the request peer.
        /// Prevents loopback contact via matching identifier and currentLocalListenEndPoints.
        /// Finally uses the DFS.AllowedPeerIPS and DFS.DisallowedPeerIPS if set.
        /// </summary>
        /// <param name="peerIdentifier"></param>
        /// <param name="peerEndPoint"></param>
        /// <param name="superPeer"></param>
        /// <returns></returns>
        public bool PeerContactAllowed(ShortGuid peerIdentifier, IPEndPoint peerEndPoint, bool superPeer)
        {
            if (peerIdentifier == NetworkComms.NetworkIdentifier)
                return false;

            List<IPEndPoint> currentLocalListenEndPoints = TCPConnection.ExistingLocalListenEndPoints();
            if (currentLocalListenEndPoints.Contains(peerEndPoint))
                return false;

            //We always allow super peers
            //If this is not a super peer and we have set the allowedPeerIPs then we check in there
            bool peerAllowed = false;
            if (!superPeer && (DFS.allowedPeerIPs.Count > 0 || DFS.disallowedPeerIPs.Count > 0))
            {
                if (DFS.allowedPeerIPs.Count > 0)
                    peerAllowed = DFS.allowedPeerIPs.Contains(peerEndPoint.Address.ToString());
                else
                    peerAllowed = !DFS.disallowedPeerIPs.Contains(peerEndPoint.Address.ToString());
            }
            else
                peerAllowed = true;

            return peerAllowed;

        }

        public ChunkFlags PeerChunkAvailability(ShortGuid peerIdentifier)
        {
            if (peerIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (PeerExistsInSwarm(peerIdentifier))
                    return peerAvailabilityByNetworkIdentifierDict[peerIdentifier].PeerChunkFlags;
                else
                    throw new Exception("A peer with the provided peerIdentifier does not exist in this swarm.");
            }
        }

        /// <summary>
        /// Broadcast to all known peers that the local DFS is removing the specified item
        /// </summary>
        /// <param name="itemCheckSum"></param>
        public void BroadcastItemRemoval(string itemCheckSum, bool removeSwarmWide)
        {
            string[] peerKeys;
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(NetworkComms.NetworkIdentifier))
                    throw new Exception("Local peer not located in peerChunkAvailabity for this item.");

                if (removeSwarmWide && !peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].SuperPeer)
                    throw new Exception("Attempted to trigger a swarm wide item removal but local is not a SuperPeer.");

                peerKeys = peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();
            }

            foreach (ShortGuid peerIdentifier in peerKeys)
            {
                try
                {
                    if (peerIdentifier == null) throw new NullReferenceException("peerIdentifier should not be null at this point.");
                    if (NetworkComms.NetworkIdentifier == null) throw new NullReferenceException("Local networkIdentifier really should NOT be null at this point.");

                    IPEndPoint peerEndPoint;
                    bool superPeer;
                    lock (peerLocker)
                    {
                        peerEndPoint = peerNetworkIdentifierToConnectionInfo[peerIdentifier].LocalEndPoint;
                        superPeer = peerAvailabilityByNetworkIdentifierDict[peerIdentifier].SuperPeer;
                    }

                    //We don't want to contact ourselves
                    if (PeerContactAllowed(peerIdentifier, peerEndPoint, superPeer))
                        //TCPConnection.GetConnection(new ConnectionInfo(peerEndPoint)).SendObject("DFS_ItemRemovalUpdate", new ItemRemovalUpdate(itemCheckSum, removeSwarmWide));
                        UDPConnection.SendObject("DFS_ItemRemovalUpdate", new ItemRemovalUpdate(NetworkComms.NetworkIdentifier, itemCheckSum, removeSwarmWide), peerEndPoint, DFS.nullCompressionSRO);
                }
                catch (CommsException)
                {
                    RemovePeerFromSwarm(peerIdentifier);
                }
                catch (KeyNotFoundException) { /*The peer has probably already disconnected*/ }
                catch (Exception e)
                {
                    RemovePeerFromSwarm(peerIdentifier);
                    NetworkComms.LogError(e, "BroadcastItemRemovalError");
                }
            }
        }

        public int NumPeersInSwarm(bool excludeSuperPeers = false)
        {
            lock (peerLocker)
            {
                if (excludeSuperPeers)
                    return (from current in peerAvailabilityByNetworkIdentifierDict where !current.Value.SuperPeer select current).Count();
                else
                    return peerAvailabilityByNetworkIdentifierDict.Count;
            }
        }

        public int NumCompletePeersInSwarm(byte totalItemChunks, bool excludeSuperPeers = false)
        {
            lock (peerLocker)
            {
                if (excludeSuperPeers)
                    return (from current in peerAvailabilityByNetworkIdentifierDict where !current.Value.SuperPeer && current.Value.PeerChunkFlags.AllFlagsSet(totalItemChunks) select current).Count();
                else
                    return (from current in peerAvailabilityByNetworkIdentifierDict where current.Value.PeerChunkFlags.AllFlagsSet(totalItemChunks) select current).Count();
            }
        }

        /// <summary>
        /// Returns an array containing the network identifiers of every peer in this swarm
        /// </summary>
        /// <returns></returns>
        public string[] AllPeerIdentifiers()
        {
            lock (peerLocker)
                return peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();
        }

        /// <summary>
        /// Returns an array containing all known peer endpoints inthe format locaIP:port
        /// </summary>
        /// <returns></returns>
        public string[] AllPeerEndPoints()
        {
            lock (peerLocker)
                return (from current in peerNetworkIdentifierToConnectionInfo select current.Value.LocalEndPoint.Address.ToString() + ":" + current.Value.LocalEndPoint.Port).ToArray();
        }

        public void ClearAllLocalAvailabilityFlags()
        {
            lock (peerLocker)
                peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].PeerChunkFlags.ClearAllFlags();
        }

        #region IThreadSafeSerialise Members
        public byte[] ThreadSafeSerialise()
        {
            try
            {
                return DPSManager.GetDataSerializer<ProtobufSerializer>().SerialiseDataObject<SwarmChunkAvailability>(this).ThreadSafeStream.ToArray();
            }
            catch(Exception)
            {
                AfterSerialise();
            }

            return null;
        }

        [ProtoBeforeSerialization]
        private void BeforeSerialise()
        {
            Monitor.Enter(peerLocker);
        }

        [ProtoAfterSerialization]
        private void AfterSerialise()
        {
            Monitor.Exit(peerLocker);
        }
        #endregion
    }

    [ProtoContract]
    public class KnownPeerEndPoints
    {
        [ProtoMember(1)]
        public string ItemChecksm { get; private set; }
        [ProtoMember(2)]
        public string[] PeerEndPoints {get; private set;}

        private KnownPeerEndPoints() { }

        public KnownPeerEndPoints(string itemCheckSum, string[] knownPeerEndPoints)
        {
            this.ItemChecksm = itemCheckSum;
            this.PeerEndPoints = knownPeerEndPoints;
        }
    }
}
