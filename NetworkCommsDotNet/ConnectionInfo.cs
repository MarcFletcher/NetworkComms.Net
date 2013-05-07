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
using ProtoBuf;
using System.Net;
using DPSBase;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Describes the current state of the connection
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>
        /// The state of this connection is undefined. This is the starting state of new connections.
        /// </summary>
        Undefined,

        /// <summary>
        /// The connection is in the process of being established.
        /// </summary>
        Establishing,

        /// <summary>
        /// The connection has been succesfully established.
        /// </summary>
        Established,

        /// <summary>
        /// The connection has been shutdown.
        /// </summary>
        Shutdown
    }

    /// <summary>
    /// Contains any information related to the configuration of a <see cref="Connection"/> object.
    /// </summary>
    [ProtoContract]
    public class ConnectionInfo : IEquatable<ConnectionInfo>
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

        bool hashCodeCacheSet = false;
        int hashCodeCache;

        /// <summary>
        /// True if the <see cref="RemoteEndPoint"/> is connectable.
        /// </summary>
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

        /// <summary>
        /// The <see cref="IPEndPoint"/> corresponding to the local end of this connection.
        /// </summary>
        public IPEndPoint LocalEndPoint { get; private set; }

        /// <summary>
        /// The <see cref="IPEndPoint"/> corresponding to the remote end of this connection.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; private set; }

        /// <summary>
        /// Describes the current state of the connection
        /// </summary>
        public ConnectionState ConnectionState { get; private set; }

        /// <summary>
        /// Returns the networkIdentifier of this peer as a ShortGuid. If the NetworkIdentifier has not yet been set returns ShortGuid.Empty.
        /// </summary>
        public ShortGuid NetworkIdentifier
        {
            get 
            {
                if (NetworkIdentifierStr == null || NetworkIdentifierStr == "") return ShortGuid.Empty;
                else return new ShortGuid(NetworkIdentifierStr);
            }
        }

        DateTime lastTrafficTime;
        object internalLocker = new object();

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
#if iOS || ANDROID
        public ConnectionInfo() { }
#else
        private ConnectionInfo() { }
