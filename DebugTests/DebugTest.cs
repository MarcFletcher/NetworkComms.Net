using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;
using System.Net;

namespace DebugTests
{
    static class DebugTest
    {
        public static void GoTCP()
        {
            NetworkComms.AppendGlobalConnectionEstablishHandler(connectionInfo => { Console.WriteLine("Connection establish handler executed for " + connectionInfo); });
            NetworkComms.AppendGlobalConnectionCloseHandler(connectionInfo => { Console.WriteLine("Connection close handler executed for " + connectionInfo); });

            NetworkComms.EnablePacketCheckSumValidation = true;

            if (false)
            {
                NetworkComms.ListenOnAllAllowedInterfaces = true;
                TCPConnection.AddNewLocalListener();

                Console.WriteLine("Listening on:");
                foreach (var entry in TCPConnection.CurrentLocalEndPoints())
                    Console.WriteLine("  " + entry.Address + ":" + entry.Port);

                NetworkComms.AppendGlobalIncomingPacketHandler<int>("NullMessage", (header, connection, message) => { Console.WriteLine("\n  ... Incoming trigger from " + connection.ConnectionInfo); });
                NetworkComms.AppendGlobalIncomingPacketHandler<int>("SRtest", (header, connection, message) => { connection.SendObject("SRresponse", "test good!"); });

                Console.WriteLine("\nReady for incoming connections.");

                Console.ReadKey(true);

                NetworkComms.Shutdown();
            }
            else
            {
                TCPConnection conn = TCPConnection.CreateConnection(new ConnectionInfo("131.111.73.200", 10000));

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
            if (true)
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
                UDPConnection.SendObject("broadcast", new byte[10], "255.255.255.255", 10000);

                UDPConnection.AddNewLocalListener();

                NetworkComms.AppendGlobalIncomingPacketHandler<string>("udpResponse", (header, connection, message) =>
                {
                    Console.WriteLine("Received UDP response. Remote end said -'" + message + "'.");
                });

                //UDPConnection.SendObject("udpTest", new byte[100], new IPEndPoint(IPAddress.Parse("192.168.0.120"), 10000));
                Thread.Sleep(10000000);
            }
        }
    }
}
