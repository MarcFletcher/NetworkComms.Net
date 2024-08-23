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
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;
using ProtoBuf;
using Newtonsoft.Json;

namespace Examples.ExamplesConsole
{
    /// <summary>
    /// Advanced send demonstrates how to send and receive more complicated objects.  
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
            Console.WriteLine("AdvancedSend Example ...\n");

            //***************************************************************//
            //              Start of interesting stuff                       //
            //***************************************************************//

            Console.WriteLine("\nNOTE: From this point on make sure both clients are configured in the same way if you want the example to work.");

            //Choose between TCP or UDP
            SelectConnectionType();

            //Choose the serialiser and processors which network comms will use
            DataSerializer dataSerializer;
            SelectDataSerializer(out dataSerializer);
            
            List<DataProcessor> dataProcessors;
            Dictionary<string,string> dataProcessorOptions;

            //We cannot select data processors if the NullSerializer was selected
            if (dataSerializer.GetType() != typeof(NullSerializer))
                SelectDataProcessors(out dataProcessors, out dataProcessorOptions);
            else
            {
                dataProcessors = new List<DataProcessor>();
                dataProcessorOptions = new Dictionary<string, string>();
            }

            NetworkComms.DefaultSendReceiveOptions = new SendReceiveOptions(dataSerializer, dataProcessors, dataProcessorOptions);

            //Add a packet handler for dealing with incoming connections.  Function will be called when a packet is received with the specified type.  We also here specify the type of object
            //we are expecting to receive.  In this case we expect an int[] for packet type ArrayTestPacketInt
            NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("ArrayByte",
                (header, connection, array) =>
                {
                    Console.WriteLine("\nReceived byte array from " + connection.ToString());

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
            else if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(BinaryFormaterSerializer))
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<BinaryFormatterCustomObject>("CustomObject",
                                (header, connection, customObject) =>
                                {
                                    Console.WriteLine("\nReceived custom binary formatter object from " + connection);
                                    Console.WriteLine(" ... intValue={0}, stringValue={1}", customObject.IntValue, customObject.StringValue);
                                });
            }
            else 
            {
                NetworkComms.AppendGlobalIncomingPacketHandler<JSONSerializerCustomObject>("CustomObject",
                                (header, connection, customObject) =>
                                {
                                    Console.WriteLine("\nReceived custom JSON object from " + connection);
                                    Console.WriteLine(" ... intValue={0}, stringValue={1}", customObject.IntValue, customObject.StringValue);
                                });
            }

            //Start listening for incoming connections
            //We want to select a random port on all available adaptors so provide 
            //an IPEndPoint using IPAddress.Any and port 0.
            //If we wanted to listen on a specific port we would use that instead of '0'
            Connection.StartListening(connectionTypeToUse, new IPEndPoint(IPAddress.Any ,0));

            //***************************************************************//
            //                End of interesting stuff                       //
            //***************************************************************//

            Console.WriteLine("Listening for incoming objects on:");
            List<EndPoint> localListeningEndPoints = Connection.ExistingLocalListenEndPoints(connectionTypeToUse);
            foreach(IPEndPoint localEndPoint in localListeningEndPoints)
                Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

            Console.WriteLine("\nPress any key if you want to send something from this client. Press q to quit.");

            while (true)
            {
                //Wait for user to press something before sending anything from this end
                var keyContinue = Console.ReadKey(true);
                if (keyContinue.Key == ConsoleKey.Q) break;

                //Create the send object based on user input
                CreateSendObject();

                //Expecting user to enter IP address as 192.168.0.1:4000
                ConnectionInfo connectionInfo = ExampleHelper.GetServerDetails();

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
                    if (toSendObject.GetType().GetElementType() == typeof(byte))
                        connectionToUse.SendObject("ArrayByte", toSendObject);
                    else
                        connectionToUse.SendObject("ArrayString", toSendObject);
                }
                else
                    connectionToUse.SendObject("CustomObject", toSendObject);

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
            Console.WriteLine("\nPlease select a connection type:\n1 - TCP\n2 - UDP\n");

