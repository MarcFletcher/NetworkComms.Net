using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;

namespace DebugTests
{
    /// <summary>
    /// Networking in only 11 lines (not including comments and whitespace of course).
    /// Note: This example deliberately includes no validation or exception handling in order to keep it as short as possible (i.e. it's easy to break).
    /// </summary>
    static class BasicSend
    {
        public static void RunExample()
        {
            NetworkComms.ListenOnAllAllowedInterfaces = true;

            //Add an incoming packet handler for a 'Message' packet Type. We can also define what we want the handler to do inline by using a lambda expression.
            //This handler will just write the incoming string message to the console window.
            NetworkComms.AppendGlobalIncomingPacketHandler<string>("Message", (header, connectionInfo, message) => { Console.WriteLine("\n  ... Incoming message from " + connectionInfo.ToString() + " saying '" + message  +"'.");});
            
            //Print the ip address and comms port we are listening on.
            //If the ip address has not been auto detected correctly, either
            //  1 - Set the LocalIP property manually before calling NetworkComms.AppendIncomingPacketHandler
            //  2 - Specify some ip prefixs to help the auto detected by setting the NetworkComms.PreferredIPPrefix property
            Console.WriteLine("Listening for messages on {0}:{1} ({2})", NetworkComms.AllAllowedLocalIPs()[0], NetworkComms.DefaultListenPort, NetworkComms.AllAllowedLocalIPs().Count);
            
            //We can loop here to allow any number of test messages to be sent and received
            while (true)
            {
                //Request a message to send somewhere
                Console.WriteLine("\nPlease enter your message and press enter (Type 'exit' to quit):");
                string message = Console.ReadLine();

                //If the user has typed exit then we leave our loop and end the example
                if (message == "exit") break;
                else
                {
                    //Once we have a message we need to know where to send it
                    //Expecting user to enter ip address as 192.168.0.1:4000
                    Console.WriteLine("Please enter the destination IP address and port, e.g 192.168.0.1:4000:");

                    //Parse the provided destination information
                    //If the user entered using a bad format we are going to get an exception
                    //Yes, we can count this as one line ;)
                    string userEnteredStr = Console.ReadLine(); string serverIP = userEnteredStr.Split(':')[0]; int serverPort = int.Parse(userEnteredStr.Split(':')[1]);

                    //Send the message to the provided destination, voila!
                    NetworkComms.SendObject("Message", serverIP, serverPort, false, message);
                }
            }

            //We should always call shutdown on comms if we have used it
            NetworkComms.ShutdownComms();
        }
    }
}
