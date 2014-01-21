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
using System.Text;
using DPSBase;
using NetworkCommsDotNet;
using ProtoBuf;
using System.Collections.Specialized;
using System.Net;
using System.Linq;

namespace ExamplesConsole
{
    /// <summary>
    /// Advanced test demonstrates how to send and receive more complicated objects.  
    /// Note that arrays of primitive types are serialised differently from arrays  
    /// of non-primitives. This is done to achieve better performance and lower memory usage                                                                                                      
    /// </summary>
    public static class UnmanagedConnection
    {
        //Array that will hold the data to be sent
        static byte[] byteDataToSend;

        static ConnectionType connectionTypeToUse;

        /// <summary>
        /// A custom listen port if it is selected. 
        /// If this remains unchanged, i.e. 0, a random port will be selected when listening.
        /// </summary>
        static int customListenPort = 0;

        /// <summary>
        /// Run the AdvancedSend example.
        /// </summary>
        public static void RunExample()
        {
            Console.WriteLine("Launching unmanaged connection example ...");

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

            //Possibly change the default local listening port
            SelectListeningPort();

            //Add a packet handler for dealing with incoming connections.  Function will be called when a packet is received with the specified type.  We also here specify the type of object
            //we are expecting to receive.  In this case we expect an int[] for packet type ArrayTestPacketInt
            NetworkComms.AppendGlobalIncomingUnmanagedPacketHandler((header, connection, array) =>
                {
                    Console.WriteLine("\nReceived unmanaged byte[] from " + connection.ToString());

                    for (int i = 0; i < array.Length; i++)
                        Console.WriteLine(i.ToString() + " - " + array[i].ToString());
                });

            //Create suitable send receive options
            SendReceiveOptions optionsToUse = new SendReceiveOptions<NullSerializer>();

            //Get the local IPEndPoints we intend to listen on
            List<EndPoint> localIPEndPoints = (from current in HostInfo.FilteredLocalIPAddresses()
                                               select ((EndPoint)new IPEndPoint(current, customListenPort))).ToList();

            //Create suitable listeners
            List<ConnectionListenerBase> listeners;
            if (connectionTypeToUse == ConnectionType.TCP)
            {
                //For each localIPEndPoint get a TCP listener
                listeners = (from current in localIPEndPoints
                             select (ConnectionListenerBase)new TCPConnectionListener(optionsToUse, ApplicationLayerProtocolStatus.Disabled)).ToList();
            }
            else
            {
                listeners = (from current in localIPEndPoints
                             select (ConnectionListenerBase)new UDPConnectionListener(optionsToUse, ApplicationLayerProtocolStatus.Disabled, UDPConnection.DefaultUDPOptions)).ToList();
            }

            //Start listening
            Connection.StartListening(listeners, localIPEndPoints, true);

            //***************************************************************//
            //                End of interesting stuff                       //
            //***************************************************************//

            Console.WriteLine("Listening for incoming byte[] on:");

            List<EndPoint> localListeningEndPoints = (connectionTypeToUse == ConnectionType.TCP ? Connection.ExistingLocalListenEndPoints(ConnectionType.TCP) : Connection.ExistingLocalListenEndPoints(ConnectionType.UDP));

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
                //Expecting user to enter ip address as 192.168.0.1:4000
                //IMPORTANT: The false provided here disables the application layer protocol
                //for outgoing connections which is what we want for this example.
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
                connectionToUse.SendObject("Unmanaged", byteDataToSend);

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
        /// Allows to choose a custom listen port
        /// </summary>
        private static void SelectListeningPort()
        {
            Console.WriteLine("Would you like to specify a custom local listen port?:\n1 - No\n2 - Yes\n");

            int selectedOption;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedOption);
                if (parseSucces && selectedOption <= 2 && selectedOption > 0) break;
                Console.WriteLine("Invalid choice. Please try again.");
            }

            if (selectedOption == 2)
            {
                Console.WriteLine(" ... selected custom local listen port. Please enter your chosen port number:");
                int selectedPort;

                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadLine(), out selectedPort);
                    if (parseSucces && selectedPort > 0) break;
                    Console.WriteLine("Invalid choice. Please try again.");
                }

                //Change the port comms will listen on
                customListenPort = selectedPort;
                Console.WriteLine(" ... custom listen port number " + customListenPort + " has been set.\n");
            }
            else
                Console.WriteLine(" ... a random listen port will be used.\n");
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

        /// <summary>
        /// Allows to choose different compressors
        /// </summary>
        private static void SelectDataProcessors(out List<DataProcessor> dataProcessors, out Dictionary<string, string> dataProcessorOptions)
        {
            dataProcessors = new List<DataProcessor>();
            dataProcessorOptions = new Dictionary<string, string>();

            #region Possible Compressor
            Console.WriteLine("Would you like to include data compression?\n1 - No\n2 - Yes\n");

            int includeCompression;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out includeCompression);
                if (parseSucces && includeCompression <= 2 && includeCompression > 0) break;
                Console.WriteLine("Invalid choice. Please try again.");
            }

            if (includeCompression == 2)
            {
                Console.WriteLine("Please select a compressor:\n1 - LZMA (Slow Speed, Best Compression)\n2 - GZip (Good Speed, Good Compression)\n3 - QuickLZ (Best Speed, Basic Compression)\n");

                int selectedCompressor;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedCompressor);
                    if (parseSucces && selectedCompressor <= 3 && selectedCompressor > 0) break;
                    Console.WriteLine("Invalid compressor choice. Please try again.");
                }

                if (selectedCompressor == 1)
                {
                    Console.WriteLine(" ... selected LZMA compressor.\n");
                    dataProcessors.Add(DPSManager.GetDataProcessor<SevenZipLZMACompressor.LZMACompressor>());
                }
                else if (selectedCompressor == 2)
                {
                    Console.WriteLine(" ... selected GZip compressor.\n");
                    dataProcessors.Add(DPSManager.GetDataProcessor<SharpZipLibCompressor.SharpZipLibGzipCompressor>());
                }
                else if (selectedCompressor == 3)
                {
                    Console.WriteLine(" ... selected QuickLZ compressor.\n");
                    dataProcessors.Add(DPSManager.GetDataProcessor<QuickLZCompressor.QuickLZ>());
                }
                else
                    throw new Exception("Unable to determine selected compressor.");
            }
            #endregion

            #region Possible Encryption
            Console.WriteLine("Would you like to include data encryption?\n1 - No\n2 - Yes\n");

            int includeEncryption;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out includeEncryption);
                if (parseSucces && includeEncryption <= 2 && includeEncryption > 0) break;
                Console.WriteLine("Invalid choice. Please try again.");
            }

            if (includeEncryption == 2)
            {
                Console.Write("Please enter an encryption password and press enter: ");
                string password = Console.ReadLine();
                Console.WriteLine();
                RijndaelPSKEncrypter.AddPasswordToOptions(dataProcessorOptions, password);
                dataProcessors.Add(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());
            }
            #endregion
        }
        #endregion
    }
}
