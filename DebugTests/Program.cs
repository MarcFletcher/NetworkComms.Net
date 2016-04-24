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

            //NetworkComms.EnablePacketCheckSumValidation = true;

            DebugTest.RunExample(args);
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
            //SymEncryptionTest.RunExample();
            //UnmanagedUDPBroadcast.RunExample();

            //LogAnalyser log = new LogAnalyser(@"C:\Users\Karnifexx\Documents\Visual Studio 2010\Projects\networkcomms.net\DebugTests\bin\Debug\DebugTests_e-P76M-6LkSFyUFFlnU0qA.txt");
            //log.LinesWithMatch(new string[] { "Received packet of type 'ConnectionSetup'" }, "matchLog4.txt");
            //log.ThreadPoolInfo("threadPool.csv");
            //log.DataSendReceive(10, "sendReceiveStats.csv");
        }
    }
}
