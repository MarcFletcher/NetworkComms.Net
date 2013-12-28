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
using DPSBase;
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
                logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, consoleTarget));
                NetworkComms.EnableLogging(logConfig);

                //Incase we run the DFS test we will also enable logging for that
                //DistributedFileSystem.DFS.EnableLogging(logConfig);
            }

            //BasicSend.RunExample();
            //AliveTest.RunExample();
            //DebugTest.GoTCP();
            //DFSTest.RunExample();
            //BandwidthLoadTest.RunExample();
            NumConnectionLoadTest.RunExample();
            //DebugTest.GoStreamTest();
            //ThreadPoolTest.RunExample();

            //LogAnalyser log = new LogAnalyser(@"C:\Users\Karnifexx\Desktop\WALerrors\ExamplesConsoleLog_zibAIsn37EOb4XQ2iLEB_w.txt");
            //log.LinesWithMatch(new string[] { "Received packet of type", "Sending a packet of type ", "Sending a UDP packet" }, "matchLog.txt");
            //log.LinesWithMatch(new string[] { "Completed ChunkAvailabilityReply using data", "Added ChunkAvailabilityReply to chunkDataCache"}, "matchLog.txt");
            //log.LinesWithMatch(new string[] { "written to TCP netstream at" }, "matchLog.txt");
            //log.ThreadPoolInfo("threadPool.csv");
            //log.DataSendReceive(10, "sendReceiveStats.csv");
        }
    }
}
