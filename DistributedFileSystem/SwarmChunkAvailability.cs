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
using NetworkCommsDotNet;
using ProtoBuf;
using System.Threading.Tasks;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.UDP;
using NetworkCommsDotNet.Connections.TCP;
using System.Threading;
using System.Net;
using System.IO;

namespace DistributedFileSystem
{
    /// <summary>
    /// Object passed around peers to keep everyone updated.
    /// </summary>
    [ProtoContract]
    public class PeerChunkAvailabilityUpdate
    {
        /// <summary>
        /// The related DFS item checksum
        /// </summary>
        [ProtoMember(1)]
        public string ItemCheckSum { get; private set; }

        /// <summary>
        /// The chunk availability flags 
        /// </summary>
        [ProtoMember(2)]
        public ChunkFlags ChunkFlags { get; private set; }

        /// <summary>
        /// The source network identifier
        /// </summary>
        [ProtoMember(3)]
        public string SourceNetworkIdentifier { get; private set; }

        private PeerChunkAvailabilityUpdate() { }

        /// <summary>
        /// Instantiate a new PeerChunkAvailabilityUpdate
        /// </summary>
        /// <param name="sourceNetworkIdentifier">The source network identifier</param>
        /// <param name="itemCheckSum">The related DFS item checksum</param>
        /// <param name="chunkFlags">The chunk availability flags </param>
        public PeerChunkAvailabilityUpdate(string sourceNetworkIdentifier, string itemCheckSum, ChunkFlags chunkFlags)
        {
            this.SourceNetworkIdentifier = sourceNetworkIdentifier;
            this.ItemCheckSum = itemCheckSum;
            this.ChunkFlags = chunkFlags;
        }
    }

    /// <summary>
    /// Utility class used to count the number of set bits in a ulong
    /// </summary>
    public static class LongBitCount
    {
        /// <summary>
        /// Returns the number of bits set to 1 in a ulong
        /// </summary>
        /// <param name="inputLong">The ulong to count</param>
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
    /// Provides a 256 length bit flag 
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
        /// Initialises the ChunkFlags. The initial state is typically 0 or totalNumChunks
        /// </summary>
        /// <param name="intialState">The initial state of the ChunkFlags</param>
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
        /// <param name="chunkIndex">The chunk index flag to check</param>
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
        /// <param name="chunkIndex">The chunk index to set</param>
        /// <param name="state">The state of the flag</param>
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
        /// Updates local chunk flags with those provided.
        /// </summary>
        /// <param name="latestChunkFlags">The new chunk flags</param>
        public void UpdateFlags(ChunkFlags latestChunkFlags)
        {
            flags0 = latestChunkFlags.flags0;
            flags1 = latestChunkFlags.flags1;
            flags2 = latestChunkFlags.flags2;
            flags3 = latestChunkFlags.flags3;
        }

        /// <summary>
        /// Returns true if all bit flags up to the provided uptoChunkIndexInclusive are set to true
        /// </summary>
        /// <param name="uptoChunkIndexInclusive">The chunk index up to which the flags should be checked</param>
        public bool AllFlagsSet(byte uptoChunkIndexInclusive)
        {
            for (byte i = 0; i < uptoChunkIndexInclusive; i++)
                if (!FlagSet(i))
                    return false;

            return true;
        }

        /// <summary>
        /// Returns the number of completed chunk
        /// </summary>
        /// <returns></returns>
        public byte NumCompletedChunks()
        {
            return (byte)(LongBitCount.CountBits(flags0) + LongBitCount.CountBits(flags1) + LongBitCount.CountBits(flags2) + LongBitCount.CountBits(flags3));
        }

        /// <summary>
        /// Sets all chunk flags to unset.
        /// </summary>
        public void ClearAllFlags()
        {
            flags0 = 0;
            flags1 = 0;
            flags2 = 0;
            flags3 = 0;
        }
    }

    /// <summary>
    /// Wrapper class which contains all of the information, for a single peer, for single distributed item. A peer has a single
    /// known chunk availability and identifier but multiple possible IPEndPoints.
    /// </summary>
    [ProtoContract]
    public class PeerInfo
    {
        private object syncRoot = new object();

        /// <summary>
        /// Identifies this peer info
        /// </summary>
        [ProtoMember(1)]
        public string PeerNetworkIdentifier { get; private set; }

        /// <summary>
        /// The chunk availability for this peer.
        /// </summary>
        [ProtoMember(2)]
        public ChunkFlags PeerChunkFlags { get; private set; }

        /// <summary>
        /// For now the only extra info we want. A superPeer is generally busier network wise and should be contacted last for data.
        /// </summary>
        [ProtoMember(3)]
        public bool SuperPeer { get; private set; }

        /// <summary>
        /// All ConnectionInfos corresponding with this peer
        /// </summary>
        private List<ConnectionInfo> PeerConnectionInfo { get; set; }

        /// <summary>
        /// Protobuf cannopt serialize ConnectionInfo so we provide a backing field and use the ProtoBeforeSerialization and ProtoAfterSerialization methodology to serialize by hand
        /// </summary>
        [ProtoMember(4)]
        private List<byte[]> _peerConnectionInfoSerialized { get; set; }

        /// <summary>
        /// Used to maintain peer status
        /// </summary>
        private Dictionary<ConnectionInfo, DateTime> IPEndPointBusyAnnounceTimeDict = new Dictionary<ConnectionInfo, DateTime>();
        private Dictionary<ConnectionInfo, bool> IPEndPointOnlineDict = new Dictionary<ConnectionInfo, bool>();
        private Dictionary<ConnectionInfo, bool> IPEndPointBusyDict = new Dictionary<ConnectionInfo, bool>();
        private Dictionary<ConnectionInfo, int> IPEndPointTimeoutCountDict = new Dictionary<ConnectionInfo, int>();

        private PeerInfo() { }

