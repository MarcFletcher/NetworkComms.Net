//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using NetworkCommsDotNet.Connections.Bluetooth;
using NetworkCommsDotNet.Connections;

namespace DebugTests
{
    class BluetoothTest
    {
        static Guid ServiceGUID = new Guid("3a768eea-cbda-4926-a82d-831cb89092aa");
        
        /// <summary>
        /// Run example
        /// </summary>
        public static void RunExample()
        {
            NetworkComms.ConnectionEstablishTimeoutMS = int.MaxValue;

            SendReceiveOptions nullCompressionSRO = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(), null, null);
            NetworkComms.DefaultSendReceiveOptions = nullCompressionSRO;

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

            //Start listenning
            BluetoothRadio defaultRadio = BluetoothRadio.PrimaryRadio;
            defaultRadio.Mode = RadioMode.Discoverable;
            Connection.StartListening(ConnectionType.Bluetooth, new BluetoothEndPoint(defaultRadio.LocalAddress, ServiceGUID), true);

            //Print the address we are listening on to make sure everything
            //worked as expected.
            Console.WriteLine("Listening for messages on:");
            foreach (var localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.Bluetooth))
                Console.WriteLine("{0}", localEndPoint);

            PeerDiscovery.EnableDiscoverable(PeerDiscovery.DiscoveryMethod.BluetoothSDP);

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
                    var endpoints = PeerDiscovery.DiscoverPeers(PeerDiscovery.DiscoveryMethod.BluetoothSDP, 1000);

                    ConnectionInfo targetServerConnectionInfo = new ConnectionInfo(new BluetoothEndPoint(new BluetoothAddress(0xE0B9A5FB552BL), ServiceGUID));

                    //There are loads of ways of sending data (see AdvancedSend example for more)
                    //but the most simple, which we use here, just uses an IP address (string) and port (integer) 
                    //We pull these values out of the ConnectionInfo object we got above and voila!
                    var connection = BluetoothConnection.GetConnection(targetServerConnectionInfo);

                    connection.SendObject("Message", "Hello world");
                }
            }

            //We should always call shutdown on comms if we have used it
            NetworkComms.Shutdown();
        }
    }
}
