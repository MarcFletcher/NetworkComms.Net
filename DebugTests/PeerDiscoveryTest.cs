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
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.Connections;

namespace DebugTests
{
    static class PeerDiscoveryTest
    {
        static byte[] sendArray = new byte[] { 3, 45, 200, 10, 9, 8, 7, 45, 96, 123 };

        static bool serverMode;

        public static void RunExample()
        {
            NetworkComms.ConnectionEstablishTimeoutMS = 600000;

            IPAddress localIPAddress = IPAddress.Parse("::1");

            Console.WriteLine("Please select mode:");
            Console.WriteLine("1 - Server (Discoverable)");
            Console.WriteLine("2 - Client (Locates clients)");

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) serverMode = true;
            else serverMode = false;

            ConsoleColor textColor = Console.ForegroundColor;
        
            if (serverMode)
            {   
                //Add a discoverable listenner 
                Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Any, 12345), true);

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("\n**********************************************************");
                Console.WriteLine("Listening for TCP messages on:");
                Console.WriteLine("**********************************************************");
                Console.ForegroundColor = textColor;
                foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP))
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("**********************************************************");
                Console.WriteLine("These listenners are also discoverable");
                Console.WriteLine("**********************************************************");
                Console.ForegroundColor = textColor;

                PeerDiscovery.EnableDiscoverable(PeerDiscovery.DiscoveryMethod.UDPBroadcast);

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("\n**********************************************************");
                Console.WriteLine("Now discoverable.");
                Console.WriteLine("Listening for UDP messages on:");
                Console.WriteLine("**********************************************************");
                Console.ForegroundColor = textColor;
                foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.UDP)) 
                   Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                Console.WriteLine("\nPress any key to quit.");
                ConsoleKeyInfo key = Console.ReadKey(true);
            }
            else
            {
                PeerDiscovery.EnableDiscoverable(PeerDiscovery.DiscoveryMethod.UDPBroadcast);
                Console.WriteLine("Now discoverable.");
                Console.WriteLine("\nListening for UDP messages on:");
                foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.UDP))
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                PeerDiscovery.OnPeerDiscovered += (endpoints) =>
                    {
                        foreach (var pair in endpoints)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine("\n**********************************************************");
                            Console.WriteLine("Endpoints discoverd of type {0}:", pair.Key);
                            Console.WriteLine("**********************************************************");
                            Console.ForegroundColor = textColor;

                            foreach (var endPoint in pair.Value)
                                Console.WriteLine("\t->\t{1}", endPoint.ToString());
                        }
                        
                    };

                PeerDiscovery.DiscoverPeersAsync(PeerDiscovery.DiscoveryMethod.UDPBroadcast);

                //Console.WriteLine("Found clients at:");
                //foreach (IPEndPoint endPoint in result)
                //    Console.WriteLine("{0}:{1}", endPoint.Address, endPoint.Port);

                Console.WriteLine("\nClient complete. Press any key to quit.");
                Console.ReadKey(true);
            }
        }

        private static void ServerDataHandler(PacketHeader header, Connection connection, byte[] data)
        {
            Console.WriteLine("Received data (" + data.Length + ") from " + connection.ToString());
        }
    }
}
