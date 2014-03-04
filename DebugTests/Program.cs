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
using NetworkCommsDotNet;
using System.Collections.Specialized;
using NLog;
using System.Threading;
using System.IO;
using NLog.Config;
using NLog.Targets;
using System.Net;
using System.Diagnostics;
using NetworkCommsDotNet.DPSBase;
using DistributedFileSystem;
using ProtoBuf;

namespace DebugTests
{
    class Program
    {      
        static void Main(string[] args)
        {
            try
            {
                Console.SetBufferSize(160, 500);
                Console.SetWindowSize(160, 25);
            }
            catch (Exception) { }

            Thread.CurrentThread.Name = "MainThread";

            if (false)
            {
                LoggingConfiguration logConfig = new LoggingConfiguration();
                FileTarget fileTarget = new FileTarget();
                fileTarget.FileName = "${basedir}/DebugTests_" + NetworkComms.NetworkIdentifier + ".txt";
                fileTarget.Layout = "${date:format=HH\\:mm\\:ss} [${threadid} - ${level}] - ${message}";
                ConsoleTarget consoleTarget = new ConsoleTarget();
                consoleTarget.Layout = "${date:format=HH\\:mm\\:ss} - ${message}";

                logConfig.AddTarget("file", fileTarget);
                logConfig.AddTarget("console", consoleTarget);

                logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, fileTarget));
                //logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, consoleTarget));
                NetworkComms.EnableLogging(logConfig);

                //Incase we run the DFS test we will also enable logging for that
                //DistributedFileSystem.DFS.EnableLogging(logConfig);
            }

            DebugTest.RunExample();
            //DebugTest.Test();
            //BasicSend.RunExample();
            //AliveTest.RunExample();
            //DebugTest.RunExample();
            //DFSTest.RunExample();
            //BandwidthLoadTest.RunExample();
            //BluetoothTest.RunExample();
            //NumConnectionLoadTest.RunExample();
            //ThreadPoolTest.RunExample();
            //SSLTest.RunExample();
            //NestedPacketTest.RunExample();
            //PeerDiscoveryTest.RunExample();
            //SelfConnectTest.RunExample();

            //LogAnalyser log = new LogAnalyser(@"C:\Users\Karnifexx\Documents\Visual Studio 2010\Projects\networkcomms.net\DebugTests\bin\Debug\DebugTests_9GvNYKHC1E-cDqu8qTneew.txt");
            //log.LinesWithMatch(new string[] { "Completed adding connection reference by endPoint" }, "matchLog.txt");
            //log.LinesWithMatch(new string[] { "Completed ChunkAvailabilityReply using data", "Added ChunkAvailabilityReply to chunkDataCache"}, "matchLog.txt");
            //log.LinesWithMatch(new string[] { "written to TCP netstream at" }, "matchLog.txt");
            //log.ThreadPoolInfo("threadPool.csv");
            //log.DataSendReceive(10, "sendReceiveStats.csv");
        }
    }
}
