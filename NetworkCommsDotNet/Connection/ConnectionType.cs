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

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// The type of <see cref="Connection"/>.
    /// </summary>
    public enum ConnectionType
    {
        /// <summary>
        /// An undefined connection type. This is used as the default value.
        /// </summary>
        Undefined,

        /// <summary>
        /// A TCP connection type. Used by <see cref="NetworkCommsDotNet.Connections.TCP.TCPConnection"/>.
        /// </summary>
        TCP,

        /// <summary>
        /// A UDP connection type. Used by <see cref="NetworkCommsDotNet.Connections.UDP.UDPConnection"/>.
        /// </summary>
        UDP,

#if !NET2 && !WINDOWS_PHONE
        /// <summary>
        /// A Bluetooth RFCOMM connection. Used by <see cref="NetworkCommsDotNet.Connections.Bluetooth.BluetoothConnection"/> 
        /// </summary>
        Bluetooth,
#endif

        //We may support others in future such as SSH, FTP, SCP etc.
    }

    /// <summary>
    /// The connections application layer protocol status.
    /// </summary>
    public enum ApplicationLayerProtocolStatus
    {
        /// <summary>
        /// Useful for selecting or searching connections when the ApplicationLayerProtocolStatus
        /// is unimportant.
        /// </summary>
        Undefined,

        /// <summary>
        /// Default value. NetworkComms.Net will use a custom application layer protocol to provide 
        /// useful features such as inline serialisation, transparent packet send and receive, 
        /// connection handshakes and remote information etc. We strongly recommend you enable the 
        /// NetworkComms.Net application layer protocol.
        /// </summary>
        Enabled,

        /// <summary>
        /// No application layer protocol will be used. TCP packets may fragment or be concatenated 
        /// with other packets. A large number of library features will be unavailable.
        /// </summary>
        Disabled
    }
}