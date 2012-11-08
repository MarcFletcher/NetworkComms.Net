//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;
using System.Net;
using System.Threading.Tasks;
using DPSBase;

namespace DebugTests
{
    static class LoadTest
    {
        static bool throwerMode;

        public static void RunExample()
        {
            Console.WriteLine("Please select thrower or catcher mode:");
            Console.WriteLine("1 - Thrower Mode (Sends data)");
            Console.WriteLine("2 - Catcher Mode (Listens for data)");

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) throwerMode = true;
            else throwerMode = false;

            if (throwerMode)
            {
                var nullCompressionSRO = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                            new List<DataProcessor>(),
                            new Dictionary<string, string>());

                IPEndPoint catcherPoint = new IPEndPoint(IPAddress.Parse("131.111.73.200"), 10000);
                byte[] throwData = new byte[20*1024*1024];
                new Random().NextBytes(throwData);

                Task.Factory.StartNew(() =>
                    {
                        while (true)
                        {
                            TCPConnection.GetConnection(new ConnectionInfo(catcherPoint)).SendObject("ThrowData", throwData, nullCompressionSRO);
                        }
                    });

                Console.WriteLine("Let the throwing begin!\n");

                while (true)
                {
                    Console.WriteLine("IN - {0} - Instance Load = {1}, 5 sec load= {2}, 15 sec load= {3}", DateTime.Now.ToLongTimeString(), NetworkComms.CurrentNetworkLoadIncoming.ToString("0.000"), NetworkComms.AverageNetworkLoadIncoming(5).ToString("0.000"), NetworkComms.AverageNetworkLoadIncoming(15).ToString("0.000"));
                    Console.WriteLine("OUT - {0} - Instance Load = {1}, 5 sec load= {2}, 15 sec load= {3}", DateTime.Now.ToLongTimeString(), NetworkComms.CurrentNetworkLoadOutgoing.ToString("0.000"), NetworkComms.AverageNetworkLoadOutgoing(5).ToString("0.000"), NetworkComms.AverageNetworkLoadOutgoing(15).ToString("0.000"));
                    Thread.Sleep(1000);
                }
            }
            else
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("ThrowData", (packetHeader, connection, data) => 
                {
                    Console.WriteLine("{0} - Caught {1} bytes thrown by {2}.", DateTime.Now.ToLongTimeString(), data.Length, connection.ConnectionInfo);
                });

                TCPConnection.StartListening();

                Console.WriteLine("Let the catching begin, on port "+TCPConnection.ExistingLocalListenEndPoints().First().Port+"!\n");
                while (true)
                {
                    Console.WriteLine("IN - {0} - Instance Load = {1}, 5 sec load= {2}, 15 sec load= {3}", DateTime.Now.ToLongTimeString(), NetworkComms.CurrentNetworkLoadIncoming.ToString("0.000"), NetworkComms.AverageNetworkLoadIncoming(5).ToString("0.000"), NetworkComms.AverageNetworkLoadIncoming(15).ToString("0.000"));
                    Console.WriteLine("OUT - {0} - Instance Load = {1}, 5 sec load= {2}, 15 sec load= {3}", DateTime.Now.ToLongTimeString(), NetworkComms.CurrentNetworkLoadOutgoing.ToString("0.000"), NetworkComms.AverageNetworkLoadOutgoing(5).ToString("0.000"), NetworkComms.AverageNetworkLoadOutgoing(15).ToString("0.000"));
                    Thread.Sleep(10000);
                }
            }
        }
    }
}