            int selectedType;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedType);
                if (parseSucces && selectedType <= 2 && selectedType > 0) break;
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
            Console.WriteLine("\nPlease select a serializer:\n" +
                "1 - Protobuf (High Performance, Versatile)\n" +
                "2 - BinaryFormatter (Quick to Implement, Very Inefficient)\n" +
                "3 - NullSerializer (High performance pass through serializer for sending byte[] only)\n" +
                "4 - JSON serializer (serializer objects to JSON)");

            int selectedSerializer;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedSerializer);
                if (parseSucces && selectedSerializer <= 4 && selectedSerializer > 0) break;
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
            else if (selectedSerializer == 3)
            {
                Console.WriteLine(" ... selected null serializer.\n");
                dataSerializer = DPSManager.GetDataSerializer<NullSerializer>();
            }
            else if(selectedSerializer == 4)
            {
                Console.WriteLine(" ... selected JSON serializer.\n");
                dataSerializer = DPSManager.GetDataSerializer<JSONSerializer>();
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
                    dataProcessors.Add(DPSManager.GetDataProcessor<NetworkCommsDotNet.DPSBase.SevenZipLZMACompressor.LZMACompressor>());
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

                //Use the nested packet feature so that we do not expose the packet type used.
                NetworkComms.DefaultSendReceiveOptions.UseNestedPacket = true;
                RijndaelPSKEncrypter.AddPasswordToOptions(dataProcessorOptions, password);
                dataProcessors.Add(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());
            }
            #endregion
        }

        /// <summary>
        /// Base method for creating an object to send
        /// </summary>
        private static void CreateSendObject()
        {
            if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(NullSerializer))
            {
                Console.WriteLine("\nThe null serializer was selected so we can only send byte[].");
                toSendType = typeof(Array);
                toSendObject = CreateArray();
            }
            else
            {
                Console.Write("\nPlease select something to send:\n" +
                    "1 - Array of bytes or strings\n");

                if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(ProtobufSerializer))
                    Console.WriteLine("2 - Custom object (Using protobuf). To use binary formatter or JSON select on startup.\n");
                else if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(BinaryFormaterSerializer))
                    Console.WriteLine("2 - Custom object (Using protobuf). To use protobuf or JSON select on startup.\n");
                else
                    Console.WriteLine("2 - Custom object (Using JSON). To use protobuf or binary formatter select on startup.\n");

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
            if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(NullSerializer))
                arrayType = typeof(byte);
            else
            {
                Console.WriteLine("Please select type of array:\n1 - byte[]\n2 - string[]\n");

                int selectedArrayType;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedArrayType);
                    if (parseSucces && selectedArrayType <= 2) break;
                    Console.WriteLine("Invalid type choice. Please try again.");
                }

                if (selectedArrayType == 1)
                {
                    Console.WriteLine(" ... selected array of byte.\n");
                    arrayType = typeof(byte);
                }
                else if (selectedArrayType == 2)
                {
                    Console.WriteLine(" ... selected array of strings.\n");
                    arrayType = typeof(string);
                }
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
                byte byteValue = 0; string stringValue = "";

                while (true)
                {
                    Console.Write(i.ToString() + " - ");
                    bool parseSucces = true;

                    string tempStr = Console.ReadLine();
                    if (arrayType == typeof(byte))
                        parseSucces = byte.TryParse(tempStr, out byteValue);
                    else
                        stringValue = tempStr;

                    if (parseSucces) break;
                    Console.WriteLine("Invalid element value entered. Please try again.");
                }

                if (arrayType == typeof(byte))
                    result.SetValue(byteValue, i);
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
            else if (NetworkComms.DefaultSendReceiveOptions.DataSerializer.GetType() == typeof(BinaryFormaterSerializer))
            {
                BinaryFormatterCustomObject customObject = new BinaryFormatterCustomObject(intValue, stringValue);
                return customObject;
            }
            else
            {
                JSONSerializerCustomObject customObject = new JSONSerializerCustomObject(intValue, stringValue);
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

        /// <summary>
        /// Custom object used when using JSON serialisation
        /// </summary>
        [JsonObject(MemberSerialization.OptIn)]
        private class JSONSerializerCustomObject
        {
            [JsonProperty]
            public int IntValue { get; private set; }
            [JsonProperty]
            public string StringValue { get; private set; }

            private JSONSerializerCustomObject(){}

            /// <summary>
            /// Constructor object for BinaryFormatterCustomObject
            /// </summary>
            /// <param name="intValue"></param>
            /// <param name="stringValue"></param>
            public JSONSerializerCustomObject(int intValue, string stringValue)
            {
                this.IntValue = intValue;
                this.StringValue = stringValue;
            }
        }
        #endregion
    }
}
