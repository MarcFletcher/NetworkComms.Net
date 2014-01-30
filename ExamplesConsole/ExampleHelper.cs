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
using System.Net;

namespace ExamplesConsole
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
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        public static ConnectionInfo GetServerDetails(ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled)
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

            return new ConnectionInfo(lastServerIPEndPoint, applicationLayerProtocol);
        }
    }
}
