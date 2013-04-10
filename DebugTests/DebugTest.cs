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
using DPSBase;
using System.IO;

namespace DebugTests
{
    static class DebugTest
    {
        static long[] sendArray = new long[] { 43125, 65346345, 23147, 6457, 2345, 7657456, 5342564, 85678576, 3245, 87658, 3456, 589, 35456, 96879 };

        public static void GoStreamTest()
        {
            Random randGen = new Random();
            byte[] someRandomData = new byte[300 * 1024 * 1024];
            randGen.NextBytes(someRandomData);
            string md5 = NetworkComms.MD5Bytes(someRandomData);

           // MemoryStream inputStream = new MemoryStream(someRandomData);

            FileStream destinationStream = new FileStream("testStream.crap", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            double result = StreamWriteWithTimeout.Write(someRandomData, someRandomData.Length, destinationStream, 80000, 0, 500);
            destinationStream.Flush();

            if (NetworkComms.MD5Bytes(destinationStream) != md5)
                throw new Exception("I think we have a problem captain!");

            destinationStream.Close();
        }

        public static void GoTCP()
        {
            //Dictionary<string, string> optionsDic = new Dictionary<string, string>();
            //SerializerBase.RijndaelPSKEncrypter.AddPasswordToOptions(optionsDic, "password");

            //SendReceiveOptions options = new SendReceiveOptions(ProcessorManager.Instance.GetSerializer<ProtobufSerializer>(),
            //    new List<DataProcessor>(){ProcessorManager.Instance.GetDataProcessor<QuickLZCompressor.QuickLZ>(), 
            //                              ProcessorManager.Instance.GetDataProcessor<RijndaelPSKEncrypter>()}, optionsDic);

            //NetworkComms.DefaultSendReceiveOptions = new SendReceiveOptions(ProcessorManager.Instance.GetSerializer<ProtobufSerializer>(), null, null);

            byte[] serialisedTest = DPSManager.GetDataSerializer<ProtobufSerializer>().SerialiseDataObject<SerialiseTest>(new SerialiseTest(2, "God")).ThreadSafeStream.ToArray();
            Array.Resize<byte>(ref serialisedTest, 100);

            SerialiseTest deserialised = DPSManager.GetDataSerializer<ProtobufSerializer>().DeserialiseDataObject<SerialiseTest>(new MemoryStream(serialisedTest));

            NetworkComms.AppendGlobalConnectionEstablishHandler(connectionInfo => { Console.WriteLine("Connection establish handler executed for " + connectionInfo); });
            NetworkComms.AppendGlobalConnectionCloseHandler(connectionInfo => { Console.WriteLine("Connection close handler executed for " + connectionInfo); });

            //NetworkComms.EnablePacketCheckSumValidation = true;

            if (false)
            {
                NetworkComms.ListenOnAllAllowedInterfaces = true;
                TCPConnection.StartListening();

                Console.WriteLine("Listening on:");
                foreach (var entry in TCPConnection.ExistingLocalListenEndPoints())
                    Console.WriteLine("  " + entry.Address + ":" + entry.Port);

                NetworkComms.AppendGlobalIncomingPacketHandler<int>("NullMessage", (header, connection, message) => { Console.WriteLine("\n  ... Incoming trigger from " + connection.ConnectionInfo); });
                NetworkComms.AppendGlobalIncomingPacketHandler<long[]>("SRtest", (header, connection, message) => 
                {
                    Console.WriteLine("Received long[] with values" + message.Aggregate("", (a, b) => { return a + ", " + b.ToString(); }));
                    connection.SendObject("SRresponse", "test good!"); 
                });

                Console.WriteLine("\nReady for incoming connections.");

                Console.ReadKey(true);

                NetworkComms.Shutdown();
            }
            else
            {
                TCPConnection conn = TCPConnection.GetConnection(new ConnectionInfo("131.111.73.200", 10000));

                for (int i = 0; i < 60; i++)
                {
                    conn.ConnectionAlive();
                    Thread.Sleep(1000);
                }

                //Thread.Sleep(5000);
                //conn.SendObject("NullMessage");
                //Thread.Sleep(5000);

                //if (conn.ConnectionAlive())
                //    Console.WriteLine("Success");
                //else
                //    Console.WriteLine("Cry!");

                //Thread.Sleep(5000);
                //Console.WriteLine(conn.SendReceiveObject<string>("SRtest", "SRresponse", 1000, sendArray));

                //NetworkComms.CloseAllConnections(new IPEndPoint[] { new IPEndPoint(IPAddress.Parse("131.111.73.200"), 10000) }, ConnectionType.TCP);

                //bool success = conn.CheckConnectionAlive(1000);
                //Thread.Sleep(6000000);
            }
        }

        public static void GoUDP()
        {
            if (false)
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<int>("udpTest", (header, connection, message) => 
                {
                    Console.WriteLine("Received UDP data.");
                    connection.SendObject("udpResponse", "test good!"); 
                });

                NetworkComms.AppendGlobalIncomingPacketHandler<int>("broadcast", (header, connection, message) =>
                {
                    Console.WriteLine("Received UDP broadcast.");
                });

                UDPConnection.StartListening();

                Console.WriteLine("\nReady for incoming udp connections.");

                Console.ReadKey(true);

                NetworkComms.Shutdown();
            }
            else
            {
                //THis is a general UDP broadcast, broadcasts are not forwarded across vpns
                //UDPConnection.SendObject("broadcast", new byte[10], "255.255.255.255", 10000);

                UDPConnection testConnection = UDPConnection.GetConnection(new ConnectionInfo("131.111.73.213", 10000), UDPOptions.None);

                byte[] sendArray = new byte[65000];
                testConnection.SendObject("udpTest", sendArray);

                UDPConnection.StartListening();

                NetworkComms.AppendGlobalIncomingPacketHandler<string>("udpResponse", (header, connection, message) =>
                {
                    Console.WriteLine("Received UDP response. Remote end said -'" + message + "'.");
                });

                UDPConnection.SendObject("udpTest", new byte[100], new IPEndPoint(IPAddress.Parse("131.111.73.213"), 10000));
                Thread.Sleep(10000000);
            }
        }

        public static void GoDFSLogParse()
        {
            string[] allLines = File.ReadAllLines("log.txt");

            using (StreamWriter sw = new StreamWriter("out.csv", false))
            {
                for (int i = 0; i < allLines.Length; i++)
                {
                    string line = allLines[i];

                    if (line.Contains(" payload bytes."))
                    {
                        int stringend = line.IndexOf(" payload bytes.", 0);
                        int stringStart = line.LastIndexOf("and ") + 4;

                        int timeEnd = line.IndexOf(" - ", 0);

                        double payloadSize = double.Parse(line.Substring(stringStart, stringend-stringStart));

                        sw.WriteLine(payloadSize + "," + line);
                    }
                }
            }
        }

        [ProtoBuf.ProtoContract]
        class SerialiseTest
        {
            [ProtoBuf.ProtoMember(1)]
            int someNumber;
            [ProtoBuf.ProtoMember(2)]
            string someString;
            [ProtoBuf.ProtoMember(3)]
            byte[] someBytes;

            private SerialiseTest() { }

            public SerialiseTest(int num, string str)
            {
                this.someNumber = num;
                this.someString = str;
                this.someBytes = new byte[300];
            }
        }
    }
}
