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
using ProtoBuf;
using NetworkCommsDotNet;

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
