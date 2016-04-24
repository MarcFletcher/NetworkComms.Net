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
