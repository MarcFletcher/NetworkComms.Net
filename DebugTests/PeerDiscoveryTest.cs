//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
using DPSBase;

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
            Console.WriteLine("1 - Server (Listens for connections)");
            Console.WriteLine("2 - Client (Creates connections to server)");

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) serverMode = true;
            else serverMode = false;

            if (serverMode)
            {

                Console.WriteLine("\nListening for messages on:");
                foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.UDP))
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                Console.WriteLine("\nPress any key to quit.");
                ConsoleKeyInfo key = Console.ReadKey(true);
            }
            else
            {
                

                Console.WriteLine("\nClient complete. Press any key to quit.");
                Console.ReadKey(true);
            }

            NetworkComms.Shutdown();
        }

        private static void ServerDataHandler(PacketHeader header, Connection connection, byte[] data)
        {
            Console.WriteLine("Received data (" + data.Length + ") from " + connection.ToString());
        }
    }
}
