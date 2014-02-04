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

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// The type of <see cref="Connection"/>.
    /// </summary>
    public enum ConnectionType
    {
        /// <summary>
        /// An undefined connection type. This is used as the default value.
        /// </summary>
        Undefined,

        /// <summary>
        /// A TCP connection type. Used by <see cref="NetworkCommsDotNet.Connections.TCP.TCPConnection"/>.
        /// </summary>
        TCP,

        /// <summary>
        /// A UDP connection type. Used by <see cref="NetworkCommsDotNet.Connections.UDP.UDPConnection"/>.
        /// </summary>
        UDP,

#if !NET2 && !WINDOWS_PHONE
        /// <summary>
        /// A Bluetooth RFCOMM connection. Used by <see cref="BluetoothConnection"/> 
        /// </summary>
        Bluetooth,
#endif

        //We may support others in future such as SSH, FTP, SCP etc.
    }

    /// <summary>
    /// The connections application layer protocol status.
    /// </summary>
    public enum ApplicationLayerProtocolStatus
    {
        /// <summary>
        /// Useful for selecting or searching connections when the ApplicationLayerProtocolStatus
        /// is unimportant.
        /// </summary>
        Undefined,

        /// <summary>
        /// Default value. NetworkComms.Net will use a custom application layer protocol to provide 
        /// useful features such as inline serialisation, transparent packet send and receive, 
        /// connection handshakes and remote information etc. We strongly recommend you enable the 
        /// NetworkComms.Net application layer protocol.
        /// </summary>
        Enabled,

        /// <summary>
        /// No application layer protocol will be used. TCP packets may fragment or be concatenated 
        /// with other packets. A large number of library features will be unavailable.
        /// </summary>
        Disabled
    }
}