//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;

namespace Examples.ExamplesConsole
{
    /// <summary>
    /// Used for testing the performance of networks
    /// </summary>
    public static class SpeedTest
    {
        static bool hostMode;

        /// <summary>
        /// Run example
        /// </summary>
        public static void RunExample()
        {
            //Select mode
            Console.WriteLine("SpeedTest Example ...\n");

            Console.WriteLine("Please select host or peer mode:");
            Console.WriteLine("1 - Host Mode (Catches Data)");
            Console.WriteLine("2 - Peer Mode (Sends Data)");

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) hostMode = true;
            else hostMode = false;

            if (hostMode)
            {
                //Prepare DFS in host mode
                #region ServerMode
                Console.WriteLine("\n ... host mode selected.");

                NetworkComms.ConnectionEstablishShutdownDelegate clientEstablishDelegate = (connection) =>
                {
                    Console.WriteLine("Client " + connection.ConnectionInfo + " connected.");
                };

                NetworkComms.ConnectionEstablishShutdownDelegate clientShutdownDelegate = (connection) =>
                {
                    Console.WriteLine("Client " + connection.ConnectionInfo + " disconnected.");
                };

                NetworkComms.PacketHandlerCallBackDelegate<byte[]> IncomingDataDelegate = (packetHeader, connection, incomingObject) =>
                {
                    Console.WriteLine("Speed bytes received from " + connection.ConnectionInfo +".");
                };

                NetworkComms.AppendGlobalConnectionEstablishHandler(clientEstablishDelegate);
                NetworkComms.AppendGlobalConnectionCloseHandler(clientShutdownDelegate);
                NetworkComms.AppendGlobalIncomingPacketHandler("SpeedData", IncomingDataDelegate);

                //Start listening for TCP connections
                //We want to select a random port on all available adaptors so provide 
                //an IPEndPoint using IPAddress.Any and port 0.
                Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Any, 0));

                Console.WriteLine("\nListening for incoming connections on:");
                foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP))
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                Console.WriteLine("\nIdentifier - {0}", NetworkComms.NetworkIdentifier);
                Console.WriteLine("\nPress 'q' to close host.\n");

                while (true)
                {
                    ConsoleKeyInfo pressedKey = Console.ReadKey(true);
                    if (pressedKey.Modifiers != ConsoleModifiers.Control && pressedKey.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine("Closing host.");
                        break;
                    }
                }
                #endregion
            }
            else if (!hostMode)
            {
                //Prepare DFS in peer mode
                #region PeerMode
                Console.WriteLine("\n ... peer mode selected.");

                Console.WriteLine("\nPlease enter how large the test data packet should be in MB and press return (larger is more accurate), e.g. 1024:");
                int numberMegsToCreate = int.Parse(Console.ReadLine());

                //Fill a byte[] with random data
                DateTime startTime = DateTime.Now;
                Random randGen = new Random();
                byte[] someRandomData = new byte[numberMegsToCreate * 1024 * 1024];
                randGen.NextBytes(someRandomData);

                Console.WriteLine("\nTest speed data created. Using {0}MB.\n", numberMegsToCreate);

                NetworkComms.PacketConfirmationTimeoutMS = 20000;
                ConnectionInfo serverConnectionInfo = ExampleHelper.GetServerDetails();

                Console.WriteLine("\nIdentifier - {0}", NetworkComms.NetworkIdentifier);

                SendReceiveOptions nullCompressionSRO = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                            new List<DataProcessor>(),
                            new Dictionary<string, string>());

                //Add options which will require receive confirmations and also include packet construction time
                //in the packet header.
                nullCompressionSRO.Options.Add("ReceiveConfirmationRequired", "");
                nullCompressionSRO.Options.Add("IncludePacketConstructionTime", "");

                TCPConnection serverConnection = TCPConnection.GetConnection(serverConnectionInfo);
                Stopwatch timer = new Stopwatch();

                while (true)
                {
                    timer.Reset();
                    timer.Start();
                    serverConnection.SendObject("SpeedData", someRandomData, nullCompressionSRO);
                    timer.Stop();
                    Console.WriteLine("SpeedData sent successfully with receive confirmation in {0}secs. Corresponds to {1}MB/s", (timer.ElapsedMilliseconds / 1000.0).ToString("0.00"), (numberMegsToCreate / (timer.ElapsedMilliseconds / 1000.0)).ToString("0.00"));
                }
                #endregion
            }

            NetworkComms.Shutdown();
        }
    }
}
