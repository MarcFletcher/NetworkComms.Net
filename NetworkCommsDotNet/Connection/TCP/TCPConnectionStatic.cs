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
using System.Threading;
using System.Net;
using System.IO;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

#if WINDOWS_PHONE || NETFX_CORE
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
#endif

namespace NetworkCommsDotNet.Connections.TCP
{
    public sealed partial class TCPConnection : IPConnection
    {
        /// <summary>
        /// By default usage of <see href="http://en.wikipedia.org/wiki/Nagle's_algorithm">Nagle's algorithm</see> during TCP exchanges is disabled for performance reasons. If you wish it to be used for newly established connections set this property to true.
        /// </summary>
        public static bool EnableNagleAlgorithmForNewConnections { get; set; }

        #region GetConnection
        /// <summary>
        /// Create a <see cref="TCPConnection"/> with the provided connectionInfo. If there is an existing connection that will be returned instead. 
        /// If a new connection is created it will be registered with NetworkComms and can be retrieved using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static TCPConnection GetConnection(ConnectionInfo connectionInfo, bool establishIfRequired = true)
        {
            return GetConnection(connectionInfo, null, null, establishIfRequired);
        }

        /// <summary>
        /// Create a TCP connection with the provided connectionInfo and sets the connection default SendReceiveOptions. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retrieved using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which will be set as this connections defaults</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static TCPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, bool establishIfRequired = true)
        {
            return GetConnection(connectionInfo, defaultSendReceiveOptions, null, establishIfRequired);
        }

#if !WINDOWS_PHONE && !NETFX_CORE
        /// <summary>
        /// Create a TCP connection with the provided connectionInfo and sets the connection default SendReceiveOptions. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retrieved using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which will be set as this connections defaults</param>
        /// <param name="sslOptions">SSLOptions to use with this connection</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static TCPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, SSLOptions sslOptions, bool establishIfRequired = true)
        {
            return GetConnection(connectionInfo, defaultSendReceiveOptions, null, establishIfRequired, sslOptions);
        }
#endif
  
#if WINDOWS_PHONE || NETFX_CORE
        /// <summary>
        /// Internal <see cref="TCPConnection"/> creation which hides the necessary internal calls
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">Connection default SendReceiveOptions</param>
        /// <param name="socket">If this is an incoming connection we will already have access to the socket, otherwise use null</param>
        /// <param name="establishIfRequired">Establish during create if true</param>
        /// <returns>An existing connection or a new one</returns>
        internal static TCPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, StreamSocket socket, bool establishIfRequired)
#else
        /// <summary>
        /// Internal <see cref="TCPConnection"/> creation which hides the necessary internal calls
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">Connection default SendReceiveOptions</param>
        /// <param name="tcpClient">If this is an incoming connection we will already have access to the tcpClient, otherwise use null</param>
        /// <param name="establishIfRequired">Establish during create if true</param>
        /// <param name="sslOptions">SSL options that will be used with this connection.</param>
        /// <returns>An existing connection or a new one</returns>
        internal static TCPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, TcpClient tcpClient, bool establishIfRequired, SSLOptions sslOptions = null)
