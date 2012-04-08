using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;

namespace DistributedFileSystem
{
    [ProtoContract]
    class ItemRemovalUpdate
    {
        [ProtoMember(1)]
        public long ItemCheckSum { get; private set; }

        [ProtoMember(2)]
        public bool RemoveSwarmWide { get; private set; }

        private ItemRemovalUpdate() { }

        public ItemRemovalUpdate(long itemCheckSum, bool removeSwarmWide)
        {
            this.ItemCheckSum = itemCheckSum;
            this.RemoveSwarmWide = removeSwarmWide;
        }
    }
}
