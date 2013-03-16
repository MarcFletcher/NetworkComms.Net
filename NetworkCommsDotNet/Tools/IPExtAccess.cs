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
        /// Attempts to guess the best local <see cref="IPAddress"/> of this machine for accessing the provided target <see cref="IPAddress"/>.
        /// </summary>
        /// <param name="targetIPAddress">The target IP which should be used to determine the best adaptor. e.g. Either a local network or public IP address.</param>
        /// <returns>Local <see cref="IPAddress"/> which is best used to contact that provided target.</returns>
        public static IPAddress AttemptBestIPAddressGuess(IPAddress targetIPAddress)
        {
#if WINDOWS_PHONE
            foreach (var name in Windows.Networking.Connectivity.NetworkInformation.GetHostNames())
                if (name.IPInformation.NetworkAdapter == Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile().NetworkAdapter)
                    return IPAddress.Parse(name.DisplayName);

            return null;
#else

            try
            {                
                //We work out the best interface for connecting with the outside world using the provided target IP
                UInt32 ipaddr = BitConverter.ToUInt32(targetIPAddress.GetAddressBytes(), 0);

                UInt32 interfaceindex = 0;
                IPExtAccess.GetBestInterface(ipaddr, out interfaceindex);

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                NetworkInterface bestInterface = null;

                foreach (var iFace in interfaces)
                {
                    if (iFace.GetIPProperties().GetIPv4Properties().Index == interfaceindex)
                    {
                        bestInterface = iFace;
                        break;
                    }
                }

                IPAddress ipAddressBest = null;

                foreach (var address in bestInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddressBest = address.Address;
                        break;
                    }
                }
                
                if (ipAddressBest != null)
                    return ipAddressBest;
            }
            catch (Exception)
            {

            }

            return null;
#endif
        }
    }
}
