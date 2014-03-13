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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using DistributedFileSystem;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

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
                ILogger logger = new LiteLogger(LiteLogger.LogMode.ConsoleAndLogFile, "DebugTests_" + NetworkComms.NetworkIdentifier + ".txt");
                NetworkComms.EnableLogging(logger);

                //Incase we run the DFS test we will also enable logging for that
                DistributedFileSystem.DFS.EnableLogging(logger);
            }

            NetworkComms.EnablePacketCheckSumValidation = true;

            DebugTest.RunExample();
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

            //LogAnalyser log = new LogAnalyser(@"log.txt");
            //log.LinesWithMatch(new string[] { "Waiting for client connnectionInfo " }, "matchLog.txt");
            //log.ThreadPoolInfo("threadPool.csv");
            //log.DataSendReceive(10, "sendReceiveStats.csv");
        }
    }
}
