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
            Console.SetBufferSize(130, 500);
            Console.SetWindowSize(130, 25);
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

            DFS.InitialiseDFS();
            DFS.InitialiseDFSLink("131.111.73.213", 2004, DFSLinkMode.LinkAndRepeat);

            while (true)
            {
                Console.WriteLine(DFS.AllLocalDFSItemKeys(true).Length + " (" + DFS.AllLocalDFSItemKeys(false).Length + ") Items. " + DFS.TotalNumCompletedChunkRequests + " Chuncks Served.");
                Thread.Sleep(5000);
            }
        }
    }
}
