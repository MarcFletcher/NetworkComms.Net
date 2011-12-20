using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;

namespace DebugTests
{
    /// <summary>
    /// Used to test the usage of NetworkComms.CheckConnectionAliveStatus()
    /// </summary>
    class AliveTest
    {
        public AliveTest()
        {

        }

        public void Go()
        {
            //Write out all comms logging
            //NetworkComms.WriteLineToLogMethod = new NetworkComms.WriteLineToLogDelegate((message) => { Console.WriteLine(" -- " + message); });

            //NetworkComms.PreferredIPPrefix = new string[] { "192.168.56" };

            NetworkComms.AppendIncomingPacketHandler<string>("Message", (header, conectionId, message) => 
            { 
                Console.WriteLine("\n  ... Incoming message from " + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientIP.ToString() + " saying '" + message  +"'.");
                NetworkComms.SendObject("MessageReturn", conectionId, false, "Got your message!");
            });

            NetworkComms.AppendGlobalConnectionCloseHandler((connectionId) =>
                {
                    Console.WriteLine("Closed connection with " + connectionId.ToString());
                });

            Console.WriteLine("Listening for messages on {0}:{1}", NetworkComms.LocalIP, NetworkComms.CommsPort.ToString());
 
            while (true)
            {
                Console.WriteLine("\nPlease enter your message and press enter, 'exit' to quit, 'test' to test connections:");
                string message = Console.ReadLine();

                //If the user has typed exit then we leave our loop and end the example
                if (message == "exit") break;
                else if (message == "test")
                {
                    DateTime startTime = DateTime.Now;

                    while ((DateTime.Now - startTime).TotalMinutes < 2)
                    {
                        NetworkComms.CheckConnectionAliveStatus(false);
                        Thread.Sleep(2000);
                    }

                    Console.WriteLine("Checking all existing connections");
                }
                else
                {
                    Console.WriteLine("Please enter the destination IP address and port, e.g 192.168.0.1:4000:");

                    string userEnteredStr = Console.ReadLine(); string serverIP = userEnteredStr.Split(':')[0]; int serverPort = int.Parse(userEnteredStr.Split(':')[1]);

                    //NetworkComms.SendObject("Message", serverIP, serverPort, false, message);
                    //Console.WriteLine(NetworkComms.SendRecieveObject<string>("Message", serverIP, serverPort, false, "MessageReturn", 300000, message));
                    NetworkComms.SendObject("DFS_ChunkAvailabilityInterestReplyComplete", serverIP, serverPort, false, "");
                }
            }

            //We should always call shutdown on comms if we have used it
            NetworkComms.ShutdownComms();
        }
    }
}
