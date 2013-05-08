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
                DistributedFileSystem.DFS.EnableLogging(logConfig);
            }

            //NetworkComms.ListenOnAllAllowedInterfaces = false;
            //NetworkComms.AllowedIPPrefixes = new string[] { "131.111", "172.24" };

            NetworkComms.AppendGlobalConnectionEstablishHandler((connection) =>
                {
                    Thread.Sleep(5000);
                    Console.WriteLine(" XXX Connection established with {0}",connection);
                    connection.SendObject("Message", "message from establish handler");
                }, true);

            BasicSend.RunExample();
            //AliveTest.RunExample();
            //DebugTest.GoTCP();
            //DFSTest.RunExample();
            //LoadTest.RunExample();
            //DebugTest.GoStreamTest();
            //ThreadPoolTest.RunExample();
        }
    }
}
