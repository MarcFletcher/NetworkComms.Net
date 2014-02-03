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

namespace Examples.ExamplesConsole
{
    /// <summary>
    /// Networking in only 11 lines (not including comments and white space of course).
    /// Note: This example deliberately includes no validation or exception handling in order to keep it as short as possible (i.e. it's easy to break).
    /// </summary>
    public static class BasicSend
    {
        /// <summary>
        /// Run example
        /// </summary>
        public static void RunExample()
        {
            //We need to define what happens when packets are received.
            //To do this we add an incoming packet handler for a 'Message' packet type. 
            //
            //We will define what we want the handler to do inline by using a lambda expression
            //http://msdn.microsoft.com/en-us/library/bb397687.aspx.
            //We could also just point the AppendGlobalIncomingPacketHandler method 
            //to a standard method (See AdvancedSend example)
            //
            //This handler will convert the incoming raw bytes into a string (this is what 
            //the <string> bit means) and then write that string to the local console window.
            NetworkComms.AppendGlobalIncomingPacketHandler<string>("Message", (packetHeader, connection, incomingString) => { Console.WriteLine("\n  ... Incoming message from " + connection.ToString() + " saying '" + incomingString + "'."); });

            //Start listening for incoming 'TCP' connections.
            //We want to select a random port on all available adaptors so provide 
            //an IPEndPoint using IPAddress.Any and port 0.
            //See also Connection.StartListening(ConnectionType.UDP, IPEndPoint)
            Connection.StartListening(ConnectionType.TCP, new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0));

            //Print the IP addresses and ports we are listening on to make sure everything
            //worked as expected.
            Console.WriteLine("Listening for TCP messages on:");
            foreach (System.Net.IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP)) Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

            //We loop here to allow any number of test messages to be sent and received
            while (true) 
            {
                //Request a message to send somewhere
                Console.WriteLine("\nPlease enter your message and press enter (Type 'exit' to quit):");
                string stringToSend = Console.ReadLine();

                //If the user has typed exit then we leave our loop and end the example
                if (stringToSend == "exit") break;
                else 
                {
                    //Once we have a message we need to know where to send it
                    //We have created a small wrapper class to help keep things clean here
                    ConnectionInfo targetServerConnectionInfo = ExampleHelper.GetServerDetails();

                    //There are loads of ways of sending data (see AdvancedSend example for more)
                    //but the most simple, which we use here, just uses an IP address (string) and port (integer) 
                    //We pull these values out of the ConnectionInfo object we got above and voila!
                    NetworkComms.SendObject("Message", ((System.Net.IPEndPoint)targetServerConnectionInfo.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)targetServerConnectionInfo.RemoteEndPoint).Port, stringToSend);
                }
            }

            //We should always call shutdown on NetworkComms.Net if we have used it
            NetworkComms.Shutdown();
        }
    }
}
