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
using System.Text;

namespace NetworkCommsDotNet
{
#if WINDOWS_PHONE
    /// <summary>
    /// A list of priorities used to handle incoming packets
    /// </summary>
    public enum QueueItemPriority
    {
        // Summary:
        //     The work item should run at low priority.
        Low = -1,
        //
        // Summary:
        //     The work item should run at normal priority. This is the default value.
        Normal = 0,
        //
        // Summary:
        //     The work item should run at high priority.
        High = 1,
    }
#else
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
#endif

}
