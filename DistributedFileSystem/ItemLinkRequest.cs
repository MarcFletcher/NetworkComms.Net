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
using ProtoBuf;

namespace DistributedFileSystem
{
    /// <summary>
    /// The link mode to use
    /// </summary>
    public enum DFSLinkMode
    {
        /// <summary>
        /// Only items existing at both ends are linked
        /// </summary>
        LinkOnly,

        /// <summary>
        /// All items existing on the target peer are retrieved and held locally. Any items already on local will be linked.
        /// </summary>
        LinkAndRepeat,
    }

    /// <summary>
    /// A wrapper used when requesting link items
    /// </summary>
    [ProtoContract]
    public class DFSLinkRequest
    {
        /// <summary>
        /// If this linkRequest object has been sent in reply to a linkRequest this boolean is true
        /// </summary>
        [ProtoMember(1)]
        public bool LinkRequestReply { get; private set; }

        /// <summary>
        /// The DFS items which can possibly be linked
        /// </summary>
        [ProtoMember(2)]
        public Dictionary<string, DateTime> AvailableItems { get; private set; }

        private DFSLinkRequest() 
        {
            if (AvailableItems == null) AvailableItems = new Dictionary<string, DateTime>();
        }

        /// <summary>
        /// Create an item link request
        /// </summary>
        /// <param name="availableItems">The available DFS items. Key is itemCheckSum. Value is Item.ItemBuildCompleted</param>
        /// <param name="linkRequestReply">True if this DFSLinkRequest is the originating or reply linkRequest</param>
        public DFSLinkRequest(Dictionary<string, DateTime> availableItems, bool linkRequestReply = false)
        {
            this.LinkRequestReply = linkRequestReply;
            this.AvailableItems = availableItems;
        }
    }
}
