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

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// A wrapper object for keeping track of sent packets. These are used if a resend is requested due to a checksum validation failure.
    /// </summary>
    class SentPacket
    {
        public int SendCount { get; private set; }
        public IPacket Packet { get; private set; }
        public DateTime SentPacketCreationTime { get; private set; }

        public SentPacket(IPacket packet)
        {
            this.SentPacketCreationTime = DateTime.Now;
            this.Packet = packet;
            this.SendCount = 1;
        }

        public void IncrementSendCount()
        {
            SendCount++;
        }

        public override string ToString()
        {
            string timeString;
#if NETFX_CORE
            timeString = NetworkCommsDotNet.Tools.XPlatformHelper.DateTimeExtensions.ToShortTimeString(SentPacketCreationTime);
#else
            timeString = (SentPacketCreationTime).ToShortTimeString();
#endif

            return "[" + timeString + "] " + Packet.PacketHeader.PacketType + " - " + Packet.PacketData.Length.ToString() + " bytes.";
        }
    }
}
