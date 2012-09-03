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
        public ConnectionType ConnectionType { get; internal set; }

        /// <summary>
        /// We store our unique peer identifier as a string so that it can be easily serialised.
        /// </summary>
        [ProtoMember(2)]
        string NetworkIdentifierStr;

        [ProtoMember(3)]
        string localEndPointIPStr; //Only set on serialise
        [ProtoMember(4)]
        int localEndPointPort; //Only set on serialise

        [ProtoMember(5)]
        public bool IsConnectable { get; private set; }

        /// <summary>
        /// The DateTime corresponding to the creation time of this connection object
        /// </summary>
        public DateTime ConnectionCreationTime { get; protected set; }

        /// <summary>
        /// True if connection was originally established by remote
        /// </summary>
        public bool ServerSide { get; internal set; }

        /// <summary>
        /// The DateTime corresponding to the creation time of this connection object
        /// </summary>
        public DateTime ConnectionEstablishedTime { get; private set; }

        public IPEndPoint LocalEndPoint { get; private set; }

        public IPEndPoint RemoteEndPoint { get; private set; }

        public bool ConnectionEstablishing { get; private set; }

        public bool ConnectionEstablished { get; private set; }

        public bool ConnectionShutdown { get; private set; }

        /// <summary>
        /// Returns the networkIdentifier of this peer as a ShortGuid
        /// </summary>
        public ShortGuid NetworkIdentifier
        {
            get 
            {
                if (NetworkIdentifierStr == null || NetworkIdentifierStr == "") return ShortGuid.Empty;
                else return new ShortGuid(NetworkIdentifierStr);
            }
            private set { NetworkIdentifierStr = value; }
        }

        protected DateTime lastTrafficTime;
        protected object internalLocker = new object();

        /// <summary>
        /// The DateTime corresponding to the time data was sent or received
        /// </summary>
        public DateTime LastTrafficTime
        {
            get
            {
                lock (internalLocker)
                    return lastTrafficTime;
            }
            protected set
            {
                lock (internalLocker)
                    lastTrafficTime = value;
            }
        }

        /// <summary>
        /// Private constructor required for deserialisation.
        /// </summary>
        private ConnectionInfo() { }

        public ConnectionInfo(IPEndPoint remoteEndPoint)
        {
            this.RemoteEndPoint = remoteEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
        }

        public ConnectionInfo(string ipAddress, int port)
        {
            this.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress),port);
            this.ConnectionCreationTime = DateTime.Now;
        }

        /// <summary>
        /// Create a connectionInfo object for a new connection
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        /// <param name="clientIP"></param>
        /// <param name="localPort"></param>
        internal ConnectionInfo(bool serverSide, ConnectionType connectionType, IPEndPoint remoteEndPoint)
        {
            this.ServerSide = serverSide;
            this.ConnectionType = connectionType;
            this.RemoteEndPoint = remoteEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
        }

        /// <summary>
        /// Create a connectionInfo object for a new connection
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        /// <param name="clientIP"></param>
        /// <param name="localPort"></param>
        internal ConnectionInfo(bool serverSide, ConnectionType connectionType, IPEndPoint remoteEndPoint, IPEndPoint localEndPoint)
        {
            this.ServerSide = serverSide;
            this.ConnectionType = connectionType;
            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = localEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
        }

        /// <summary>
        /// Create a connectionInfo object which can be used to inform a remote point of local information
        /// </summary>
        /// <param name="localNetworkIdentifier"></param>
        /// <param name="localEndPoint"></param>
        internal ConnectionInfo(ConnectionType connectionType, ShortGuid localNetworkIdentifier, IPEndPoint localEndPoint, bool isConnectable)
        {
            this.ConnectionType = connectionType;
            this.NetworkIdentifier = localNetworkIdentifier;
            this.LocalEndPoint = localEndPoint;
            this.IsConnectable = isConnectable;
        }

        [ProtoBeforeSerialization]
        private void OnSerialise()
        {
            localEndPointIPStr = LocalEndPoint.Address.ToString();
            localEndPointPort = LocalEndPoint.Port;
        }

        [ProtoAfterDeserialization]
        private void OnDeserialise()
        {
            LocalEndPoint = new IPEndPoint(IPAddress.Parse(localEndPointIPStr), localEndPointPort);
        }

        /// <summary>
        /// Marks the connection as establishing
        /// </summary>
        internal void NoteStartConnectionEstablish()
        {
            lock(internalLocker)
            {
                if (ConnectionShutdown) throw new ConnectionSetupException("Unable to mark as establishing as connection has already shutdown.");

                if (ConnectionEstablishing) throw new ConnectionSetupException("Connection already marked as establishing");
                else ConnectionEstablishing = true;
            }
        }

        /// <summary>
        /// Set this connection info to established.
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        internal void NoteCompleteConnectionEstablish()
        {
            lock (internalLocker)
            {
                if (ConnectionShutdown) throw new ConnectionSetupException("Unable to mark as established as connection has already shutdown.");

                if (!ConnectionEstablishing) throw new ConnectionSetupException("Connection should be marked as establishing before calling CompleteConnectionEstablish");

                if (ConnectionEstablished) throw new ConnectionSetupException("Connection already marked as establised.");

                ConnectionEstablished = true;
                ConnectionEstablishedTime = DateTime.Now;

                if (NetworkIdentifier == ShortGuid.Empty) throw new ConnectionSetupException("Unable to set connection established until networkIdentifier has been set.");
            }
        }

        /// <summary>
        /// Note this connection as shutdown
        /// </summary>
        internal void NoteConnectionShutdown()
        {
            lock (internalLocker)
            {
                ConnectionShutdown = true;
                ConnectionEstablished = false;
                ConnectionEstablishing = false;
            }
        }

        /// <summary>
        /// Update the localEndPoint information for this connection
        /// </summary>
        /// <param name="localEndPoint"></param>
        internal void UpdateLocalEndPointInfo(IPEndPoint localEndPoint)
        {
            this.LocalEndPoint = localEndPoint;
        }

        /// <summary>
        /// During a connection handShake we might be provided with more update information regarding endPoints, connectability and identifiers
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        /// <param name="remoteEndPoint"></param>
        internal void UpdateInfoAfterRemoteHandshake(ConnectionInfo handshakeInfo)
        {
            NetworkIdentifier = handshakeInfo.NetworkIdentifier;
            RemoteEndPoint = handshakeInfo.LocalEndPoint;
            IsConnectable = handshakeInfo.IsConnectable;
        }

        /// <summary>
        /// Updates the last traffic time for this connection
        /// </summary>
        internal void UpdateLastTrafficTime()
        {
            lock (internalLocker)
                lastTrafficTime = DateTime.Now;
        }

        public bool Equals(ConnectionInfo x, ConnectionInfo y)
        {
            return (x.NetworkIdentifier.ToString() == y.NetworkIdentifier.ToString() && x.RemoteEndPoint.Equals(y.RemoteEndPoint));
        }

        public int GetHashCode(ConnectionInfo obj)
        {
            return obj.NetworkIdentifier.GetHashCode() ^ obj.RemoteEndPoint.GetHashCode();
        }

        /// <summary>
        /// Returns a string containing suitable information about this connection
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string returnString = "[" + ConnectionType + "] ";

            if (ConnectionEstablished)
                returnString += LocalEndPoint.Address + ":" + LocalEndPoint.Port + " -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port + " (" + NetworkIdentifierStr + ")";
            else
            {
                if (RemoteEndPoint != null && LocalEndPoint != null)
                    returnString += LocalEndPoint.Address + ":" + LocalEndPoint.Port + " -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port;
                else if (RemoteEndPoint != null)
                    returnString += "Local -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port;
            }

            return returnString.Trim();
        }

    }
}
