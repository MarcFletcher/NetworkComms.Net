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
            NameValueCollection properties = new NameValueCollection();
            properties["configType"] = "FILE";
            properties["configFile"] = "log4net.config";
            NetworkComms.EnableLogging(new Log4NetLoggerFactoryAdapter(properties));

            //BasicSend.RunExample();
            //AliveTest.RunExample();
            DFSTest.RunExample();
        }
    }
}
