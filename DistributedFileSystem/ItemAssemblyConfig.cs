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
using NetworkCommsDotNet;
using ProtoBuf;

namespace DistributedFileSystem
{
    /// <summary>
    /// Provides all the information a new peer requires in order to build the DFS item
    /// </summary>
    [ProtoContract]
    public class ItemAssemblyConfig
    {
        /// <summary>
        /// The serialised SwarmChunkAvailability 
        /// </summary>
        [ProtoMember(1)]
        public byte[] SwarmChunkAvailabilityBytes { get; private set; }

        /// <summary>
        /// Total number of chunks in this DFS item
        /// </summary>
        [ProtoMember(2)]
        public byte TotalNumChunks { get; private set; }

        /// <summary>
        /// Maximum size of each chunk in bytes. The final chunk may be less than this value.
        /// </summary>
        [ProtoMember(3)]
        public int ChunkSizeInBytes { get; private set; }

        /// <summary>
        /// Total item size in bytes
        /// </summary>
        [ProtoMember(4)]
        public int TotalItemSizeInBytes { get; private set; }

        /// <summary>
        /// MD5 checksum of assembled item. Used for validating a completed build
        /// </summary>
        [ProtoMember(5)]
        public string ItemCheckSum { get; private set; }

        /// <summary>
        /// Optional MD5 checksums for individual chunks.
        /// </summary>
        [ProtoMember(11)]
        public string[] ChunkCheckSums { get; private set; }

        /// <summary>
        /// The packet type to use once the item has been fully assembled
        /// </summary>
        [ProtoMember(6)]
        public string CompletedPacketType { get; private set; }

        /// <summary>
        /// The cascade depth to use when building this item. Default is 1
        /// </summary>
        [ProtoMember(7)]
        public int ItemBuildCascadeDepth { get; private set; }

        /// <summary>
        /// A category string which can be used to group distributed items together
        /// </summary>
        [ProtoMember(8)]
        public string ItemTypeStr { get; private set; }

        /// <summary>
        /// The target to where the item should be built, i.e. memory or disk
        /// </summary>
        [ProtoMember(9)]
        public ItemBuildTarget ItemBuildTarget {get; private set;}

        /// <summary>
        /// A unique identifier for this item, usually a file name
        /// </summary>
        [ProtoMember(10)]
        public string ItemIdentifier {get; private set;}

        /// <summary>
        /// Private constructor for serialisation.
        /// </summary>
        private ItemAssemblyConfig() { }

        /// <summary>
        /// Instantiate a new ItemAssemblyConfig
        /// </summary>
        /// <param name="itemToDistribute">The DFS item for which this ItemAssemblyConfig should be created.</param>
        /// <param name="completedPacketType">The packet type to use once the item has been fully assembled</param>
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
