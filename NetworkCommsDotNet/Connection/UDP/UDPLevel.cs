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
    /// A UDP connection can support a very large flexibility of levels. The most basic is none which sends conectionless UDP packets all the way
    /// upto an emulated TCP level. Future versions of networkComms will support an ever increasing number of levels.
    /// </summary>
    [Flags]
    public enum UDPLevel
    {
        None = 0x0, //Network comms 2.0 will only support the most basic udp level
        //EstablishHandshake = 0x1, //This will probably be the first feature implemented post 2.0
    }
}