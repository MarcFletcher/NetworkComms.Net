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
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Threading.Tasks;

namespace NetworkCommsDotNet
{
    public partial class TCPConnection : Connection
    {
        /// <summary>
        /// The TcpClient corresponding to this connection.
        /// </summary>
        TcpClient tcpClient;

        /// <summary>
        /// The networkstream associated with the tcpClient.
        /// </summary>
        NetworkStream tcpClientNetworkStream;

        /// <summary>
        /// Create a <see cref="TCPConnection"/> with the provided connectionInfo. If there is an existing connection that will be returned instead. 
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.RetrieveConnection()"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static TCPConnection CreateConnection(ConnectionInfo connectionInfo, bool establishIfRequired = true)
        {
            return CreateConnection(connectionInfo, null, null, establishIfRequired);
        }

        /// <summary>
        /// Create a TCP connection with the provided connectionInfo and sets the connection default SendReceiveOptions. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.RetrieveConnection()"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which will be set as this connections defaults</param>
        /// <param name="establishIfRequired">If true will establish the TCP connection with the remote end point before returning</param>
        /// <returns>Returns a <see cref="TCPConnection"/></returns>
        public static TCPConnection CreateConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, bool establishIfRequired = true)
        {
            return CreateConnection(connectionInfo, defaultSendReceiveOptions, null, establishIfRequired);
        }

        /// <summary>
        /// Internal <see cref="TCPConnection"/> creation which hides the necessary internal calls
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">Connection default SendReceiveOptions</param>
        /// <param name="tcpClient">If this is an incoming connection we will already have access to the tcpClient, otherwise use null</param>
        /// <param name="establishIfRequired">Establish during create if true</param>
        /// <returns>An existing connection or a new one</returns>
        internal static TCPConnection CreateConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, TcpClient tcpClient, bool establishIfRequired = true)
        {
            connectionInfo.ConnectionType = ConnectionType.TCP;

            //If we have a tcpClient at this stage we must be serverside
            if (tcpClient != null) connectionInfo.ServerSide = true;

            bool newConnection = false;
            TCPConnection connection;

            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                //Check to see if a conneciton already exists, if it does return that connection, if not return a new one
                if (NetworkComms.ConnectionExists(connectionInfo.RemoteEndPoint, connectionInfo.ConnectionType))
                    connection = (TCPConnection)NetworkComms.RetrieveConnection(connectionInfo.RemoteEndPoint, connectionInfo.ConnectionType);
                else
                {
                    //We add a reference to networkComms for this connection within the constructor
                    connection = new TCPConnection(connectionInfo, defaultSendReceiveOptions, tcpClient);
                    newConnection = true;
                }
            }

            if (establishIfRequired)
            {
                if (newConnection) connection.EstablishConnection();
                else  connection.WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS);
            }

            TriggerConnectionKeepAliveThread();

            return connection;
        }

        /// <summary>
        /// TCP connection constructor
        /// </summary>
        private TCPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, TcpClient tcpClient)
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            //We don't guarantee that the tcpClient has been created yet
            if (tcpClient != null) this.tcpClient = tcpClient;
        }

        /// <summary>
        /// Establish the connection
        /// </summary>
        protected override void EstablishConnectionSpecific()
        {
            if (tcpClient == null) ConnectTCPClient();

            //We should now be able to set the connectionInfo localEndPoint
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

            //Start listening for incoming data
            StartIncomingDataListen();

            IPEndPoint existingListener = TCPConnection.ExistingLocalListenEndPoints(ConnectionInfo.LocalEndPoint.Address);

            //If we are server side and we have just received an incoming connection we need to return a conneciton id
            //This id will be used in all future connections from this machine
            if (ConnectionInfo.ServerSide)
            {
                if (existingListener == null) throw new ConnectionSetupException("Detected a server side connection when an existing listener was not present.");

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Waiting for client connnectionInfo from " + ConnectionInfo);

                //Wait for the client to send its identification
                if (!connectionSetupWait.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS))
                    throw new ConnectionSetupException("Timeout waiting for client connectionInfo with " + ConnectionInfo + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                if (connectionSetupException)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ServerSide with " + ConnectionInfo + ", " + connectionSetupExceptionStr);
                    throw new ConnectionSetupException("ServerSide. " + connectionSetupExceptionStr);
                }

                //Once we have the clients id we send our own
                //SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), this, false, new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, NetworkComms.CommsPort), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), new ConnectionInfo(ConnectionType.TCP, NetworkComms.NetworkIdentifier, new IPEndPoint(ConnectionInfo.LocalEndPoint.Address, existingListener.Port), true), NetworkComms.InternalFixedSendReceiveOptions);
            }
            else
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending connnectionInfo to " + ConnectionInfo);

                //As the client we initiated the connection we now forward our local node identifier to the server
                //If we are listening we include our local listen port as well
                //SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), this, false, (NetworkComms.isListening ? new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, NetworkComms.CommsPort) : new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, -1)), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);

                SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), new ConnectionInfo(ConnectionType.TCP, NetworkComms.NetworkIdentifier, new IPEndPoint(ConnectionInfo.LocalEndPoint.Address, (existingListener != null ? existingListener.Port : ConnectionInfo.LocalEndPoint.Port)), existingListener != null), NetworkComms.InternalFixedSendReceiveOptions);

                //Wait here for the server end to return its own identifier
                if (!connectionSetupWait.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS))
                    throw new ConnectionSetupException("Timeout waiting for server connnectionInfo from " + ConnectionInfo + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                //If we are client side we can update the localEndPoint for this connection to reflect what the remote end might see if we are also listening
                if (existingListener != null) ConnectionInfo.UpdateLocalEndPointInfo(existingListener);

                if (connectionSetupException)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ClientSide with " + ConnectionInfo + ", " + connectionSetupExceptionStr);
                    throw new ConnectionSetupException("ClientSide. " + connectionSetupExceptionStr);
                }
            }

            //Once the connection has been established we may want to re-enable the 'nagle algorithm' used for reducing network congestion (apparently).
            //By default we leave the nagle algorithm disabled because we want the quick through put when sending small packets
            if (EnableNagleAlgorithmForNewConnections)
            {
                tcpClient.NoDelay = false;
                tcpClient.Client.NoDelay = false;
            }
        }

        /// <summary>
        /// If we were not provided with a tcpClient on creation we need to create one
        /// </summary>
        private void ConnectTCPClient()
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Establishing TCP client with " + ConnectionInfo);

                //We now connect to our target
                tcpClient = new TcpClient(new IPEndPoint(IPAddress.Any, 0));

                bool connectSuccess = true;

                //Start the connection using the asyn version
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
            if (!NetworkComms.ConnectionExists(ConnectionInfo.RemoteEndPoint, ConnectionType.TCP))
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

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Listening for incoming data from " + ConnectionInfo);
        }
    }
}
