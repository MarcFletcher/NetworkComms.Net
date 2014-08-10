//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
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
