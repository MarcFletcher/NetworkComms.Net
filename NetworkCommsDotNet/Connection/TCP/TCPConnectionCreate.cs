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
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using DPSBase;

#if WINDOWS_PHONE
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
#endif

namespace NetworkCommsDotNet
{
    public sealed partial class TCPConnection : Connection
    {
#if WINDOWS_PHONE
        /// <summary>
        /// The windows phone socket corresponding to this connection.
        /// </summary>
        StreamSocket socket;
#else
        /// <summary>
        /// The TcpClient corresponding to this connection.
        /// </summary>
        TcpClient tcpClient;

        /// <summary>
        /// The networkstream associated with the tcpClient.
        /// </summary>
        NetworkStream tcpClientNetworkStream;
#endif

        /// <summary>
        /// Create a <see cref="TCPConnection"/> with the provided connectionInfo. If there is an existing connection that will be returned instead. 
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static TCPConnection GetConnection(ConnectionInfo connectionInfo, bool establishIfRequired = true)
        {
            if (connectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
                return GetConnection(connectionInfo, null, null, establishIfRequired);
            else
            {
                //For unmanaged connections we need to make sure that the NullSerializer is being used.
                SendReceiveOptions optionsToUse = NetworkComms.DefaultSendReceiveOptions;

                //If the default data serializer is not NullSerializer we create custom options for unmanaged connections.
                if (optionsToUse.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    optionsToUse = new SendReceiveOptions<NullSerializer>();

                return GetConnection(connectionInfo, optionsToUse, null, establishIfRequired);
            }
        }

        /// <summary>
        /// Create a TCP connection with the provided connectionInfo and sets the connection default SendReceiveOptions. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which will be set as this connections defaults</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static TCPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, bool establishIfRequired = true)
        {
            if (connectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled && defaultSendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                throw new ConnectionSetupException("Attempted to get connection where ApplicationLayerProtocolEnabled is false and the provided serializer is not NullSerializer.");

            return GetConnection(connectionInfo, defaultSendReceiveOptions, null, establishIfRequired);
        }

        /// <summary>
        /// Internal <see cref="TCPConnection"/> creation which hides the necessary internal calls
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">Connection default SendReceiveOptions</param>
        /// <param name="tcpClient">If this is an incoming connection we will already have access to the tcpClient, otherwise use null</param>
        /// <param name="establishIfRequired">Establish during create if true</param>
        /// <returns>An existing connection or a new one</returns>
#if WINDOWS_PHONE
        internal static TCPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, StreamSocket socket, bool establishIfRequired = true)
#else
        internal static TCPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, TcpClient tcpClient, bool establishIfRequired = true)
#endif
        {
            if (connectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled && defaultSendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                throw new ConnectionSetupException("Attempted to get connection where ApplicationLayerProtocolEnabled is false and the provided serializer is not NullSerializer.");

            connectionInfo.ConnectionType = ConnectionType.TCP;

            //If we have a tcpClient at this stage we must be serverside
#if WINDOWS_PHONE
             if (socket != null) connectionInfo.ServerSide = true;
#else
            if (tcpClient != null) connectionInfo.ServerSide = true;
#endif

            bool newConnection = false;
            TCPConnection connection;

            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                List<Connection> existingConnections = NetworkComms.GetExistingConnection(connectionInfo.RemoteEndPoint, connectionInfo.LocalEndPoint, connectionInfo.ConnectionType, connectionInfo.ApplicationLayerProtocol);

                //Check to see if a conneciton already exists, if it does return that connection, if not return a new one
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
#if WINDOWS_PHONE
                    connection = new TCPConnection(connectionInfo, defaultSendReceiveOptions, socket);
#else
                    connection = new TCPConnection(connectionInfo, defaultSendReceiveOptions, tcpClient);
#endif
                    newConnection = true;
                }
            }

            if (newConnection && establishIfRequired) connection.EstablishConnection(); 
            else if (!newConnection) connection.WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS);

            if (!NetworkComms.commsShutdown) TriggerConnectionKeepAliveThread();

            return connection;
        }

        /// <summary>
        /// TCP connection constructor
        /// </summary>
#if WINDOWS_PHONE
        private TCPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, StreamSocket socket)
#else
        private TCPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, TcpClient tcpClient)
#endif
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            //We don't guarantee that the tcpClient has been created yet
#if WINDOWS_PHONE
            if (socket != null) this.socket = socket;
#else
            if (tcpClient != null) this.tcpClient = tcpClient;
#endif  
        }

        /// <summary>
        /// Establish the connection
        /// </summary>
        protected override void EstablishConnectionSpecific()
        {
#if WINDOWS_PHONE
            if (socket == null) ConnectSocket();

            //For the local endpoint
            var localEndPoint = new IPEndPoint(IPAddress.Parse(socket.Information.LocalAddress.CanonicalName.ToString()), int.Parse(socket.Information.LocalPort));

            //We should now be able to set the connectionInfo localEndPoint
            NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteEndPoint, localEndPoint);
            ConnectionInfo.UpdateLocalEndPointInfo(localEndPoint);

            //Set the outgoing buffer size
            socket.Control.OutboundBufferSizeInBytes = (uint)NetworkComms.SendBufferSizeBytes;