        /// <summary>
        /// Initialise a new PeerInfo
        /// </summary>
        /// <param name="peerConnectionInfo">All ConnectionInfos corresponding with this peer</param>
        /// <param name="peerChunkFlags">The initial ChunkFlags for this peer</param>
        /// <param name="superPeer">True if this is a SuperPeer</param>
        public PeerInfo(List<ConnectionInfo> peerConnectionInfo, ChunkFlags peerChunkFlags, bool superPeer)
        {
            if (peerConnectionInfo.Count == 0)
                throw new ArgumentException("Provided peerConnectionInfo list must contain at least one element.");

            this.PeerNetworkIdentifier = peerConnectionInfo[0].NetworkIdentifier;

            if (this.PeerNetworkIdentifier == null || this.PeerNetworkIdentifier == ShortGuid.Empty)
                throw new Exception("PeerInfo PeerNetworkIdentifier should not be empty.");

            foreach (ConnectionInfo info in peerConnectionInfo)
            {
                if (info.NetworkIdentifier != this.PeerNetworkIdentifier)
                    throw new Exception("The provided peerConnectionInfo contains more than one unique NetworkIdentifier.");

                //Add the necessary entries into status dictionaries
                IPEndPointBusyAnnounceTimeDict[info] = DateTime.Now;
                IPEndPointOnlineDict[info] = false;
                IPEndPointBusyDict[info] = false;
                IPEndPointTimeoutCountDict[info] = 0;
            }

            this.PeerConnectionInfo = peerConnectionInfo;
            this.PeerChunkFlags = peerChunkFlags;
            this.SuperPeer = superPeer;
        }

        /// <summary>
        /// Returns PeerConnectionInfo.Count
        /// </summary>
        public int NumberOfConnectionInfos { get { return PeerConnectionInfo.Count; } }

        /// <summary>
        /// Returns true if this peer has at least one on line ipEndPoint
        /// </summary>
        /// <returns></returns>
        public bool HasAtleastOneOnlineIPEndPoint() { return (from current in IPEndPointOnlineDict.Values where current select current).Count() > 0; }

        /// <summary>
        /// Returns true if the specified peer has the specified IPEndPoint online.
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the peer to check</param>
        /// <param name="peerIPEndPoint">The IPEndPoint of the peer to check</param>
        /// <returns></returns>
        public bool IsPeerIPEndPointOnline(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);

                if (SuperPeer) return true;