#endif
        {
            connectionInfo.ConnectionType = ConnectionType.TCP;

            //If we have a tcpClient at this stage we must be server side
#if WINDOWS_PHONE || NETFX_CORE
             if (socket != null) connectionInfo.ServerSide = true;
#else
            if (tcpClient != null) connectionInfo.ServerSide = true;
            if (sslOptions == null) sslOptions = new SSLOptions();
#endif

            //Set default connection options if none have been provided
            if (defaultSendReceiveOptions == null) defaultSendReceiveOptions = NetworkComms.DefaultSendReceiveOptions;

            bool newConnection = false;
            TCPConnection connection;

            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                List<Connection> existingConnections = NetworkComms.GetExistingConnection(connectionInfo.RemoteIPEndPoint, connectionInfo.LocalIPEndPoint, connectionInfo.ConnectionType, connectionInfo.ApplicationLayerProtocol);

                //Check to see if a connection already exists, if it does return that connection, if not return a new one
                if (existingConnections.Count > 0)
                {
                    if (NetworkComms.LoggingEnabled)
                        NetworkComms.Logger.Trace("Attempted to create new TCPConnection to connectionInfo='" + connectionInfo + "' but there is an existing connection. Existing connection will be returned instead.");

                    establishIfRequired = false;
                    connection = (TCPConnection)existingConnections[0];
                }
                else
                {
                    if (NetworkComms.LoggingEnabled)
                        NetworkComms.Logger.Trace("Creating new TCPConnection to connectionInfo='" + connectionInfo + "'." + (establishIfRequired ? " Connection will be established." : " Connection will not be established."));

                    if (connectionInfo.ConnectionState == ConnectionState.Establishing)
                        throw new ConnectionSetupException("Connection state for connection " + connectionInfo + " is marked as establishing. This should only be the case here due to a bug.");

                    //If an existing connection does not exist but the info we are using suggests it should we need to reset the info
                    //so that it can be reused correctly. This case generally happens when using Comms in the format 
                    //TCPConnection.GetConnection(info).SendObject(packetType, objToSend);
                    if (connectionInfo.ConnectionState == ConnectionState.Established || connectionInfo.ConnectionState == ConnectionState.Shutdown)
                        connectionInfo.ResetConnectionInfo();

                    //We add a reference to networkComms for this connection within the constructor
#if WINDOWS_PHONE || NETFX_CORE
                    connection = new TCPConnection(connectionInfo, defaultSendReceiveOptions, socket);
#else
                    connection = new TCPConnection(connectionInfo, defaultSendReceiveOptions, tcpClient, sslOptions);
#endif
                    newConnection = true;
                }
            }

            if (newConnection && establishIfRequired) connection.EstablishConnection(); 
            else if (!newConnection) connection.WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS);

            if (!NetworkComms.commsShutdown) TriggerConnectionKeepAliveThread();

            return connection;
        }
        #endregion

        #region Depreciated
        /// <summary>
        /// Accept new incoming TCP connections on all allowed IP's and Port's
        /// </summary>
        /// <param name="useRandomPortFailOver">If true and the default local port is not available will select one at random. If false and a port is unavailable listening will not be enabled on that adaptor</param>
        [Obsolete("Depreciated, please use Connection.StartListening.")]
        public static void StartListening(bool useRandomPortFailOver = false)
        {
            List<IPAddress> localIPs = HostInfo.IP.FilteredLocalAddresses();

            try
            {
                foreach (IPAddress ip in localIPs)
                {
                    try
                    {
                        StartListening(new IPEndPoint(ip, 0), useRandomPortFailOver);
                    }
                    catch (CommsSetupShutdownException)
                    {

                    }
                }
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Accept new TCP connections on specified list of <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="localEndPoints">The localEndPoints to listen for connections on</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available on a given IPEndPoint will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        [Obsolete("Depreciated, please use Connection.StartListening.")]
        public static void StartListening(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
            if (localEndPoints == null) throw new ArgumentNullException("localEndPoints", "Provided List<IPEndPoint> cannot be null.");

            try
            {
                foreach (var endPoint in localEndPoints)
                    StartListening(endPoint, useRandomPortFailOver);
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Accept new incoming TCP connections on specified <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="newLocalEndPoint">The localEndPoint to listen for connections on.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        [Obsolete("Depreciated, please use Connection.StartListening.")]
        public static void StartListening(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true, bool makeDiscoverable = false)
        {
            TCPConnectionListener listener = new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, makeDiscoverable);
            Connection.StartListening(listener, newLocalEndPoint, useRandomPortFailOver);
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding to all current TCP local listeners
        /// </summary>
        /// <returns>List of <see cref="IPEndPoint"/> corresponding to all current TCP local listeners</returns>
        [Obsolete("Depreciated, please use Connection.ExistingLocalListenEndPoints.")]
        public static List<IPEndPoint> ExistingLocalListenEndPoints()
        {
            List<IPEndPoint> result = new List<IPEndPoint>();
            foreach (IPEndPoint endPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP))
                result.Add(endPoint);

            return result;
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding to a possible local listeners on the provided <see cref="IPAddress"/>. If not listening on provided <see cref="IPAddress"/> returns empty list.
        /// </summary>
        /// <param name="ipAddress">The <see cref="IPAddress"/> to match to a possible local listener</param>
        /// <returns>If listener exists returns <see cref="IPAddress"/> otherwise null</returns>
        [Obsolete("Depreciated, please use Connection.ExistingLocalListenEndPoints.")]
        public static List<IPEndPoint> ExistingLocalListenEndPoints(IPAddress ipAddress)
        {
            List<IPEndPoint> result = new List<IPEndPoint>();
            foreach (IPEndPoint endPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP, new IPEndPoint(ipAddress, 0)))
                result.Add(endPoint);

            return result;
        }

        /// <summary>
        /// Returns true if listening for new TCP connections.
        /// </summary>
        /// <returns>True if listening for new TCP connections.</returns>
        [Obsolete("Depreciated, please use Connection.Listening.")]
        public static bool Listening()
        {
            return Connection.Listening(ConnectionType.TCP);
        }
        #endregion
    }
}