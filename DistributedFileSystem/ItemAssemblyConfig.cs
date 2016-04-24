// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

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
        public long TotalItemSizeInBytes { get; private set; }

        /// <summary>
        /// MD5 checksum of assembled item. Used for validating a completed build
        /// </summary>
        [ProtoMember(5)]
        public string CompleteDataCheckSum { get; private set; }

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
        /// Build mode determines how the item should be built, i.e. memory or disk as a single stream or multiple blocks
        /// </summary>
        [ProtoMember(9)]
        public DataBuildMode ItemBuildMode {get; private set;}

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
            this.CompleteDataCheckSum = itemToDistribute.Data.CompleteDataCheckSum;
            this.ChunkCheckSums = itemToDistribute.Data.ChunkCheckSums;
            this.TotalNumChunks = itemToDistribute.Data.TotalNumChunks;
            this.ChunkSizeInBytes = itemToDistribute.Data.ChunkSizeInBytes;
            this.TotalItemSizeInBytes = itemToDistribute.Data.ItemBytesLength;
            this.SwarmChunkAvailabilityBytes = itemToDistribute.SwarmChunkAvailability.ThreadSafeSerialise();
            this.CompletedPacketType = completedPacketType;
            this.ItemBuildCascadeDepth = itemToDistribute.ItemBuildCascadeDepth;
            this.ItemTypeStr = itemToDistribute.ItemTypeStr;
            this.ItemIdentifier = itemToDistribute.ItemIdentifier;
            this.ItemBuildMode = itemToDistribute.Data.DataBuildMode;
        }
    }
}
