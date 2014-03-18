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
            NetworkComms.AppendGlobalIncomingPacketHandler<string>("Data", (header, connection, message) =>
                {
                    Console.WriteLine("Server received - " + message);
                    //connection.SendObject(header.RequestedReturnPacketType, "replyMessage");
                });

            Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Any, 10000));

            Thread thread1 = new Thread(() => {
                Connection conn = TCPConnection.GetConnection(new ConnectionInfo(IPTools.ParseEndPointFromString("::1:10000")));
                conn.SendObject("Data", "test1");
                Thread.Sleep(int.MaxValue);
            });

            Thread thread2 = new Thread(() => {
                Connection conn = TCPConnection.GetConnection(new ConnectionInfo(IPTools.ParseEndPointFromString("::1:10000")));
                conn.SendObject("Data", "test2");
                Thread.Sleep(int.MaxValue);
            });

            thread1.Start();
            thread2.Start();

            Console.WriteLine("Client done!");
            Console.ReadKey();
        }
    }
}
