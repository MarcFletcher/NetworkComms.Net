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
    static class AliveTest
    {
        public static void RunExample()
        {
            //Write out all comms logging
            //NetworkComms.WriteLineToLogMethod = new NetworkComms.WriteLineToLogDelegate((message) => { Console.WriteLine(" -- " + message); });

            //NetworkComms.PreferredIPPrefix = new string[] { "192.168.56" };

            NetworkComms.AppendIncomingPacketHandler<string>("Message", (header, conectionId, message) => 
            { 
                Console.WriteLine("\n  ... Incoming message from " + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientIP.ToString() + " saying '" + message  +"'.");
                //NetworkComms.SendObject("MessageReturn", conectionId, false, "Got your message!");
            });

            NetworkComms.AppendGlobalConnectionCloseHandler((connectionId) =>
                {
                    Console.WriteLine("Closed connection with " + connectionId.ToString());
                });

            Console.WriteLine("Listening for messages on {0}:{1}", NetworkComms.LocalIP, NetworkComms.CommsPort.ToString());
 
            while (true)
            {
                Console.WriteLine("\nPlease enter 'server', 'client' or 'exit' to quit:");
                string message = Console.ReadLine();

                //If the user has typed exit then we leave our loop and end the example
                if (message == "exit") break;
                else if (message == "server")
                {
                    while (true)
                    {
                        //If we are the server we just twiddle our thumbs for ever
                        Thread.Sleep(5000);
                    }
                }
                else
                {
                    Console.WriteLine("Please enter the server IP address and port, e.g 192.168.0.1:4000:");
                    string userEnteredStr = Console.ReadLine(); string serverIP = userEnteredStr.Split(':')[0]; int serverPort = int.Parse(userEnteredStr.Split(':')[1]);

                    DateTime lastMessageTime = DateTime.Now.AddDays(-1);

                    while (true)
                    {
                        try
                        {
                            //Every 10 minutes the client sends a little string
                            if ((DateTime.Now - lastMessageTime).TotalMinutes > 10)
                            {
                                NetworkComms.SendObject("Message", serverIP, serverPort, false, "Hello server!");
                                lastMessageTime = DateTime.Now;
                            }
                            else
                                Thread.Sleep(5000);
                        }
                        catch (Exception ex)
                        {
                            NetworkComms.LogError(ex, "ClientError");
                        }
                    }
                }
            }

            //We should always call shutdown on comms if we have used it
            NetworkComms.ShutdownComms();
        }
    }
}
