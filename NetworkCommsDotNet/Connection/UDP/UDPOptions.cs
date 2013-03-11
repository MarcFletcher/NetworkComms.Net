//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A <see cref="UDPConnection"/> could support different combinations of features. i.e. From the most basic (None) which sends conectionless UDP packets
    /// upto an emulated TCP. Future versions of NetworkCommsDotNet will support an ever increasing number of UDP features.
    /// This flag enum is used to specifiy which of the available features should be used.
    /// </summary>
    [Flags]
    public enum UDPOptions
    {
        /// <summary>
        /// The most basic UDP option. All UDP packets are sent connectionless with no error handling, sequencing or duplication prevention.
        /// </summary>
        None = 0x0,

        //The following UDP options are on the roadmap for implementation.

        //Handshake the connection before sending user data. Ensures the remote end is actually listening.
        //Handshake = 0x1,

        //Ensures packets can only be received in the order they were sent. e.g. Prevents old messages arriving late from being handled.
        //Sequenced = 0x2,
    }
}