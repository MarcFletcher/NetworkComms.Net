//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;
using System.Net;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.IO;
using InTheHand.Net;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.Bluetooth;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;
using System.Threading.Tasks;

namespace DebugTests
{
    /// <summary>
    /// A scrap board class for solving whatever bug is currently causing issues
    /// </summary>
    static class DebugTest
    {
        public static void RunExample(string[] args)
        {
            if (args.Length == 0 || args[0] == "client")
                RunClient();
            else
                RunServer();
        }

        private static void RunServer()
        {

            //We have used NetworkComms so we should ensure that we correctly call shutdown
            NetworkComms.Shutdown();
        }

        private static void RunClient()
        {
  
        }
    }
}
