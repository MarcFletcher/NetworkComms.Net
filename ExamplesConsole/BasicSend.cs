using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;

namespace ExamplesConsole
{
    /// <summary>
    /// Networking in only 11 lines (not including comments and whitespace of course).
    /// Note: This example deliberately includes no validation or exception handling in order to keep it as short as possible (i.e. it's easy to break).
    /// </summary>
    public static class BasicSend
    {
        /// <summary>
        /// Run example
        /// </summary>
        public static void RunExample()
        {
            //Add an incoming packet handler for a 'Message' packet Type. We can also define what we want the handler to do inline by using a lambda expression.
            //This handler will just write the incoming string message to the console window.
            NetworkComms.AppendGlobalIncomingPacketHandler<string>("Message", (packetHeader, connection, message) => { Console.WriteLine("\n  ... Incoming message from " + connection.ToString() + " saying '" + message + "'."); });

            //Add a 'TCP' listener so that incoming connections can be accepted. See also UDPConnection.StartListening()
            TCPConnection.StartListening(true);

            //Print the IP addresses and ports we are listening on.
            Console.WriteLine("Listening for messages on:");
            foreach (System.Net.IPEndPoint localEndPoint in TCPConnection.ExistingLocalListenEndPoints()) Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

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
                    Console.WriteLine("Please enter the destination IP address and press enter, e.g '192.168.0.1'");

                    //Parse the provided destination information
                    //If the user entered using a bad format we are going to get an exception
                    string ipAddressStr = Console.ReadLine();

                    //Send the message to the provided destination, voila!
                    NetworkComms.SendObject("Message", ipAddressStr, message);
                }
            }

            //We should always call shutdown on comms if we have used it
            NetworkComms.Shutdown();
        }
    }
}
