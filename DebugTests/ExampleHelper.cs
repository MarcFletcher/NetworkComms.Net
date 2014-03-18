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
using NetworkCommsDotNet.Tools;
using System.Net;

namespace DebugTests
{
    /// <summary>
    /// Provides implementation shared across examples
    /// </summary>
    public static class ExampleHelper
    {
        static IPEndPoint lastServerIPEndPoint = null;

        /// <summary>
        /// Request user to provide server details and returns the result as a <see cref="ConnectionInfo"/> object. Performs the necessary validation and prevents code duplication across examples.
        /// </summary>
        /// <param name="connectionInfo"></param>
        public static void GetServerDetails(out ConnectionInfo connectionInfo)
        {
            if (lastServerIPEndPoint != null)
                Console.WriteLine("Please enter the destination IP and port. To reuse '{0}:{1}' use r:", lastServerIPEndPoint.Address, lastServerIPEndPoint.Port);
            else
                Console.WriteLine("Please enter the destination IP address and port, e.g. '192.168.0.1:10000':");

            while (true)
            {
                try
                {
                    //Parse the provided information
                    string userEnteredStr = Console.ReadLine();

                    if (userEnteredStr.Trim() == "r" && lastServerIPEndPoint != null)
                        break;
                    else
                    {
                        lastServerIPEndPoint = IPTools.ParseEndPointFromString(userEnteredStr);
                        break;
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Unable to determine host IP address and port. Check format and try again:");
                }
            }

            connectionInfo = new ConnectionInfo(lastServerIPEndPoint);
        }
    }
}
