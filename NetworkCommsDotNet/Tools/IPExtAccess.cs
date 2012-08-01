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
    /// External dll access. This will fail on a non windows system.
    /// </summary>
    public static class IPExtAccess
    {
        [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
        static extern int GetBestInterface(UInt32 DestAddr, out UInt32 BestIfIndex);

        /// <summary>
        /// Attempts to guess the best local ip address of this machine using dll hooks in Windows API and the provided targetIPAddress.
        /// </summary>
        /// <returns>IP address or null if failed.</returns>
        public static string AttemptBestIPAddressGuess(IPAddress targetIPAddress)
        {
            try
            {
                //We work out the best interface for connecting with the outside world
                //If we are going to try and choose an ip address this one makes the most sense
                //Using Google DNS server as reference IP
                UInt32 ipaddr = BitConverter.ToUInt32(targetIPAddress.GetAddressBytes(), 0);

                UInt32 interfaceindex = 0;
                IPExtAccess.GetBestInterface(ipaddr, out interfaceindex);

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                var bestInterface = (from current in interfaces
                                     where current.GetIPProperties().GetIPv4Properties().Index == interfaceindex
                                     select current).First();

                var ipAddressBest = (from current in bestInterface.GetIPProperties().UnicastAddresses
                                     where current.Address.AddressFamily == AddressFamily.InterNetwork
                                     select current.Address).First().ToString();

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
