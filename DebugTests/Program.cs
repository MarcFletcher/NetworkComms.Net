using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Collections.Specialized;
using Common.Logging.Simple;
using Common.Logging;
using Common.Logging.Log4Net;
using log4net.Repository;
using log4net.Layout;
using log4net.Appender;
using System.Threading;
using System.IO;
using DistributedFileSystem;

namespace DebugTests
{
    class Program
    {      
        static void Main(string[] args)
        {
            try
            {
                Console.SetBufferSize(130, 500);
                Console.SetWindowSize(130, 25);
            }
            catch (Exception) { }

            Thread.CurrentThread.Name = "MainThread";

            //Configure the logger here
            //NameValueCollection properties = new NameValueCollection();
            //properties["configType"] = "FILE";
            //properties["configFile"] = "log4net.config";
            //DFS.EnableLogging(new Log4NetLoggerFactoryAdapter(properties));

            //BasicSend.RunExample();
            //AliveTest.RunExample();
            //DebugTest.Go();
            //DFSTest.RunExample();

            NetworkComms.PreferredIPPrefix = new string[] { "131", "172" };

            DFS.InitialiseDFS();
            DFS.InitialiseDFSLink("131.111.73.213", 2004, DFSLinkMode.LinkAndRepeat);

            if (NetworkComms.HostName.StartsWith("gpu-s1") || NetworkComms.HostName.StartsWith("gpu-s2"))
            {
                Console.WriteLine("Detected gpu-s1/s2, increasing InterfaceLinkSpeed to 1Gbps");
                NetworkComms.InterfaceLinkSpeed = 1000000000;
            }

            while (true)
            {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " - " + DFS.AllLocalDFSItemKeys(true).Length + " (" + DFS.AllLocalDFSItemKeys(false).Length + ") Items. " + DFS.TotalNumCompletedChunkRequests + " Chunks Served. "+NetworkComms.TotalNumConnections()+" Connections. Comms Load "+(NetworkComms.AverageNetworkLoad(5)*NetworkComms.InterfaceLinkSpeed/8.0E6).ToString("0.0")+"MB/s.");
                Thread.Sleep(5000);
            }
        }
    }
}
