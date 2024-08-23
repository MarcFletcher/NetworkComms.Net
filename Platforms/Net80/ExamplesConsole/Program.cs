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
using System.Linq;
using System.Text;
using System.Threading;

using NetworkCommsDotNet;
using NetworkCommsDotNet.Tools;

namespace Examples.ExamplesConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.SetBufferSize(120, 200);
                Console.SetWindowSize(120, 25);
            }
            catch (NotImplementedException) { }

            Thread.CurrentThread.Name = "MainThread";

            Console.WriteLine("Initiating NetworkCommsDotNet examples.\n");

            //Ask user if they want to enable comms logging
            SelectLogging();

#if DEBUG
            //Set debug timeouts
            SetDebugTimeouts();
#endif

            //All we do here is let the user choice a specific example
            Console.WriteLine("Please selected an example:\n");

            //Print out the available examples
            int totalNumberOfExamples = 9;
            Console.WriteLine("1 - Basic - Message Send (Only 11 lines!)");
            Console.WriteLine();
            Console.WriteLine("2 - Intermediate - Message Send");
            Console.WriteLine("3 - Intermediate - Peer Discovery");
            Console.WriteLine();
            Console.WriteLine("4 - Advanced - Object Send");
            Console.WriteLine("5 - Advanced - Distributed File System");
            Console.WriteLine("6 - Advanced - Remote Procedure Call");
            Console.WriteLine("7 - Advanced - Unmanaged Connections");
            Console.WriteLine("8 - Advanced - TCP (SSL) Connections");
            Console.WriteLine("");
            Console.WriteLine("9 - Debug - Speed Test");

            //Get the user choice
            Console.WriteLine("");
            int selectedExample;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey().KeyChar.ToString(), out selectedExample);
                if (parseSucces && selectedExample <= totalNumberOfExamples) break;
                Console.WriteLine("\nInvalid example choice. Please try again.");
            }

            //Clear all input so that each example can do it's own thing
            Console.Clear();

            //Run the selected example
            try
            {
                #region Run Example
                switch (selectedExample)
                {
                    case 1:
                        BasicSend.RunExample();
                        break;
                    case 2:
                        IntermediateSend.RunExample();
                        break;
                    case 3:
                        PeerDiscoveryExample.RunExample();
                        break;
                    case 4:
                        AdvancedSend.RunExample();
                        break;
                    case 5:
                        DFSTest.RunExample();
                        break;
                    case 6:
                        RPCExample.RunExample();
                        break;
                    case 7:
                        UnmanagedConnectionExample.RunExample();
                        break;
                    case 8:
                        SSLExample.RunExample();
                        break;
                    case 9:
                        SpeedTest.RunExample();
                        break;
                    default:
                        Console.WriteLine("Selected an invalid example number. Please restart and try again.");
                        break;
                }
                #endregion
            }
            catch (Exception ex)
            {
                //If an error was uncaught by the examples we can log the exception to a file here
                LogTools.LogException(ex, "ExampleError");
                NetworkComms.Shutdown();
                Console.WriteLine(ex.ToString());
            }

            //When we are done we give the user a chance to see all output
            Console.WriteLine("\n\nExample has completed. Please press any key to close.");
            Console.ReadKey(true);
        }

        /// <summary>
        /// Increase default timeouts so that we can easily step through code when running the examples in debug mode.
        /// </summary>
        private static void SetDebugTimeouts()
        {
            NetworkComms.ConnectionEstablishTimeoutMS = int.MaxValue;
            NetworkComms.PacketConfirmationTimeoutMS = int.MaxValue;
            NetworkComms.ConnectionAliveTestTimeoutMS = int.MaxValue;
        }

        private static void SelectLogging()
        {
            //If the user wants to enable logging 
            Console.WriteLine("To enable comms logging press 'y'. To leave logging disabled and continue press any other key.\n");

            if (Console.ReadKey(true).Key == ConsoleKey.Y)
            {
                //Select the logger to use
                Console.WriteLine("Please select the logger to use:\n1 - NetworkComms.Net LiteLogger\n2 - External NLog Logger");

                //Parse the user input for the selected logger
                int selectedLogger;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedLogger);
                    if (parseSucces && selectedLogger <= 2 && selectedLogger > 0) break;
                    Console.WriteLine("Invalid logger choice. Please try again.");
                }

                //Set the desired logger
                ILogger logger;
                if (selectedLogger == 1)
                {
                    Console.WriteLine(" ... selected NetworkComms.Net LiteLogger.\n");

                    //////////////////////////////////////////////////////////////////////
                    //// SIMPLE CONSOLE ONLY LOGGING
                    //////////////////////////////////////////////////////////////////////
                    //ILogger logger = new LiteLogger(LiteLogger.LogMode.ConsoleOnly);
                    //NetworkComms.EnableLogging(logConfig);

                    //////////////////////////////////////////////////////////////////////
                    //// THE FOLLOWING CONFIG LOGS TO BOTH A FILE AND CONSOLE
                    //////////////////////////////////////////////////////////////////////
                    string logFileName = "ExamplesConsoleLog_" + NetworkComms.NetworkIdentifier + ".txt";
                    logger = new LiteLogger(LiteLogger.LogMode.ConsoleAndLogFile, logFileName);
                }
                else if (selectedLogger == 2)
                {
                    Console.WriteLine(" ... selected external NLog logger.\n");

                    //We create an instance of the NLogLogger class which uses a default implementation of 
                    //the ILogger interface provided with NetworkComms.Net
                    logger = new NLogLogger();
                }
                else
                    throw new Exception("Unable to determine selected connection type.");

                //Enable logging using the selected logger
                NetworkComms.EnableLogging(logger);

                //In case we run the DFS test we will also enable logging for that
                DistributedFileSystem.DFS.EnableLogging(logger);

                //We can write to our logger from an external application as well
                NetworkComms.Logger.Info("NetworkComms.Net logging enabled.");
            }
        }
    }
}
