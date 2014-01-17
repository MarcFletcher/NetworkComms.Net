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

#if NETFX_CORE
using NetworkCommsDotNet.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

#if NET4 || NET35
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
#endif

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
        /// The connection has been successfully established.
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
        string localEndPointAddressStr; //Only set on serialise
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
        /// The <see cref="EndPoint"/> corresponding to the local end of this connection.
        /// </summary>
        public EndPoint LocalEndPoint { get; private set; }

        /// <summary>
        /// The <see cref="EndPoint"/> corresponding to the local end of this connection.
        /// </summary>
        public EndPoint RemoteEndPoint { get; private set; }

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
        /// If enabled NetworkComms.Net uses a custom application layer protocol to provide 
        /// useful features such as inline serialisation, transparent packet transmission, 
        /// remote peer information etc. Default: ApplicationLayerProtocolStatus.Enabled
        /// </summary>
        [ProtoMember(6)]
        public ApplicationLayerProtocolStatus ApplicationLayerProtocol { get; private set; }

        #region Internal Usages
        /// <summary>
        /// The localEndPoint cast as <see cref="IPEndPoint"/>.
        /// </summary>
        internal IPEndPoint LocalIPEndPoint
        {
            get
            {
                try
                {
                    return (IPEndPoint)LocalEndPoint;
                }
                catch (InvalidCastException ex)
                {
                    throw new InvalidCastException("Unable to cast LocalEndPoint to IPEndPoint.", ex);
                }
            }
        }

        /// <summary>
        /// The remoteEndPoint cast as <see cref="IPEndPoint"/>.
        /// </summary>
        internal IPEndPoint RemoteIPEndPoint
        {
            get
            {
                try
                {
                    return (IPEndPoint)RemoteEndPoint;
                }
                catch (InvalidCastException ex)
                {
                    throw new InvalidCastException("Unable to cast LocalEndPoint to IPEndPoint.", ex);
                }
            }
        }

#if NET4 || NET35

        /// <summary>
        /// The localEndPoint cast as <see cref="IPEndPoint"/>.
        /// </summary>
        internal BluetoothEndPoint LocalBTEndPoint
        {
            get
            {
                try
                {
                    return (BluetoothEndPoint)LocalEndPoint;
                }
                catch (InvalidCastException ex)
                {
                    throw new InvalidCastException("Unable to cast LocalEndPoint to IPEndPoint.", ex);
                }
            }
        }

        /// <summary>
        /// The remoteEndPoint cast as <see cref="IPEndPoint"/>.
        /// </summary>
        internal BluetoothEndPoint RemoteBTEndPoint
        {
            get
            {
                try
                {
                    return (BluetoothEndPoint)RemoteEndPoint;
                }
                catch (InvalidCastException ex)
                {
                    throw new InvalidCastException("Unable to cast LocalEndPoint to IPEndPoint.", ex);
                }
            }
        }
#endif

        #endregion

        /// <summary>
        /// Private constructor required for deserialisation.
        /// </summary>
#if iOS || ANDROID
        public ConnectionInfo() { }
#else
        private ConnectionInfo() { }
