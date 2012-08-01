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
    /// <summary>
    /// A TCPConnection represents each established tcp connection between two peers.
    /// </summary>
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
        /// Create a connection with the provided connectionInfo. If there is an existing connection that is returned instead.
        /// </summary>
        /// <param name="connectionInfo"></param>
        /// <param name="establishIfRequired"></param>
        /// <returns></returns>
        public static TCPConnection CreateConnection(ConnectionInfo connectionInfo, bool establishIfRequired = true)
        {
            return CreateConnection(connectionInfo, null, establishIfRequired);
        }

        /// <summary>
        /// Internal create which has the possibility of an existing TcpClient
        /// </summary>
        /// <param name="connectionInfo"></param>
        /// <param name="tcpClient"></param>
        /// <param name="establishIfRequired"></param>
        /// <returns></returns>
        internal static TCPConnection CreateConnection(ConnectionInfo connectionInfo, TcpClient tcpClient, bool establishIfRequired = true)
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
                    connection = new TCPConnection(connectionInfo, tcpClient);
                    newConnection = true;
                }
            }

            if (establishIfRequired)
            {
                if (newConnection) connection.EstablishConnection();
                else  connection.WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS);
            }

            return connection;
        }

        /// <summary>
        /// TCP connection constructor
        /// </summary>
        protected TCPConnection(ConnectionInfo connectionInfo, TcpClient tcpClient)
            : base(connectionInfo)
        {
            //We don't guarantee that the tcpClient has been created yet
            if (tcpClient != null) this.tcpClient = tcpClient;
        }

        /// <summary>
        /// Establish a connection with the provided TcpClient
        /// </summary>
        /// <param name="sourceClient"></param>
        protected override void EstablishConnectionInternal()
        {
            if (tcpClient == null) ConnectTCPClient();

            ConnectionInfo.UpdateLocalEndPointInfo((IPEndPoint)tcpClient.Client.LocalEndPoint);

            //We are going to be using the networkStream quite a bit so we pull out a reference once here
            tcpClientNetworkStream = tcpClient.GetStream();

            //When we tell the socket/client to close we want it to do so immediately
            //this.tcpClient.LingerState = new LingerOption(false, 0);

            //We need to set the keep alive option otherwise the connection will just die at some random time should we not be using it
            //NOTE: This did not seem to work reliably so was replaced with the keepAlive packet feature
            //this.tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            tcpClient.ReceiveBufferSize = NetworkComms.receiveBufferSizeBytes;
            tcpClient.SendBufferSize = NetworkComms.sendBufferSizeBytes;

            //This disables the 'nagle alogrithm'
            //http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.nodelay.aspx
            //Basically we may want to send lots of small packets (<200 bytes) and sometimes those are time critical (e.g. when establishing a connection)
            //If we leave this enabled small packets may never be sent until a suitable send buffer length threshold is passed. i.e. BAD
            tcpClient.NoDelay = true;
            tcpClient.Client.NoDelay = true;

            //Start listening for incoming data
            StartIncomingDataListen();

            IPEndPoint existingListener = TCPConnection.ExistingConnectionListener(ConnectionInfo.LocalEndPoint.Address);

            //If we are server side and we have just received an incoming connection we need to return a conneciton id
            //This id will be used in all future connections from this machine
            if (ConnectionInfo.ServerSide)
            {
                if (existingListener == null) throw new ConnectionSetupException("Detected a server side connection when an existing listener was not present.");

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Waiting for client connnectionInfo from " + ConnectionInfo);

                //Wait for the client to send its identification
                if (!connectionSetupWait.WaitOne(NetworkComms.connectionEstablishTimeoutMS))
                    throw new ConnectionSetupException("Timeout waiting for client connectionInfo with " + ConnectionInfo + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                if (connectionSetupException)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ServerSide. " + connectionSetupExceptionStr);
                    throw new ConnectionSetupException("ServerSide. " + connectionSetupExceptionStr);
                }

                //Once we have the clients id we send our own
                //SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), this, false, new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, NetworkComms.CommsPort), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), new ConnectionInfo(ConnectionType.TCP, NetworkComms.localNetworkIdentifier, new IPEndPoint(ConnectionInfo.LocalEndPoint.Address, existingListener.Port), true), NetworkComms.InternalFixedSendReceiveOptions);
            }
            else
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending connnectionInfo to " + ConnectionInfo);

                //As the client we initiated the connection we now forward our local node identifier to the server
                //If we are listening we include our local listen port as well
                //SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), this, false, (NetworkComms.isListening ? new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, NetworkComms.CommsPort) : new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, -1)), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);

                SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), new ConnectionInfo(ConnectionType.TCP, NetworkComms.localNetworkIdentifier, new IPEndPoint(ConnectionInfo.LocalEndPoint.Address, (existingListener != null ? existingListener.Port : ConnectionInfo.LocalEndPoint.Port)), existingListener != null), NetworkComms.InternalFixedSendReceiveOptions);

                //Wait here for the server end to return its own identifier
                if (!connectionSetupWait.WaitOne(NetworkComms.connectionEstablishTimeoutMS))
                    throw new ConnectionSetupException("Timeout waiting for server connnectionInfo from " + ConnectionInfo + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                //If we are client side we still update the localEndPoint for this connection to reflect what the remote end sees
                if (existingListener != null) ConnectionInfo.UpdateLocalEndPointInfo(existingListener);

                if (connectionSetupException)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ClientSide. " + connectionSetupExceptionStr);
                    throw new ConnectionSetupException("ClientSide. " + connectionSetupExceptionStr);
                }
            }

            if (ConnectionInfo.ConnectionShutdown) throw new ConnectionSetupException("Connection was closed during handshake.");

            //A quick idiot test
            if (ConnectionInfo == null) throw new ConnectionSetupException("ConnectionInfo should never be null at this point.");

            if (ConnectionInfo.NetworkIdentifier == ShortGuid.Empty)
                throw new ConnectionSetupException("Remote network identifier should have been set by this point.");

            //Once the connection has been established we may want to re-enable the 'nagle algorithm' used for reducing network congestion (apparently).
            //By default we leave the nagle algorithm disabled because we want the quick through put when sending small packets
            if (NetworkComms.EnableNagleAlgorithmForEstablishedConnections)
            {
                tcpClient.NoDelay = false;
                tcpClient.Client.NoDelay = false;
            }
        }

        /// <summary>
        /// If we were not provided with a tcpClient on creation we need to establish that now
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
                    if (!ar.AsyncWaitHandle.WaitOne(NetworkComms.connectionEstablishTimeoutMS, false))
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

        private void StartIncomingDataListen()
        {
            if (!NetworkComms.ConnectionExists(ConnectionInfo.RemoteEndPoint, ConnectionType.TCP))
            {
                CloseConnection(true, 18);
                throw new ConnectionSetupException("A connection reference by endPoint should exist before starting an incoming data listener.");
            }

            lock (sendLocker)
            {
                if (NetworkComms.connectionListenModeUseSync)
                {
                    if (incomingDataListenThread == null)
                    {
                        incomingDataListenThread = new Thread(IncomingDataSyncWorker);
                        incomingDataListenThread.Priority = NetworkComms.timeCriticalThreadPriority;
                        incomingDataListenThread.Name = "IncomingDataListener";
                        incomingDataListenThread.Start();
                    }
                }
                else
                    tcpClientNetworkStream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingPacketHandler), tcpClientNetworkStream);
            }

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Listening for incoming data from " + ConnectionInfo);
        }

        /// <summary>
        /// Return true if the connection is established within the provided timeout, otherwise false
        /// </summary>
        /// <param name="waitTimeoutMS"></param>
        /// <returns></returns>
        internal bool WaitForConnectionEstablish(int waitTimeoutMS)
        {
            return connectionSetupWait.WaitOne(waitTimeoutMS);
        }
    }
}
