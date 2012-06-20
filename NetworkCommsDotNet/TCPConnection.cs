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
    public class TCPConnection : Connection
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
        /// The thread listening for incoming data should we be using synchronous methods.
        /// </summary>
        Thread incomingDataListenThread = null;

        /// <summary>
        /// The current incoming data buffer
        /// </summary>
        byte[] dataBuffer;

        /// <summary>
        /// The total bytes read so far within dataBuffer
        /// </summary>
        int totalBytesRead;

        /// <summary>
        /// Connection constructor
        /// </summary>
        /// <param name="serverSide">True if this connection was requested by a remote client.</param>
        /// <param name="connectionEndPoint">The IP information of the remote client.</param>
        public TCPConnection(bool serverSide, TcpClient tcpClient)
        {
            this.ConnectionCreationTime = DateTime.Now;
            
            this.tcpClient = tcpClient;
            this.ConnectionEndPoint = new IPEndPoint(IPAddress.Parse(tcpClient.Client.RemoteEndPoint.ToString().Split(':')[0]), int.Parse(tcpClient.Client.RemoteEndPoint.ToString().Split(':')[1])); ;
            this.ConnectionLocalPoint = new IPEndPoint(IPAddress.Parse(tcpClient.Client.LocalEndPoint.ToString().Split(':')[0]), int.Parse(tcpClient.Client.LocalEndPoint.ToString().Split(':')[1])); ;

            this.ServerSide = serverSide;
            this.packetBuilder = new ConnectionPacketBuilder();
            this.dataBuffer = new byte[NetworkComms.receiveBufferSizeBytes];
        }

        /// <summary>
        /// Establish a connection with the provided TcpClient
        /// </summary>
        /// <param name="sourceClient"></param>
        public void EstablishConnection()
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Establishing connection with " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port);

                DateTime establishStartTime = DateTime.Now;

                if (ConnectionInfo.ConnectionEstablished || ConnectionInfo.ConnectionShutdown)
                    throw new ConnectionSetupException("Attempting to re-establish an already established or closed connection.");

                if (NetworkComms.commsShutdown)
                    throw new ConnectionSetupException("Attempting to establish new connection while comms is shutting down.");

                //Ensure that we do not already have a connection from this client
                //this.tcpClient = sourceClient;
                this.tcpClientNetworkStream = tcpClient.GetStream();
                
                //When we tell the socket/client to close we want it to do so immediately
                //this.tcpClient.LingerState = new LingerOption(false, 0);

                //We need to set the keep alive option otherwise the connection will just die at some random time should we not be using it
                //this.tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                this.tcpClient.ReceiveBufferSize = NetworkComms.receiveBufferSizeBytes;
                this.tcpClient.SendBufferSize = NetworkComms.sendBufferSizeBytes;

                //This disables the 'nagle alogrithm'
                //http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.nodelay.aspx
                //Basically we may want to send lots of small packets (<200 bytes) and sometimes those are time critical (e.g. when establishing a connection)
                //If we leave this enabled small packets may never be sent until a suitable send buffer length threshold is passed. i.e. BAD
                this.tcpClient.NoDelay = true;
                this.tcpClient.Client.NoDelay = true;

                //Start listening for incoming data
                StartIncomingDataListen();

                //If we are server side and we have just received an incoming connection we need to return a conneciton id
                //This id will be used in all future connections from this machine
                if (ConnectionInfo.ServerSide)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("New connection detected from " + ConnectionInfo.RemoteEndPoint.Address + ", waiting for client connId.");

                    //Wait for the client to send its identification
                    if (!connectionSetupWait.WaitOne(NetworkComms.connectionEstablishTimeoutMS))
                        throw new ConnectionSetupException("Timeout waiting for client connId from " + NetworkComms.LocalIP + " to " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", EstablishConnection() entered at " + establishStartTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                    if (connectionSetupException)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ServerSide. " + connectionSetupExceptionStr);
                        throw new ConnectionSetupException("ServerSide. " + connectionSetupExceptionStr);
                    }

                    //Once we have the clients id we send our own
                    SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), new ConnectionInfo(NetworkComms.NetworkNodeIdentifier), NetworkComms.internalFixedSendRecieveOptions);
                }
                else
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Initiating connection to " + ConnectionInfo.RemoteEndPoint.Address);

                    //As the client we initiated the connection we now forward our local node identifier to the server
                    //If we are listening we include our local listen port as well
                    NetworkComms.SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), this, false, (NetworkComms.isListening ? new ConnectionInfo(NetworkComms.NetworkNodeIdentifier, NetworkComms.CommsPort) : new ConnectionInfo(NetworkComms.NetworkNodeIdentifier, -1)), NetworkComms.internalFixedSendRecieveOptions);

                    //Wait here for the server end to return its own identifier
                    if (!connectionSetupWait.WaitOne(NetworkComms.connectionEstablishTimeoutMS))
                        throw new ConnectionSetupException("Timeout waiting for server connId from " + NetworkComms.LocalIP + " to " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", EstablishConnection() entered at " + establishStartTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.fff"));

                    if (connectionSetupException)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ClientSide. " + connectionSetupExceptionStr);
                        throw new ConnectionSetupException("ClientSide. " + connectionSetupExceptionStr);
                    }
                }

                if (ConnectionInfo.ConnectionShutdown)
                    throw new ConnectionSetupException("Connection was closed during handshake.");

                //A quick idiot test
                if (!ConnectionInfo.ConnectionEstablished)
                    throw new ConnectionSetupException("Connection should be marked as established at this point.");

                //Only once the connection has been succesfully established do we record it in our connection dictionary
                lock (NetworkComms.globalDictAndDelegateLocker)
                {
                    if (NetworkComms.allConnectionsById.ContainsKey(this.ConnectionInfo.RemoteNetworkIdentifier))
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("... connection succesfully established with " + ConnectionInfo.RemoteEndPoint.Address + " at connId " + this.ConnectionInfo.RemoteNetworkIdentifier.ToString());
                    }
                    else
                        throw new ConnectionSetupException("ConnectionId not located in connections dictionary on final check. Remote end must have disconnected during handshake.");
                }

                //Once the connection has been established we may want to re-enable the 'nagle algorithm' used for reducing network congestion (apparently).
                //By default we leave the nagle algorithm disabled because we want the quick through put when sending small packets
                if (NetworkComms.EnableNagleAlgorithmForEstablishedConnections)
                {
                    this.tcpClient.NoDelay = false;
                    this.tcpClient.Client.NoDelay = false;
                }

                ConnectionInfo.ConnectionEstablished = true;
                connectionEstablishWait.Set();
            }
            catch (SocketException e)
            {
                //If anything goes wrong we close the connection.
                CloseConnection(true,5);
                throw new ConnectionSetupException(e.ToString());
            }
            catch (Exception ex)
            {
                //If anything goes wrong we close the connection.
                CloseConnection(true, 6);

                //For some odd reason not all SocketExceptions get caught above, so another check here
                if (ex.GetBaseException().GetType() == typeof(SocketException))
                    throw new ConnectionSetupException(ex.ToString());
                else
                    throw;
            }
        }

        /// <summary>
        /// Closes a connection
        /// </summary>
        /// <param name="closeDueToError">Closing a connection due an error possibly requires a few extra steps.</param>
        /// <param name="logLocation">Optional debug parameter.</param>
        public void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
            try
            {
                //The following closes attempt to correctly close the connection
                //Try to close the networkStream
                try
                {
                    if (tcpClientNetworkStream != null)
                        tcpClientNetworkStream.Close();
                }
                catch (Exception)
                {
                }
                finally
                {
                    tcpClientNetworkStream = null;
                }

                //Try to close the tcpClient
                try
                {
                    tcpClient.Client.Disconnect(false);
                    tcpClient.Client.Close();
                    tcpClient.Client.Dispose();
                }
                catch (Exception)
                {
                }

                //Try to close the tclClient
                try
                {
                    tcpClient.Close();
                }
                catch (Exception)
                {
                }

                try
                {
                    //If we are calling close from the listen thread we are actually in the same thread
                    //We must guarantee the listen thread stops even if that means we need to nuke it
                    //If we did not we may not be able to shutdown properly.
                    if (incomingDataListenThread != null && incomingDataListenThread != Thread.CurrentThread && (incomingDataListenThread.ThreadState == System.Threading.ThreadState.WaitSleepJoin || incomingDataListenThread.ThreadState == System.Threading.ThreadState.Running))
                    {
                        //If we have made it this far we give the ythread a further 50ms to finish before nuking.
                        if (!incomingDataListenThread.Join(50))
                        {
                            incomingDataListenThread.Abort();
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn("Incoming data listen thread for " + ConnectionId + " aborted.");
                        }
                    }
                }
                catch (Exception)
                {

                }
            }
            catch (Exception ex)
            {
                if (ex is ThreadAbortException)
                { /*Ignore the threadabort exception if we had to nuke the listen thread*/ }
                else
                    throw;
            }
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

        /// <summary>
        /// Starts listening for incoming data for this connection. Can choose between async or sync depending on the value of connectionListenModeIsSync
        /// </summary>
        private void StartIncomingDataListen()
        {
            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                if (!NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionEndPoint))
                {
                    CloseConnection(true, 18);
                    return;
                }
            }

            lock (sendLocker)
            {
                if (ConnectionInfo.ConnectionShutdown)
                    throw new ConnectionSetupException("Unable to start listening if connection is shutDown.");
                else
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
            }

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Listening for incoming data from " + ConnectionEndPoint.Address.ToString() + ":" + ConnectionEndPoint.Port);
        }

        /// <summary>
        /// Synchronous incoming connection data worker
        /// </summary>
        private void IncomingDataSyncWorker()
        {
            bool dataAvailable = false;

            try
            {
                while (true)
                {
                    if (ConnectionInfo.ConnectionShutdown)
                        break;

                    int bufferOffset = 0;

                    //We need a buffer for our incoming data
                    //First we try to reuse a previous buffer
                    if (packetBuilder.CurrentPacketCount() > 0 && packetBuilder.NumUnusedBytesMostRecentPacket() > 0)
                        dataBuffer = packetBuilder.RemoveMostRecentPacket(ref bufferOffset);
                    else
                        //If we have nothing to reuse we allocate a new buffer
                        dataBuffer = new byte[NetworkComms.receiveBufferSizeBytes];

                    //We block here until there is data to read
                    //When we read data we read until method returns or we fill the buffer length
                    totalBytesRead = tcpClientNetworkStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                    //Check to see if there is more data ready to be read
                    dataAvailable = tcpClientNetworkStream.DataAvailable;

                    //If we read any data it gets handed off to the packetBuilder
                    if (totalBytesRead > 0)
                    {
                        ConnectionInfo.UpdateLastTrafficTime();

                        //If we have read a single byte which is 0 and we are not expecting other data
                        if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesRead == 0)
                        {
                            //if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... null packet removed in IncomingPacketHandler(). 1");
                        }
                        else
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... " + totalBytesRead + " bytes added to packetBuilder.");
                            packetBuilder.AddPacket(totalBytesRead, dataBuffer);
                        }
                    }
                    else if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionShutdown))
                    {
                        //If we read 0 bytes and there is no data available we should be shutting down
                        CloseConnection(false, -1);
                        break;
                    }
        
                    //If we have read some data and we have more or equal what was expected we attempt a data handoff
                    if (packetBuilder.TotalBytesRead > 0 && packetBuilder.TotalBytesRead >= packetBuilder.TotalBytesExpected)
                        IncomingPacketHandleHandOff(packetBuilder);
                }
            }
            //On any error here we close the connection
            catch (NullReferenceException)
            {
                CloseConnection(true, 7);
            }
            catch (IOException)
            {
                CloseConnection(true, 8);
            }
            catch (ObjectDisposedException)
            {
                CloseConnection(true, 9);
            }
            catch (SocketException)
            {
                CloseConnection(true, 10);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 11);
            }

            //Clear the listen thread object because the thread is about to end
            incomingDataListenThread = null;

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Incoming data listen thread ending for " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port);
        }

        /// <summary>
        /// Asynchronous incoming connection data delegate
        /// </summary>
        /// <param name="ar"></param>
        private void IncomingPacketHandler(IAsyncResult ar)
        {
            Thread.CurrentThread.Priority = NetworkComms.timeCriticalThreadPriority;

            //int bytesRead;
            bool dataAvailable;

            try
            {
                NetworkStream netStream = (NetworkStream)ar.AsyncState;

                if (netStream.CanRead)
                {
                    totalBytesRead = netStream.EndRead(ar) + totalBytesRead;
                    dataAvailable = netStream.DataAvailable;

                    if (totalBytesRead > 0)
                    {
                        LastTrafficTime = DateTime.Now;

                        //If we have read a single byte which is 0 and we are not expecting other data
                        if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesRead == 0)
                        {
                            //if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... null packet removed in IncomingPacketHandler(). 1");
                        }
                        else
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... " + totalBytesRead + " bytes added to packetBuilder.");

                            //If there is more data to get then add it to the packets lists;
                            packetBuilder.AddPacket(totalBytesRead, dataBuffer);

                            //If we have more data we might as well continue reading syncronously
                            while (dataAvailable)
                            {
                                int bufferOffset = 0;

                                //We need a buffer for our incoming data
                                //First we try to reuse a previous buffer
                                if (packetBuilder.CurrentPacketCount() > 0 && packetBuilder.NumUnusedBytesMostRecentPacket() > 0)
                                    dataBuffer = packetBuilder.RemoveMostRecentPacket(ref bufferOffset);
                                else
                                    //If we have nothing to reuse we allocate a new buffer
                                    dataBuffer = new byte[NetworkComms.receiveBufferSizeBytes];

                                totalBytesRead = netStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                                if (totalBytesRead > 0)
                                {
                                    LastTrafficTime = DateTime.Now;

                                    //If we have read a single byte which is 0 and we are not expecting other data
                                    if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesRead == 0)
                                    {
                                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... null packet removed in IncomingPacketHandler(). 2");
                                        //LastTrafficTime = DateTime.Now;
                                    }
                                    else
                                    {
                                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... " + totalBytesRead + " bytes added to packetBuilder.");
                                        packetBuilder.AddPacket(totalBytesRead, dataBuffer);
                                        dataAvailable = netStream.DataAvailable;
                                    }
                                }
                                else
                                    break;
                            }
                        }
                    }

                    if (packetBuilder.TotalBytesRead > 0 && packetBuilder.TotalBytesRead >= packetBuilder.TotalBytesExpected)
                    {
                        //Once we think we might have enough data we call the incoming packet handle handoff
                        //Should we have a complete packet this method will start the appriate task
                        //This method will now clear byes from the incoming packets if we have received something complete.
                        IncomingPacketHandleHandOff(packetBuilder);
                    }

                    if (totalBytesRead == 0 && (!dataAvailable || ConnectionShutdown))
                        CloseConnection(false, -2);
                    else
                    {
                        //We need a buffer for our incoming data
                        //First we try to reuse a previous buffer
                        if (packetBuilder.CurrentPacketCount() > 0 && packetBuilder.NumUnusedBytesMostRecentPacket() > 0)
                            dataBuffer = packetBuilder.RemoveMostRecentPacket(ref totalBytesRead);
                        else
                        {
                            //If we have nothing to reuse we allocate a new buffer
                            dataBuffer = new byte[NetworkComms.receiveBufferSizeBytes];
                            totalBytesRead = 0;
                        }

                        netStream.BeginRead(dataBuffer, totalBytesRead, dataBuffer.Length - totalBytesRead, IncomingPacketHandler, netStream);
                    }
                }
                else
                    CloseConnection(false, -5);
            }
            catch (IOException)
            {
                CloseConnection(true, 12);
            }
            catch (ObjectDisposedException)
            {
                CloseConnection(true, 13);
            }
            catch (SocketException)
            {
                CloseConnection(true, 14);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 15);
            }

            Thread.CurrentThread.Priority = ThreadPriority.Normal;
        }

        /// <summary>
        /// Handle an incoming ConnectionSetup packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        private void ConnectionSetupHandler(byte[] packetDataSection)
        {
            //We should never be trying to handshake an established connection
            if (this.ConnectionInfo != null) throw new ConnectionSetupException("Received connectionsetup packet after connection had already been configured.");
            this.ConnectionInfo = NetworkComms.internalFixedSerializer.DeserialiseDataObject<ConnectionInfo>(packetDataSection, NetworkComms.internalFixedCompressor);

            //We use the following bool to track a possible existing connection which needs closing
            bool possibleExistingConnectionWithPeer_ByIdentifier=false;
            bool possibleExistingConnectionWithPeer_ByEndPoint=false;
            TCPConnection existingConnection = null;

            //We first try to establish everything within this lock in one go
            //If we can't quite complete the establish we have to come out of the lock at try to sort the problem
            bool connectionEstablishedSuccess = ConnectionSetupHandlerInner(ref possibleExistingConnectionWithPeer_ByIdentifier, ref possibleExistingConnectionWithPeer_ByEndPoint, ref existingConnection);

            //If we were not succesfull at establishing the connection we need to sort it out!
            if (!connectionEstablishedSuccess && !connectionSetupException)
            {
                if (existingConnection == null)
                    throw new Exception("Connection establish issues and existingConnection was left as null.");

                if (possibleExistingConnectionWithPeer_ByIdentifier)
                {
                    //If we have a clash on identifier we always assume it is an old connection by the same client
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Existing connection detected from peer with identifier " + ConnectionInfo.RemoteNetworkIdentifier + ". Closing existing connection in favour of new one.");

                    //Tested and this is not the problem
                    if (existingConnection.tcpClientNetworkStream == this.tcpClientNetworkStream || existingConnection.tcpClient == this.tcpClient)
                    {
                        connectionSetupException = true;
                        connectionSetupExceptionStr = " ... existing connection shares networkStream or tcpClient object. Unable to continue with connection establish.";
                    }
                    else
                        existingConnection.CloseConnection(true, 1);
                }
                else if (possibleExistingConnectionWithPeer_ByEndPoint)
                {
                    //If we have a clash by endPoint we test the existing connection
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Existing connection detected from provided endpoint, " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + ". Testing existing connection.");
                    if (existingConnection.CheckConnectionAliveState(1000))
                    {
                        //If the existing connection comes back as alive we don't allow this one to go any further
                        //This might happen if two peers try to connect to each other at the same time
                        connectionSetupException = true;
                        connectionSetupExceptionStr = " ... existing live connection at provided end point for this connection, no need for a second. End Point - " + ConnectionInfo.RemoteNetworkIdentifier + ", " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + ". ";
                    }
                }

                //We only try again if we did not log an exception
                if (!connectionSetupException)
                {
                    //If we have made it this far we need to make sure we still have an entry in the endPoint dictioanry for 'this'
                    lock (NetworkComms.globalDictAndDelegateLocker)
                    {
                        if (!NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionEndPoint))
                            NetworkComms.allConnectionsByEndPoint.Add(ConnectionEndPoint, this);
                    }

                    //Once we have tried to sort the problem we can try to finish the establish one last time
                    connectionEstablishedSuccess = ConnectionSetupHandlerInner(ref possibleExistingConnectionWithPeer_ByIdentifier, ref possibleExistingConnectionWithPeer_ByEndPoint, ref existingConnection);

                    //If we still failed then that's it for this establish
                    if (!connectionEstablishedSuccess && !connectionSetupException)
                    {
                        connectionSetupException = true;
                        connectionSetupExceptionStr = "Attempted to establish conneciton with " + ConnectionEndPoint.Address.ToString() + ":" + ConnectionEndPoint.Port + ", but due to an existing connection this was not possible.";
                    }
                }
            }

            //Trigger any setup waits
            connectionSetupWait.Set();
        }

        /// <summary>
        /// Attempts to complete the connection establish with a minimum of locking to prevent possible deadlocking
        /// </summary>
        /// <param name="possibleExistingConnectionWithPeer_ByIdentifier"></param>
        /// <param name="possibleExistingConnectionWithPeer_ByEndPoint"></param>
        /// <returns></returns>
        private bool ConnectionSetupHandlerInner(ref bool possibleExistingConnectionWithPeer_ByIdentifier, ref bool possibleExistingConnectionWithPeer_ByEndPoint, ref Connection existingConnection)
        {
            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                //If we no longer have the original endPoint reference (set in the constructor) then the connection must have been closed already
                if (!NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionInfo.RemoteEndPoint))
                {
                    connectionSetupException = true;
                    connectionSetupExceptionStr = "Connection setup received after connection closure with " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port;
                }
                else
                {
                    //We need to check for a possible GUID clash
                    //Probability of a clash is approx 0.1% if 1E19 connection are maintained simultaneously (This many connections has not be tested ;))
                    //It's far more likely we have a strange scenario where a remote peer is trying to establish a second independant connection (which should not really happen in the first place)
                    //but hey, we live in a crazy world!
                    if (this.ConnectionInfo.RemoteNetworkIdentifier == NetworkComms.NetworkNodeIdentifier)
                    {
                        connectionSetupException = true;
                        connectionSetupExceptionStr = "Remote peer has same network idendifier to local, " + ConnectionInfo.RemoteNetworkIdentifier + ". Although technically near impossible some special (engineered) scenarios make this more probable.";
                    }
                    else if (NetworkComms.allConnectionsById.ContainsKey(ConnectionInfo.RemoteNetworkIdentifier) && NetworkComms.allConnectionsById[ConnectionInfo.RemoteNetworkIdentifier].ContainsKey(ConnectionInfo.ConnectionType))
                    {
                        possibleExistingConnectionWithPeer_ByIdentifier = true;
                        existingConnection = NetworkComms.allConnectionsById[ConnectionInfo.RemoteNetworkIdentifier][ConnectionInfo.ConnectionType];
                    }
                    else
                    {
                        //Record the new connection
                        NetworkComms.allConnectionsById.Add(this.ConnectionInfo.RemoteNetworkIdentifier, this);

                        //If the recorded endPoint port does not match the latest connectionInfo object then we should correct it
                        //This will happen if we are establishing the connection at the server end and have a client which is also listening on a specific port
                        if (ConnectionInfo.RemoteEndPoint.Port != this.ConnectionInfo.ClientPort && this.ConnectionInfo.ClientPort != -1)
                        {
                            //Remove the entry for 'this' before we change our local ConnectionEndPoint
                            NetworkComms.allConnectionsByEndPoint.Remove(ConnectionEndPoint);
                            ConnectionEndPoint = new IPEndPoint(ConnectionEndPoint.Address, this.ConnectionInfo.ClientPort);

                            if (NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionEndPoint))
                            {
                                //If we have an existing entry at the new end point we unroll the changes made in this method and set the necessary boolean
                                NetworkComms.allConnectionsById.Remove(this.ConnectionInfo.RemoteNetworkIdentifier);
                                possibleExistingConnectionWithPeer_ByEndPoint = true;
                                existingConnection = NetworkComms.allConnectionsByEndPoint[ConnectionEndPoint];
                            }
                            else
                            {
                                //Readd to the endPoint dict using the updated ConnectionEndPoint
                                NetworkComms.allConnectionsByEndPoint.Add(ConnectionEndPoint, this);
                                return true;
                            }
                        }
                        else
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Send the provided packet to the remote peer
        /// </summary>
        /// <param name="packetTypeStr"></param>
        /// <param name="packetData"></param>
        /// <param name="destinationIPAddress"></param>
        /// <param name="receiveConfirmationRequired"></param>
        /// <returns></returns>
        private void SendPacket(Packet packet)
        {
            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Entering packet send of '" + packet.PacketHeader.PacketType + "' packetType to " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + (ConnectionEstablished ? " (" + ConnectionId + ")" : ""));

            //Multiple threads may try to send packets at the same time so wait one at a time here
            lock (sendLocker)
            {
                //We don't allow sends on a closed connection
                if (ConnectionInfo.ConnectionShutdown) throw new CommunicationException("Attempting to send packet on connection which has been closed or is currently closing.");

                string confirmationCheckSum = "";
                AutoResetEvent confirmationWaitSignal = new AutoResetEvent(false);
                bool remotePeerDisconnectedDuringWait = false;

                #region Delegates
                //Specify a delegate we may use if we require receive confirmation
                NetworkComms.PacketHandlerCallBackDelegate<string> confirmationDelegate = (packetHeader, connectionId, incomingString) =>
                {
                    if (connectionId == this.ConnectionInfo.RemoteNetworkIdentifier)
                    {
                        confirmationCheckSum = incomingString;
                        confirmationWaitSignal.Set();
                    }
                };

                //We use the following delegate to quickly force a response timeout if the remote end disconnects during a send/wait
                NetworkComms.ConnectionShutdownDelegate ConfirmationShutDownDelegate = (sourceConnectionId) =>
                {
                    if (sourceConnectionId == this.ConnectionInfo.RemoteNetworkIdentifier)
                    {
                        remotePeerDisconnectedDuringWait = true;
                        confirmationWaitSignal.Set();
                    }
                };
                #endregion

                try
                {
                    //Add the confirmation handler if required
                    if (packet.PacketHeader.ReceiveConfirmationRequired)
                    {
                        NetworkComms.AppendIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), confirmationDelegate, false);
                        AppendConnectionSpecificShutdownHandler(ConfirmationShutDownDelegate);
                    }

                    //If this packet is not a checkSumFailResend
                    if (NetworkComms.EnablePacketCheckSumValidation && packet.PacketHeader.PacketType != Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend))
                    {
                        //We only want to keep packets when they are under some provided theshold
                        //otherwise this becomes a quick 'memory leak'
                        if (packet.PacketData.Length < NetworkComms.checkSumMismatchSentPacketCacheMaxByteLimit)
                        {
                            lock (sentPacketsLocker)
                                if (!sentPackets.ContainsKey(packet.PacketHeader.CheckSumHash))
                                    sentPackets.Add(packet.PacketHeader.CheckSumHash, new OldSentPacket(packet));
                        }
                    }

                    //To keep memory copies to a minimum we send the header and payload in two calls to networkStream.Write
                    byte[] headerBytes = packet.SerialiseHeader(NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending a packet of type '" + packet.PacketHeader.PacketType + "' to " + RemoteClientIP + " (" + (!ConnectionInfo.ConnectionEstablished ? "NA" : this.ConnectionInfo.RemoteNetworkIdentifier.ToString()) + "), containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");

                    tcpClientNetworkStream.Write(headerBytes, 0, headerBytes.Length);
                    tcpClientNetworkStream.Write(packet.PacketData, 0, packet.PacketData.Length);

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... " + (headerBytes.Length + packet.PacketData.Length).ToString() + " bytes written to netstream.");

                    if (!tcpClient.Connected)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Error("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
                        throw new CommunicationException("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
                    }

                    #region sentPackets Cleanup
                    //If sent packets is greater than 40 we delete anything older than a minute
                    lock (sentPacketsLocker)
                    {
                        if (sentPackets.Count > 40)
                        {
                            sentPackets = (from current in sentPackets.Values
                                           where current.packet.PacketHeader.PacketCreationTime < DateTime.Now.AddMinutes(-1)
                                           select new
                                           {
                                               key = current.packet.PacketHeader.CheckSumHash,
                                               value = current
                                           }).ToDictionary(p => p.key, p => p.value);
                        }
                    }
                    #endregion

                    //If we required receive confirmation we now wait for that confirmation
                    if (packet.PacketHeader.ReceiveConfirmationRequired)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... waiting for receive confirmation packet.");

                        if (!(confirmationWaitSignal.WaitOne(NetworkComms.packetConfirmationTimeoutMS)))
                            throw new ConfirmationTimeoutException("Confirmation packet timeout.");

                        if (remotePeerDisconnectedDuringWait)
                            throw new ConfirmationTimeoutException("Remote end closed connection before confirmation packet was returned.");
                        else
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... confirmation packet received.");
                        }
                    }

                    //Update the traffic time as late as possible incase there is a problem
                    ConnectionInfo.UpdateLastTrafficTime();
                }
                catch (ConfirmationTimeoutException)
                {
                    throw;
                }
                catch (CommunicationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new CommunicationException(ex.ToString());
                }
                finally
                {
                    if (packet.PacketHeader.ReceiveConfirmationRequired)
                    {
                        //Cleanup our delegates
                        NetworkComms.RemoveIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), confirmationDelegate);
                        RemoveConnectionSpecificShutdownHandler(ConfirmationShutDownDelegate);
                    }
                }
            }

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Completed packet send of '" + packet.PacketHeader.PacketType + "' packetType to " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + (ConnectionEstablished ? " (" + ConnectionId + ")" : ""));
        }

        /// <summary>
        /// Send a null packet (1 byte long) to this connection. Helps keep the connection alive but also bandwidth usage to absolute minimum. If an exception is thrown the connection will be closed.
        /// </summary>
        public void SendNullPacket()
        {
            try
            {
                //Only once the connection has been established do we send null packets
                if (ConnectionInfo.ConnectionEstablished)
                {
                    //Multiple threads may try to send packets at the same time so we need this lock to prevent a thread cross talk
                    lock (sendLocker)
                    {
                        //if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Sending null packet to " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + (connectionEstablished ? " (" + ConnectionId + ")." : "."));

                        //Send a single 0 byte
                        tcpClientNetworkStream.Write(new byte[] { 0 }, 0, 1);

                        //Update the traffic time after we have written to netStream
                        ConnectionInfo.UpdateLastTrafficTime();
                    }
                }
            }
            catch (Exception)
            {
                CloseConnection(true, 19);
                //throw new CommunicationException(ex.ToString());
            }
        }
    }
}
