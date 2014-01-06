using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using InTheHand.Net.Sockets;
using DPSBase;
using System.Threading;
using InTheHand.Net;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    public sealed partial class BluetoothConnection : Connection
    {
        BluetoothClient btClient;

        NetworkStream btClientNetworkStream;

        /// <summary>
        /// By default usage of <see href="http://en.wikipedia.org/wiki/Nagle's_algorithm">Nagle's algorithm</see> during TCP exchanges is disabled for performance reasons. If you wish it to be used for newly established connections set this property to true.
        /// </summary>
        public static bool EnableNagleAlgorithmForNewConnections { get; set; }

        /// <summary>
        /// Create a <see cref="TCPConnection"/> with the provided connectionInfo. If there is an existing connection that will be returned instead. 
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static BluetoothConnection GetConnection(ConnectionInfo connectionInfo, bool establishIfRequired = true)
        {
            return GetConnection(connectionInfo, null, null, establishIfRequired);
        }

        /// <summary>
        /// Create a TCP connection with the provided connectionInfo and sets the connection default SendReceiveOptions. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which will be set as this connections defaults</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static BluetoothConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, bool establishIfRequired = true)
        {
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
        internal static BluetoothConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, BluetoothClient btClient, bool establishIfRequired = true)
        {
            connectionInfo.ConnectionType = ConnectionType.TCP;

            //If we have a tcpClient at this stage we must be serverside
            if (btClient != null) connectionInfo.ServerSide = true;

            bool newConnection = false;
            BluetoothConnection connection;

            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                List<Connection> existingConnections = NetworkComms.GetExistingConnection(connectionInfo.RemoteEndPoint, connectionInfo.LocalEndPoint, connectionInfo.ConnectionType, connectionInfo.ApplicationLayerProtocol);

                //Check to see if a conneciton already exists, if it does return that connection, if not return a new one
                if (existingConnections.Count > 0)
                {
                    if (NetworkComms.LoggingEnabled)
                        NetworkComms.Logger.Trace("Attempted to create new BluetoothConnection to connectionInfo='" + connectionInfo + "' but there is an existing connection. Existing connection will be returned instead.");

                    establishIfRequired = false;
                    connection = (BluetoothConnection)existingConnections[0];
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
                    connection = new BluetoothConnection(connectionInfo, defaultSendReceiveOptions, btClient);
     
                    newConnection = true;
                }
            }

            if (newConnection && establishIfRequired) connection.EstablishConnection();
            else if (!newConnection) connection.WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS);

            if (!NetworkComms.commsShutdown) TriggerConnectionKeepAliveThread();

            return connection;
        }

        /// <summary>
        /// Bluetooth connection constructor
        /// </summary>
        private BluetoothConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, BluetoothClient btClient)
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            if (btClient != null)
                this.btClient = btClient;            
        }

        /// <summary>
        /// Establish the connection
        /// </summary>
        protected override void EstablishConnectionSpecific()
        {
            if (btClient == null) ConnectSocket();

            //We should now be able to set the connectionInfo localEndPoint
            NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteEndPoint, btClient.Client.LocalEndPoint);
            ConnectionInfo.UpdateLocalEndPointInfo(btClient.Client.LocalEndPoint);
            
            btClient.Client.ReceiveBufferSize = NetworkComms.ReceiveBufferSizeBytes;
            btClient.Client.SendBufferSize = NetworkComms.SendBufferSizeBytes;

            //We are going to be using the networkStream quite a bit so we pull out a reference once here
            btClientNetworkStream = btClient.GetStream();

            //This disables the 'nagle alogrithm'
            //http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.nodelay.aspx
            //Basically we may want to send lots of small packets (<200 bytes) and sometimes those are time critical (e.g. when establishing a connection)
            //If we leave this enabled small packets may never be sent until a suitable send buffer length threshold is passed. i.e. BAD
            btClient.Client.NoDelay = true;

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

            //Once the connection has been established we may want to re-enable the 'nagle algorithm' used for reducing network congestion (apparently).
            //By default we leave the nagle algorithm disabled because we want the quick through put when sending small packets
            if (EnableNagleAlgorithmForNewConnections)
            {
                btClient.Client.NoDelay = false;
            }
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

                //We now connect to our target
                btClient = new BluetoothClient();

                //Start the connection using the asyn version
                //This allows us to choose our own connection establish timeout
                IAsyncResult ar = btClient.BeginConnect((ConnectionInfo.RemoteEndPoint as BluetoothEndPoint), null, null);
                WaitHandle connectionWait = ar.AsyncWaitHandle;
                try
                {
                    if (!ar.AsyncWaitHandle.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS, false))
                    {
                        btClient.Close();
                        connectSuccess = false;
                    }

                    btClient.EndConnect(ar);
                }
                finally
                {
                    connectionWait.Close();
                }

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

            lock (delegateLocker)
            {
                if (NetworkComms.ConnectionListenModeUseSync)
                {
                    if (incomingDataListenThread == null)
                    {
                        incomingDataListenThread = new Thread(IncomingBluetoothDataSyncWorker);
                        //Incoming data always gets handled in a time critical fashion
                        incomingDataListenThread.Priority = NetworkComms.timeCriticalThreadPriority;
                        incomingDataListenThread.Name = "IncomingDataListener";
                        incomingDataListenThread.Start();
                    }
                }
                else
                    btClientNetworkStream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingBluetoothPacketHandler), btClientNetworkStream);
            }

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Listening for incoming data from " + ConnectionInfo);
        }
    }
}
