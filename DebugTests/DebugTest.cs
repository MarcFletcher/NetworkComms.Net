using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;
using System.Net;
using SerializerBase;
using SerializerBase.Protobuf;

namespace DebugTests
{
    static class DebugTest
    {
        public static void GoTCP()
        {
            Dictionary<string, string> optionsDic = new Dictionary<string, string>();
            SerializerBase.RijndaelPSKEncrypter.AddPasswordToOptions(optionsDic, "password");

            SendReceiveOptions options = new SendReceiveOptions(ProcessorManager.Instance.GetSerializer<ProtobufSerializer>(),
                new List<DataProcessor>(){ProcessorManager.Instance.GetDataProcessor<QuickLZCompressor.QuickLZ>(), 
                                          ProcessorManager.Instance.GetDataProcessor<RijndaelPSKEncrypter>()}, optionsDic);

            NetworkComms.DefaultSendReceiveOptions = options;

            NetworkComms.AppendGlobalConnectionEstablishHandler(connectionInfo => { Console.WriteLine("Connection establish handler executed for " + connectionInfo); });
            NetworkComms.AppendGlobalConnectionCloseHandler(connectionInfo => { Console.WriteLine("Connection close handler executed for " + connectionInfo); });

            NetworkComms.EnablePacketCheckSumValidation = true;

            if (true)
            {
                NetworkComms.ListenOnAllAllowedInterfaces = true;
                TCPConnection.AddNewLocalListener();

                Console.WriteLine("Listening on:");
                foreach (var entry in TCPConnection.CurrentLocalEndPoints())
                    Console.WriteLine("  " + entry.Address + ":" + entry.Port);

                NetworkComms.AppendGlobalIncomingPacketHandler<int>("NullMessage", (header, connection, message) => { Console.WriteLine("\n  ... Incoming trigger from " + connection.ConnectionInfo); });
                NetworkComms.AppendGlobalIncomingPacketHandler<string>("SRtest", (header, connection, message) => { connection.SendObject("SRresponse", "test good!"); });

                Console.WriteLine("\nReady for incoming connections.");

                Console.ReadKey(true);

                NetworkComms.Shutdown();
            }
            else
            {
                TCPConnection conn = TCPConnection.CreateConnection(new ConnectionInfo("131.111.73.213", 10000));

                Thread.Sleep(5000);
                conn.SendObject("NullMessage");
                Thread.Sleep(5000);

                if (conn.ConnectionAlive())
                    Console.WriteLine("Success");
                else
                    Console.WriteLine("Cry!");

                Thread.Sleep(5000);
                Console.WriteLine(conn.SendReceiveObject<string>("SRtest", "SRresponse", 1000));

                //NetworkComms.CloseAllConnections(new IPEndPoint[] { new IPEndPoint(IPAddress.Parse("131.111.73.200"), 10000) }, ConnectionType.TCP);

                //bool success = conn.CheckConnectionAlive(1000);
                Thread.Sleep(6000000);
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

                UDPConnection.AddNewLocalListener();

                Console.WriteLine("\nReady for incoming udp connections.");

                Console.ReadKey(true);

                NetworkComms.Shutdown();
            }
            else
            {
                //THis is a general UDP broadcast, broadcasts are not forwarded across vpns
                //UDPConnection.SendObject("broadcast", new byte[10], "255.255.255.255", 10000);

                UDPConnection testConnection = UDPConnection.CreateConnection(new ConnectionInfo("131.111.73.213", 10000), UDPLevel.None);

                byte[] sendArray = new byte[65000];
                testConnection.SendObject("udpTest", sendArray);

                UDPConnection.AddNewLocalListener();

                NetworkComms.AppendGlobalIncomingPacketHandler<string>("udpResponse", (header, connection, message) =>
                {
                    Console.WriteLine("Received UDP response. Remote end said -'" + message + "'.");
                });

                UDPConnection.SendObject("udpTest", new byte[100], new IPEndPoint(IPAddress.Parse("131.111.73.213"), 10000));
                Thread.Sleep(10000000);
            }
        }
    }
}
