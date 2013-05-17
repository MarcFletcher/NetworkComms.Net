//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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

namespace DistributedFileSystem
{
    /// <summary>
    /// Provides all the information a new peer requires in order to construct the distributed file locally
    /// </summary>
    [ProtoContract]
    public class ItemAssemblyConfig
    {
        /// <summary>
        /// Starting Peers
        /// </summary>
        [ProtoMember(1)]
        public byte[] SwarmChunkAvailabilityBytes { get; private set; }

        //total num chunks
        //chunk size
        [ProtoMember(2)]
        public byte TotalNumChunks { get; private set; }
        [ProtoMember(3)]
        public int ChunkSizeInBytes { get; private set; }

        //total final size in bytes
        [ProtoMember(4)]
        public int TotalItemSizeInBytes { get; private set; }

        /// <summary>
        /// MD5 of final assembled item
        /// </summary>
        [ProtoMember(5)]
        public string ItemCheckSum { get; private set; }

        /// <summary>
        /// Optional checksums for individual chunks.
        /// </summary>
        [ProtoMember(11)]
        public string[] ChunkCheckSums { get; private set; }

        /// <summary>
        /// The packetType to use when the item has been fully assembled
        /// </summary>
        [ProtoMember(6)]
        public string CompletedPacketType { get; private set; }

        /// <summary>
        /// The cascade depth to use when building this item. Default is normally 0
        /// </summary>
        [ProtoMember(7)]
        public int ItemBuildCascadeDepth { get; private set; }

        /// <summary>
        /// A string which can be used to distinguish this distributed item from others
        /// </summary>
        [ProtoMember(8)]
        public string ItemTypeStr { get; private set; }

        /// <summary>
        /// The target to where the item should be built, i.e. memory or disk
        /// </summary>
        [ProtoMember(9)]
        public ItemBuildTarget ItemBuildTarget {get; private set;}

        /// <summary>
        /// A unique identifier for this item, usually a filename
        /// </summary>
        [ProtoMember(10)]
        public string ItemIdentifier {get; private set;}

        /// <summary>
        /// Private constructor for serialisation.
        /// </summary>
        private ItemAssemblyConfig() { }

        public ItemAssemblyConfig(DistributedItem itemToDistribute, string completedPacketType)
        {
            this.ItemCheckSum = itemToDistribute.ItemCheckSum;
            this.ChunkCheckSums = itemToDistribute.ChunkCheckSums;
            this.TotalNumChunks = itemToDistribute.TotalNumChunks;
            this.ChunkSizeInBytes = itemToDistribute.ChunkSizeInBytes;
            this.TotalItemSizeInBytes = itemToDistribute.ItemBytesLength;
            this.SwarmChunkAvailabilityBytes = itemToDistribute.SwarmChunkAvailability.ThreadSafeSerialise();
            this.CompletedPacketType = completedPacketType;
            this.ItemBuildCascadeDepth = itemToDistribute.ItemBuildCascadeDepth;
            this.ItemTypeStr = itemToDistribute.ItemTypeStr;
            this.ItemIdentifier = itemToDistribute.ItemIdentifier;
            this.ItemBuildTarget = itemToDistribute.ItemBuildTarget;
        }
    }
}
