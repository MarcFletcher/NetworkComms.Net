//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Used to determine a valid local <see href="http://en.wikipedia.org/wiki/IP_address">IP address</see>, using the Windows API, to provided targets. This method is only supported in a Windows environment.
    /// </summary>
    public static class IPExtAccess
    {
        [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
        static extern int GetBestInterface(UInt32 DestAddr, out UInt32 BestIfIndex);

        /// <summary>
        /// Attempts to guess the best local IP address of this machine for accessing the provided targetIP.
        /// </summary>
        /// <param name="targetIPAddress">The target IP which should be used to determine the best adaptor. e.g. Either a local network or public IP address.</param>
        /// <returns></returns>
        public static IPAddress AttemptBestIPAddressGuess(IPAddress targetIPAddress)
        {
            try
            {
                //We work out the best interface for connecting with the outside world using the provided target IP
                UInt32 ipaddr = BitConverter.ToUInt32(targetIPAddress.GetAddressBytes(), 0);

                UInt32 interfaceindex = 0;
                IPExtAccess.GetBestInterface(ipaddr, out interfaceindex);

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                var bestInterface = (from current in interfaces
                                     where current.GetIPProperties().GetIPv4Properties().Index == interfaceindex
                                     select current).First();

                var ipAddressBest = (from current in bestInterface.GetIPProperties().UnicastAddresses
                                     where current.Address.AddressFamily == AddressFamily.InterNetwork
                                     select current.Address).First();

                if (ipAddressBest != null)
                    return ipAddressBest;
            }
            catch (Exception)
            {

            }

            return null;
        }
    }
}
