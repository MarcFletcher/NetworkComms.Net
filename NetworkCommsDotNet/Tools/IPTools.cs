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
using System.Text;
using System.Net;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A collection of tools for deadling with <see href="http://en.wikipedia.org/wiki/IP_address">IP addresses</see>.
    /// </summary>
    public static class IPTools
    {
        /// <summary>
        /// Converts an IPAddress in string form (IPv4 or IPv6) with an appended port number, e.g. 192.168.0.10:10000 or ::1:10000, into an <see cref="System.Net.IPEndPoint"/>.
        /// </summary>
        /// <param name="ipAddressAndPort">The IP and Port to be parsed</param>
        /// <returns>The equivalent <see cref="System.Net.IPEndPoint"/></returns>
        public static IPEndPoint ParseEndPointFromString(string ipAddressAndPort)
        {
            if (ipAddressAndPort == null) throw new ArgumentNullException("ipAddressAndPort", "string cannot be null.");

            int lastColonPosition = ipAddressAndPort.LastIndexOf(':');
            string serverIP = ipAddressAndPort.Substring(0, lastColonPosition);

            int serverPort;
            if (!int.TryParse(ipAddressAndPort.Substring(lastColonPosition + 1, ipAddressAndPort.Length - lastColonPosition - 1), out serverPort))
                throw new ArgumentException("Provided ipAddressAndPort string was not succesfully parsed to a port number.", "ipAddressAndPort");

            IPAddress ipAddress;
            if (!IPAddress.TryParse(serverIP, out ipAddress))
                throw new ArgumentException("Provided ipAddressAndPort string was not succesfully parsed to an IPAddress.", "ipAddressAndPort");

            return new IPEndPoint(ipAddress, serverPort);
        }
    }
}
