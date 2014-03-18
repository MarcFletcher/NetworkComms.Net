//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
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
