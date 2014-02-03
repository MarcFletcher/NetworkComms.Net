//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
using System.Threading;

using NLog;
using NLog.Config;
using NLog.Targets;
using NetworkCommsDotNet;

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
                NetworkComms.LogError(ex, "ExampleError");
                NetworkComms.Shutdown();
                Console.WriteLine(ex.ToString());
            }

            //When we are done we give the user a chance to see all output
            Console.WriteLine("\n\nExample has completed. Please press any key to close.");
            Console.ReadKey(true);
        }

        private static void SelectLogging()
        {
            //If the user wants to enable logging 
            Console.WriteLine("To enable comms logging press 'y'. To leave logging disabled and continue press any other key.\n");

            if (Console.ReadKey(true).Key == ConsoleKey.Y)
            {
                //////////////////////////////////////////////////////////////////////
                //// SIMPLE CONSOLE ONLY LOGGING
                //// See http://nlog-project.org/ for more information
                //// Requires that the file NLog.dll is present 
                //////////////////////////////////////////////////////////////////////
                //LoggingConfiguration logConfig = new LoggingConfiguration();
                //ConsoleTarget consoleTarget = new ConsoleTarget();
                //consoleTarget.Layout = "${date:format=HH\\:mm\\:ss} [${level}] - ${message}";
                //logConfig.AddTarget("console", consoleTarget);
                //logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, consoleTarget));
                //NetworkComms.EnableLogging(logConfig);

                //////////////////////////////////////////////////////////////////////
                //// THE FOLLOWING CONFIG LOGS TO BOTH A FILE AND CONSOLE
                //// See http://nlog-project.org/ for more information
                //// Requires that the file NLog.dll is present 
                //////////////////////////////////////////////////////////////////////
                LoggingConfiguration logConfig = new LoggingConfiguration();
                FileTarget fileTarget = new FileTarget();
                fileTarget.FileName = "${basedir}/ExamplesConsoleLog_"+NetworkComms.NetworkIdentifier+".txt";
                fileTarget.Layout = "${date:format=HH\\:mm\\:ss\\:fff} [${threadid} - ${level}] - ${message}";
                ConsoleTarget consoleTarget = new ConsoleTarget();
                consoleTarget.Layout = "${date:format=HH\\:mm\\:ss} - ${message}";

                logConfig.AddTarget("file", fileTarget);
                logConfig.AddTarget("console", consoleTarget);

                logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, fileTarget));
                logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, consoleTarget));
                NetworkComms.EnableLogging(logConfig);

                //Incase we run the DFS test we will also enable logging for that
                DistributedFileSystem.DFS.EnableLogging(logConfig);

                //We can write to our logger from an external program as well
                NetworkComms.Logger.Info("NetworkCommsDotNet logging enabled. DEBUG level ouput and above directed to console. ALL output also directed to log file, ExamplesConsoleLog_" + NetworkComms.NetworkIdentifier + ".txt." + Environment.NewLine);
            }
        }
    }
}