                if (IPEndPointOnlineDict.ContainsKey(connectionInfo))
                    return IPEndPointOnlineDict[connectionInfo];
                else
                    return false;
            }
        }

        /// <summary>
        /// Update the provided peer IPEndPoint online status
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <param name="onlineStatus">The new online status</param>
        public void SetPeerIPEndPointOnlineStatus(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint, bool onlineStatus)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);
                IPEndPointOnlineDict[connectionInfo] = onlineStatus;
            }
        }

        /// <summary>
        /// Returns the current busy status of the requested peer IPEndPoint
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public bool IsPeerIPEndPointBusy(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);

                if (IPEndPointBusyDict.ContainsKey(connectionInfo))
                    return IPEndPointBusyDict[connectionInfo];
                else
                    return false;
            }
        }

        /// <summary>
        /// Update the provided peer IPEndPoint busy status
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <param name="busyStatus">The new peer busy status</param>
        public void SetPeerIPEndPointBusyStatus(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint, bool busyStatus)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);

                IPEndPointBusyDict[connectionInfo] = busyStatus;
                IPEndPointBusyAnnounceTimeDict[connectionInfo] = DateTime.Now;
            }
        }

        /// <summary>
        /// Clear any busy flags set for the IPEndPoints of this peer if they are older than the provided MS
        /// </summary>
        /// <param name="msSinceBusyToClear">Milliseconds since busy flag was set to clear</param>
        public void CheckAllIPEndPointBusyFlags(int msSinceBusyToClear)
        {
            lock (syncRoot)
            {
                List<ConnectionInfo> connectionInfos = IPEndPointBusyDict.Keys.ToList();
                foreach (var connectionInfo in connectionInfos)
                {
                    //If marked as busy
                    if (IPEndPointBusyDict[connectionInfo])
                    {
                        if ((DateTime.Now - IPEndPointBusyAnnounceTimeDict[connectionInfo]).TotalMilliseconds > msSinceBusyToClear)
                            IPEndPointBusyDict[connectionInfo] = false;
                    }
                }
            }
        }

        /// <summary>
        /// Return the current timeout count value.
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public int GetCurrentTimeoutCount(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);

                if (IPEndPointTimeoutCountDict.ContainsKey(connectionInfo))
                    return IPEndPointTimeoutCountDict[connectionInfo];
                else
                    return 0;
            }
        }

        /// <summary>
        /// Returns the new timeout count value after incrementing the timeout count.
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public int GetNewTimeoutCount(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);

                if (IPEndPointTimeoutCountDict.ContainsKey(connectionInfo))
                    return ++IPEndPointTimeoutCountDict[connectionInfo];
                else
                {
                    IPEndPointTimeoutCountDict[connectionInfo] = 1;
                    return 1;
                }
            }
        }

        /// <summary>
        /// Returns a new list containing all peer ConnectionInfos
        /// </summary>
        /// <returns></returns>
        public List<ConnectionInfo> GetConnectionInfo()
        {
            lock (syncRoot)
                return PeerConnectionInfo.ToList();
        }

        /// <summary>
        /// Removes the provided connectionInfo from all internal dictionaries. Returns true if connectionInfo exists, otherwise false
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public bool RemovePeerIPEndPoint(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                if (PeerConnectionInfo.Contains(connectionInfo))
                {
                    PeerConnectionInfo.Remove(connectionInfo);
                    IPEndPointBusyAnnounceTimeDict.Remove(connectionInfo);
                    IPEndPointOnlineDict.Remove(connectionInfo);
                    IPEndPointBusyDict.Remove(connectionInfo);
                    IPEndPointTimeoutCountDict.Remove(connectionInfo);

                    return true;
                }
                else
                    return false;
            }
        }

        /// <summary>
        /// Add new IPEndPoint for a peer. Returns true if successfully added, otherwise false.
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public bool AddPeerIPEndPoint(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);

                if (PeerConnectionInfo.Contains(connectionInfo))
                    return false;
                else
                {
                    PeerConnectionInfo.Add(connectionInfo);
                    IPEndPointBusyAnnounceTimeDict.Add(connectionInfo, DateTime.Now);
                    IPEndPointOnlineDict.Add(connectionInfo, false);
                    IPEndPointBusyDict.Add(connectionInfo, false);
                    IPEndPointTimeoutCountDict.Add(connectionInfo, 0);

                    return true;
                }
            }
        }

        /// <summary>
        /// Returns true if the provided IPEndPoint exists for this peer
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public bool PeerContainsIPEndPoint(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            ConnectionInfo connectionInfo = new ConnectionInfo(ConnectionType.TCP, networkIdentifier, peerIPEndPoint, true);
            lock (syncRoot)
            {
                ValidateNetworkIdentifier(connectionInfo);

                return PeerConnectionInfo.Contains(connectionInfo);
            }
        }

        /// <summary>
        /// A private method which checks the provided network identifier with that expected.
        /// </summary>
        /// <param name="connectionInfo"></param>
        private void ValidateNetworkIdentifier(ConnectionInfo connectionInfo)
        {
            if (this.PeerNetworkIdentifier != connectionInfo.NetworkIdentifier)
                throw new Exception("Attempted to modify PeerInfo for peer " + PeerNetworkIdentifier + " with data corresponding with peer " + connectionInfo.NetworkIdentifier);
        }

        /// <summary>
        /// Returns a clean descriptor for this PeerInfo
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "PeerInfo - " + PeerNetworkIdentifier + " ["+NumberOfConnectionInfos+"]";
        }

        [ProtoBeforeSerialization]
        private void BeforeSerialise()
        {
            _peerConnectionInfoSerialized = new List<byte[]>();

            foreach (ConnectionInfo info in PeerConnectionInfo)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    info.Serialize(ms);
                    ms.Flush();
                    _peerConnectionInfoSerialized.Add(ms.ToArray());
                }
            }
        }

        [ProtoAfterDeserialization]
        private void AfterSerialise()
        {
            PeerConnectionInfo = new List<ConnectionInfo>();

            foreach (byte[] info in _peerConnectionInfoSerialized)
            {
                using (MemoryStream ms = new MemoryStream(info))
                {
                    ConnectionInfo result;
                    ConnectionInfo.Deserialize(ms, out result);
                    PeerConnectionInfo.Add(result);
                }
            }
        }
    }

    /// <summary>
    /// Wrapper class which contains all of the information, for all peers, for a single distributed item.
    /// </summary>
    [ProtoContract]
    public class SwarmChunkAvailability
    {
        /// <summary>
        /// Our primary list of peerInfo which is keyed on networkIdentifier
        /// </summary>
        [ProtoMember(1)]
        Dictionary<string, PeerInfo> peerAvailabilityByNetworkIdentifierDict;

        /// <summary>
        /// An index for peers based on IPEndPoints. Key represents a conversion 
        /// from IPEndPoint.ToString() to network identifier
        /// </summary>
        [ProtoMember(2)]
        Dictionary<string, string> peerEndPointToNetworkIdentifier;

        /// <summary>
        /// Triggered when first peer is recorded as being alive
        /// </summary>
        ManualResetEvent alivePeersReceivedEvent = new ManualResetEvent(false);

        /// <summary>
        /// A thread sync root
        /// </summary>
        object peerLocker = new object();

        /// <summary>
        /// Blank constructor used for serialisation
        /// </summary>
        private SwarmChunkAvailability() { }

        /// <summary>
        /// Creates a new instance of SwarmChunkAvailability
        /// </summary>
        /// <param name="sourceConnectionInfoList">A list of sources. Possibly multiple peers each with multiple IPEndPoints.</param>
        /// <param name="totalNumChunks">The total number of chunks in the associated DFS item, used for initialising peer chunkflags</param>
        public SwarmChunkAvailability(List<ConnectionInfo> sourceConnectionInfoList, byte totalNumChunks)
        {
            //When initialising the chunk availability we add the starting source in the initialisation
            peerAvailabilityByNetworkIdentifierDict = new Dictionary<string, PeerInfo>();
            peerEndPointToNetworkIdentifier = new Dictionary<string, string>();

            foreach (ConnectionInfo info in sourceConnectionInfoList)
            {
                //A peer has a unique network identifier but many endpoints
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(info.NetworkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[info.NetworkIdentifier].AddPeerIPEndPoint(info.NetworkIdentifier, (IPEndPoint)info.LocalEndPoint);
                else
                    peerAvailabilityByNetworkIdentifierDict.Add(info.NetworkIdentifier, new PeerInfo(new List<ConnectionInfo>() { info }, new ChunkFlags(totalNumChunks), true));

                if (peerEndPointToNetworkIdentifier.ContainsKey(info.LocalEndPoint.ToString()))
                    throw new Exception("sourceConnectionInfoList contained a duplicate entry for the IPEndPoint " + info.LocalEndPoint.ToString());
                else
                    peerEndPointToNetworkIdentifier.Add(info.LocalEndPoint.ToString(), info.NetworkIdentifier);
            }

            if (DFS.loggingEnabled) DFS._DFSLogger.Debug("New swarmChunkAvailability created using " + sourceConnectionInfoList + " possible sources.");
        }

        /// <summary>
        /// Builds a dictionary of chunk availability throughout the current swarm for chunks we don't have locally. Keys are chunkIndex, peer network identifier, and peer total chunk count
        /// </summary>
        /// <param name="totalChunksInItem">The total number of chunks in this item</param>
        /// <param name="nonLocalPeerAvailability">A quick reference dictionary for matching ConnectionInfo with PeerInfo</param>
        /// <returns></returns>
        public Dictionary<byte, Dictionary<ConnectionInfo, PeerInfo>> CachedNonLocalChunkExistences(byte totalChunksInItem, out Dictionary<ConnectionInfo, PeerInfo> nonLocalPeerAvailability)
        {
            lock (peerLocker)
            {
                Dictionary<byte, Dictionary<ConnectionInfo, PeerInfo>> chunkExistence = new Dictionary<byte, Dictionary<ConnectionInfo, PeerInfo>>();
                nonLocalPeerAvailability = new Dictionary<ConnectionInfo, PeerInfo>();

                //We add an entry to the dictionary for every chunk we do not yet have
                for (byte i = 0; i < totalChunksInItem; i++)
                    if (!PeerHasChunk(NetworkComms.NetworkIdentifier, i))
                        chunkExistence.Add(i, new Dictionary<ConnectionInfo, PeerInfo>());

                //Now for each peer we know about we add them to the list if they have a chunk of interest
                foreach (var peer in peerAvailabilityByNetworkIdentifierDict)
                {
                    //This is the only place we clear a peers busy status
                    peer.Value.CheckAllIPEndPointBusyFlags(DFS.PeerBusyTimeoutMS);

                    //For this peer for every chunk we are looking for
                    for (byte i = 0; i < totalChunksInItem; i++)
                    {
                        //If we do not have the desired chunk but the current peer does
                        if (chunkExistence.ContainsKey(i) && peer.Value.PeerChunkFlags.FlagSet(i))
                        {
                            //Add a new entry for every connectionInfo available for this peer
                            List<ConnectionInfo> peerConnectionInfos = peer.Value.GetConnectionInfo();
                            foreach (ConnectionInfo info in peerConnectionInfos)
                            {
                                //We check every connectionInfo for allowed contact separately
                                if (PeerContactAllowed(peer.Key, (IPEndPoint)info.LocalEndPoint, peer.Value.SuperPeer))
                                {
                                    chunkExistence[i].Add(info, peer.Value);

                                    if (!nonLocalPeerAvailability.ContainsKey(info))
                                        nonLocalPeerAvailability.Add(info, peer.Value);
                                }
                            }
                        }
                    }
                }

                return chunkExistence;
            }
        }

        /// <summary>
        /// Set the provided peer IPEndPoint busy status to busy
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        public void SetIPEndPointBusy(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SetPeerIPEndPointBusyStatus(networkIdentifier, peerIPEndPoint, true);
            }
        }

        /// <summary>
        /// Get the provided peer IPEndPoint busy status
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public bool IPEndPointBusy(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[networkIdentifier].IsPeerIPEndPointBusy(networkIdentifier, peerIPEndPoint);
            }

            return false;
        }

        /// <summary>
        /// Set the provided peer IPEndPoint as offline
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        public void SetIPEndPointOffline(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SetPeerIPEndPointOnlineStatus(networkIdentifier, peerIPEndPoint, true);
            }
        }

        /// <summary>
        /// Get the provided peer IPEndPoint online status
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public bool IPEndPointOnline(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[networkIdentifier].IsPeerIPEndPointOnline(networkIdentifier, peerIPEndPoint);
            }

            return false;
        }

        /// <summary>
        /// Returns true if a peer with the provided networkIdentifier exists in this SwarmChunkAvailability
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <returns></returns>
        public bool PeerExistsInSwarm(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
                return peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier);
        }

        /// <summary>
        /// Returns true if a peer with the provided IPEndPoint exists in the swarm
        /// </summary>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public bool PeerExistsInSwarm(IPEndPoint peerIPEndPoint)
        {
            lock (peerLocker)
            {
                return peerEndPointToNetworkIdentifier.ContainsKey(peerIPEndPoint.ToString());
            }
        }

        /// <summary>
        /// Returns true if the specified peer has the specified chunkIndex.
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="chunkIndex">The desired chunkIndex</param>
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
        /// Returns true if a peer has a complete copy of the DFS item
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="totalNumChunks">The total number of chunks in this item</param>
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
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <returns></returns>
        public bool PeerIsSuperPeer(ShortGuid networkIdentifier)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return false;

                return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer;
            }
        }

        /// <summary>
        /// Returns the new timeout count value after incrementing the timeout count for the provided peer IPEndPoint.
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerIPEndPoint">The relevant IPEndPoint</param>
        /// <returns></returns>
        public int GetNewTimeoutCount(ShortGuid networkIdentifier, IPEndPoint peerIPEndPoint)
        {
            if (networkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return 0;

                return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].GetNewTimeoutCount(networkIdentifier, peerIPEndPoint);
            }
        }

        /// <summary>
        /// Deletes knowledge of a peer IPEndPoint from our local swarm chunk availability. 
        /// If peerEndPoint.Address is IPAddress.Any then the entire peer will be deleted. 
        /// </summary>
        /// <param name="networkIdentifier">The network identifier of the relevant peer</param>
        /// <param name="peerEndPoint">The relevant IPEndPoint</param>
        /// <param name="forceRemoveWholePeer">If true every IPEndPoint is removed for the provided network identifier</param>
        public void RemovePeerIPEndPointFromSwarm(ShortGuid networkIdentifier, IPEndPoint peerEndPoint, bool forceRemoveWholePeer = false)
        {
            try
            {
                if (networkIdentifier == null || networkIdentifier == ShortGuid.Empty) 
                    throw new Exception("networkIdentifier should not be empty.");

                if (peerEndPoint.Address == IPAddress.Any && !forceRemoveWholePeer)
                    throw new Exception("IPEndPoint may only reference IPAddress.Any if forceRemoveWholePeer is true.");

                lock (peerLocker)
                {
                    //If the have an entry for this peer in peerAvailabilityByNetworkIdentifierDict
                    //We only remove the peer if we have more than one and it is not a super peer
                    if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    {
                        //We can remove this peer if
                        //1. We have set force remove
                        //or
                        //2. We have more than at least 1 other peer AND if this is a super peer we need at least 1 other super peer in order to remove
                        if (forceRemoveWholePeer ||
                            (peerAvailabilityByNetworkIdentifierDict.Count > 1 &&
                                (!peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer ||
                                (from current in peerAvailabilityByNetworkIdentifierDict where current.Value.SuperPeer select current.Key).Count() > 1)))
                        {
                            //If we have set force remove for the whole peer
                            //or this is the last IPEndPoint for the peer
                            if (forceRemoveWholePeer || peerAvailabilityByNetworkIdentifierDict[networkIdentifier].NumberOfConnectionInfos == 1)
                            {
                                //We need to remove all traces of this peer
                                if (peerAvailabilityByNetworkIdentifierDict[networkIdentifier].NumberOfConnectionInfos == 1 &&
                                    !peerAvailabilityByNetworkIdentifierDict[networkIdentifier].GetConnectionInfo()[0].LocalEndPoint.Equals(peerEndPoint))
                                    throw new Exception("Possible corruption detected in SwarmChunkAvailability - 1 - " + peerAvailabilityByNetworkIdentifierDict[networkIdentifier].GetConnectionInfo()[0].LocalEndPoint + " - " + peerEndPoint);

                                if (peerEndPointToNetworkIdentifier.ContainsKey(peerEndPoint.ToString()) && peerEndPointToNetworkIdentifier[peerEndPoint.ToString()] != networkIdentifier)
                                    throw new Exception("Possible corruption detected in SwarmChunkAvailability - 2");

                                List<ConnectionInfo> peerConnectionInfos = peerAvailabilityByNetworkIdentifierDict[networkIdentifier].GetConnectionInfo();

                                foreach(ConnectionInfo connInfo in peerConnectionInfos)
                                    peerEndPointToNetworkIdentifier.Remove(connInfo.LocalEndPoint.ToString());

                                peerAvailabilityByNetworkIdentifierDict.Remove(networkIdentifier);

                                if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... removed entire peer from swarm - " + networkIdentifier + ".");
                            }
                            else
                            {
                                bool removeResult = peerAvailabilityByNetworkIdentifierDict[networkIdentifier].RemovePeerIPEndPoint(networkIdentifier, peerEndPoint);
                                peerEndPointToNetworkIdentifier.Remove(peerEndPoint.ToString());

                                if (removeResult)
                                {
                                    if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... removed peer IPEndPoint from swarm - " + networkIdentifier + " - " + peerEndPoint.ToString() + ".");
                                }
                                else
                                {
                                    if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... attempted to removed peer IPEndPoint from swarm but it didn't exist - " + networkIdentifier + " - " + peerEndPoint.ToString() + ".");
                                }
                            }
                        }
                        else
                        {
                            if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... remove failed as forceRemove= " + forceRemoveWholePeer + ", peerAvailabilityByNetworkIdentifierDict.Count=" + peerAvailabilityByNetworkIdentifierDict.Count + ", isSuperPeer=" + peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer + ", superPeerCount=" + (from current in peerAvailabilityByNetworkIdentifierDict where current.Value.SuperPeer select current.Key).Count());
                        }
                    }
                    else
                    {
                        if (DFS.loggingEnabled) DFS._DFSLogger.Trace(" ... peer did not exist in peerAvailabilityByNetworkIdentifierDict. Checking for old ipEndPoint references");

                        //Remove any accidental entries left in the endpoint dict
                        peerEndPointToNetworkIdentifier.Remove(peerEndPoint.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                LogTools.LogException(ex, "Error_RemovePeerFromSwarm");
            }
        }

        /// <summary>
        /// Adds or updates a peer to the local availability list. Useful for when a peer informs us of an updated availability.
        /// </summary>
        /// <param name="connectionInfo">The connectionInfo of the remote peer</param>
        /// <param name="latestChunkFlags">The new chunk flags</param>
        /// <param name="superPeer">True if this peer is a superPeer</param>
        /// <param name="setIPEndPointOnline">Set the relevant IPEndPoint online as a result of updating chunk flags</param>
        public void AddOrUpdateCachedPeerChunkFlags(ConnectionInfo connectionInfo, ChunkFlags latestChunkFlags, bool superPeer = false, bool setIPEndPointOnline = true)
        {
            if (connectionInfo.NetworkIdentifier == ShortGuid.Empty) throw new Exception("networkIdentifier should not be empty.");

            lock (peerLocker)
            {
                if (connectionInfo.ConnectionType != ConnectionType.TCP) throw new Exception("Only the TCP side of a DFS peer should be tracked.");

                //Extract the correct endpoint from the provided connectionInfo
                //If this is taken from a connection we are after the remoteEndPoint
                IPEndPoint endPointToUse = null;
                if (((IPEndPoint)connectionInfo.RemoteEndPoint).Address == IPAddress.Any ||
                    ((IPEndPoint)connectionInfo.RemoteEndPoint).Address == IPAddress.IPv6Any)
                    endPointToUse = (IPEndPoint)connectionInfo.LocalEndPoint;
                else
                    endPointToUse = (IPEndPoint)connectionInfo.RemoteEndPoint;

                string endPointToUseString = endPointToUse.ToString();
                //We can only add a peer if it is listening correctly
                if (endPointToUse.Port <= DFS.MaxTargetLocalPort && endPointToUse.Port >= DFS.MinTargetLocalPort)
                {
                    //Ensure the endpoint is correctly recorded
                    RemoveOldPeerAtEndPoint(connectionInfo.NetworkIdentifier, endPointToUse);

                    //If we have an existing record of this peer
                    if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(connectionInfo.NetworkIdentifier))
                    {
                        //If the existing peerInfo is not aware of this endPoint
                        if (!peerAvailabilityByNetworkIdentifierDict[connectionInfo.NetworkIdentifier].PeerContainsIPEndPoint(connectionInfo.NetworkIdentifier, endPointToUse))
                        {
                            //Add the information to the peerInfo and local index
                            peerAvailabilityByNetworkIdentifierDict[connectionInfo.NetworkIdentifier].AddPeerIPEndPoint(connectionInfo.NetworkIdentifier, endPointToUse);
                            peerEndPointToNetworkIdentifier[endPointToUseString] = connectionInfo.NetworkIdentifier;
                        }

                        //Finally update the chunk flags
                        peerAvailabilityByNetworkIdentifierDict[connectionInfo.NetworkIdentifier].PeerChunkFlags.UpdateFlags(latestChunkFlags);

                        if (DFS.loggingEnabled) DFS.Logger.Trace("Updated existing chunk flags for "+ connectionInfo);
                    }
                    else
                    {
                        //If we don't know anything about this peer we add it to our local swarm availability
                        //We used comms to get any existing connections to the peer
                        //We have to create new ConnectionInfo in the select as we need to correctly set the "LOCAL IPEndPoint" when passing to new PeerInfo()
                        List<ConnectionInfo> peerConnectionInfos = (from current in NetworkComms.GetExistingConnection(connectionInfo.NetworkIdentifier, ConnectionType.TCP) select new ConnectionInfo(ConnectionType.TCP, current.ConnectionInfo.NetworkIdentifier, current.ConnectionInfo.RemoteEndPoint, true)).ToList();
                        
                        //Don't forget to add the originating info if it was not pulled out from above
                        ConnectionInfo originatingConnectionInfo = new ConnectionInfo(ConnectionType.TCP, connectionInfo.NetworkIdentifier, endPointToUse, true);
                        if (!peerConnectionInfos.Contains(originatingConnectionInfo)) peerConnectionInfos.Add(originatingConnectionInfo);
                        
                        peerAvailabilityByNetworkIdentifierDict.Add(connectionInfo.NetworkIdentifier, new PeerInfo(peerConnectionInfos, latestChunkFlags, superPeer));

                        //We finish by adding the endPoint references
                        foreach (ConnectionInfo connInfo in peerConnectionInfos)
                            peerEndPointToNetworkIdentifier[connInfo.LocalEndPoint.ToString()] = connectionInfo.NetworkIdentifier;

                        if (DFS.loggingEnabled) DFS.Logger.Trace("Added new chunk flags for " + connectionInfo);
                    }

                    if (setIPEndPointOnline)
                        //By updating cached peer chunk flags we set the peer as being online
                        peerAvailabilityByNetworkIdentifierDict[connectionInfo.NetworkIdentifier].SetPeerIPEndPointOnlineStatus(connectionInfo.NetworkIdentifier, endPointToUse, true);

                    //We will trigger the alive peers event when we have at least a third of the existing peers
                    if (!alivePeersReceivedEvent.WaitOne(0))
                    {
                        int numOnlinePeers = (from current in peerAvailabilityByNetworkIdentifierDict.Values where current.HasAtleastOneOnlineIPEndPoint() select current).Count();
                        if (numOnlinePeers >= DFS.MaxTotalItemRequests || numOnlinePeers > peerAvailabilityByNetworkIdentifierDict.Count / 3.0)
                            alivePeersReceivedEvent.Set();
                    }
                }
                else
                    LogTools.LogException(new Exception("Attempted to AddOrUpdateCachedPeerChunkFlags for client which was not listening or was using port outside the valid DFS range."), "PeerChunkFlagsUpdateError", "IP:" + endPointToUse.Address.ToString() + ", Port:" + endPointToUse.Port);
            }
        }

        /// <summary>
        /// Removes any peers which have the same endPoint as the provided currentActivePeerEndPoint except one with matching currentActivePeerIdentifier
        /// </summary>
        /// <param name="currentActivePeerIdentifier">The NetworkIdenfier of the known active peer</param>
        /// <param name="currentActivePeerEndPoint">The endPoint of the known active peer</param>
        public void RemoveOldPeerAtEndPoint(ShortGuid currentActivePeerIdentifier, IPEndPoint currentActivePeerEndPoint)
        {
            lock (peerLocker)
            {
                //If we already have an entry for the provided endPoint but it does not match the provided currentActivePeerIdentifier
                //We need to remove the provided endPoint from the old peer
                string ipEndPointString = currentActivePeerEndPoint.ToString();
                if (peerEndPointToNetworkIdentifier.ContainsKey(ipEndPointString) && peerEndPointToNetworkIdentifier[ipEndPointString] != currentActivePeerIdentifier)
                {
                    //Remove the provided currentActivePeerEndPoint from the old peer
                    RemovePeerIPEndPointFromSwarm(peerEndPointToNetworkIdentifier[ipEndPointString], currentActivePeerEndPoint);
                }
            }
        }

        /// <summary>
        /// Sets our local availability
        /// </summary>
        /// <param name="chunkIndex">The chunk index flag to update</param>
        /// <param name="setAvailable">The availability of the provided chunk</param>
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
        /// Update the chunk availability by contacting all existing peers. If a cascade depth greater than 1 is 
        /// provided will also contact each peers peers.
        /// </summary>
        /// <param name="itemCheckSum">The checksum associated with this item. This will be used when contacting other peers
        /// for an update.</param>
        /// <param name="cascadeDepth">The depth of the update cascade. 0 - Contact only known peers for an update. 1 - Contact
        /// known peers and retrieve their known peers as well. >1 - Not implemented.</param>
        /// <param name="responseWaitMS">The maximum time to wait for the first update reply to be received before continuing.</param>
        /// <param name="buildLog">An optional build log that can be updated with the progress of this method.</param>
        public void UpdatePeerAvailability(string itemCheckSum, int cascadeDepth, int responseWaitMS = 5000, Action<string> buildLog = null)
        {
            if (buildLog != null) buildLog("Starting UpdatePeerAvailability update.");

            Dictionary<string, List<ConnectionInfo>> peerConnInfoDict = new Dictionary<string, List<ConnectionInfo>>();
            lock (peerLocker) peerConnInfoDict = (from current in peerAvailabilityByNetworkIdentifierDict.Keys
                                                  select new KeyValuePair<string, List<ConnectionInfo>>(current, 
                                                      peerAvailabilityByNetworkIdentifierDict[current].GetConnectionInfo())
                                                  ).ToDictionary(entry => entry.Key, entry => entry.Value);

            if (cascadeDepth > 0)
            {
                if (cascadeDepth > 1) throw new NotImplementedException("A cascading update greater than 1 is not yet supported.");

                //If we are going to cascade peers we need to contact all our known peers and get them to send us their known peers
                #region GetAllUnknownPeers
                //Contact all known endPoints and request an availability update
                foreach (var peer in peerConnInfoDict)
                {
                    PeerInfo peerInfo = null;
                    try
                    {
                        peerInfo = peerAvailabilityByNetworkIdentifierDict[peer.Key]; 
                    }
                    catch (KeyNotFoundException)
                    {
                        //This exception will get thrown if we try to access a peers connecitonInfo from peerNetworkIdentifierToConnectionInfo 
                        //but it has been removed since we accessed the peerKeys at the start of this method
                        //We could probably be a bit more careful with how we maintain these references but we either catch the
                        //exception here or NetworkComms.Net will throw one when we try to connect to an old peer.
                        RemovePeerIPEndPointFromSwarm(peer.Key, new IPEndPoint(IPAddress.Any, 0), true);
                        if (buildLog != null) buildLog("Removing " + peer.Key + " from item swarm due to KeyNotFoundException.");
                    }

                    if (peerInfo != null)
                    {
                        foreach (ConnectionInfo connInfo in peer.Value)
                        {
                            try
                            {
                                //We don't want to contact ourselves plus we check the possible exception lists
                                if (PeerContactAllowed(peer.Key, (IPEndPoint)connInfo.LocalEndPoint, peerInfo.SuperPeer))
                                {
                                    if (buildLog != null) buildLog("Contacting " + peer.Key + " - " + connInfo.LocalEndPoint.ToString() + " for a DFS_KnownPeersRequest.");

                                    UDPConnection.SendObject("DFS_KnownPeersRequest", itemCheckSum, (IPEndPoint)connInfo.LocalEndPoint, DFS.nullCompressionSRO);
                                }
                            }
                            catch (CommsException)
                            {
                                //If a peer has disconnected or fails to respond we just remove them from the list
                                RemovePeerIPEndPointFromSwarm(peer.Key, (IPEndPoint)connInfo.LocalEndPoint);
                                if (buildLog != null) buildLog("Removing " + peer.Key + " - " + connInfo.LocalEndPoint.ToString() + " from item swarm due to CommsException.");
                            }
                            catch (Exception ex)
                            {
                                LogTools.LogException(ex, "UpdatePeerChunkAvailabilityError_1");
                            }
                        }
                    }
                }
                #endregion
            }

            //Contact all our original peers and request a chunk availability update
            #region GetAllOriginalPeerAvailabilityFlags
            //Our own current availability
            foreach (var peer in peerConnInfoDict)
            {
                PeerInfo peerInfo = null;
                try
                {
                    peerInfo = peerAvailabilityByNetworkIdentifierDict[peer.Key];
                }
                catch (KeyNotFoundException)
                {
                    //This exception will get thrown if we try to access a peers connecitonInfo from peerNetworkIdentifierToConnectionInfo 
                    //but it has been removed since we accessed the peerKeys at the start of this method
                    //We could probably be a bit more careful with how we maintain these references but we either catch the
                    //exception here or NetworkComms.Net will throw one when we try to connect to an old peer.
                    RemovePeerIPEndPointFromSwarm(peer.Key, new IPEndPoint(IPAddress.Any, 0), true);
                    if (buildLog != null) buildLog("Removing " + peer.Key + " from item swarm due to KeyNotFoundException.");
                }

                if (peerInfo != null)
                {
                    foreach (ConnectionInfo connInfo in peer.Value)
                    {
                        try
                        {
                            //We don't want to contact ourselves and for now that includes anything having the same IP as us
                            if (PeerContactAllowed(peer.Key, (IPEndPoint)connInfo.LocalEndPoint, peerInfo.SuperPeer))
                            {
                                if (buildLog != null) buildLog("Contacting " + peer.Key + " - " + connInfo.LocalEndPoint.ToString() + " for a DFS_ChunkAvailabilityRequest from within UpdatePeerAvailability.");

                                //Request a chunk update
                                UDPConnection.SendObject("DFS_ChunkAvailabilityRequest", itemCheckSum, (IPEndPoint)connInfo.LocalEndPoint, DFS.nullCompressionSRO);
                            }
                        }
                        catch (CommsException)
                        {
                            //If a peer has disconnected or fails to respond we just remove them from the list
                            RemovePeerIPEndPointFromSwarm(peer.Key, (IPEndPoint)connInfo.LocalEndPoint);
                            if (buildLog != null) buildLog("Removing " + peer.Key + " - " + connInfo.LocalEndPoint.ToString() + " from item swarm due to CommsException.");
                        }
                        catch (Exception ex)
                        {
                            LogTools.LogException(ex, "UpdatePeerChunkAvailabilityError_2");
                        }
                    }
                }
            }
            #endregion

            if (alivePeersReceivedEvent.WaitOne(responseWaitMS))
            {
                if (buildLog != null)
                    buildLog("Completed SwarmChunkAvailability update successfully.");

                //If the event was successfully triggered we wait an additional 250ms to give other responses a chance to be handled
                Thread.Sleep(250);
            }
            else
            {
                if (buildLog != null)
                    buildLog("Completed SwarmChunkAvailability update by timeout.");
            }
        }

        /// <summary>
        /// Metric used to determine the health of a chunk and whether swarm will benefit from a broadcasted update. A value 
        /// greater than 1 signifies a healthy chunk availability.
        /// </summary>
        /// <param name="chunkIndex">The relevant chunk index</param>
        /// <param name="totalNumChunks">The total number of chunks in this item</param>
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
        /// <param name="chunkIndex">The relevant chunkIndex</param>
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
        /// <param name="itemCheckSum">The checksum associated with this item</param>
        public void BroadcastLocalAvailability(string itemCheckSum)
        {
            Dictionary<string, List<ConnectionInfo>> peerConnInfoDict = new Dictionary<string, List<ConnectionInfo>>();
            ChunkFlags localChunkFlags;

            lock (peerLocker)
            {
                localChunkFlags = peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].PeerChunkFlags;

                peerConnInfoDict = (from current in peerAvailabilityByNetworkIdentifierDict.Keys
                                                  select new KeyValuePair<string, List<ConnectionInfo>>(current,
                                                      peerAvailabilityByNetworkIdentifierDict[current].GetConnectionInfo())
                                                  ).ToDictionary(entry => entry.Key, entry => entry.Value);
            }

            foreach (var peer in peerConnInfoDict)
            {
                PeerInfo peerInfo = null;
                try
                {
                    peerInfo = peerAvailabilityByNetworkIdentifierDict[peer.Key];
                }
                catch (KeyNotFoundException)
                {
                    //This exception will get thrown if we try to access a peers connecitonInfo from peerNetworkIdentifierToConnectionInfo 
                    //but it has been removed since we accessed the peerKeys at the start of this method
                    //We could probably be a bit more careful with how we maintain these references but we either catch the
                    //exception here or NetworkComms.Net will throw one when we try to connect to an old peer.
                    RemovePeerIPEndPointFromSwarm(peer.Key, new IPEndPoint(IPAddress.Any, 0), true);
                }

                if (peerInfo != null)
                {
                    foreach (ConnectionInfo connInfo in peer.Value)
                    {
                        try
                        {
                            //We don't want to contact ourselves and for now that includes anything having the same IP as us
                            if (PeerContactAllowed(peer.Key, (IPEndPoint)connInfo.LocalEndPoint, peerInfo.SuperPeer))
                                UDPConnection.SendObject("DFS_PeerChunkAvailabilityUpdate", new PeerChunkAvailabilityUpdate(NetworkComms.NetworkIdentifier, itemCheckSum, localChunkFlags), (IPEndPoint)connInfo.LocalEndPoint, DFS.nullCompressionSRO);
                        }
                        catch (Exception)
                        {
                            //If a peer has disconnected or fails to respond we just remove them from the list
                            RemovePeerIPEndPointFromSwarm(peer.Key, (IPEndPoint)connInfo.LocalEndPoint);

                            if (DFS.loggingEnabled)
                                DFS.Logger.Trace("Removed peer from swarm during BroadcastLocalAvailability due to exception - " + peer.Key + " - " + connInfo.LocalEndPoint.ToString());
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Closes established connections with completed peers as they are now redundant.
        /// </summary>
        /// <param name="totalNumChunks">The total number of chunks in this item</param>
        public void CloseConnectionsToCompletedPeers(byte totalNumChunks)
        {
            Dictionary<string, PeerInfo> dictCopy;
            lock (peerLocker)
                dictCopy = new Dictionary<string, PeerInfo>(peerAvailabilityByNetworkIdentifierDict);

            Parallel.ForEach(dictCopy, peer =>
            {
                try
                {
                    if (!peer.Value.SuperPeer && peer.Key != NetworkComms.NetworkIdentifier)
                    {
                        if (peer.Value.PeerChunkFlags.AllFlagsSet(totalNumChunks))
                        {
                            var connections = NetworkComms.GetExistingConnection(peer.Key, ConnectionType.TCP);
                            if (connections != null) 
                                foreach (var connection in connections) 
                                    connection.CloseConnection(false);
                        }
                    }
                }
                catch (Exception) { }
            });
        }

        /// <summary>
        /// Single method for determining if contact can be made with the request peer.
        /// Prevents loop back contact via matching identifier and currentLocalListenEndPoints.
        /// Finally uses the DFS.AllowedPeerIPS and DFS.DisallowedPeerIPS if set.
        /// </summary>
        /// <param name="peerIdentifier">The relevant network identifier</param>
        /// <param name="peerEndPoint">The relevant IPEndPoint</param>
        /// <param name="superPeer">True if this peer is a super peer</param>
        /// <returns></returns>
        public bool PeerContactAllowed(ShortGuid peerIdentifier, IPEndPoint peerEndPoint, bool superPeer)
        {
            if (peerIdentifier == NetworkComms.NetworkIdentifier)
                return false;

            List<EndPoint> currentLocalListenEndPoints = Connection.ExistingLocalListenEndPoints(ConnectionType.TCP);
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

        /// <summary>
        /// Returns the chunk flag availability of the requested peer.
        /// </summary>
        /// <param name="peerIdentifier">The relevant network identifier</param>
        /// <returns></returns>
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
        /// Broadcast to all known peers that the local DFS is removing the specified item.
        /// </summary>
        /// <param name="itemCheckSum">The checksum associated with this item</param>
        /// <param name="removeSwarmWide">True if the item should be removed by all peers, swarm wide</param>
        public void BroadcastItemRemoval(string itemCheckSum, bool removeSwarmWide)
        {
            Dictionary<string, List<ConnectionInfo>> peerConnInfoDict = new Dictionary<string, List<ConnectionInfo>>();
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(NetworkComms.NetworkIdentifier))
                    throw new Exception("Local peer not located in peerChunkAvailabity for this item.");

                if (removeSwarmWide && !peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].SuperPeer)
                    throw new Exception("Attempted to trigger a swarm wide item removal but local is not a SuperPeer.");

                peerConnInfoDict = (from current in peerAvailabilityByNetworkIdentifierDict.Keys
                                    select new KeyValuePair<string, List<ConnectionInfo>>(current,
                                        peerAvailabilityByNetworkIdentifierDict[current].GetConnectionInfo())
                                                  ).ToDictionary(entry => entry.Key, entry => entry.Value);
            }

            foreach (var peer in peerConnInfoDict)
            {
                PeerInfo peerInfo = null;
                try
                {
                    peerInfo = peerAvailabilityByNetworkIdentifierDict[peer.Key];
                }
                catch (KeyNotFoundException)
                {
                    //This exception will get thrown if we try to access a peers connecitonInfo from peerNetworkIdentifierToConnectionInfo 
                    //but it has been removed since we accessed the peerKeys at the start of this method
                    //We could probably be a bit more careful with how we maintain these references but we either catch the
                    //exception here or NetworkComms.Net will throw one when we try to connect to an old peer.
                    RemovePeerIPEndPointFromSwarm(peer.Key, new IPEndPoint(IPAddress.Any, 0), true);
                }

                if (peerInfo != null)
                {
                    foreach (ConnectionInfo connInfo in peer.Value)
                    {
                        try
                        {
                            //We don't want to contact ourselves and for now that includes anything having the same ip as us
                            if (PeerContactAllowed(peer.Key, (IPEndPoint)connInfo.LocalEndPoint, peerInfo.SuperPeer))
                                UDPConnection.SendObject("DFS_ItemRemovalUpdate", new ItemRemovalUpdate(NetworkComms.NetworkIdentifier, itemCheckSum, removeSwarmWide), (IPEndPoint)connInfo.LocalEndPoint, DFS.nullCompressionSRO);
                        }
                        catch(CommsException)
                        {
                            RemovePeerIPEndPointFromSwarm(peer.Key, (IPEndPoint)connInfo.LocalEndPoint);
                        }
                        catch (Exception ex)
                        {
                            //If a peer has disconnected or fails to respond we just remove them from the list
                            RemovePeerIPEndPointFromSwarm(peer.Key, (IPEndPoint)connInfo.LocalEndPoint);

                            LogTools.LogException(ex, "BroadcastItemRemovalError");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The number of peers in this swarm
        /// </summary>
        /// <param name="excludeSuperPeers">True if super peers should be excluded from the count.</param>
        /// <returns></returns>
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

        /// <summary>
        /// The number of complete peers in this swarm.
        /// </summary>
        /// <param name="totalItemChunks">The total number of chunks in this item</param>
        /// <param name="excludeSuperPeers">True if super peers should be excluded from the count.</param>
        /// <returns></returns>
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
        /// Returns an array containing all known peer endpoints in the format locaIP:port
        /// </summary>
        /// <returns></returns>
        public string[] AllPeerEndPoints()
        {
            lock (peerLocker)
                return (from current in peerAvailabilityByNetworkIdentifierDict 
                    select current.Value.GetConnectionInfo().Select(info => 
                    {
                        return ((IPEndPoint)info.LocalEndPoint).Address.ToString() + ":" + ((IPEndPoint)info.LocalEndPoint).Port.ToString();
                    })).Aggregate(new List<string>(), (left, right) => { return left.Union(right).ToList(); }).ToArray();
        }

        /// <summary>
        /// Clear all chunk availability flags for the local peer
        /// </summary>
        public void ClearAllLocalAvailabilityFlags()
        {
            lock (peerLocker)
                peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkIdentifier].PeerChunkFlags.ClearAllFlags();
        }

        #region IThreadSafeSerialise Members
        /// <summary>
        /// Serialise this swarm chunk availability in a thread safe manner
        /// </summary>
        /// <returns></returns>
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

    /// <summary>
    /// A wrapper classed used to inform remote peers of our known peers
    /// </summary>
    [ProtoContract]
    public class KnownPeerEndPoints
    {
        /// <summary>
        /// The checksum identifier for the included peer end points.
        /// </summary>
        [ProtoMember(1)]
        public string ItemChecksm { get; private set; }

        /// <summary>
        /// All known IPEndPoints for this item
        /// </summary>
        [ProtoMember(2)]
        public string[] PeerEndPoints {get; private set;}

        private KnownPeerEndPoints() { }

        /// <summary>
        /// Initialise a new KnownPeerEndPoints
        /// </summary>
        /// <param name="itemCheckSum">The checksum identifier for the included peer end points.</param>
        /// <param name="knownPeerEndPoints">All known IPEndPoints for this item</param>
        public KnownPeerEndPoints(string itemCheckSum, string[] knownPeerEndPoints)
        {
            this.ItemChecksm = itemCheckSum;
            this.PeerEndPoints = knownPeerEndPoints;
        }
    }
}