#endif

        /// <summary>
        /// Create a new ConnectionInfo object pointing at the provided remote <see cref="IPEndPoint"/>.
        /// Uses the custom NetworkComms.Net application layer protocol.
        /// </summary>
        /// <param name="remoteEndPoint">The end point corresponding with the remote target</param>
        public ConnectionInfo(EndPoint remoteEndPoint)
        {
            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = new IPEndPoint((RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any), 0);
            this.ConnectionCreationTime = DateTime.Now;
            this.ApplicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled;
        }

        /// <summary>
        /// Create a new ConnectionInfo object pointing at the provided remote <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="remoteEndPoint">The end point corresponding with the remote target</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        public ConnectionInfo(EndPoint remoteEndPoint, ApplicationLayerProtocolStatus applicationLayerProtocol)
        {
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined)
                throw new ArgumentException("A value of ApplicationLayerProtocolStatus.Undefined is invalid when creating instance of ConnectionInfo.", "applicationLayerProtocol");

            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = new IPEndPoint((RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any), 0);
            this.ConnectionCreationTime = DateTime.Now;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
        }

        /// <summary>
        /// Create a new ConnectionInfo object pointing at the provided remote ipAddress and port. 
        /// Provided ipAddress and port are parsed in to <see cref="RemoteEndPoint"/>. Uses the 
        /// custom NetworkComms.Net application layer protocol.
        /// </summary>
        /// <param name="remoteIPAddress">IP address of the remote target in string format, e.g. "192.168.0.1"</param>
        /// <param name="remotePort">The available port of the remote target. 
        /// Valid ports are 1 through 65535. Port numbers less than 256 are reserved for well-known services (like HTTP on port 80) and port numbers less than 1024 generally require admin access</param>
        public ConnectionInfo(string remoteIPAddress, int remotePort)
        {
            IPAddress ipAddress;
            if (!IPAddress.TryParse(remoteIPAddress, out ipAddress))
                throw new ArgumentException("Provided remoteIPAddress string was not successfully parsed.", "remoteIPAddress");

            this.RemoteEndPoint = new IPEndPoint(ipAddress, remotePort);
            this.LocalEndPoint = new IPEndPoint((RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any), 0);
            this.ConnectionCreationTime = DateTime.Now;
            this.ApplicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled;
        }

        /// <summary>
        /// Create a new ConnectionInfo object pointing at the provided remote ipAddress and port. 
        /// Provided ipAddress and port are parsed in to <see cref="RemoteEndPoint"/>.
        /// </summary>
        /// <param name="remoteIPAddress">IP address of the remote target in string format, e.g. "192.168.0.1"</param>
        /// <param name="remotePort">The available port of the remote target. 
        /// Valid ports are 1 through 65535. Port numbers less than 256 are reserved for well-known services (like HTTP on port 80) and port numbers less than 1024 generally require admin access</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        public ConnectionInfo(string remoteIPAddress, int remotePort, ApplicationLayerProtocolStatus applicationLayerProtocol)
        {
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined)
                throw new ArgumentException("A value of ApplicationLayerProtocolStatus.Undefined is invalid when creating instance of ConnectionInfo.", "applicationLayerProtocol");

            IPAddress ipAddress;
            if (!IPAddress.TryParse(remoteIPAddress, out ipAddress))
                throw new ArgumentException("Provided remoteIPAddress string was not successfully parsed.", "remoteIPAddress");

            this.RemoteEndPoint = new IPEndPoint(ipAddress, remotePort);
            this.LocalEndPoint = new IPEndPoint((RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any), 0);
            this.ConnectionCreationTime = DateTime.Now;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
        }

        /// <summary>
        /// Create a connectionInfo object which can be used to inform a remote peer of local connectivity.
        /// Uses the custom NetworkComms.Net application layer protocol.
        /// </summary>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="localNetworkIdentifier">The local network identifier</param>
        /// <param name="localEndPoint">The localEndPoint which should be referenced remotely</param>
        /// <param name="isConnectable">True if connectable on provided localEndPoint</param>
        public ConnectionInfo(ConnectionType connectionType, ShortGuid localNetworkIdentifier, EndPoint localEndPoint, bool isConnectable)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException("localEndPoint", "localEndPoint may not be null");

            this.ConnectionType = connectionType;
            this.NetworkIdentifierStr = localNetworkIdentifier.ToString();
            this.RemoteEndPoint = new IPEndPoint((localEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any), 0);
            this.LocalEndPoint = localEndPoint;
            this.IsConnectable = isConnectable;
            this.ApplicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled;
        }

        /// <summary>
        /// Create a connectionInfo object which can be used to inform a remote peer of local connectivity
        /// </summary>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="localNetworkIdentifier">The local network identifier</param>
        /// <param name="localEndPoint">The localEndPoint which should be referenced remotely</param>
        /// <param name="isConnectable">True if connectable on provided localEndPoint</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        public ConnectionInfo(ConnectionType connectionType, ShortGuid localNetworkIdentifier, EndPoint localEndPoint, bool isConnectable, ApplicationLayerProtocolStatus applicationLayerProtocol)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException("localEndPoint", "localEndPoint may not be null");

            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined)
                throw new ArgumentException("A value of ApplicationLayerProtocolStatus.Undefined is invalid when creating instance of ConnectionInfo.", "applicationLayerProtocol");

            this.ConnectionType = connectionType;
            this.NetworkIdentifierStr = localNetworkIdentifier.ToString();
            this.LocalEndPoint = localEndPoint;
            this.RemoteEndPoint = new IPEndPoint((localEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any), 0);
            this.IsConnectable = isConnectable;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
        }

        /// <summary>
        /// Create a connectionInfo object for a new connection.
        /// </summary>
        /// <param name="serverSide">True if this connection is being created serverSide</param>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="remoteEndPoint">The remoteEndPoint of this connection</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        internal ConnectionInfo(bool serverSide, ConnectionType connectionType, EndPoint remoteEndPoint, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled)
        {
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined)
                throw new ArgumentException("A value of ApplicationLayerProtocolStatus.Undefined is invalid when creating instance of ConnectionInfo.", "applicationLayerProtocol");

            this.ServerSide = serverSide;
            this.ConnectionType = connectionType;
            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = new IPEndPoint((remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any), 0);
            this.ConnectionCreationTime = DateTime.Now;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
        }

        /// <summary>
        /// Create a connectionInfo object for a new connection.
        /// </summary>
        /// <param name="serverSide">True if this connection is being created serverSide</param>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="remoteEndPoint">The remoteEndPoint of this connection</param>
        /// <param name="localEndPoint">The localEndpoint of this connection</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        internal ConnectionInfo(bool serverSide, ConnectionType connectionType, EndPoint remoteEndPoint, EndPoint localEndPoint, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException("localEndPoint", "localEndPoint may not be null");

            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined)
                throw new ArgumentException("A value of ApplicationLayerProtocolStatus.Undefined is invalid when creating instance of ConnectionInfo.", "applicationLayerProtocol");

            this.ServerSide = serverSide;
            this.ConnectionType = connectionType;
            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = localEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
        }

        [ProtoBeforeSerialization]
        private void OnSerialise()
        {
            lock (internalLocker)
            {
                if (LocalEndPoint as IPEndPoint != null)
                {
                    localEndPointAddressStr = LocalIPEndPoint.Address.ToString();
                    localEndPointPort = LocalIPEndPoint.Port;
                }

#if NET4 || NET35
                if (LocalEndPoint as InTheHand.Net.BluetoothEndPoint != null)
                {
                    localEndPointAddressStr = LocalBTEndPoint.Address.ToString();
                    localEndPointPort = LocalBTEndPoint.Port;
                }
#endif
            }
        }

        [ProtoAfterDeserialization]
        private void OnDeserialise()
        {   
#if NET4 || NET35
            if (ConnectionType == NetworkCommsDotNet.ConnectionType.Bluetooth)
            {
                BluetoothAddress btAddress;
                if(!BluetoothAddress.TryParse(localEndPointAddressStr, out btAddress))
                    throw new ArgumentException("Failed to parse BluetoothAddress from localEndPointAddressStr", "localEndPointAddressStr");

                LocalEndPoint = new BluetoothEndPoint(btAddress, BluetoothService.SerialPort, localEndPointPort);
                return;
            }
#endif

            IPAddress ipAddress;
            if (!IPAddress.TryParse(localEndPointAddressStr, out ipAddress))
                throw new ArgumentException("Failed to parse IPAddress from localEndPointAddressStr", "localEndPointAddressStr");

            LocalEndPoint = new IPEndPoint(ipAddress, localEndPointPort);
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

                if (ConnectionState == ConnectionState.Established) throw new ConnectionSetupException("Connection already marked as established.");

                ConnectionState = ConnectionState.Established;
                ConnectionEstablishedTime = DateTime.Now;

                //The below only really applied to TCP connections
                //We only expect a remote network identifier for managed connections
                //if (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled && NetworkIdentifier == ShortGuid.Empty)
                //    throw new ConnectionSetupException("Remote network identifier should have been set by this point.");
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
        internal void UpdateLocalEndPointInfo(EndPoint localEndPoint)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException("localEndPoint", "localEndPoint may not be null.");

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
        internal void UpdateInfoAfterRemoteHandshake(ConnectionInfo handshakeInfo, EndPoint remoteEndPoint)
        {
            lock (internalLocker)
            {
                NetworkIdentifierStr = handshakeInfo.NetworkIdentifier.ToString();
                RemoteEndPoint = remoteEndPoint;

                //Not sure what this section was supposed to do
                //It certainly creates a bug at the moment
                //if (LocalEndPoint.GetType() == typeof(IPEndPoint) && handshakeInfo.LocalEndPoint.GetType() == typeof(IPEndPoint))
                //    ((IPEndPoint)LocalEndPoint).Address = ((IPEndPoint)handshakeInfo.LocalEndPoint).Address;
                //else
                //    throw new NotImplementedException("UpdateInfoAfterRemoteHandshake not implemented for EndPoints of type " + LocalEndPoint.GetType());

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
        /// Compares this <see cref="ConnectionInfo"/> object with obj and returns true if obj is ConnectionInfo and both 
        /// the <see cref="NetworkIdentifier"/> and <see cref="RemoteEndPoint"/> match.
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
        /// Compares this <see cref="ConnectionInfo"/> object with other and returns true if both the <see cref="NetworkIdentifier"/> 
        /// and <see cref="RemoteEndPoint"/> match.
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
                if (left.RemoteEndPoint != null && right.RemoteEndPoint != null && left.LocalEndPoint != null && right.LocalEndPoint != null)
                    return (left.NetworkIdentifier.ToString() == right.NetworkIdentifier.ToString() && left.RemoteEndPoint.Equals(right.RemoteEndPoint) && left.LocalEndPoint.Equals(right.LocalEndPoint) && left.ApplicationLayerProtocol == right.ApplicationLayerProtocol);
                if (left.RemoteEndPoint != null && right.RemoteEndPoint != null)
                    return (left.NetworkIdentifier.ToString() == right.NetworkIdentifier.ToString() && left.RemoteEndPoint.Equals(right.RemoteEndPoint) && left.ApplicationLayerProtocol == right.ApplicationLayerProtocol);
                else if (left.LocalEndPoint != null && right.LocalEndPoint != null)
                    return (left.NetworkIdentifier.ToString() == right.NetworkIdentifier.ToString() && left.LocalEndPoint.Equals(right.LocalEndPoint) && left.ApplicationLayerProtocol == right.ApplicationLayerProtocol);
                else
                    return (left.NetworkIdentifier.ToString() == right.NetworkIdentifier.ToString() && left.ApplicationLayerProtocol==right.ApplicationLayerProtocol);
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
                    if (RemoteEndPoint != null & LocalEndPoint != null)
                        hashCodeCache = NetworkIdentifier.GetHashCode() ^ LocalEndPoint.GetHashCode() ^ RemoteEndPoint.GetHashCode() ^ (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? 1 << 31 : 0);
                    if (RemoteEndPoint != null)
                        hashCodeCache = NetworkIdentifier.GetHashCode() ^ RemoteEndPoint.GetHashCode() ^ (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? 1 << 31 : 0);
                    else if (LocalEndPoint != null)
                        hashCodeCache = NetworkIdentifier.GetHashCode() ^ LocalEndPoint.GetHashCode() ^ (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? 1 << 31 : 0);
                    else
                        hashCodeCache = NetworkIdentifier.GetHashCode() ^ (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? 1 << 31 : 0);

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
            string returnString = "[" + ConnectionType.ToString() + "-" + (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? "E" : "D") + "] ";

            if (RemoteEndPoint != null && LocalEndPoint != null)
                returnString += LocalEndPoint.ToString() + " -> " + RemoteEndPoint.ToString();
            else if (RemoteEndPoint != null)
                returnString += "Local -> " + RemoteEndPoint.ToString();
            else if (LocalEndPoint != null)
                returnString += LocalEndPoint.ToString() + " " + (IsConnectable ? "Connectable" : "NotConnectable");

            if (NetworkIdentifier != ShortGuid.Empty)
                returnString += " (" + NetworkIdentifier + ")";

            return returnString.Trim();
        }
    }
}
