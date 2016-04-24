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
using NetworkCommsDotNet;
using NetworkCommsDotNet.Tools;

namespace DistributedFileSystem
{
    [ProtoContract]
    class ItemRemovalUpdate
    {
        [ProtoMember(1)]
        public string ItemCheckSum { get; private set; }
        [ProtoMember(2)]
        public bool RemoveSwarmWide { get; private set; }
        [ProtoMember(3)]
        public string SourceNetworkIdentifier { get; private set; }

        private ItemRemovalUpdate() { }

        public ItemRemovalUpdate(ShortGuid sourceNetworkIdentifier, string itemCheckSum, bool removeSwarmWide)
        {
            if (sourceNetworkIdentifier == null)
                throw new NullReferenceException("Unable to create ItemRemovalUpdate unless a valid sourceNetworkIdentifier is provided.");

            this.SourceNetworkIdentifier = sourceNetworkIdentifier;
            this.ItemCheckSum = itemCheckSum;
            this.RemoveSwarmWide = removeSwarmWide;
        }
    }
}
