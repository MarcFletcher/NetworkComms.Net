// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Specialized;
using System.Net;
using System.Linq;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.UDP;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Tools;

namespace Examples.ExamplesConsole
{
    /// <summary>
    /// Advanced test demonstrates how to send and receive more complicated objects.  
    /// Note that arrays of primitive types are serialised differently from arrays  
    /// of non-primitives. This is done to achieve better performance and lower memory usage                                                                                                      
    /// </summary>
    public static class UnmanagedConnectionExample
    {
        /// <summary>
        /// The array that will be sent
        /// </summary>
        static byte[] byteDataToSend;

        /// <summary>
        /// The connection type to use
        /// </summary>
        static ConnectionType connectionTypeToUse;

        /// <summary>
        /// Run the AdvancedSend example.
        /// </summary>
        public static void RunExample()
        {
            Console.WriteLine("Unmanaged Connection Example ...\n");

            //***************************************************************//
            //              Start of interesting stuff                       //
            //***************************************************************//

            Console.WriteLine("\nNOTE: From this point on make sure both clients are configured in the same way if you want the example to work.");

            Console.WriteLine("\nIMPORTANT!! - Many of the features offered by NetworkComms.Net rely on managed connections, "+
                "i.e. those which enable the custom ApplicationLayerProtocol. If you use unmanaged connections, i.e. where the custom "+
                "application protocol has been disabled, you must take into account TCP packet fragmentation and concatenation, "+
                "correctly handling it, for all circumstances.");

            //Choose between unmanaged TCP or UDP
            SelectConnectionType();

            //Add a packet handler for dealing with incoming unmanaged data
            NetworkComms.AppendGlobalIncomingUnmanagedPacketHandler((header, connection, array) =>
                {
                    Console.WriteLine("\nReceived unmanaged byte[] from " + connection.ToString());

                    for (int i = 0; i < array.Length; i++)
                        Console.WriteLine(i.ToString() + " - " + array[i].ToString());
                });

            //Create suitable send receive options for use with unmanaged connections
            SendReceiveOptions optionsToUse = new SendReceiveOptions<NullSerializer>();

            //Get the local IPEndPoints we intend to listen on
            //The port provided is '0' meaning select a random port.
            List<IPEndPoint> localIPEndPoints = (from current in HostInfo.IP.FilteredLocalAddresses()
                                               select new IPEndPoint(current, 0)).ToList();

            //Create suitable listeners depending on the desired connectionType
            List<ConnectionListenerBase> listeners;
            if (connectionTypeToUse == ConnectionType.TCP)
            {
                //For each localIPEndPoint get a TCP listener
                //We need to set the ApplicationLayerProtocolStatus to Disabled
                listeners = (from current in localIPEndPoints
                             select (ConnectionListenerBase)new TCPConnectionListener(optionsToUse, ApplicationLayerProtocolStatus.Disabled)).ToList();
            }
            else
            {
                //We need to set the ApplicationLayerProtocolStatus to Disabled
                listeners = (from current in localIPEndPoints
                             select (ConnectionListenerBase)new UDPConnectionListener(optionsToUse, ApplicationLayerProtocolStatus.Disabled, UDPConnection.DefaultUDPOptions)).ToList();
            }

            //Start listening
            Connection.StartListening(listeners, localIPEndPoints, true);

            //***************************************************************//
            //                End of interesting stuff                       //
            //***************************************************************//

            Console.WriteLine("Listening for incoming byte[] on:");
            List<EndPoint> localListeningEndPoints = Connection.ExistingLocalListenEndPoints(connectionTypeToUse);
            foreach (IPEndPoint localEndPoint in localListeningEndPoints)
                Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

            Console.WriteLine("\nPress any key if you want to send data from this client. Press q to quit.");

            while (true)
            {
                //Wait for user to press something before sending anything from this end
                var keyContinue = Console.ReadKey(true);
                if (keyContinue.Key == ConsoleKey.Q) break;

                //Create the send object based on user input
                byteDataToSend = CreateSendArray();

                //Get remote endpoint address
                //Expecting user to enter IP address as 192.168.0.1:4000
                ConnectionInfo connectionInfo = ExampleHelper.GetServerDetails(ApplicationLayerProtocolStatus.Disabled);

                //***************************************************************//
                //              Start of interesting stuff                       //
                //***************************************************************//

                Connection connectionToUse;

                //Create the connection
                if (connectionTypeToUse == ConnectionType.TCP)
                    connectionToUse = TCPConnection.GetConnection(connectionInfo, optionsToUse);
                else
                    connectionToUse = UDPConnection.GetConnection(connectionInfo, UDPOptions.None, optionsToUse);

                //Send the object
                connectionToUse.SendUnmanagedBytes(byteDataToSend);

                //***************************************************************//
                //                End of interesting stuff                       //
                //***************************************************************//

                Console.WriteLine("\nSend complete. Press 'q' to quit or any other key to send something else.");
            }

            //***************************************************************//
            //              Start of interesting stuff                       //
            //***************************************************************//

            //Make sure you call shutdown when finished to clean up.
            NetworkComms.Shutdown();

            //***************************************************************//
            //                End of interesting stuff                       //
            //***************************************************************//
        }

        #region Customisation Methods

        private static void SelectConnectionType()
        {
            Console.WriteLine("\nPlease select a connection type:\n1 - Unmanaged TCP\n2 - Unmanaged UDP\n");

            int selectedType;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedType);
                if (parseSucces && selectedType <= 2) break;
                Console.WriteLine("Invalid connection type choice. Please try again.");
            }

            if (selectedType == 1)
            {
                Console.WriteLine(" ... selected TCP.\n");
                connectionTypeToUse = ConnectionType.TCP;
            }
            else if (selectedType == 2)
            {
                Console.WriteLine(" ... selected UDP.\n");
                connectionTypeToUse = ConnectionType.UDP;
            }
            else
                throw new Exception("Unable to determine selected connection type.");
        }

        /// <summary>
        /// Delegate which can be used to log comms method
        /// </summary>
        /// <param name="strToLog"></param>
        private static void LogMethod(string strToLog)
        {
            Console.WriteLine("    - " + strToLog);
        }

        /// <summary>
        /// Set object to send as array of primitives
        /// </summary>
        /// <returns></returns>
        private static byte[] CreateSendArray()
        {
            byte[] result;

            #region Number of Elements
            Console.WriteLine("Please enter the number of byte[] elements to send:");

            int numberOfElements;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadLine(), out numberOfElements);
                if (parseSucces) break;
                Console.WriteLine("Invalid number of elements entered. Please try again.");
            }
            #endregion

            //Create the array that we are going to populate
            result = new byte[numberOfElements];

            #region Populate Elements
            Console.WriteLine("\nPlease enter a valid byte element:");
            for (int i = 0; i < result.Length; i++)
            {
                byte byteValue = 0; 

                while (true)
                {
                    Console.Write(i.ToString() + " - ");
                    bool parseSucces = true;

                    string tempStr = Console.ReadLine();
                    parseSucces = byte.TryParse(tempStr, out byteValue);

                    if (parseSucces) break;
                    Console.WriteLine("Invalid element value entered. Please try again.");
                }

                result.SetValue(byteValue, i);
            }
            #endregion

            //Return the completed array
            return result;
        }
        #endregion
    }
}
