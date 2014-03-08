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
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Reserved packetTypeStrs. Removing or modifying these will prevent NetworkComms.Net from working
    /// </summary>
    enum ReservedPacketType
    {
        Confirmation,
        CheckSumFailResend,
        AliveTestPacketRequest,
        AliveTestPacketResponse,
        ConnectionSetup,
        Unmanaged,
        NestedPacket,
    }
}
