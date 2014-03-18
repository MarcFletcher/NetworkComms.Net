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
