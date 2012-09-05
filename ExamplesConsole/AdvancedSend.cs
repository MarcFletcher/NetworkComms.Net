//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DPSBase;
using NetworkCommsDotNet;
using ProtoBuf;
using System.Collections.Specialized;
using Common.Logging.Log4Net;

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

        public static void RunExample()
        {
            Console.WriteLine("Launching advanced object send example ...");

            //***************************************************************//
            //              Start of interesting stuff                       //
            //***************************************************************//

            //Ask user if they want to enable comms logging
            SelectLogging();

            //Set the default serialiser and compressor for network comms to use
            Console.WriteLine("\nNOTE: Make sure both clients are configured in the same way if you want this to work.");
            NetworkComms.DefaultSerializer = SelectDataSerializer();
            NetworkComms.DefaultCompressor = SelectDataProcessors();

            SelectListeningPort();

            //Add a packet handler for dealing with incoming connections.  Fuction will be called when a packet is received with the specified type.  We also here specify the type of object
            //we are expecting to receive.  In this case we expect an int[] for packet type ArrayTestPacketInt
            NetworkComms.AppendIncomingPacketHandler<int[]>("ArrayInt",
                (header, conectionId, array) =>
                {
                    Console.WriteLine("\nReceived integer array from " + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientIP + ":" + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientPort);

                    for (int i = 0; i < array.Length; i++)
                        Console.WriteLine(i.ToString() + " - " + array[i].ToString());
                });

            //As above but this time we expect a string[], and use a different packet type to distinguish the difference 
            NetworkComms.AppendIncomingPacketHandler<string[]>("ArrayString",
                (header, conectionId, array) =>
                {
                    Console.WriteLine("\nReceived string array from " + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientIP + ":" + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientPort);

                    for (int i = 0; i < array.Length; i++)
                        Console.WriteLine(i.ToString() + " - " + array[i]);
                });

            //Our custom object packet handler will be different depending on which serializer we have chosen
            if (NetworkComms.DefaultSerializer.GetType() == typeof(SerializerBase.Protobuf.ProtobufSerializer))
            {
                NetworkComms.AppendIncomingPacketHandler<ProtobufCustomObject>("CustomObject",
                                (header, conectionId, customObject) =>
                                {
                                    Console.WriteLine("\nReceived custom protobuf object from " + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientIP + ":" + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientPort);
                                    Console.WriteLine(" ... intValue={0}, stringValue={1}", customObject.IntValue, customObject.StringValue);
                                });
            }
            else
            {
                NetworkComms.AppendIncomingPacketHandler<BinaryFormatterCustomObject>("CustomObject",
                                (header, conectionId, customObject) =>
                                {
                                    Console.WriteLine("\nReceived custom binary formatter object from " + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientIP + ":" + NetworkComms.ConnectionIdToConnectionInfo(conectionId).ClientPort);
                                    Console.WriteLine(" ... intValue={0}, stringValue={1}", customObject.IntValue, customObject.StringValue);
                                });
            }

            //***************************************************************//
            //                End of interesting stuff                       //
            //***************************************************************//

            Console.WriteLine("Listening for incoming objects on " + NetworkComms.LocalIP + ":" + NetworkComms.CommsPort.ToString() + "." +
                "\nPress any key if you want to send something from this client.");

            while (true)
            {
                //Wait for user to press something before sending anything from this end
                Console.ReadKey(true);

                //Create the send object based on user input
                CreateSendObject();

                //Expecting user to enter ip address as 192.168.0.1:4000
                string serverIP; int serverPort;
                ExampleHelper.GetServerDetails(out serverIP, out serverPort);

                //***************************************************************//
                //              Start of interesting stuff                       //
                //***************************************************************//

                //Send the object
                if (toSendType == typeof(Array))
                {
                    if (toSendObject.GetType().GetElementType() == typeof(int))
                        NetworkComms.SendObject("ArrayInt", serverIP, serverPort, false, toSendObject);
                    else
                        NetworkComms.SendObject("ArrayString", serverIP, serverPort, false, toSendObject);
                }
                else
                    NetworkComms.SendObject("CustomObject", serverIP, serverPort, false, toSendObject);

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
            NetworkComms.ShutdownComms();

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
                NameValueCollection properties = new NameValueCollection();
                properties["configType"] = "FILE";
                properties["configFile"] = "log4net.config";
                NetworkComms.EnableLogging(new Log4NetLoggerFactoryAdapter(properties));

                Console.WriteLine(" ... logging enabled. DEBUG level ouput and above directed to console. ALL output also directed to log file, log.txt.");

                //We can write to our logger from an external program as well
                NetworkComms.Logger.Info("networkComms.net logging enabled");
            }
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
        private static DataSerializer SelectDataSerializer()
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
                return SerializerBase.Protobuf.ProtobufSerializer.Instance;
            }
            else if (selectedSerializer == 2)
            {
                Console.WriteLine(" ... selected binary formatter serializer.\n");
                return SerializerBase.BinaryFormaterSerializer.Instance;
            }
            else
                throw new Exception("Unable to determine selected serializer.");
        }

        /// <summary>
        /// Allows to choose different compressors
        /// </summary>
        private static List<DataProcessor> SelectDataProcessors()
        {
            Console.WriteLine("Please select a compressor:\n1 - None\n2 - LZMA (Slow Speed, Best Compression)\n3 - GZip (Good Speed, Good Compression)\n4 - QuickLZ (Best Speed, Basic Compression)\n");

            int selectedCompressor;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedCompressor);
                if (parseSucces && selectedCompressor <= 4 && selectedCompressor > 0) break;
                Console.WriteLine("Invalid compressor choice. Please try again.");
            }

            if (selectedCompressor == 1)
            {
                Console.WriteLine(" ... selected null compressor.\n");
                return SerializerBase.NullCompressor.Instance;
            }
            else if (selectedCompressor == 2)
            {
                Console.WriteLine(" ... selected LZMA compressor.\n");
                return SevenZipLZMACompressor.LZMACompressor.Instance;
            }
            else if (selectedCompressor == 3)
            {
                Console.WriteLine(" ... selected GZip compressor.\n");
                return SharpZipLibCompressor.SharpZipLibGzipCompressor.Instance;
            }
            else if (selectedCompressor == 4)
            {
                Console.WriteLine(" ... selected QuickLZ compressor.\n");
                return QuickLZCompressor.QuickLZ.Instance;
            }
            else
                throw new Exception("Unable to determine selected compressor.");
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

                NetworkComms.CommsPort = selectedPort;
                Console.WriteLine(" ... custom listen port number " + NetworkComms.CommsPort + " has been set.\n");
            }
            else
                Console.WriteLine(" ... default listen port number "+NetworkComms.CommsPort+" of will be used.\n");
        }

        /// <summary>
        /// Base method for creating an object to send
        /// </summary>
        private static void CreateSendObject()
        {
            Console.Write("\nPlease select something to send:\n" +
                "1 - Array of ints or strings)\n");

            if (NetworkComms.DefaultSerializer.GetType() == typeof(SerializerBase.Protobuf.ProtobufSerializer))
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

            if (NetworkComms.DefaultSerializer.GetType() == typeof(SerializerBase.Protobuf.ProtobufSerializer))
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
            /// <param name="listValue"></param>
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
            /// Constructor object for ProtobufCustomObject
            /// </summary>
            /// <param name="intValue"></param>
            /// <param name="stringValue"></param>
            /// <param name="listValue"></param>
            public BinaryFormatterCustomObject(int intValue, string stringValue)
            {
                this.IntValue = intValue;
                this.StringValue = stringValue;
            }
        }
        #endregion
    }
}
