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
