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
using System.Threading.Tasks;

namespace DebugTests
{
    /*
     * Run multiple clients using a batch script containing the following
     * 
     *  for /l %%i in (1, 1, 100) do (
     *  start "title of the process" "DebugTests.exe" 
     *  )
     */

    /// <summary>
    /// A scrap board class for solving whatever bug is currently causing issues
    /// </summary>
    static class ClientHammer
    {
        public static void RunExample(string[] args)
        {
            if (args.Length == 0 || args[0] == "client")
                RunClient();
            else
                RunServer();
        }

        private static void RunServer()
        {
            //ILogger logger = new LiteLogger(LiteLogger.LogMode.LogFileOnly, "DebugTests_" + NetworkComms.NetworkIdentifier + ".txt");
            //NetworkComms.EnableLogging(logger);

            //Slightly improves performance when we have many simultaneous incoming connections.
            NetworkComms.ConnectionListenModeUseSync = true;

            //Trigger the method PrintIncomingMessage when a packet of type 'Message' is received
            //We expect the incoming object to be a string which we state explicitly by using <string>
            NetworkComms.AppendGlobalIncomingPacketHandler<string>("Message", PrintIncomingMessage);
            NetworkComms.AppendGlobalConnectionEstablishHandler(OnConnectionEstablished);
            NetworkComms.AppendGlobalConnectionCloseHandler(OnConnectionClosed);

            Connection.StartListening(ConnectionType.TCP, new System.Net.IPEndPoint(System.Net.IPAddress.Any, 4000));

            //Print out the IPs and ports we are now listening on
            Console.WriteLine("Server listening for TCP connection on:");
            foreach (System.Net.IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP))
                Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

            //Let the user close the server
            Console.WriteLine("\nPress any key to close server.");
            Console.ReadKey(true);

            //We have used NetworkComms so we should ensure that we correctly call shutdown
            NetworkComms.Shutdown();
        }

        private static void RunClient()
        {
            Console.WriteLine("Client launching ...");

            string Server = "127.0.0.1";
            int Port = 4000;
            string SlaveNameFormat = "Slave {0}";

            var slaveId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var endpoint = new IPEndPoint(IPAddress.Parse(Server), Port);

            var connection = TCPConnection.GetConnection(new ConnectionInfo(endpoint));
            var slaveName = string.Format(SlaveNameFormat, slaveId);

            Console.WriteLine(string.Format("{0} reporting for duty!", slaveName));
            connection.SendObject("Message", string.Format("{0} reporting for duty!", slaveName));
            Thread.Sleep(5000);
            Console.WriteLine(string.Format("{0} unregistering duty!", slaveName));
            connection.SendObject("Message", string.Format("{0} unregistering duty!", slaveName));
            Thread.Sleep(5000);
            Console.WriteLine("Closing");
        }

        private static void OnConnectionClosed(Connection connection)
        {
            Console.WriteLine("Connection closed!");
        }

        private static void OnConnectionEstablished(Connection connection)
        {
            Console.WriteLine("Connection established!");
        }

        private static void PrintIncomingMessage(PacketHeader header, Connection connection, string message)
        {
            Console.WriteLine(message);
        }
    }
}
