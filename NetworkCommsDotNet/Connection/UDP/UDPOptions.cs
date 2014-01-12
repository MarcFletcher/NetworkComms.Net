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

#if NETFX_CORE
using NetworkCommsDotNet.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A <see cref="UDPConnection"/> could support different combinations of features. i.e. From the most basic (None) which 
    /// sends connectionless UDP packets up to an emulated TCP. Future versions of NetworkCommsDotNet will support an ever 
    /// increasing number of UDP features. This flag enum is used to specify which of the available features should be used.
    /// </summary>
    [Flags]
    public enum UDPOptions
    {
        /// <summary>
        /// The most basic UDP option. All UDP packets are sent connectionless with no error handling, sequencing or duplication prevention.
        /// </summary>
        None = 0x0,

        /// <summary>
        /// Performs a connection handshake, which ensures the remote end is alive at the time of the connection
        /// establish. Also exchanges network identifier and possible remote listening port.
        /// </summary>
        Handshake = 0x1,

        //The following UDP options are on the roadmap for future implementation.

        //Ensures packets can only be received in the order they were sent. e.g. Prevents old messages arriving late from being handled.
        //Sequenced = 0x2,

        //Notify the remote peer we are close/removing the connection
        //ConnectionCloseNotify = 0x3,
    }

    /// <summary>
    /// A small wrapper class which allows an initialising UDP datagram
    /// to be handled within a connection instantiation if required.
    /// </summary>
    internal class HandshakeUDPDatagram
    {
        public bool DatagramHandled { get; set; }
        public byte[] DatagramBytes { get; private set; }

        public HandshakeUDPDatagram(byte[] datagramBytes)
        {
            DatagramBytes = datagramBytes;
        }
    }
}