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
                            TCPConnection.CreateConnection(new ConnectionInfo(catcherPoint)).SendObject("ThrowData", throwData, nullCompressionSRO);
                        }
                    });

                Console.WriteLine("Let the throwing begin!\n");

                while (true)
                {
                    Console.WriteLine("{0} - Instance Load = {1}, 5 sec load= {2}, 15 sec load= {3}", DateTime.Now.ToLongTimeString(), NetworkComms.CurrentNetworkLoad.ToString("0.000"), NetworkComms.AverageNetworkLoad(5).ToString("0.000"), NetworkComms.AverageNetworkLoad(15).ToString("0.000"));
                    Thread.Sleep(1000);
                }
            }
            else
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("ThrowData", (packetHeader, connection, data) => 
                {
                    Console.WriteLine("{0} - Caught {1} bytes thrown by {2}.", DateTime.Now.ToLongTimeString(), data.Length, connection.ConnectionInfo);
                });

                TCPConnection.AddNewLocalListener();

                Console.WriteLine("Let the catching begin!\n");
                while (true)
                    Thread.Sleep(10000);
            }
        }
    }
}
