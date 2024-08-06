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

using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// IP Connection base class for NetworkComms.Net. This contains the functionality and tools shared by any connections
    /// that use IP related endPoints such as <see cref="NetworkCommsDotNet.Connections.TCP.TCPConnection"/> and <see cref="NetworkCommsDotNet.Connections.UDP.UDPConnection"/>.
    /// </summary>
    public abstract class IPConnection : Connection
    {
        static IPConnection()
        {
            DOSProtection = new DOSProtection();
        }

        /// <summary>
        /// Create a new IP connection object
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo corresponding to the new connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which should be used as connection defaults</param>
        protected IPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions)
            : base(connectionInfo, defaultSendReceiveOptions)
        {

        }

        #region IP Security
        /// <summary>
        /// The NetworkComms.Net DOS protection class. By default DOSProtection is disabled.
        /// </summary>
        public static DOSProtection DOSProtection { get; private set; }

        /// <summary>
        /// If set NetworkComms.Net will only accept incoming connections from the provided IP ranges. 
        /// </summary>
        public static IPRange[] AllowedIncomingIPRanges { get; set; }
        #endregion
    }
}
