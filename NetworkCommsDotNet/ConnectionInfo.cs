// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.Connections;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

#if NET4 || NET35
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
#endif

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = Foundation.PreserveAttribute;
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
        /// The connection is in the process of being established/initialised.
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
    public class ConnectionInfo : IEquatable<ConnectionInfo>, IExplicitlySerialize
    {
        /// <summary>
        /// The type of this connection
        /// </summary>
        public ConnectionType ConnectionType { get; internal set; }

        /// <summary>
        /// We store our unique peer identifier as a string so that it can be easily serialised.
        /// </summary>
        string NetworkIdentifierStr;

        string localEndPointAddressStr; //Only set on serialise
        int localEndPointPort; //Only set on serialise

        bool hashCodeCacheSet = false;
        int hashCodeCache;

        /// <summary>
        /// True if the <see cref="RemoteEndPoint"/> is connectable.
        /// </summary>
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
        /// If this connection is <see cref="ServerSide"/> references the listener that was used.
        /// </summary>
        public ConnectionListenerBase ConnectionListener { get; internal set; }

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
#if ANDROID || iOS
        [Preserve]
#endif
        private ConnectionInfo() { }

        /// <summary>
        /// Create a new ConnectionInfo object pointing at the provided remote <see cref="IPEndPoint"/>.
        /// Uses the custom NetworkComms.Net application layer protocol.
        /// </summary>
        /// <param name="remoteEndPoint">The end point corresponding with the remote target</param>
        public ConnectionInfo(EndPoint remoteEndPoint)
        {
            this.RemoteEndPoint = remoteEndPoint;
            
            switch (remoteEndPoint.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    break;
                case AddressFamily.InterNetworkV6:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                    break;
#if NET4 || NET35
                case (AddressFamily)32:
                    this.LocalEndPoint = new BluetoothEndPoint(BluetoothAddress.None, BluetoothService.SerialPort);
                    break;
#endif
            }
            
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
            
            switch (remoteEndPoint.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    break;
                case AddressFamily.InterNetworkV6:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                    break;
#if NET4 || NET35
                case (AddressFamily)32:
                    this.LocalEndPoint = new BluetoothEndPoint(BluetoothAddress.None, BluetoothService.SerialPort);
                    break;
#endif
            }

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
            
            switch (this.RemoteEndPoint.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    break;
                case AddressFamily.InterNetworkV6:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                    break;
#if NET4 || NET35
                case (AddressFamily)32:
                    this.LocalEndPoint = new BluetoothEndPoint(BluetoothAddress.None, BluetoothService.SerialPort);
                    break;
#endif
            }

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
            
            switch (this.RemoteEndPoint.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    break;
                case AddressFamily.InterNetworkV6:
                    this.LocalEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                    break;
#if NET4 || NET35
                case (AddressFamily)32:
                    this.LocalEndPoint = new BluetoothEndPoint(BluetoothAddress.None, BluetoothService.SerialPort);
                    break;
#endif
            }

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

            switch (localEndPoint.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    this.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    break;
                case AddressFamily.InterNetworkV6:
                    this.RemoteEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                    break;
#if NET4 || NET35
                case (AddressFamily)32:
                    this.RemoteEndPoint = new BluetoothEndPoint(BluetoothAddress.None, BluetoothService.SerialPort);
                    break;
#endif
            }

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

            switch (localEndPoint.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    this.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    break;
                case AddressFamily.InterNetworkV6:
                    this.RemoteEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                    break;
#if NET4 || NET35
                case (AddressFamily)32:
                    this.RemoteEndPoint = new BluetoothEndPoint(BluetoothAddress.None, BluetoothService.SerialPort);
                    break;
#endif
            }

            this.IsConnectable = isConnectable;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
        }

        /// <summary>
        /// Create a connectionInfo object for a new connection.
        /// </summary>
        /// <param name="connectionType">The type of connection</param>
        /// <param name="remoteEndPoint">The remoteEndPoint of this connection</param>
        /// <param name="localEndPoint">The localEndpoint of this connection</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="connectionListener">The listener associated with this connection if server side</param>
        internal ConnectionInfo(ConnectionType connectionType, EndPoint remoteEndPoint, EndPoint localEndPoint, 
            ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled, 
            ConnectionListenerBase connectionListener = null)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException("localEndPoint", "localEndPoint may not be null");

            if (remoteEndPoint == null)
                throw new ArgumentNullException("remoteEndPoint", "remoteEndPoint may not be null");

            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined)
                throw new ArgumentException("A value of ApplicationLayerProtocolStatus.Undefined is invalid when creating instance of ConnectionInfo.", "applicationLayerProtocol");

            this.ServerSide = (connectionListener!=null);
            this.ConnectionListener = connectionListener;
            this.ConnectionType = connectionType;
            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = localEndPoint;
            this.ConnectionCreationTime = DateTime.Now;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
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
                //For now we will uncomment and see if there was a reason during testing
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
            //Add a useful connection state identifier
            string connectionStateIdentifier;
            switch (ConnectionState)
            {
                case ConnectionState.Undefined:
                    connectionStateIdentifier = "U";
                    break;
                case ConnectionState.Establishing:
                    connectionStateIdentifier = "I";
                    break;
                case ConnectionState.Established:
                    connectionStateIdentifier = "E";
                    break;
                case ConnectionState.Shutdown:
                    connectionStateIdentifier = "S";
                    break;
                default:
                    throw new Exception("Unexpected connection state.");
            }

            string returnString = "[" + ConnectionType.ToString() + "-" + (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? "E" : "D") + "-" + connectionStateIdentifier + "] ";

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

        #region IExplicitlySerialize Members

        /// <inheritdoc />
        public void Serialize(Stream outputStream)
        {
            List<byte[]> data = new List<byte[]>();

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
                byte[] conTypeData = BitConverter.GetBytes((int)ConnectionType);

                data.Add(conTypeData);

                byte[] netIDData = Encoding.UTF8.GetBytes(NetworkIdentifierStr);
                byte[] netIDLengthData = BitConverter.GetBytes(netIDData.Length);

                data.Add(netIDLengthData);
                data.Add(netIDData);

                byte[] localEPAddreessData = Encoding.UTF8.GetBytes(localEndPointAddressStr);
                byte[] localEPAddreessLengthData = BitConverter.GetBytes(localEPAddreessData.Length);

                data.Add(localEPAddreessLengthData);
                data.Add(localEPAddreessData);

                byte[] localPortData = BitConverter.GetBytes(localEndPointPort);

                data.Add(localPortData);

                byte[] isConnectableData = BitConverter.GetBytes(IsConnectable);

                data.Add(isConnectableData);

                byte[] AppLayerEnabledData = BitConverter.GetBytes((int)ApplicationLayerProtocol);

                data.Add(AppLayerEnabledData);
            }

            foreach (byte[] datum in data)
                outputStream.Write(datum, 0, datum.Length);            
        }

        /// <inheritdoc />
        public void Deserialize(System.IO.Stream inputStream)
        {
            byte[] conTypeData = new byte[sizeof(int)]; inputStream.Read(conTypeData, 0, conTypeData.Length); 
            
            ConnectionType = (ConnectionType)BitConverter.ToInt32(conTypeData, 0);
            
            byte[] netIDLengthData = new byte[sizeof(int)]; inputStream.Read(netIDLengthData, 0, netIDLengthData.Length);
            byte[] netIDData = new byte[BitConverter.ToInt32(netIDLengthData, 0)]; inputStream.Read(netIDData, 0, netIDData.Length); 
            
            NetworkIdentifierStr = new String(Encoding.UTF8.GetChars(netIDData));

            byte[] localEPAddreessLengthData = new byte[sizeof(int)]; inputStream.Read(localEPAddreessLengthData, 0, sizeof(int));
            byte[] localEPAddreessData = new byte[BitConverter.ToInt32(localEPAddreessLengthData, 0)]; inputStream.Read(localEPAddreessData, 0, localEPAddreessData.Length); 
            
            localEndPointAddressStr = new String(Encoding.UTF8.GetChars(localEPAddreessData));

            byte[] localPortData = new byte[sizeof(int)]; inputStream.Read(localPortData, 0, sizeof(int));

            localEndPointPort = BitConverter.ToInt32(localPortData, 0);

            byte[] isConnectableData = new byte[sizeof(int)]; inputStream.Read(isConnectableData, 0, sizeof(bool));
            
            IsConnectable = BitConverter.ToBoolean(isConnectableData, 0);

            byte[] AppLayerEnabledData = new byte[sizeof(int)]; inputStream.Read(AppLayerEnabledData, 0, sizeof(int));

            ApplicationLayerProtocol = (ApplicationLayerProtocolStatus)BitConverter.ToInt32(AppLayerEnabledData, 0);
            
#if NET4 || NET35
            if (ConnectionType == ConnectionType.Bluetooth)
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
        /// Deserializes from a memory stream to a <see cref="ConnectionInfo"/> object
        /// </summary>
        /// <param name="ms">The memory stream containing the serialized <see cref="ConnectionInfo"/></param>
        /// <param name="result">The deserialized <see cref="ConnectionInfo"/></param>
        public static void Deserialize(MemoryStream ms, out ConnectionInfo result)
        {
            result = new ConnectionInfo();
            result.Deserialize(ms);            
        }

        #endregion
    }
}
