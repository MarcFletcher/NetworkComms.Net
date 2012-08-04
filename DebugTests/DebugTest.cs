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

            if (true)
            {
                NetworkComms.ListenOnAllAllowedInterfaces = true;
                TCPConnection.AddNewLocalConnectionListener();

                Console.WriteLine("Listening on:");
                foreach (var entry in TCPConnection.CurrentLocalEndPoints())
                    Console.WriteLine("  " + entry.Address + ":" + entry.Port);

                Console.WriteLine("\nReady for incoming connections.");

                Console.ReadKey(true);

                NetworkComms.Shutdown();
            }
            else
            {
                TCPConnection conn = TCPConnection.CreateConnection(new ConnectionInfo("131.111.73.200", 10000));

                //NetworkComms.CloseAllConnections(new IPEndPoint[] { new IPEndPoint(IPAddress.Parse("131.111.73.200"), 10000) }, ConnectionType.TCP);

                //bool success = conn.CheckConnectionAlive(1000);
                Thread.Sleep(6000000);
            }
        }
    }
}
