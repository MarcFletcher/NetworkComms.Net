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
using ProtoBuf;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Wrapper class for connection information.
    /// </summary>
    [ProtoContract]
    public class ConnectionInfo : IEqualityComparer<ConnectionInfo>
    {
        /// <summary>
        /// We store our unique peer identifier as a string so that it can be easily serialised.
        /// </summary>
        [ProtoMember(1)]
        string networkIdentifier;

        /// <summary>
        /// The IP address of this peer
        /// </summary>
        [ProtoMember(2)]
        public string ClientIP { get; private set; }

        /// <summary>
        /// The port this peer is listening on for new connections. Will be -1 if not listening.
        /// </summary>
        [ProtoMember(3)]
        public int ClientPort { get; private set; }

        /// <summary>
        /// Returns the networkIdentifier of this peer as a ShortGuid
        /// </summary>
        public ShortGuid NetworkIdentifier
        {
            get { return new ShortGuid(networkIdentifier); }
            private set { networkIdentifier = value; }
        }

        /// <summary>
        /// Private constructor required for deserialisation.
        /// </summary>
        private ConnectionInfo() { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <param name="clientIP"></param>
        /// <param name="localPort"></param>
        public ConnectionInfo(string networkIdentifier, string clientIP, int localPort)
        {
            this.networkIdentifier = networkIdentifier;
            this.ClientIP = clientIP;
            this.ClientPort = localPort;
        }

        #region IEqualityComparer<ConnectionInfo> Members

        public bool Equals(ConnectionInfo x, ConnectionInfo y)
        {
            return (x.NetworkIdentifier.ToString() == y.NetworkIdentifier.ToString());
        }

        public int GetHashCode(ConnectionInfo obj)
        {
            return obj.NetworkIdentifier.GetHashCode();
        }

        #endregion
    }
}