#else
            if (tcpClient == null) ConnectSocket();

            //We should now be able to set the connectionInfo localEndPoint
            NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteEndPoint, (IPEndPoint)tcpClient.Client.LocalEndPoint);
            ConnectionInfo.UpdateLocalEndPointInfo((IPEndPoint)tcpClient.Client.LocalEndPoint);

            //We are going to be using the networkStream quite a bit so we pull out a reference once here
            tcpClientNetworkStream = tcpClient.GetStream();

            //When we tell the socket/client to close we want it to do so immediately
            //this.tcpClient.LingerState = new LingerOption(false, 0);

            //We need to set the keep alive option otherwise the connection will just die at some random time should we not be using it
            //NOTE: This did not seem to work reliably so was replaced with the keepAlive packet feature
            //this.tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            tcpClient.ReceiveBufferSize = NetworkComms.ReceiveBufferSizeBytes;
            tcpClient.SendBufferSize = NetworkComms.SendBufferSizeBytes;

            //This disables the 'nagle alogrithm'
            //http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.nodelay.aspx
            //Basically we may want to send lots of small packets (<200 bytes) and sometimes those are time critical (e.g. when establishing a connection)
            //If we leave this enabled small packets may never be sent until a suitable send buffer length threshold is passed. i.e. BAD
            tcpClient.NoDelay = true;
            tcpClient.Client.NoDelay = true;
#endif

            //Start listening for incoming data
            StartIncomingDataListen();

            //If the application layer protocol is enabled we handshake the connection
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
                ConnectionHandshake();
            else
            {
                //If there is no handshake we can now consider the connection established
                TriggerConnectionEstablishDelegates();

                //Trigger any connection setup waits
                connectionSetupWait.Set();
            }

#if !WINDOWS_PHONE
            //Once the connection has been established we may want to re-enable the 'nagle algorithm' used for reducing network congestion (apparently).
            //By default we leave the nagle algorithm disabled because we want the quick through put when sending small packets
            if (EnableNagleAlgorithmForNewConnections)
            {
                tcpClient.NoDelay = false;
                tcpClient.Client.NoDelay = false;
            }
#endif
        }

        /// <summary>
        /// If we were not provided with a tcpClient on creation we need to create one
        /// </summary>
        private void ConnectSocket()
        {
            try
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Connecting TCP client with " + ConnectionInfo);

                bool connectSuccess = true;
#if WINDOWS_PHONE
                //We now connect to our target
                socket = new StreamSocket();
                socket.Control.NoDelay = EnableNagleAlgorithmForNewConnections;

                CancellationTokenSource cancelAfterTimeoutToken = new CancellationTokenSource(NetworkComms.ConnectionEstablishTimeoutMS);

                try
                {
                    if (ConnectionInfo.LocalEndPoint != null)
                    {
                        var endpointPairForConnection = new Windows.Networking.EndpointPair(new Windows.Networking.HostName(ConnectionInfo.LocalEndPoint.Address.ToString()), ConnectionInfo.LocalEndPoint.Port.ToString(),
                                                        new Windows.Networking.HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString());

                        var task = socket.ConnectAsync(endpointPairForConnection).AsTask(cancelAfterTimeoutToken.Token);
                        task.Wait();
                    }
                    else
                    {
                        var task = socket.ConnectAsync(new Windows.Networking.HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString()).AsTask(cancelAfterTimeoutToken.Token);
                        task.Wait();
                    }
                }
                catch (Exception)
                {
                    socket.Dispose();
                    connectSuccess = false;
                }
#else
                //We now connect to our target
                tcpClient = new TcpClient(ConnectionInfo.RemoteEndPoint.AddressFamily);

                //Start the connection using the asyn version
                //This allows us to choose our own connection establish timeout
                IAsyncResult ar = tcpClient.BeginConnect(ConnectionInfo.RemoteEndPoint.Address, ConnectionInfo.RemoteEndPoint.Port, null, null);
                WaitHandle connectionWait = ar.AsyncWaitHandle;
                try
                {
                    if (!ar.AsyncWaitHandle.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS, false))
                    {
                        tcpClient.Close();
                        connectSuccess = false;
                    }

                    tcpClient.EndConnect(ar);
                }
                finally
                {
                    connectionWait.Close();
                }
#endif

                if (!connectSuccess) throw new ConnectionSetupException("Timeout waiting for remoteEndPoint to accept TCP connection.");
            }
            catch (Exception ex)
            {
                CloseConnection(true, 17);
                throw new ConnectionSetupException("Error during TCP connection establish with destination (" + ConnectionInfo + "). Destination may not be listening or connect timed out. " + ex.ToString());
            }
        }

        /// <summary>
        /// Starts listening for incoming data on this TCP connection
        /// </summary>
        protected override void StartIncomingDataListen()
        {
            if (!NetworkComms.ConnectionExists(ConnectionInfo.RemoteEndPoint, ConnectionInfo.LocalEndPoint, ConnectionType.TCP, ConnectionInfo.ApplicationLayerProtocol))
            {
                CloseConnection(true, 18);
                throw new ConnectionSetupException("A connection reference by endPoint should exist before starting an incoming data listener.");
            }

#if WINDOWS_PHONE
            var stream = socket.InputStream.AsStreamForRead();
            stream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingTCPPacketHandler), stream);   
#else
            lock (delegateLocker)
            {
                if (NetworkComms.ConnectionListenModeUseSync)
                {
                    if (incomingDataListenThread == null)
                    {
                        incomingDataListenThread = new Thread(IncomingTCPDataSyncWorker);
                        //Incoming data always gets handled in a time critical fashion
                        incomingDataListenThread.Priority = NetworkComms.timeCriticalThreadPriority;
                        incomingDataListenThread.Name = "IncomingDataListener";
                        incomingDataListenThread.Start();
                    }
                }
                else
                    tcpClientNetworkStream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingTCPPacketHandler), tcpClientNetworkStream);
            }
#endif

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Listening for incoming data from " + ConnectionInfo);
        }
    }
}