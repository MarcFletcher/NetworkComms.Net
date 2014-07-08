//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
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
