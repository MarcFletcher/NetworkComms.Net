using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace DebugTests
{
    static class SelfConnectTest
    {
        public static void RunExample()
        {
            NetworkComms.ConnectionEstablishTimeoutMS = 10000000;

            NetworkComms.AppendGlobalIncomingPacketHandler<string>("Message", (packetHeader, connection, incomingString) => 
            { 
                Console.WriteLine("\n  ... Incoming message from " + connection.ToString() + " saying '" + incomingString + "'."); 
            });

            //Start listening for incoming 'TCP' connections.
            Connection.StartListening(ConnectionType.TCP, IPTools.ParseEndPointFromString("127.0.0.1:10000"));

            //Send a message which will attempt to connect to local
            TCPConnection.GetConnection(new ConnectionInfo(IPTools.ParseEndPointFromString("127.0.0.1:10000"))).SendObject("Message", "hello!");

            Console.ReadKey();

            //Send a message which will attempt to connect to local
            TCPConnection.GetConnection(new ConnectionInfo(IPTools.ParseEndPointFromString("127.0.0.1:10000"))).SendObject("Message", "hello2!");

            Console.ReadKey();
        }
    }
}
