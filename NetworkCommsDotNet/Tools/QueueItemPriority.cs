//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet.Tools
{
//#if WINDOWS_PHONE || NETFX_CORE
//    /// <summary>
//    /// A list of priorities used to handle incoming packets
//    /// </summary>
//    public enum QueueItemPriority
//    {
//        // Summary:
//        //     The work item should run at low priority.
//        Low = -1,
//        //
//        // Summary:
//        //     The work item should run at normal priority. This is the default value.
//        Normal = 0,
//        //
//        // Summary:
//        //     The work item should run at high priority.
//        High = 1,
//    }
//#else
    /// <summary>
    /// A list of priorities used to handle incoming packets
    /// </summary>
    public enum QueueItemPriority
    {
        /// <summary>
        /// The System.Threading.Thread can be scheduled after threads with any other priority.
        /// </summary>
        Lowest = 0,
        
        /// <summary>
        ///  The System.Threading.Thread can be scheduled after threads with Normal priority and before those with Lowest priority.
        /// </summary>
        BelowNormal = 1,
        
        /// <summary>
        /// The System.Threading.Thread can be scheduled after threads with AboveNormal priority and before those with BelowNormal priority. Threads have Normal priority by default.
        /// </summary>
        Normal = 2,
        
        /// <summary>
        /// The System.Threading.Thread can be scheduled after threads with Highest priority and before those with Normal priority.
        /// </summary>
        AboveNormal = 3,
        
        /// <summary>
        /// The System.Threading.Thread can be scheduled before threads with any other priority.
        /// </summary>
        Highest = 4,
    }
//#endif
}
