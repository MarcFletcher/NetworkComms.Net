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
using System.Threading;
using System.IO;
using NetworkCommsDotNet.Connections;

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Private wrapper class used for passing data to the incoming packet item worker
    /// </summary>
    class PriorityQueueItem
    {
        public QueueItemPriority Priority { get; private set; }
        public Connection Connection { get; private set; }
        public PacketHeader PacketHeader { get; private set; }
        public MemoryStream DataStream { get; private set; }
        public SendReceiveOptions SendReceiveOptions { get; private set; }

        /// <summary>
        /// Initialise a new PriorityQueueItem
        /// </summary>
        /// <param name="priority"></param>
        /// <param name="connection"></param>
        /// <param name="packetHeader"></param>
        /// <param name="dataStream"></param>
        /// <param name="sendReceiveOptions"></param>
        public PriorityQueueItem(QueueItemPriority priority, Connection connection, PacketHeader packetHeader, MemoryStream dataStream, SendReceiveOptions sendReceiveOptions)
        {
            if (connection == null) throw new ArgumentNullException("connection", "Provided Connection parameter cannot be null.");
            if (packetHeader == null) throw new ArgumentNullException("packetHeader", "Provided PacketHeader parameter cannot be null.");
            if (dataStream == null) throw new ArgumentNullException("dataStream", "Provided MemoryStream parameter cannot be null.");
            if (sendReceiveOptions == null) throw new ArgumentNullException("sendReceiveOptions", "Provided sendReceiveOptions cannot be null.");

            this.Priority = priority;
            this.Connection = connection;
            this.PacketHeader = packetHeader;
            this.DataStream = dataStream;
            this.SendReceiveOptions = sendReceiveOptions;
        }
    }
}
