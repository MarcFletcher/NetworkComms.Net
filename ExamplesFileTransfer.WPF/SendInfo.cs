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
using System.Linq;
using System.Text;

using ProtoBuf;

namespace Examples.ExamplesFileTransfer.WPF
{
    /// <summary>
    /// Information class used to associate incoming data with the correct ReceivedFile
    /// </summary>
    [ProtoContract]
    class SendInfo
    {
        /// <summary>
        /// Corresponding filename
        /// </summary>
        [ProtoMember(1)]
        public string Filename { get; private set; }

        /// <summary>
        /// The starting point for the associated data
        /// </summary>
        [ProtoMember(2)]
        public long BytesStart { get; private set; }

        /// <summary>
        /// The total number of bytes expected for the whole ReceivedFile
        /// </summary>
        [ProtoMember(3)]
        public long TotalBytes { get; private set; }

        /// <summary>
        /// The packet sequence number corresponding to the associated data
        /// </summary>
        [ProtoMember(4)]
        public long PacketSequenceNumber { get; private set; }

        /// <summary>
        /// Private constructor required for deserialisation
        /// </summary>
        private SendInfo() { }

        /// <summary>
        /// Create a new instance of SendInfo
        /// </summary>
        /// <param name="filename">Filename corresponding to data</param>
        /// <param name="totalBytes">Total bytes of the whole ReceivedFile</param>
        /// <param name="bytesStart">The starting point for the associated data</param>
        /// <param name="packetSequenceNumber">Packet sequence number corresponding to the associated data</param>
        public SendInfo(string filename, long totalBytes, long bytesStart, long packetSequenceNumber)
        {
            this.Filename = filename;
            this.TotalBytes = totalBytes;
            this.BytesStart = bytesStart;
            this.PacketSequenceNumber = packetSequenceNumber;
        }
    }
}
