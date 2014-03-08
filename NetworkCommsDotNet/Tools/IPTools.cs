//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using NetworkCommsDotNet.DPSBase;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// A collection of tools for dealing with <see href="http://en.wikipedia.org/wiki/IP_address">IP addresses</see>.
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
                throw new ArgumentException("Provided ipAddressAndPort string was not successfully parsed to a port number.", "ipAddressAndPort");

            IPAddress ipAddress;
            if (!IPAddress.TryParse(serverIP, out ipAddress))
                throw new ArgumentException("Provided ipAddressAndPort string was not successfully parsed to an IPAddress.", "ipAddressAndPort");

            return new IPEndPoint(ipAddress, serverPort);
        }

        /// <summary>
        /// Returns true if the provided address exists within the provided subnet.
        /// </summary>
        /// <param name="address">The address to check, i.e. 192.168.0.10</param>
        /// <param name="subnet">The subnet, i.e. 192.168.0.0</param>
        /// <param name="mask">The subnet mask, i.e. 255.255.255.0</param>
        /// <returns>True if address is in the provided subnet</returns>
        public static bool IsAddressInSubnet(IPAddress address, IPAddress subnet, IPAddress mask)
        {
            if (address == null) throw new ArgumentNullException("address", "Provided IPAddress cannot be null.");
            if (subnet == null) throw new ArgumentNullException("subnet", "Provided IPAddress cannot be null.");
            if (mask == null) throw new ArgumentNullException("mask", "Provided IPAddress cannot be null.");

            //Catch for IPv6
            if (subnet.AddressFamily == AddressFamily.InterNetworkV6 ||
                mask.AddressFamily == AddressFamily.InterNetworkV6)
                throw new NotImplementedException("This method does not yet support IPv6. Please contact NetworkComms.Net support if you would like this functionality.");
            //If we have provided IPV4 subnets and masks and we have an ipv6 address then return false
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return false;

            byte[] addrBytes = address.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();
            byte[] maskedAddressBytes = new byte[addrBytes.Length];

            //Catch for IPv6
            if (maskBytes.Length < maskedAddressBytes.Length)
                return false;

            for (int i = 0; i < maskedAddressBytes.Length; ++i)
                maskedAddressBytes[i] = (byte)(addrBytes[i] & maskBytes[i]);

            IPAddress maskedAddress = new IPAddress(maskedAddressBytes);
            bool equal = subnet.Equals(maskedAddress);

            return equal;
        }

        /// <summary>
        /// Determines the most appropriate local end point to contact the provided remote end point. 
        /// Testing shows this method takes on average 1.6ms to return.
        /// </summary>
        /// <param name="remoteIPEndPoint">The remote end point</param>
        /// <returns>The selected local end point</returns>
        public static IPEndPoint BestLocalEndPoint(IPEndPoint remoteIPEndPoint)
        {
            if (remoteIPEndPoint == null) throw new ArgumentNullException("remoteIPEndPoint", "Provided IPEndPoint cannot be null.");

#if WINDOWS_PHONE || NETFX_CORE
            var t = Windows.Networking.Sockets.DatagramSocket.GetEndpointPairsAsync(new Windows.Networking.HostName(remoteIPEndPoint.Address.ToString()), remoteIPEndPoint.Port.ToString()).AsTask();
            if (t.Wait(20) && t.Result.Count > 0)
            {
                var enumerator = t.Result.GetEnumerator();
                enumerator.MoveNext();

                var endpointPair = enumerator.Current;                
                return new IPEndPoint(IPAddress.Parse(endpointPair.LocalHostName.DisplayName.ToString()), int.Parse(endpointPair.LocalServiceName));
            }
            else
                throw new ConnectionSetupException("Unable to determine correct local end point.");
#else
            //We use UDP as its connectionless hence faster
            IPEndPoint result;
            using (Socket testSocket = new Socket(remoteIPEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                testSocket.Connect(remoteIPEndPoint);
                result = (IPEndPoint)testSocket.LocalEndPoint;
            }

            return result;
#endif
        }

#if !WINDOWS_PHONE && !NETFX_CORE
        [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
        static extern int GetBestInterface(UInt32 DestAddr, out UInt32 BestIfIndex);
#endif

        /// <summary>
        /// Depreciated - . Attempts to guess the best local <see cref="IPAddress"/> of this machine for accessing 
        /// the provided target <see cref="IPAddress"/>. using the Windows API, to provided targets. 
        /// This method is only supported in a Windows environment.
        /// </summary>
        /// <param name="targetIPAddress">The target IP which should be used to determine the best 
        /// local address. e.g. Either a local network or public IP address.</param>
        /// <returns>Local <see cref="IPAddress"/> which is best used to contact that provided target.</returns>
        [Obsolete("Method is depreciated, please use BestLocalEndPoint(IPEndPoint) instead")]
        public static IPAddress AttemptBestIPAddressGuess(IPAddress targetIPAddress)
        {
            if (targetIPAddress == null)
                throw new ArgumentNullException("targetIPAddress", "Provided IPAddress cannot be null.");

#if WINDOWS_PHONE || NETFX_CORE
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
                GetBestInterface(ipaddr, out interfaceindex);

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

    /// <summary>
    /// A class that encapsulates an IPv4 or IPv6 range. 
    /// Used for checking if an IPAddress is within an IPRange.
    /// </summary>
    public class IPRange
    {
        /// <summary>
        /// Number of most significant bits used for network-identifying portion of address. 
        /// The remaining bits specify the host identifier.
        /// </summary>
        private int numAddressBits;

        /// <summary>
        /// IPAddress as bytes
        /// </summary>
        private byte[] addressBytes;

        /// <summary>
        /// Initialise an IPRange using the provided CIDR notation.
        /// </summary>
        /// <param name="rangeCIDR">IP range using CIDR notation, e.g. "192.168.1.0/24" contains 192.168.1.0 to 192.168.1.255</param>
        public IPRange(string rangeCIDR)
        {
            if (rangeCIDR == null) throw new ArgumentNullException("rangeCIDR");

            string[] parts = rangeCIDR.Split('/');
            if (parts.Length != 2) throw new FormatException("Invalid CIDR syntax used. Please check provided rangeCIDR and try again.");

            try
            {
                //Parse the address and number of address bits
                addressBytes = IPAddress.Parse(parts[0]).GetAddressBytes();
                numAddressBits = int.Parse(parts[1]);
            }
            catch (FormatException)
            {
                throw new FormatException("Invalid CIDR syntax used. Please check provided rangeCIDR and try again.");
            }
        }

        /// <summary>
        /// Initialise an IPRange using the provided address and subnet mask.
        /// </summary>
        /// <param name="address">The address range to create</param>
        /// <param name="subnetmask">The subnet mask that specifies the network-identifying portion of the address</param>
        public IPRange(IPAddress address, IPAddress subnetmask)
        {
            addressBytes = address.GetAddressBytes();
            byte[] subnetmaskBytes = subnetmask.GetAddressBytes();

            for (int i = 0; i < subnetmaskBytes.Length; i++)
            {
                if (subnetmaskBytes[i] == 255)
                    numAddressBits += 8;
                else
                {
                    //Count the remaining bits
                    int byteToCount = subnetmaskBytes[i];
                    int bitCount = 0;
                    bool foundBitSet = false;
                    while (byteToCount != 0)
                    {
                        if ((byteToCount & 1) == 1)
                        {
                            foundBitSet = true;
                            bitCount++;
                        }
                        else if (foundBitSet)
                            //If we come across a zero after already seeing set bits the net mask is invalid
                            throw new ArgumentException("Invalid subnet mask provided. Please check and try again.", "subnetmask");

                        byteToCount >>= 1;
                    }

                    numAddressBits += bitCount;

                    //All following bytes should be 0 for a valid mask
                    for (int n = i + 1; n < subnetmaskBytes.Length; n++)
                    {
                        if (subnetmaskBytes[n] != 0)
                            throw new ArgumentException("Invalid subnet mask provided. Please check and try again.", "subnetmask");
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Returns true if this IPRange contains the provided IPAddress
        /// </summary>
        /// <param name="ipAddress">The IPAddress to check</param>
        /// <returns></returns>
        public bool Contains(IPAddress ipAddress)
        {
            if (ipAddress == null) throw new ArgumentNullException("ipAddress");

            return this.Contains(ipAddress.GetAddressBytes());
        }

        /// <summary>
        /// Returns true if this IPRange contains the provided IPAddress
        /// </summary>
        /// <param name="ipAddressStr">The IPAddress to check</param>
        /// <returns></returns>
        public bool Contains(string ipAddressStr)
        {
            if (ipAddressStr == null) throw new ArgumentNullException("ipAddress");
            
            IPAddress ipAddress;
            if (!IPAddress.TryParse(ipAddressStr, out ipAddress))
                throw new FormatException("Failed to parse ipAddressStr to IPAddress");

            return this.Contains(ipAddress.GetAddressBytes());
        }

        /// <summary>
        /// Returns true if this IPRange contains the provided IPAddress bytes
        /// </summary>
        /// <param name="ipAddressBytes">The IPAddress bytes to check</param>
        /// <returns></returns>
        public bool Contains(byte[] ipAddressBytes)
        {
            if (ipAddressBytes == null) throw new ArgumentNullException("ipAddressBytes");

            //Check for the easy scenario when IPv4 != IPv6
            if (addressBytes.Length != ipAddressBytes.Length)
                return false; 

            int currentByteIndex = 0;
            int currentAddressBit;

            //Start with the MSBs that encapsulate the network address
            //If less than 8 bits encapsulate the network address skip this
            for (currentAddressBit = numAddressBits; currentAddressBit >= 8; currentAddressBit -= 8)
            {
                //If the network address portion in 8 bit steps does not match
                //then the addresses are incorrect.
                if (addressBytes[currentByteIndex] != ipAddressBytes[currentByteIndex])
                    return false;

                //Increment the byte index
                currentByteIndex++;
            }

            //If we still have bit's to compare
            //This will happen if numAddressBits % 8 != 0
            if (currentAddressBit > 0)
            {
                //Create a mask of the bits left to compare
                int mask = (byte)~(255 >> currentAddressBit);

                //Compare the remaining bits using a mask
                if ((addressBytes[currentByteIndex] & mask) != (ipAddressBytes[currentByteIndex] & mask))
                    return false;
            }

            //If we have made it here the provided IPAddress in within this IPRange
            return true;
        }

        /// <summary>
        /// Returns a list of all IPAddresses in the specified range
        /// </summary>
        /// <returns></returns>
        public List<IPAddress> AllAddressesInRange()
        {
            //Determine the first and last addresses
            byte[] firstAddressBytes = new byte[addressBytes.Length];
            byte[] lastAddressBytes = new byte[addressBytes.Length];

            for(int i=0; i<addressBytes.Length; i++)
            {
                if (numAddressBits >= (i+1)*8)
                {
                    firstAddressBytes[i] = addressBytes[i];
                    lastAddressBytes[i] = addressBytes[i];
                }
                else
                {
                    int numRemainingAddressBits = numAddressBits - (i * 8);

                    if (numRemainingAddressBits > 0)
                    {
                        firstAddressBytes[i] = (byte)(addressBytes[i] & (byte)~(255 >> numRemainingAddressBits));
                        lastAddressBytes[i] = (byte)(addressBytes[i] | (byte)(255 >> numRemainingAddressBits));
                    }
                    else
                    {
                        firstAddressBytes[i] = 0;
                        lastAddressBytes[i] = 255;
                    }
                }
            }

            IPAddress firstAddress = new IPAddress(firstAddressBytes);
            IPAddress lastAddress = new IPAddress(lastAddressBytes);

            //Now fill in  all of the gaps
            return AllAddressesBetween(firstAddress, lastAddress);
        }

        /// <summary>
        /// Returns all IPAddresses that are between the provided addresses
        /// </summary>
        /// <param name="firstAddress"></param>
        /// <param name="lastAddress"></param>
        /// <returns></returns>
        public static List<IPAddress> AllAddressesBetween(IPAddress firstAddress, IPAddress lastAddress)
        {
            List<IPAddress> result = new List<IPAddress>();

            byte[] firstAddressBytes = firstAddress.GetAddressBytes();
            byte[] lastAddressBytes = lastAddress.GetAddressBytes();

            RecursivePopulate(firstAddressBytes, lastAddressBytes, new byte[0], result);

            return result;
        }

        /// <summary>
        /// Recursively populates the result list by looping over all address byte levels
        /// </summary>
        /// <param name="firstAddressBytes"></param>
        /// <param name="lastAddressBytes"></param>
        /// <param name="knownBytes"></param>
        /// <param name="result"></param>
        private static void RecursivePopulate(byte[] firstAddressBytes, byte[] lastAddressBytes, byte[] knownBytes, List<IPAddress> result)
        {
            if (result == null) throw new ArgumentNullException("result can not be null");

            if (knownBytes.Length == firstAddressBytes.Length)
            {
                //Catch the very first address at the bottom of the least significant byte
                if (knownBytes[knownBytes.Length - 1] != 0)
                    result.Add(new IPAddress(knownBytes));
            }
            else
            {
                for (int currentByte = firstAddressBytes[knownBytes.Length]; currentByte <= lastAddressBytes[knownBytes.Length]; currentByte++)
                {
                    byte[] newKnownBytes = new byte[knownBytes.Length + 1];
                    for (int i = 0; i < knownBytes.Length; i++)
                        newKnownBytes[i] = knownBytes[i];

                    newKnownBytes[knownBytes.Length] = (byte)currentByte;

                    RecursivePopulate(firstAddressBytes, lastAddressBytes, newKnownBytes, result);
                }
            }
        }

        /// <summary>
        /// Returns true if the provided IPAddress is within one of the provided IPRanges, otherwise false
        /// </summary>
        /// <param name="ranges">The ranges to search</param>
        /// <param name="ipAddress">The IPAddress to find in ranges</param>
        /// <returns></returns>
        public static bool Contains(IEnumerable<IPRange> ranges, IPAddress ipAddress)
        {
            if (ranges == null) throw new ArgumentNullException("ranges");
            if (ipAddress == null) throw new ArgumentNullException("ipAddress");

            foreach (IPRange range in ranges)
            {
                if (range.Contains(ipAddress))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns a clean ToString of the IPRange
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return new IPAddress(addressBytes) + "/" + numAddressBits.ToString();
        }
    }
}
