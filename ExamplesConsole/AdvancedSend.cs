//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DPSBase;
using NetworkCommsDotNet;
using ProtoBuf;
using System.Collections.Specialized;
using NLog;
using System.Net;
using NLog.Config;
using NLog.Targets;

namespace ExamplesConsole
{
    /// <summary>
    /// Advanced test demonstrates how to send and receive more complicated objects.  
    /// Note that arrays of primitive types are serialised differently from arrays  
    /// of non-primitives. This is done to achieve better performance and lower memory usage                                                                                                      
    /// </summary>
    public static class AdvancedSend
    {
        //Array that will hold the data to be sent
        static Type toSendType;
        static object toSendObject;

        static ConnectionType connectionTypeToUse;

        /// <summary>
        /// Run the AdvancedSend example.
        /// </summary>
        public static void RunExample()
        {
            Console.WriteLine("Launching advanced object send example ...");

            //***************************************************************//
            //              Start of interesting stuff                       //
            //***************************************************************//

            //Ask user if they want to enable comms logging
            SelectLogging();

            Console.WriteLine("\nNOTE: From this point on make sure both clients are configured in the same way if you want the example to work.");

            //Choose between TCP or UDP
            SelectConnectionType();

            //Choose the serialiser and processors which network comms will use
            DataSerializer dataSerializer;
            SelectDataSerializer(out dataSerializer);
            
            List<DataProcessor> dataProcessors;
            Dictionary<string,string> dataProcessorOptions;
            SelectDataProcessors(out dataProcessors, out dataProcessorOptions);

            NetworkComms.DefaultSendReceiveOptions = new SendReceiveOptions(dataSerializer, dataProcessors, dataProcessorOptions);

            SelectListeningPort();

            //Add a packet handler for dealing with incoming connections.  Fuction will be called when a packet is received with the specified type.  We also here specify the type of object
            //we are expecting to receive.  In this case we expect an int[] for packet type ArrayTestPacketInt
            NetworkComms.AppendGlobalIncomingPacketHandler<int[]>("ArrayInt",
                (header, connection, array) =>
                {
                    Console.WriteLine("\nReceived integer array from " + connection.ToString());

                    for (int i = 0; i < array.Length; i++)
                        Console.WriteLine(i.ToString() + " - " + array[i].ToString());
                });

            //As above but this time we expect a string[], and use a different packet type to distinguish the difference 
            NetworkComms.AppendGlobalIncomingPacketHandler<string[]>("ArrayString",
                (header, connection, array) =>
                {
                    Console.WriteLine("\nReceived string array from " + connection);

                    for (int i = 0; i < array.Length; i++)
                        Console.WriteLine(i.ToString() + " - " + array[i]);
                });

            //Our custom object packet handler will be different depending on which serializer we have chosen
            if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(ProtobufSerializer))
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<ProtobufCustomObject>("CustomObject",
                                (header, connection, customObject) =>
                                {
                                    Console.WriteLine("\nReceived custom protobuf object from " + connection);
                                    Console.WriteLine(" ... intValue={0}, stringValue={1}", customObject.IntValue, customObject.StringValue);
                                });
            }
            else
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<BinaryFormatterCustomObject>("CustomObject",
                                (header, connection, customObject) =>
                                {
                                    Console.WriteLine("\nReceived custom binary formatter object from " + connection);
                                    Console.WriteLine(" ... intValue={0}, stringValue={1}", customObject.IntValue, customObject.StringValue);
                                });
            }

            if (connectionTypeToUse == ConnectionType.TCP)
                TCPConnection.StartListening(true);
            else
                UDPConnection.StartListening(true);

            //***************************************************************//
            //                End of interesting stuff                       //
            //***************************************************************//

            Console.WriteLine("Listening for incoming objects on:");
            
            List<IPEndPoint> localListeningEndPoints = (connectionTypeToUse == ConnectionType.TCP ? TCPConnection.ExistingLocalListenEndPoints() : UDPConnection.ExistingLocalListenEndPoints());
            
            foreach(IPEndPoint localEndPoint in localListeningEndPoints)
                Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

            Console.WriteLine("\nPress any key if you want to send something from this client.");

            while (true)
            {
                //Wait for user to press something before sending anything from this end
                Console.ReadKey(true);

                //Create the send object based on user input
                CreateSendObject();

                //Expecting user to enter ip address as 192.168.0.1:4000
                ConnectionInfo connectionInfo;
                ExampleHelper.GetServerDetails(out connectionInfo);

                //***************************************************************//
                //              Start of interesting stuff                       //
                //***************************************************************//

                Connection connectionToUse;

                //Create the connection
                if (connectionTypeToUse == ConnectionType.TCP)
                    connectionToUse = TCPConnection.GetConnection(connectionInfo);
                else
                    connectionToUse = UDPConnection.GetConnection(connectionInfo, UDPOptions.None);

                //Send the object
                if (toSendType == typeof(Array))
                {
                    if (toSendObject.GetType().GetElementType() == typeof(int))
                        connectionToUse.SendObject("ArrayInt", toSendObject);
                    else
                        connectionToUse.SendObject("ArrayString", toSendObject);
                }
                else
                    connectionToUse.SendObject("CustomObject", toSendObject);

                //***************************************************************//
                //                End of interesting stuff                       //
                //***************************************************************//

                Console.WriteLine("\nSend complete. Press 'q' to quit or any other key to send something else.");
                var keyContinue = Console.ReadKey(true);
                if (keyContinue.Key == ConsoleKey.Q) break;
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

        private static void SelectLogging()
        {
            //If the user wants to enable logging 
            Console.WriteLine("\nTo enable comms logging press 'y'. To leave logging disabled and continue press any other key.");
            var loggingEnableKey = Console.ReadKey(true);

            if (loggingEnableKey.Key == ConsoleKey.Y)
            {
                //////////////////////////////////////////////////////////////////////
                /////////////// VERY SIMPLE CONOLSE ONLY LOGGER AVAILABLE ////////////
                //////////////////////////////////////////////////////////////////////
                //NameValueCollection properties = new NameValueCollection();
                //properties["showDateTime"] = "true";
                //properties["showLogName"] = "false";
                //properties["level"] = "All";
                //NetworkComms.EnableLogging(new Common.Logging.Simple.ConsoleOutLoggerFactoryAdapter(properties));

                //////////////////////////////////////////////////////////////////////
                //// THE FOLLOWING LOGGER USES THE MUCH MORE VERSATILE LOG4NET////////
                //// Requires the prescense of Common.Logging.Log4Net.dll & log4net.dll 
                //////////////////////////////////////////////////////////////////////
                LoggingConfiguration logConfig = new LoggingConfiguration();
                FileTarget fileTarget = new FileTarget();
                fileTarget.FileName = "${basedir}/file.txt";
                fileTarget.Layout = "${date:format=HH\\:MM\\:ss} ${logger} ${message}";

                logConfig.AddTarget("file", fileTarget);

                LoggingRule rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
                logConfig.LoggingRules.Add(rule);
                
                NetworkComms.EnableLogging(logConfig);

                Console.WriteLine(" ... logging enabled. DEBUG level ouput and above directed to console. ALL output also directed to log file, log.txt.");

                //We can write to our logger from an external program as well
                NetworkComms.Logger.Info("NetworkCommsDotNet logging enabled");
            }
        }

        private static void SelectConnectionType()
        {
            Console.WriteLine("\nPlease select a connection type:\n1 - TCP\n2 - UDP\n");

            int selectedType;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedType);
                if (parseSucces && selectedType <= 2) break;
                Console.WriteLine("Invalid connection type choice. Please try again.");
            }

            if (selectedType == 1)
            {
                Console.WriteLine(" ... selected TCP.");
                connectionTypeToUse = ConnectionType.TCP;
            }
            else if (selectedType == 2)
            {
                Console.WriteLine(" ... selected UDP.");
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
        /// Allows to choose different serializers
        /// </summary>
        private static void SelectDataSerializer(out DataSerializer dataSerializer)
        {
            Console.WriteLine("\nPlease select a serializer:\n1 - Protobuf (High Performance, Versatile)\n2 - BinaryFormatter (Quick to Implement, Very Inefficient)\n");

            int selectedSerializer;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedSerializer);
                if (parseSucces && selectedSerializer <= 2) break;
                Console.WriteLine("Invalid serializer choice. Please try again.");
            }

            if (selectedSerializer == 1)
            {
                Console.WriteLine(" ... selected protobuf serializer.\n");
                dataSerializer = DPSManager.GetDataSerializer<ProtobufSerializer>();
            }
            else if (selectedSerializer == 2)
            {
                Console.WriteLine(" ... selected binary formatter serializer.\n");
                dataSerializer = DPSManager.GetDataSerializer<BinaryFormaterSerializer>();
            }
            else
                throw new Exception("Unable to determine selected serializer.");
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
                RijndaelPSKEncrypter.AddPasswordToOptions(dataProcessorOptions, password);
                dataProcessors.Add(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());
            }
            #endregion
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
                NetworkComms.DefaultListenPort = selectedPort;
                Console.WriteLine(" ... custom listen port number " + NetworkComms.DefaultListenPort + " has been set.\n");
            }
            else
                Console.WriteLine(" ... default listen port number " + NetworkComms.DefaultListenPort + " of will be used.\n");
        }

        /// <summary>
        /// Base method for creating an object to send
        /// </summary>
        private static void CreateSendObject()
        {
            Console.Write("\nPlease select something to send:\n" +
                "1 - Array of ints or strings\n");

            if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(ProtobufSerializer))
                Console.WriteLine("2 - Custom object (Using protobuf). To use binary formatter select on startup.\n");
            else
                Console.WriteLine("2 - Custom object (Using binary formatter). To use protobuf select on startup.\n");

            int selectedObjectType;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedObjectType);
                if (parseSucces && selectedObjectType <= 2 && selectedObjectType > 0) break;
                Console.WriteLine("Invalid send choice. Please try again.");
            }

            if (selectedObjectType == 1)
            {
                Console.WriteLine(" ... selected array of primitives.\n");
                toSendType = typeof(Array);
                toSendObject = CreateArray();
            }
            else if (selectedObjectType == 2)
            {
                Console.WriteLine(" ... selected custom object.\n");
                toSendType = typeof(object);
                toSendObject = CreateCustomObject();
            }
            else
                throw new Exception("Unable to determine selected send choice.");
        }

        /// <summary>
        /// Set object to send as array of primitives
        /// </summary>
        /// <returns></returns>
        private static Array CreateArray()
        {
            Type arrayType = default(Type);
            Array result = null;

            #region Array Type
            Console.WriteLine("Please select type of array:\n1 - int[]\n2 - string[]\n");

            int selectedArrayType;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedArrayType);
                if (parseSucces && selectedArrayType <= 2) break;
                Console.WriteLine("Invalid type choice. Please try again.");
            }

            if (selectedArrayType == 1)
            {
                Console.WriteLine(" ... selected array of ints.\n");
                arrayType = typeof(int);
            }
            else if (selectedArrayType == 2)
            {
                Console.WriteLine(" ... selected array of strings.\n");
                arrayType = typeof(string);
            }
            #endregion

            #region Number of Elements
            Console.WriteLine("Please enter the number of array elements:");

            int numberOfElements;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadLine(), out numberOfElements);
                if (parseSucces) break;
                Console.WriteLine("Invalid number of elements entered. Please try again.");
            }
            #endregion

            //Create the array that we are going to populate
            result = Array.CreateInstance(arrayType, numberOfElements);

            #region Populate Elements
            Console.WriteLine("\nPlease enter elements:");
            for (int i = 0; i < result.Length; i++)
            {
                int intValue = 0; string stringValue = "";

                while (true)
                {
                    Console.Write(i.ToString() + " - ");
                    bool parseSucces = true;

                    string tempStr = Console.ReadLine();
                    if (arrayType == typeof(int))
                        parseSucces = int.TryParse(tempStr, out intValue);
                    else
                        stringValue = tempStr;

                    if (parseSucces) break;
                    Console.WriteLine("Invalid element value entered. Please try again.");
                }

                if (arrayType == typeof(int))
                    result.SetValue(intValue, i);
                else
                    result.SetValue(stringValue, i);
            }
            #endregion

            //Return the completed array
            return result;
        }

        /// <summary>
        /// Set object to send as custom object
        /// </summary>
        /// <returns></returns>
        private static object CreateCustomObject()
        {
            int intValue = 0;
            string stringValue = "blank";

            //Need user input
            Console.WriteLine("Please enter a number to store in intValue:");
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadLine(), out intValue);
                if (parseSucces) break;
                Console.WriteLine("Invalid value entered. Please try again.");
            }

            Console.WriteLine("\nPlease enter a string to store in stringValue:");
            stringValue = Console.ReadLine();

            if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(ProtobufSerializer))
            {
                ProtobufCustomObject customObject = new ProtobufCustomObject(intValue, stringValue);
                return customObject;
            }
            else
            {
                BinaryFormatterCustomObject customObject = new BinaryFormatterCustomObject(intValue, stringValue);
                return customObject;
            }
        }

        /// <summary>
        /// Custom object used when using protobuf serialisation
        /// </summary>
        [ProtoContract]
        private class ProtobufCustomObject
        {
            [ProtoMember(1)]
            public int IntValue { get; private set; }

            [ProtoMember(2)]
            public string StringValue { get; private set; }

            /// <summary>
            /// Private constructor required for protobuf
            /// </summary>
            private ProtobufCustomObject() { }

            /// <summary>
            /// Constructor object for ProtobufCustomObject
            /// </summary>
            /// <param name="intValue"></param>
            /// <param name="stringValue"></param>
            public ProtobufCustomObject(int intValue, string stringValue)
            {
                this.IntValue = intValue;
                this.StringValue = stringValue;
            }
        }

        /// <summary>
        /// Custom object used when using binary formatter serialisation
        /// </summary>
        [Serializable]
        private class BinaryFormatterCustomObject
        {
            public int IntValue { get; private set; }
            public string StringValue { get; private set; }

            /// <summary>
            /// Constructor object for BinaryFormatterCustomObject
            /// </summary>
            /// <param name="intValue"></param>
            /// <param name="stringValue"></param>
            public BinaryFormatterCustomObject(int intValue, string stringValue)
            {
                this.IntValue = intValue;
                this.StringValue = stringValue;
            }
        }
        #endregion
    }
}
