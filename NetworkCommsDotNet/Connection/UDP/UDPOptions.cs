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

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Connections.UDP
{
    /// <summary>
    /// A <see cref="UDPConnection"/> could support different combinations of features. i.e. From the most basic (None) which 
    /// sends connectionless UDP packets up to an emulated TCP. Future versions of NetworkCommsDotNet will support an ever 
    /// increasing number of UDP features. This flag enum is used to specify which of the available features should be used.
    /// </summary>
    [Flags]
    public enum UDPOptions
    {
        /// <summary>
        /// The most basic UDP option. All UDP packets are sent connectionless with no error handling, sequencing or duplication prevention.
        /// </summary>
        None = 0x0,

        /// <summary>
        /// Performs a connection handshake, which ensures the remote end is alive at the time of the connection
        /// establish. Also exchanges network identifier and possible remote listening port.
        /// </summary>
        Handshake = 0x1,

        //The following UDP options are on the roadmap for future implementation.

        //Ensures packets can only be received in the order they were sent. e.g. Prevents old messages arriving late from being handled.
        //Sequenced = 0x2,

        //Notify the remote peer we are close/removing the connection
        //ConnectionCloseNotify = 0x3,
    }

    /// <summary>
    /// A small wrapper class which allows an initialising UDP datagram
    /// to be handled within a connection instantiation if required.
    /// </summary>
    internal class HandshakeUDPDatagram
    {
        public bool DatagramHandled { get; set; }
        public byte[] DatagramBytes { get; private set; }

        public HandshakeUDPDatagram(byte[] datagramBytes)
        {
            DatagramBytes = datagramBytes;
        }
    }
}