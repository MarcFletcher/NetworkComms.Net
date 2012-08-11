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
        public static void Go()
        {
            NetworkComms.AppendGlobalConnectionEstablishHandler(connectionInfo => { Console.WriteLine("Connection establish handler executed for " + connectionInfo); });
            NetworkComms.AppendGlobalConnectionCloseHandler(connectionInfo => { Console.WriteLine("Connection close handler executed for " + connectionInfo); });

            NetworkComms.EnablePacketCheckSumValidation = true;

            if (false)
            {
                NetworkComms.ListenOnAllAllowedInterfaces = true;
                TCPConnection.AddNewLocalConnectionListener();

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
    }
}
