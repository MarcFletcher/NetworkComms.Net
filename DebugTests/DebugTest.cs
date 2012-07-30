using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;

namespace DebugTests
{
    static class DebugTest
    {
        public static void Go()
        {
            if (false)
            {
                NetworkComms.ListenOnAllAllowedInterfaces = true;

                TCPConnection.AddNewLocalEndPointListen();

                Console.WriteLine("Listening on:");
                foreach (var entry in TCPConnection.CurrentLocalEndPoints())
                    Console.WriteLine("  " + entry.Address + ":" + entry.Port);

                Console.ReadKey(true);

                TCPConnection.Shutdown();
            }
            else
            {
                TCPConnection conn = TCPConnection.CreateConnection(new ConnectionInfo("131.111.73.200", 10000));
            }
        }
    }
}
