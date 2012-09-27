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
        /// The most basic UDP option. All UDP packets are sent contionless with no error handling
        /// </summary>
        None = 0x0,

        //EstablishHandshake = 0x1, //This will probably be the first feature implemented post 2.0
    }
}