#endif

        /// <summary>
        /// Create a new ConnectionInfo object pointing at the provided remote <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="remoteEndPoint">The end point corresponding with the remote target</param>
        public ConnectionInfo(IPEndPoint remoteEndPoint)
        {
            this.RemoteEndPoint = remoteEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
        }

        /// <summary>
        /// Create a new ConnectionInfo object pointing at the provided remote ipAddress and port. Provided ipAddress and port are parsed in to <see cref="RemoteEndPoint"/>.
        /// </summary>
        /// <param name="remoteIPAddress">IP address of the remote target in string format, e.g. "192.168.0.1"</param>
        /// <param name="remotePort">The available port of the remote target. 
        /// Valid ports are 1 through 65535. Port numbers less than 256 are reserved for well-known services (like HTTP on port 80) and port numbers less than 1024 generally require admin access</param>
        public ConnectionInfo(string remoteIPAddress, int remotePort)
        {
            IPAddress ipAddress;
            if (!IPAddress.TryParse(remoteIPAddress, out ipAddress))
                throw new ArgumentException("Provided remoteIPAddress string was not succesfully parsed.", "remoteIPAddress");

            this.RemoteEndPoint = new IPEndPoint(ipAddress, remotePort);
            this.ConnectionCreationTime = DateTime.Now;
        }

        /// <summary>
        /// Create a connectionInfo object which can be used to inform a remote peer of local connectivity
        /// </summary>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="localNetworkIdentifier">The local network identifier</param>
        /// <param name="localEndPoint">The localEndPoint which should be referenced remotely</param>
        /// <param name="isConnectable">True if connectable on provided localEndPoint</param>
        public ConnectionInfo(ConnectionType connectionType, ShortGuid localNetworkIdentifier, IPEndPoint localEndPoint, bool isConnectable)
        {
            this.ConnectionType = connectionType;
            this.NetworkIdentifierStr = localNetworkIdentifier.ToString();
            this.LocalEndPoint = localEndPoint;
            this.IsConnectable = isConnectable;
        }

        /// <summary>
        /// Create a connectionInfo object for a new connection.
        /// </summary>
        /// <param name="serverSide">True if this connection is being created serverSide</param>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="remoteEndPoint">The remoteEndPoint of this connection</param>
        internal ConnectionInfo(bool serverSide, ConnectionType connectionType, IPEndPoint remoteEndPoint)
        {
            this.ServerSide = serverSide;
            this.ConnectionType = connectionType;
            this.RemoteEndPoint = remoteEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
        }

        /// <summary>
        /// Create a connectionInfo object for a new connection.
        /// </summary>
        /// <param name="serverSide">True if this connection is being created serverSide</param>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="remoteEndPoint">The remoteEndPoint of this connection</param>
        /// <param name="localEndPoint">The localEndpoint of this connection</param>
        internal ConnectionInfo(bool serverSide, ConnectionType connectionType, IPEndPoint remoteEndPoint, IPEndPoint localEndPoint)
        {
            this.ServerSide = serverSide;
            this.ConnectionType = connectionType;
            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = localEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
        }

        [ProtoBeforeSerialization]
        private void OnSerialise()
        {
            lock (internalLocker)
            {
                localEndPointIPStr = LocalEndPoint.Address.ToString();
                localEndPointPort = LocalEndPoint.Port;
            }
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
                if (ConnectionState == ConnectionState.Shutdown) throw new ConnectionSetupException("Unable to mark as establishing as connection has already shutdown.");

                if (ConnectionState == ConnectionState.Establishing) throw new ConnectionSetupException("Connection already marked as establishing");
                else ConnectionState = ConnectionState.Establishing;
            }
        }

        /// <summary>
        /// Set this connectionInfo as established.
        /// </summary>
        internal void NoteCompleteConnectionEstablish()
        {
            lock (internalLocker)
            {
                if (ConnectionState == ConnectionState.Shutdown) throw new ConnectionSetupException("Unable to mark as established as connection has already shutdown.");

                if (!(ConnectionState == ConnectionState.Establishing)) throw new ConnectionSetupException("Connection should be marked as establishing before calling CompleteConnectionEstablish");

                if (ConnectionState == ConnectionState.Established) throw new ConnectionSetupException("Connection already marked as establised.");

                ConnectionState = ConnectionState.Established;
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
                ConnectionState = ConnectionState.Shutdown;
        }

        /// <summary>
        /// Update the localEndPoint information for this connection
        /// </summary>
        /// <param name="localEndPoint"></param>
        internal void UpdateLocalEndPointInfo(IPEndPoint localEndPoint)
        {
            lock (internalLocker)
            {
                hashCodeCacheSet = false;
                this.LocalEndPoint = localEndPoint;
            }
        }

        /// <summary>
        /// During a connection handShake we might be provided with more update information regarding endPoints, connectability and identifiers
        /// </summary>
        /// <param name="handshakeInfo"><see cref="ConnectionInfo"/> provided by remoteEndPoint during connection handshake.</param>
        /// <param name="remoteEndPoint">The correct remoteEndPoint of this connection.</param>
        internal void UpdateInfoAfterRemoteHandshake(ConnectionInfo handshakeInfo, IPEndPoint remoteEndPoint)
        {
            lock (internalLocker)
            {
                NetworkIdentifierStr = handshakeInfo.NetworkIdentifier.ToString();
                RemoteEndPoint = remoteEndPoint;
                LocalEndPoint.Address = handshakeInfo.LocalEndPoint.Address;
                IsConnectable = handshakeInfo.IsConnectable;
            }
        }

        /// <summary>
        /// Updates the last traffic time for this connection
        /// </summary>
        internal void UpdateLastTrafficTime()
        {
            lock (internalLocker)
                lastTrafficTime = DateTime.Now;
        }

        /// <summary>
        /// Replaces the current networkIdentifier with that provided
        /// </summary>
        /// <param name="networkIdentifier">The new networkIdentifier for this connectionInfo</param>
        public void ResetNetworkIdentifer(ShortGuid networkIdentifier)
        {
            NetworkIdentifierStr = networkIdentifier.ToString();
        }

        /// <summary>
        /// A connectionInfo object may be used across multiple connection sessions, i.e. due to a possible timeout. 
        /// This method resets the state of the connectionInfo object so that it may be reused.
        /// </summary>
        internal void ResetConnectionInfo()
        {
            lock (internalLocker)
            {
                ConnectionState = ConnectionState.Undefined;
            }
        }

        /// <summary>
        /// Compares this <see cref="ConnectionInfo"/> object with obj and returns true if obj is ConnectionInfo and both the <see cref="NetworkIdentifier"/> and <see cref="RemoteEndPoint"/> match.
        /// </summary>
        /// <param name="obj">The object to test of equality</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            lock (internalLocker)
            {
                var other = obj as ConnectionInfo;
                if (((object)other) == null)
                    return false;
                else
                    return this == other;
            }
        }

        /// <summary>
        /// Compares this <see cref="ConnectionInfo"/> object with other and returns true if both the <see cref="NetworkIdentifier"/> and <see cref="RemoteEndPoint"/> match.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(ConnectionInfo other)
        {
            lock (internalLocker)
                return this == other;
        }

        /// <summary>
        /// Returns left.Equals(right)
        /// </summary>
        /// <param name="left">Left connectionInfo</param>
        /// <param name="right">Right connectionInfo</param>
        /// <returns>True if both are equal, otherwise false</returns>
        public static bool operator ==(ConnectionInfo left, ConnectionInfo right)
        {
            if (((object)left) == ((object)right)) return true;
            else if (((object)left) == null || ((object)right) == null) return false;
            else
            {
                if (left.RemoteEndPoint != null && right.RemoteEndPoint != null)
                    return (left.NetworkIdentifier.ToString() == right.NetworkIdentifier.ToString() && left.RemoteEndPoint.Equals(right.RemoteEndPoint));
                else
                    return (left.NetworkIdentifier.ToString() == right.NetworkIdentifier.ToString());
            }
        }

        /// <summary>
        /// Returns !left.Equals(right)
        /// </summary>
        /// <param name="left">Left connectionInfo</param>
        /// <param name="right">Right connectionInfo</param>
        /// <returns>True if both are different, otherwise false</returns>
        public static bool operator !=(ConnectionInfo left, ConnectionInfo right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns NetworkIdentifier.GetHashCode() ^ RemoteEndPoint.GetHashCode();
        /// </summary>
        /// <returns>The hashcode for this connection info</returns>
        public override int GetHashCode()
        {
            lock (internalLocker)
            {
                if (!hashCodeCacheSet)
                {
                    if (RemoteEndPoint != null)
                        hashCodeCache = NetworkIdentifier.GetHashCode() ^ RemoteEndPoint.GetHashCode();
                    else
                        hashCodeCache = NetworkIdentifier.GetHashCode();

                    hashCodeCacheSet = true;
                }

                return hashCodeCache;
            }
        }

        /// <summary>
        /// Returns a string containing suitable information about this connection
        /// </summary>
        /// <returns>A string containing suitable information about this connection</returns>
        public override string ToString()
        {
            string returnString = "[" + ConnectionType.ToString() + "] ";

            if (ConnectionState == ConnectionState.Established)
                returnString += LocalEndPoint.Address + ":" + LocalEndPoint.Port.ToString() + " -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port.ToString() + " (" + NetworkIdentifier + ")";
            else
            {
                if (RemoteEndPoint != null && LocalEndPoint != null)
                    returnString += LocalEndPoint.Address + ":" + LocalEndPoint.Port.ToString() + " -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port.ToString();
                else if (RemoteEndPoint != null)
                    returnString += "Local -> " + RemoteEndPoint.Address + ":" + RemoteEndPoint.Port.ToString();
                else if (LocalEndPoint != null)
                    returnString += LocalEndPoint.Address + ":" + LocalEndPoint.Port.ToString() + " " + (IsConnectable ? "Connectable" : "NotConnectable");
            }

            return returnString.Trim();
        }
    }
}
