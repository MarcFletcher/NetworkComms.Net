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

namespace DebugTests
{
    /// <summary>
    /// A scrap board class for solving whatever bug is currently causing issues
    /// </summary>
    static class DebugTest
    {
        public static void RunExample()
        {
            //Get the serializer and data processors
            TCPConnectionListener listener = new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled);
            Connection.StartListening(listener, new IPEndPoint(IPAddress.Any, 10000));

            Console.WriteLine("Client done!");
            Console.ReadKey();
        }
    }
}
