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
    static class DebugTest
    {
        public static void RunExample()
        {
            SendReceiveOptions optionsToUseForUDPOutput = new SendReceiveOptions<NullSerializer>();
            SendReceiveOptions optionsToUseForUDPInput = new SendReceiveOptions<NullSerializer>();

            UDPConnectionListener udpListener = new UDPConnectionListener(optionsToUseForUDPInput, ApplicationLayerProtocolStatus.Disabled, UDPOptions.None);

            //Add a packet handler for dealing with incoming unmanaged data
            udpListener.AppendIncomingUnmanagedPacketHandler(HandleIncomingUDPPacket);

            Connection.StartListening(udpListener, new IPEndPoint(IPAddress.Any, 10000));

            //Stop listening and attempt to use the same port again
            Connection.StopListening(udpListener);
            Connection.StartListening(udpListener, new IPEndPoint(IPAddress.Any, 10000));

            //Stop listening and attempt to use a different port 
            Connection.StopListening(udpListener);
            Connection.StartListening(udpListener, new IPEndPoint(IPAddress.Any, 20000));
        }

        private static void HandleIncomingUDPPacket(PacketHeader header, Connection connection, byte[] array)
        {
            string sz = string.Format("Received {0} bytes from ", array.Length);
            System.Diagnostics.Trace.WriteLine(sz + connection.ToString());
        }
    }
}
