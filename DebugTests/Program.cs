//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

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

            //NetworkComms.ListenOnAllAllowedInterfaces = false;
            //NetworkComms.AllowedIPPrefixes = new string[] { "131.111", "172.24" };

            //BasicSend.RunExample();
            //AliveTest.RunExample();
            //DebugTest.GoTCP();
            //DFSTest.RunExample();
            //LoadTest.RunExample();
            DebugTest.GoDFSLogParse();
        }
    }
}
