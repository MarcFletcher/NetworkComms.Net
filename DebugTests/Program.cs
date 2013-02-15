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
using NLog;
using System.Threading;
using System.IO;
//using DistributedFileSystem;
using NLog.Config;
using NLog.Targets;
using System.Net;
using System.Diagnostics;
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

            if (true)
            {
                //Configure the logger here
                LoggingConfiguration logConfig = new LoggingConfiguration();
                FileTarget fileTarget = new FileTarget();
                fileTarget.FileName = "${basedir}/file.txt";
                fileTarget.Layout = "${date:format=HH\\:MM\\:ss} ${logger} ${message}";

                logConfig.AddTarget("file", fileTarget);

                LoggingRule rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
                logConfig.LoggingRules.Add(rule);

                NetworkComms.EnableLogging(logConfig);                
                //DFS.EnableLogging(logConfig);
            }

            //NetworkComms.ListenOnAllAllowedInterfaces = false;
            //NetworkComms.AllowedIPPrefixes = new string[] { "131.111", "172.24" };

            BasicSend.RunExample();
            //AliveTest.RunExample();
            //DebugTest.GoTCP();
            //DFSTest.RunExample();
            //LoadTest.RunExample();
            //DebugTest.GoDFSLogParse();
        }
    }
}
