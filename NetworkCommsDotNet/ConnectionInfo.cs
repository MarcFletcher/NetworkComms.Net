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
using System.Net;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Wrapper class for connection information.
    /// </summary>
    [ProtoContract]
    public class ConnectionInfo : IEqualityComparer<ConnectionInfo>
    {
        /// <summary>
        /// The type of this connection
        /// </summary>
        [ProtoMember(1)]
        public ConnectionType ConnectionType { get; protected set; }

        /// <summary>
        /// The DateTime corresponding to the creation time of this connection object
        /// </summary>
        public DateTime ConnectionCreationTime { get; protected set; }

        /// <summary>
        /// True if connection was originally established by remote
        /// </summary>
        public bool ServerSide { get; protected set; }

        /// <summary>
        /// The DateTime corresponding to the creation time of this connection object
        /// </summary>
        public DateTime ConnectionEstablishedTime { get; private set; }

        /// <summary>
        /// We store our unique peer identifier as a string so that it can be easily serialised.
        /// </summary>
        [ProtoMember(2)]
        string remoteNetworkIdentifierStr;

        /// <summary>
        /// Returns the networkIdentifier of this peer as a ShortGuid
        /// </summary>
        public ShortGuid RemoteNetworkIdentifier
        {
            get 
            {
                if (ConnectionEstablished)
                    return new ShortGuid(remoteNetworkIdentifierStr);
                else
                    throw new ConnectionSetupException("Unable to access RemoteNetworkIdentifier until connection is successfully established.");
            }
            private set { remoteNetworkIdentifierStr = value; }
        }

        public IPEndPoint LocalEndPoint { get; private set; }

        [ProtoMember(3)]
        public IPEndPoint RemoteEndPoint { get; private set; }

        public bool ConnectionEstablished { get; private set; }

        public bool ConnectionShutdown { get; internal set; }

        protected DateTime lastTrafficTime;
        protected object lastTrafficTimeLocker = new object();

        /// <summary>
        /// The DateTime corresponding to the time data was sent or received
        /// </summary>
        public DateTime LastTrafficTime
        {
            get
            {
                lock (lastTrafficTimeLocker)
                    return lastTrafficTime;
            }
            protected set
            {
                lock (lastTrafficTimeLocker)
                    lastTrafficTime = value;
            }
        }

        /// <summary>
        /// Private constructor required for deserialisation.
        /// </summary>
        private ConnectionInfo() { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        /// <param name="clientIP"></param>
        /// <param name="localPort"></param>
        public ConnectionInfo(bool serverSide, ConnectionType connectionType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        {
            this.ServerSide = serverSide;
            this.ConnectionType = connectionType;
            this.LocalEndPoint = localEndPoint;
            this.RemoteEndPoint = remoteEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
        }

        /// <summary>
        /// Set this connection info to established.
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        public void SetEstablished(ShortGuid remoteNetworkIdentifier)
        {
            SetEstablised(remoteNetworkIdentifier, null);
        }

        /// <summary>
        /// Set this connection info to established including an update of the remoteEndPoint.
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        /// <param name="remoteEndPoint"></param>
        public void SetEstablised(ShortGuid remoteNetworkIdentifier, IPEndPoint remoteEndPoint)
        {
            ConnectionEstablished = true;
            ConnectionEstablishedTime = DateTime.Now;
            RemoteNetworkIdentifier = remoteNetworkIdentifier;

            if (RemoteEndPoint != null) RemoteEndPoint = remoteEndPoint;
        }

        /// <summary>
        /// Returns a string containing suitable information about this connection
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string returnString = "[" + ConnectionType + "] ";

            if (ConnectionEstablished)
                returnString += LocalEndPoint.Address + ":" + LocalEndPoint.Port + " -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port + " (" + remoteNetworkIdentifierStr + ")";
            else
            {
                if (RemoteEndPoint != null && LocalEndPoint !=null)
                    returnString+= LocalEndPoint.Address + ":" + LocalEndPoint.Port + " -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port;
                else
                    returnString+= "NA";
            }

            return returnString;
        }

        internal void UpdateLastTrafficTime()
        {
            lock (lastTrafficTimeLocker)
                lastTrafficTime = DateTime.Now;
        }

        #region IEqualityComparer<ConnectionInfo> Members

        public bool Equals(ConnectionInfo x, ConnectionInfo y)
        {
            return (x.RemoteNetworkIdentifier.ToString() == y.RemoteNetworkIdentifier.ToString());
        }

        public int GetHashCode(ConnectionInfo obj)
        {
            return obj.RemoteNetworkIdentifier.GetHashCode();
        }

        #endregion
    }
}
