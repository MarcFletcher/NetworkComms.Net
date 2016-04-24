// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
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
