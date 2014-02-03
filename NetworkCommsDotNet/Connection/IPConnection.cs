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

using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.NetworkInformation;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
using System.Threading.Tasks;
using Windows.Storage;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// IP Connection base class for NetworkComms.Net. This contains the functionality and tools shared by any connections
    /// that use IP related endPoints such as <see cref="TCPConnection"/> and <see cref="UDPConnection"/>.
    /// </summary>
    public abstract class IPConnection : Connection
    {
        static IPConnection()
        {
            DOSProtection = new DOSProtection();
        }

        /// <summary>
        /// Create a new IP connection object
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo corresponding to the new connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which should be used as connection defaults</param>
        protected IPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions)
            : base(connectionInfo, defaultSendReceiveOptions)
        {

        }

        #region IP Security
        /// <summary>
        /// The NetworkComms.Net DOS protection class. By default DOSProtection is disabled.
        /// </summary>
        public static DOSProtection DOSProtection { get; private set; }

        /// <summary>
        /// If set NetworkComms.Net will only accept incoming connections from the provided IP ranges. 
        /// </summary>
        public static IPRange[] AllowedIncomingIPRanges { get; set; }
        #endregion
    }
}
