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

namespace DebugTests
{
    static class DebugTest
    {
        static byte[] sendArray = new byte[] { 3, 45, 200, 10, 9, 8, 7, 45, 96, 123 };
        static bool serverMode;

        public static void RunExample()
        {
            Console.WriteLine("Please select mode:");
            Console.WriteLine("1 - Server (Listens for connections)");
            Console.WriteLine("2 - Client (Creates connections to server)");

            NetworkComms.DefaultSendReceiveOptions = new SendReceiveOptions<ProtobufSerializer>();

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) serverMode = true;
            else serverMode = false;

            //IPAddress localIPAddress = IPAddress.Parse("::1");

            if (serverMode)
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("Data", (header, connection, data) =>
                {
                    Console.WriteLine("Received data (" + data.Length + ") from " + connection.ToString());
                });

                //Establish handler
                NetworkComms.AppendGlobalConnectionEstablishHandler((connection) =>
                {
                    Console.WriteLine("Connection established - " + connection);
                });

                //Close handler
                NetworkComms.AppendGlobalConnectionCloseHandler((connection) =>
                {
                    Console.WriteLine("Connection closed - " + connection);
                });

                List<ConnectionListenerBase> listeners = new List<ConnectionListenerBase>() { 
                    new BluetoothConnectionListener(NetworkComms.DefaultSendReceiveOptions,
                        ApplicationLayerProtocolStatus.Enabled, true)};

                List<EndPoint> listeningEndPoints = new List<EndPoint>() { new BluetoothEndPoint(BluetoothAddress.None, Guid.Empty, 0)};

                Connection.StartListening(listeners, listeningEndPoints, true);

                //Console.WriteLine("Listening for UDP messages on:");
                //foreach (IPEndPoint localEndPoint in UDPConnection.ExistingLocalListenEndPoints()) 
                //    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

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

        public static void Test()
        {
            IPRange range = new IPRange("192.168.1.0/24");

            List<string> ipToTest = new List<string>();

            for (int i = 0; i < 256; i++)
                ipToTest.Add("192.168.1." + i.ToString());

            foreach (string ip in ipToTest)
            {
                Console.WriteLine(ip + " - " + (range.Contains(ip) ? "YES" : "NO"));
            }

            Console.WriteLine("\nPress any key to quit.");
            Console.ReadKey(true);
        }
    }
}
