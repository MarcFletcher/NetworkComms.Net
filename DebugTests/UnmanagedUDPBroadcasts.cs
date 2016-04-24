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
using NetworkCommsDotNet;
using System.Threading;
using System.Net;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.IO;
using InTheHand.Net;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.Bluetooth;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;

namespace DebugTests
{
    /// <summary>
    /// A scrap board class for solving whatever bug is currently causing issues
    /// </summary>
    static class UnmanagedUDPBroadcast
    {
        public static void RunExample()
        {
            //Ensure we use the null serializer for unmanaged connections
            SendReceiveOptions options = new SendReceiveOptions<NullSerializer>();

            //Setup listening for incoming unmanaged UDP broadcasts
            UDPConnectionListener listener = new UDPConnectionListener(options, ApplicationLayerProtocolStatus.Disabled, UDPOptions.None);
            Connection.StartListening(listener, new IPEndPoint(IPAddress.Any, 10000));

            //Add a packet handler for unmanaged connections
            NetworkComms.AppendGlobalIncomingUnmanagedPacketHandler((packetHeader, connection, incomingBytes) => {
                Console.WriteLine("Received {0} bytes from {1}", incomingBytes.Length, connection.ConnectionInfo.RemoteEndPoint);
            });

            //Generate some test data to broadcast
            byte[] dataToSend = new byte[] { 1, 2,3, 4 };

            //Create an unmanaged packet manually and broadcast the test data
            //In future this part of the API could potentially be improved to make it clearer
            using(Packet sendPacket = new Packet("Unmanaged", dataToSend, options))
                UDPConnection.SendObject<byte[]>(sendPacket, new IPEndPoint(IPAddress.Broadcast, 10000), options, ApplicationLayerProtocolStatus.Disabled);

            Console.WriteLine("Client done!");
            Console.ReadKey();
        }
    }
}
