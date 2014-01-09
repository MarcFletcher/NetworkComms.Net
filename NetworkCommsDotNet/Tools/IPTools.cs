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
using System.Net.Sockets;

namespace NetworkCommsDotNet
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
        /// Returns true if this IPRange contains the provided IPAddress
        /// </summary>
        /// <param name="ipAddress">The IPAddress to check</param>
        /// <returns></returns>
        public bool Contains(IPAddress ipAddress)
        {
            return this.Contains(ipAddress.GetAddressBytes());
        }

        /// <summary>
        /// Returns true if this IPRange contains the provided IPAddress
        /// </summary>
        /// <param name="ipAddress">The IPAddress to check</param>
        /// <returns></returns>
        public bool Contains(string ipAddress)
        {
            return this.Contains(IPAddress.Parse(ipAddress).GetAddressBytes());
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
        /// Returns a clean ToString of the IPRange
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return new IPAddress(addressBytes) + "/" + numAddressBits;
        }
    }
}
