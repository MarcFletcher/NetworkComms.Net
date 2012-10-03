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
//using DistributedFileSystem;

namespace DebugTests
{
    class Program
    {      
        static void Main(string[] args)
        {
            try
            {
                Console.SetBufferSize(180, 500);
                Console.SetWindowSize(180, 25);
            }
            catch (Exception) { }

            Thread.CurrentThread.Name = "MainThread";

            if (false)
            {
                //Configure the logger here
                NameValueCollection properties = new NameValueCollection();
                properties["configType"] = "FILE";
                properties["configFile"] = "log4net_comms.config";
                var logger = new Log4NetLoggerFactoryAdapter(properties);

                NetworkComms.EnableLogging(logger);
                DFS.EnableLogging(logger);
            }

            //NameValueCollection properties = new NameValueCollection();
            //properties["showDateTime"] = "true";
            //properties["showLogName"] = "false";
            //properties["level"] = "All";

            //NetworkComms.EnableLogging(new Common.Logging.Simple.ConsoleOutLoggerFactoryAdapter(properties));

            var ident = DPSBase.ProtobufSerializer.Instance;
            var ident2 = SevenZipLZMACompressor.LZMACompressor.Instance;

            NetworkComms.ListenOnAllAllowedInterfaces = false;
            NetworkComms.AllowedIPPrefixes = new string[] { "131.111", "172.24" };

            //BasicSend.RunExample();
            //AliveTest.RunExample();
            //DebugTest.GoTCP();
            //DFSTest.RunExample();
            LoadTest.RunExample();
        }
    }
}
