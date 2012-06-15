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
    public class TCPConnection
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
        /// Information about this connection
        /// </summary>
        IPEndPoint ConnectionEndPoint { get; set; }
        IPEndPoint ConnectionLocalPoint { get; set; }

        public ConnectionInfo ConnectionInfo { get; private set; }

        /// <summary>
        /// Lockers for maintaing thread safe operation
        /// </summary>
        object packetSendLocker = new object();
        object delegateLocker = new object();

        /// <summary>
        /// A multicast function delegate for maintaining connection specific shutdown delegates
        /// </summary>
        NetworkComms.ConnectionShutdownDelegate connectionSpecificShutdownDelegate;

        /// <summary>
        /// Connection setup parameters
        /// </summary>
        ManualResetEvent connectionSetupWait = new ManualResetEvent(false);
        DateTime tcpConnectionCreationTime;
        volatile bool connectionSetupException = false;
        string connectionSetupExceptionStr = "";
        bool connectionEstablished = false;
        volatile bool connectionShutdown = false;
        ManualResetEvent connectionEstablishWait = new ManualResetEvent(false);
        public bool ServerSide { get; private set; }

        DateTime lastTrafficTime;
        object lastTrafficTimeLocker = new object();

        /// <summary>
        /// The packet builder for this connection
        /// </summary>
        ConnectionPacketBuilder packetBuilder;

        /// <summary>
        /// The current incoming data buffer
        /// </summary>
        byte[] dataBuffer;

        /// <summary>
        /// The total bytes read so far within dataBuffer
        /// </summary>
        int totalBytesRead;

        #region Get & Set
        public TcpClient TCPClient
        {
            get { return tcpClient; }
        }

        public string RemoteClientIP
        {
            get { return ConnectionEndPoint.Address.ToString(); }
        }

        public string LocalConnectionIP
        {
            get { return ConnectionLocalPoint.Address.ToString(); }
        }

        public ShortGuid ConnectionId
        {
            get
            {
                if (ConnectionInfo == null)
                    throw new CommunicationException("Attempted to access connectionId when connectionInfo was null.");
                else
                    return ConnectionInfo.NetworkIdentifier;
            }
        }

        public DateTime LastTrafficTime
        {
            get 
            { 
                lock (lastTrafficTimeLocker) 
                    return lastTrafficTime; 
            }
            private set
            {
                lock (lastTrafficTimeLocker)
                    lastTrafficTime = value; 
            }
        }
        #endregion

        /// <summary>
        /// Maintains a list of sent packets for the purpose of confirmation and possible resends.
        /// </summary>
        object sentPacketsLocker = new object();
        Dictionary<string, OldSentPacket> sentPackets = new Dictionary<string, OldSentPacket>();
        private class OldSentPacket
        {
            public int sendCount = 1;
            public Packet packet;

            public OldSentPacket(Packet packet)
            {
                this.packet = packet;
            }
        }

        /// <summary>
        /// Temporary data store while data is incoming yet incomplete.
        /// </summary>
        private class ConnectionPacketBuilder
        {
            List<byte[]> packets = new List<byte[]>();
            List<int> packetActualBytes = new List<int>();

            int totalBytesRead = 0;
            int totalBytesExpected = 0;

            public int TotalBytesRead
            {
                get { return totalBytesRead; }
            }

            public ConnectionPacketBuilder()
            {

            }

            /// <summary>
            /// The total number of bytes expected to complete a whole packet
            /// </summary>
            public int TotalBytesExpected
            {
                get { return totalBytesExpected; }
                set { totalBytesExpected = value; }
            }

            /// <summary>
            /// Clears n bytes from recorded packets, starting at the beginning
            /// </summary>
            /// <param name="numBytesToRemove"></param>
            public void ClearNTopBytes(int numBytesToRemove)
            {
                if (numBytesToRemove > 0)
                {
                    if (numBytesToRemove > totalBytesRead)
                        throw new CommunicationException("Attempting to remove more bytes than exist in the ConnectionPacketBuilder");

                    int bytesRemoved = 0;

                    //We will always remove bytes in order of the entries
                    for (int i = 0; i < packets.Count; i++)
                    {
                        if (packetActualBytes[i] > numBytesToRemove - bytesRemoved)
                        {
                            //Remove the necessary bytes from this packet and rebuild
                            //New array length is the original length minus the amount we need to remove
                            byte[] newPacketByteArray = new byte[packetActualBytes[i] - (numBytesToRemove - bytesRemoved)];
                            Buffer.BlockCopy(packets[i], numBytesToRemove - bytesRemoved, newPacketByteArray, 0, newPacketByteArray.Length);

                            bytesRemoved += packetActualBytes[i] - newPacketByteArray.Length;
                            packets[i] = newPacketByteArray;
                            packetActualBytes[i] = newPacketByteArray.Length;

                            //Stop removing data here
                            break;
                        }
                        else if (i > packets.Count - 1)
                        {
                            //When i == (packet.Count - 1) I would expect the above if condition to always be true
                            throw new CommunicationException("This should be impossible.");
                        }
                        else
                        {
                            //If we want to remove this entire packet we can just set the list reference to null
                            bytesRemoved += packetActualBytes[i];
                            packets[i] = null;
                            packetActualBytes[i] = -1;
                        }
                    }

                    if (bytesRemoved != numBytesToRemove)
                        throw new CommunicationException("bytesRemoved should really equal the requested numBytesToRemove");

                    //Reset the totalBytesRead
                    totalBytesRead -= bytesRemoved;

                    //Get rid of any null packets
                    packets = (from current in packets
                               where current != null
                               select current).ToList();

                    packetActualBytes = (from current in packetActualBytes
                                         where current > -1
                                         select current).ToList();

                    //This is a really bad place to put a garbage collection as it hammers the CPU
                    //GC.Collect();
                }
            }

            /// <summary>
            /// Appends a new packet to the packetBuilder
            /// </summary>
            /// <param name="packetBytes"></param>
            /// <param name="packet"></param>
            public void AddPacket(int packetBytes, byte[] packet)
            {
                totalBytesRead += packetBytes;

                packets.Add(packet);
                packetActualBytes.Add(packetBytes);
            }

            /// <summary>
            /// Returns the most recent added packet to the builder and removes it from the list.
            /// Used to more efficiently ustilise allocated arrays.
            /// </summary>
            /// <returns></returns>
            public byte[] RemoveMostRecentPacket(ref int lastPacketBytesRead)
            {
                if (packets.Count > 0)
                {
                    int lastPacketIndex = packets.Count - 1;

                    lastPacketBytesRead = packetActualBytes[lastPacketIndex];
                    byte[] returnArray = packets[lastPacketIndex];

                    totalBytesRead -= packetActualBytes[lastPacketIndex];

                    packets.RemoveAt(lastPacketIndex);
                    packetActualBytes.RemoveAt(lastPacketIndex);

                    return returnArray;
                }
                else
                    throw new Exception("Unable to remove most recent packet as packet list is empty.");
            }

            /// <summary>
            /// Returns the number of packets currently in the packetbuilder
            /// </summary>
            /// <returns></returns>
            public int CurrentPacketCount()
            {
                return packets.Count;
            }

            /// <summary>
            /// Returns the number of unused bytes from the most recently added packet
            /// </summary>
            /// <returns></returns>
            public int NumUnusedBytesMostRecentPacket()
            {
                if (packets.Count > 0)
                {
                    int lastPacketIndex = packets.Count - 1;
                    return packets[lastPacketIndex].Length - packetActualBytes[lastPacketIndex];
                }
                else
                    throw new Exception("Unable to return requested size as packet list is empty.");
            }

            /// <summary>
            /// Returns the value of the front byte.
            /// </summary>
            /// <returns></returns>
            public byte FirstByte()
            {
                return packets[0][0];
            }

            /// <summary>
            /// Copies all data in the packetbuilder into a new array and returns
            /// </summary>
            /// <returns></returns>
            public byte[] GetAllData()
            {
                byte[] returnArray = new byte[totalBytesRead];

                int currentStart = 0;
                for (int i = 0; i < packets.Count; i++)
                {
                    Buffer.BlockCopy(packets[i], 0, returnArray, currentStart, packetActualBytes[i]);
                    currentStart += packetActualBytes[i];
                }

                return returnArray;
            }

            /// <summary>
            /// Copies the requested number of bytes from the packetbuilder into a new array and returns
            /// </summary>
            /// <param name="startIndex"></param>
            /// <param name="length"></param>
            /// <returns></returns>
            public byte[] ReadDataSection(int startIndex, int length)
            {
                byte[] returnArray = new byte[length];
                int runningTotal = 0, writeTotal = 0;
                int startingPacketIndex;

                int firstPacketStartIndex = 0;
                //First find the correct starting packet
                for (startingPacketIndex = 0; startingPacketIndex < packets.Count; startingPacketIndex++)
                {
                    if (startIndex - runningTotal <= packetActualBytes[startingPacketIndex])
                    {
                        firstPacketStartIndex = startIndex - runningTotal;
                        break;
                    }
                    else
                        runningTotal += packetActualBytes[startingPacketIndex];
                }

                //Copy the bytes of interest
                for (int i = startingPacketIndex; i < packets.Count; i++)
                {
                    if (i == startingPacketIndex)
                    {
                        if (length > packetActualBytes[i] - firstPacketStartIndex)
                            //If we want from some starting point to the end of the packet
                            Buffer.BlockCopy(packets[i], firstPacketStartIndex, returnArray, writeTotal, packetActualBytes[i] - firstPacketStartIndex);
                        else
                        {
                            //We only want part of the packet
                            Buffer.BlockCopy(packets[i], firstPacketStartIndex, returnArray, writeTotal, length);
                            writeTotal += length;
                            break;
                        }

                        writeTotal = packetActualBytes[i] - firstPacketStartIndex;
                    }
                    else
                    {
                        //We are no longer on the first packet
                        if (packetActualBytes[i] + writeTotal >= length)
                        {
                            //We have reached the last packet of interest
                            Buffer.BlockCopy(packets[i], 0, returnArray, writeTotal, length - writeTotal);
                            writeTotal += length - writeTotal;
                            break;
                        }
                        else
                        {
                            Buffer.BlockCopy(packets[i], 0, returnArray, writeTotal, packetActualBytes[i]);
                            writeTotal += packetActualBytes[i];
                        }
                    }
                }

                if (writeTotal != length)
                    throw new Exception("Not enough data available in packetBuilder to complete request.");

                return returnArray;
            }
        }

        /// <summary>
        /// Connection constructor
        /// </summary>
        /// <param name="serverSide">True if this connection was requested by a remote client.</param>
        /// <param name="connectionEndPoint">The IP information of the remote client.</param>
        public TCPConnection(bool serverSide, TcpClient tcpClient)
        {
            this.tcpConnectionCreationTime = DateTime.Now;
            
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
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Establishing connection with " + ConnectionEndPoint.Address.ToString() + ":" + ConnectionEndPoint.Port);

                DateTime establishStartTime = DateTime.Now;

                if (connectionEstablished || connectionShutdown)
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
                if (ServerSide)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("New connection detected from " + RemoteClientIP + ", waiting for client connId.");

                    //Wait for the client to send its identification
                    if (!connectionSetupWait.WaitOne(NetworkComms.connectionEstablishTimeoutMS))
                        throw new ConnectionSetupException("Timeout waiting for client connId from " + NetworkComms.LocalIP + " to " + RemoteClientIP + ":" + ConnectionEndPoint.Port + ". Connection created at " + tcpConnectionCreationTime.ToString("HH:mm:ss.fff") + ", EstablishConnection() entered at " + establishStartTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                    if (connectionSetupException)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ServerSide. " + connectionSetupExceptionStr);
                        throw new ConnectionSetupException("ServerSide. " + connectionSetupExceptionStr);
                    }

                    //Once we have the clients id we send our own
                    NetworkComms.SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), this, false, new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, NetworkComms.CommsPort), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                }
                else
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Initiating connection to " + RemoteClientIP);

                    //As the client we initiated the connection we now forward our local node identifier to the server
                    //If we are listening we include our local listen port as well
                    NetworkComms.SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), this, false, (NetworkComms.isListening ? new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, NetworkComms.CommsPort) : new ConnectionInfo(NetworkComms.localNetworkIdentifier.ToString(), LocalConnectionIP, -1)), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);

                    //Wait here for the server end to return its own identifier
                    if (!connectionSetupWait.WaitOne(NetworkComms.connectionEstablishTimeoutMS))
                        throw new ConnectionSetupException("Timeout waiting for server connId from " + NetworkComms.LocalIP + " to " + RemoteClientIP + ":" + ConnectionEndPoint.Port + ". Connection created at " + tcpConnectionCreationTime.ToString("HH:mm:ss.fff") + ", EstablishConnection() entered at " + establishStartTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.fff"));

                    if (connectionSetupException)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection setup exception. ClientSide. " + connectionSetupExceptionStr);
                        throw new ConnectionSetupException("ClientSide. " + connectionSetupExceptionStr);
                    }
                }

                if (this.connectionShutdown)
                    throw new ConnectionSetupException("Connection was closed during handshake.");

                //A quick idiot test
                if (this.ConnectionInfo == null)
                    throw new ConnectionSetupException("ConnectionInfo should never be null at this point.");

                //Only once the connection has been succesfully established do we record it in our connection dictionary
                lock (NetworkComms.globalDictAndDelegateLocker)
                {
                    if (NetworkComms.allConnectionsById.ContainsKey(this.ConnectionInfo.NetworkIdentifier))
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("... connection succesfully established with " + RemoteClientIP + " at connId " + this.ConnectionInfo.NetworkIdentifier.ToString());
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

                connectionEstablished = true;
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
        /// <param name="callLocation">Optional debug parameter.</param>
        public void CloseConnection(bool closeDueToError, int callLocation = 0)
        {
            if (closeDueToError)
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Closing connection with " + RemoteClientIP + " due to error [" + callLocation + "] - (" + (ConnectionInfo == null ? "NA" : ConnectionInfo.NetworkIdentifier.ToString()) + ")");
            else
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Closing connection with " + RemoteClientIP + " [" + callLocation + "] - (" + (ConnectionInfo == null ? "NA" : ConnectionInfo.NetworkIdentifier.ToString()) + ")");

            try
            {
                //Close connection my get called multiple times for a given connection depending on the reason for being closed
                bool firstClose = false;

                connectionShutdown = true;

                //Set possible error cases
                if (closeDueToError)
                {
                    connectionSetupException = true;
                    connectionSetupExceptionStr = "Connection has been closed.";
                }

                //Ensure we are not waiting for a connection to be established if we have died due to error
                connectionSetupWait.Set();

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

                //Once we think we have closed the connection it's time to get rid of our other references
                lock (NetworkComms.globalDictAndDelegateLocker)
                {
                    if (this.ConnectionInfo != null)
                    {
                        //We establish whether we have already done this step
                        if (NetworkComms.allConnectionsById.ContainsKey(this.ConnectionInfo.NetworkIdentifier))
                        {
                            //Maintain a reference if this is our first connection close
                            firstClose = true;
                        }

                        //Keep a reference of the connection for possible debugging later
                        if (NetworkComms.oldConnectionIdToConnectionInfo.ContainsKey(this.ConnectionInfo.NetworkIdentifier))
                            NetworkComms.oldConnectionIdToConnectionInfo[this.ConnectionInfo.NetworkIdentifier] = this.ConnectionInfo;
                        else
                            NetworkComms.oldConnectionIdToConnectionInfo.Add(this.ConnectionInfo.NetworkIdentifier, this.ConnectionInfo);

                        //Remove by network identifier
                        NetworkComms.allConnectionsById.Remove(this.ConnectionInfo.NetworkIdentifier);
                    }

                    //We can now remove this connection by end point as well
                    if (NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionEndPoint))
                        NetworkComms.allConnectionsByEndPoint.Remove(ConnectionEndPoint);
                }

                //Almost there
                //Last thing is to call any connection specific shutdown delegates
                if (firstClose && connectionSpecificShutdownDelegate != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered connection specific shutdown delegate for " + RemoteClientIP + " (" + this.ConnectionInfo.NetworkIdentifier.ToString() + ")");
                    connectionSpecificShutdownDelegate(this.ConnectionInfo.NetworkIdentifier);
                }

                //Last but not least we call any global connection shutdown delegates
                if (firstClose && NetworkComms.globalConnectionShutdownDelegates != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered global shutdown delegate for " + RemoteClientIP + " (" + this.ConnectionInfo.NetworkIdentifier.ToString() + ")");
                    NetworkComms.globalConnectionShutdownDelegates(this.ConnectionInfo.NetworkIdentifier);
                }
            }
            catch (Exception ex)
            {
                if (ex is ThreadAbortException)
                { /*Ignore the threadabort exception if we had to nuke the listen thread*/ }
                else
                    NetworkComms.LogError(ex, "ConnectionShutdownError");
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

            lock (packetSendLocker)
            {
                if (connectionShutdown)
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
                    if (connectionShutdown)
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
                        LastTrafficTime = DateTime.Now;

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
                    else if (totalBytesRead == 0 && (!dataAvailable || connectionShutdown))
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

                    if (totalBytesRead == 0 && (!dataAvailable || connectionShutdown))
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
        /// Attempts to use the data provided in packetBuilder to recreate something usefull. If we don't have enough data yet that value is set in packetBuilder.
        /// </summary>
        /// <param name="packetBuilder"></param>
        private void IncomingPacketHandleHandOff(ConnectionPacketBuilder packetBuilder)
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... checking for completed packet with " + packetBuilder.TotalBytesRead + " bytes read.");

                //Loop until we are finished with this packetBuilder
                int loopCounter = 0;
                while (true)
                {
                    //If we have ended up with a null packet at the front, probably due to some form of concatentation we can pull it off here
                    //It is possible we have concatenation of several null packets along with real data so we loop until the firstByte is greater than 0
                    if (packetBuilder.FirstByte() == 0)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... null packet removed in IncomingPacketHandleHandOff(), loop index - " + loopCounter);
                        //LastTrafficTime = DateTime.Now;

                        packetBuilder.ClearNTopBytes(1);

                        //Reset the expected bytes to 0 so that the next check starts from scratch
                        packetBuilder.TotalBytesExpected = 0;

                        //If we have run out of data completely then we can return immediately
                        if (packetBuilder.TotalBytesRead == 0) return;
                    }
                    else
                    {
                        //First determine the expected size of a header packet
                        int packetHeaderSize = packetBuilder.FirstByte() + 1;

                        //Do we have enough data to build a header?
                        if (packetBuilder.TotalBytesRead < packetHeaderSize)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... ... more data required for complete packet header.");

                            //Set the expected number of bytes and then return
                            packetBuilder.TotalBytesExpected = packetHeaderSize;
                            return;
                        }

                        //We have enough for a header
                        PacketHeader topPacketHeader = new PacketHeader(packetBuilder.ReadDataSection(1, packetHeaderSize - 1), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);

                        //Idiot test
                        if (topPacketHeader.PacketType == null)
                            throw new SerialisationException("packetType value in packetHeader should never be null");

                        //We can now use the header to establish if we have enough payload data
                        //First case is when we have not yet received enough data
                        if (packetBuilder.TotalBytesRead < packetHeaderSize + topPacketHeader.PayloadPacketSize)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... ... more data required for complete packet payload.");

                            //Set the expected number of bytes and then return
                            packetBuilder.TotalBytesExpected = packetHeaderSize + topPacketHeader.PayloadPacketSize;
                            return;
                        }
                        //Second case is we have enough data
                        else if (packetBuilder.TotalBytesRead >= packetHeaderSize + topPacketHeader.PayloadPacketSize)
                        {
                            //We can either have exactly the right amount or even more than we were expecting
                            //We may have too much data if we are sending high quantities and the packets have been concatenated
                            //no problem!!

                            //Build the necessary task input data
                            object[] completedData = new object[2];
                            completedData[0] = topPacketHeader;
                            completedData[1] = packetBuilder.ReadDataSection(packetHeaderSize, topPacketHeader.PayloadPacketSize);

                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Received packet of type '" + topPacketHeader.PacketType + "' from " + RemoteClientIP + (ConnectionInfo == null ? "" : " (" + ConnectionInfo.NetworkIdentifier.ToString() + ")") + ", containing " + packetHeaderSize + " header bytes and " + topPacketHeader.PayloadPacketSize + " payload bytes.");

                            if (NetworkComms.reservedPacketTypeNames.Contains(topPacketHeader.PacketType))
                            {
                                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... handling packet type '" + topPacketHeader.PacketType + "' inline. Loop index - " + loopCounter);
                                //If this is a reserved packetType we call the method inline so that it gets dealt with immediately
                                CompleteIncomingPacketWorker(completedData);
                            }
                            else
                            {
                                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... launching task to handle packet type '" + topPacketHeader.PacketType + "'. Loop index - " + loopCounter);
                                //If not a reserved packetType we run the completion in a seperate task so that this thread can continue to receive incoming data
                                Task.Factory.StartNew(CompleteIncomingPacketWorker, completedData);
                            }

                            //We clear the bytes we have just handed off
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Removing " + (packetHeaderSize + topPacketHeader.PayloadPacketSize).ToString() + " bytes from incoming packet buffer.");
                            packetBuilder.ClearNTopBytes(packetHeaderSize + topPacketHeader.PayloadPacketSize);

                            //Reset the expected bytes to 0 so that the next check starts from scratch
                            packetBuilder.TotalBytesExpected = 0;

                            //If we have run out of data completely then we can return immediately
                            if (packetBuilder.TotalBytesRead == 0) return;
                        }
                        else
                            throw new CommunicationException("This should be impossible!");
                    }

                    loopCounter++;
                }
            }
            catch (Exception ex)
            {
                //Any error, throw an exception.
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("A fatal exception occured in IncomingPacketHandleHandOff(), connection with " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + " be closed. See log file for more information.");

                NetworkComms.LogError(ex, "CommsError");
                CloseConnection(true, 16);
            }
        }

        /// <summary>
        /// Once we have received all incoming data we can handle it further.
        /// </summary>
        /// <param name="packetBytes"></param>
        private void CompleteIncomingPacketWorker(object packetBytes)
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... packet hand off task started.");

                //LastTrafficTime = DateTime.Now;

                //Check for a shutdown connection
                if (connectionShutdown) return;

                //Idiot check
                if (packetBytes == null) throw new NullReferenceException("Provided object packetBytes should really not be null.");

                //Unwrap with an idiot check
                object[] completedData = packetBytes as object[];
                if (completedData == null) throw new NullReferenceException("Type cast to object[] failed in CompleteIncomingPacketWorker.");

                //Unwrap with an idiot check
                PacketHeader packetHeader = completedData[0] as PacketHeader;
                if (packetHeader == null) throw new NullReferenceException("Type cast to PacketHeader failed in CompleteIncomingPacketWorker.");

                //Unwrap with an idiot check
                byte[] packetDataSection = completedData[1] as byte[];
                if (packetDataSection == null) throw new NullReferenceException("Type cast to byte[] failed in CompleteIncomingPacketWorker.");

                //We only look at the check sum if we want to and if it has been set by the remote end
                if (NetworkComms.EnablePacketCheckSumValidation && packetHeader.CheckSumHash.Length > 0)
                {
                    //Validate the checkSumhash of the data
                    if (packetHeader.CheckSumHash != NetworkComms.MD5Bytes(packetDataSection))
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn(" ... corrupted packet header detected.");

                        //We have corruption on a resend request, something is very wrong so we throw an exception.
                        if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend)) throw new CheckSumException("Corrupted md5CheckFailResend packet received.");

                        //Instead of throwing an exception we can request the packet to be resent
                        Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend), false, packetHeader.CheckSumHash, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                        SendPacket(returnPacket);

                        //We need to wait for the packet to be resent before going further
                        return;
                    }
                }

                //Remote end may have requested packet receive confirmation so we send that now
                if (packetHeader.ReceiveConfirmationRequired)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... sending requested receive confirmation packet.");

                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), false, packetHeader.CheckSumHash, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                    SendPacket(returnPacket);
                }

                //We can now pass the data onto the correct delegate
                //First we have to check for our reserved packet types
                //The following large sections have been factored out to make reading and debugging a little easier
                if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend))
                    CheckSumFailResendHandler(packetDataSection);
                else if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup))
                    ConnectionSetupHandler(packetDataSection);
                else if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket) && (NetworkComms.internalFixedSerializer.DeserialiseDataObject<bool>(packetDataSection, NetworkComms.internalFixedCompressor)) == false)
                {
                    //If we have received a ping packet from the originating source we reply with true
                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), false, true, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                    SendPacket(returnPacket);
                }

                //We allow users to add their own custom handlers for reserved packet types here
                //else
                if (true)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Triggering handlers for packet of type '" + packetHeader.PacketType + "' from " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port);

                    //Idiot check
                    if (RemoteClientIP == null || this.ConnectionInfo == null)
                        throw new CommunicationException("RemoteClientIP or ConnectionInfo is null. Probably due to connection closure.");

                    NetworkComms.TriggerPacketHandler(packetHeader, this.ConnectionInfo.NetworkIdentifier, packetDataSection);

                    //This is a really bad place to put a garbage collection, comment left in so that it does'nt get added again at some later date
                    //We don't want the CPU to JUST be trying to garbage collect the WHOLE TIME
                    //GC.Collect();
                }
            }
            catch (CommunicationException)
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("A communcation exception occured in CompleteIncomingPacketWorker(), connection with " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + " be closed.");
                CloseConnection(true, 2);
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An unhandled exception occured in CompleteIncomingPacketWorker(), connection with " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + " be closed. See log file for more information.");

                NetworkComms.LogError(ex, "CommsError");
                CloseConnection(true, 3);
            }
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
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Existing connection detected from peer with identifier " + ConnectionInfo.NetworkIdentifier + ". Closing existing connection in favour of new one.");

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
                        connectionSetupExceptionStr = " ... existing live connection at provided end point for this connection, no need for a second. End Point - " + ConnectionInfo.NetworkIdentifier + ", " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + ". ";
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
        private bool ConnectionSetupHandlerInner(ref bool possibleExistingConnectionWithPeer_ByIdentifier, ref bool possibleExistingConnectionWithPeer_ByEndPoint, ref TCPConnection existingConnection)
        {
            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                //If we no longer have the original endPoint reference (set in the constructor) then the connection must have been closed already
                if (!NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionEndPoint))
                {
                    connectionSetupException = true;
                    connectionSetupExceptionStr = "Connection setup received after connection closure with " + ConnectionEndPoint.Address.ToString() + ":" + ConnectionEndPoint.Port;
                }
                else
                {
                    //We need to check for a possible GUID clash
                    //Probability of a clash is approx 0.1% if 1E19 connection are maintained simultaneously (This many connections has not be tested ;))
                    //It's far more likely we have a strange scenario where a remote peer is trying to establish a second independant connection (which should not really happen in the first place)
                    //but hey, we live in a crazy world!
                    if (this.ConnectionInfo.NetworkIdentifier == NetworkComms.NetworkNodeIdentifier)
                    {
                        connectionSetupException = true;
                        connectionSetupExceptionStr = "Remote peer has same network idendifier to local, " + ConnectionInfo.NetworkIdentifier + ". Although technically near impossible some special (engineered) scenarios make this more probable.";
                    }
                    else if (NetworkComms.allConnectionsById.ContainsKey(ConnectionInfo.NetworkIdentifier))
                    {
                        possibleExistingConnectionWithPeer_ByIdentifier = true;
                        existingConnection = NetworkComms.allConnectionsById[ConnectionInfo.NetworkIdentifier];
                    }
                    else
                    {
                        //Record the new connection
                        NetworkComms.allConnectionsById.Add(this.ConnectionInfo.NetworkIdentifier, this);

                        //If the recorded endPoint port does not match the latest connectionInfo object then we should correct it
                        //This will happen if we are establishing the connection at the server end and have a client which is also listening on a specific port
                        if (ConnectionEndPoint.Port != this.ConnectionInfo.ClientPort && this.ConnectionInfo.ClientPort != -1)
                        {
                            //Remove the entry for 'this' before we change our local ConnectionEndPoint
                            NetworkComms.allConnectionsByEndPoint.Remove(ConnectionEndPoint);
                            ConnectionEndPoint = new IPEndPoint(ConnectionEndPoint.Address, this.ConnectionInfo.ClientPort);

                            if (NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionEndPoint))
                            {
                                //If we have an existing entry at the new end point we unroll the changes made in this method and set the necessary boolean
                                NetworkComms.allConnectionsById.Remove(this.ConnectionInfo.NetworkIdentifier);
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
        /// Handle an incoming CheckSumFailResend packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        private void CheckSumFailResendHandler(byte[] packetDataSection)
        {
            //If we have been asked to resend a packet then we just go through the list and resend it.
            OldSentPacket packetToReSend;
            lock (sentPacketsLocker)
            {
                string checkSumRequested = NetworkComms.internalFixedSerializer.DeserialiseDataObject<string>(packetDataSection, NetworkComms.internalFixedCompressor);

                if (sentPackets.ContainsKey(checkSumRequested))
                    packetToReSend = sentPackets[checkSumRequested];
                else
                    throw new CheckSumException("There was no packet sent with a matching check sum");
            }

            //If we have already tried resending the packet 10 times something has gone horribly wrong
            if (packetToReSend.sendCount > 10) throw new CheckSumException("Packet sent resulted in a catastropic checksum check exception.");

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn(" ... resending packet due to MD5 mismatch.");

            //Increment send count and then resend
            packetToReSend.sendCount++;
            SendPacket(packetToReSend.packet);
        }

        /// <summary>
        /// Send the provided packet to the remote peer
        /// </summary>
        /// <param name="packetTypeStr"></param>
        /// <param name="packetData"></param>
        /// <param name="destinationIPAddress"></param>
        /// <param name="receiveConfirmationRequired"></param>
        /// <returns></returns>
        public void SendPacket(Packet packet)
        {
            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Entering packet send of '" + packet.PacketHeader.PacketType + "' packetType to " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + (connectionEstablished ? " (" + ConnectionId + ")" : ""));

            //Multiple threads may try to send packets at the same time so wait one at a time here
            lock (packetSendLocker)
            {
                //We don't allow sends on a closed connection
                if (connectionShutdown) throw new CommunicationException("Attempting to send packet on connection which has been closed or is currently closing.");

                string confirmationCheckSum = "";
                AutoResetEvent confirmationWaitSignal = new AutoResetEvent(false);
                bool remotePeerDisconnectedDuringWait = false;

                #region Delegates
                //Specify a delegate we may use if we require receive confirmation
                NetworkComms.PacketHandlerCallBackDelegate<string> confirmationDelegate = (packetHeader, connectionId, incomingString) =>
                {
                    if (connectionId == this.ConnectionInfo.NetworkIdentifier)
                    {
                        confirmationCheckSum = incomingString;
                        confirmationWaitSignal.Set();
                    }
                };

                //We use the following delegate to quickly force a response timeout if the remote end disconnects during a send/wait
                NetworkComms.ConnectionShutdownDelegate ConfirmationShutDownDelegate = (sourceConnectionId) =>
                {
                    if (sourceConnectionId == this.ConnectionInfo.NetworkIdentifier)
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

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending a packet of type '" + packet.PacketHeader.PacketType + "' to " + RemoteClientIP + " (" + (this.ConnectionInfo == null ? "NA" : this.ConnectionInfo.NetworkIdentifier.ToString()) + "), containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");

                    tcpClientNetworkStream.Write(headerBytes, 0, headerBytes.Length);
                    tcpClientNetworkStream.Write(packet.PacketData, 0, packet.PacketData.Length);

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... " + (headerBytes.Length + packet.PacketData.Length).ToString() + " bytes written to netstream.");

                    if (!TCPClient.Connected)
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
                    LastTrafficTime = DateTime.Now;
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

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Completed packet send of '" + packet.PacketHeader.PacketType + "' packetType to " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + (connectionEstablished ? " (" + ConnectionId + ")" : ""));
        }

        /// <summary>
        /// Send a null packet (1 byte long) to this connection. Helps keep the connection alive but also bandwidth usage to absolute minimum. If an exception is thrown the connection will be closed.
        /// </summary>
        public void SendNullPacket()
        {
            try
            {
                //Only once the connection has been established do we send null packets
                if (connectionEstablished)
                {
                    //Multiple threads may try to send packets at the same time so we need this lock to prevent a thread cross talk
                    lock (packetSendLocker)
                    {
                        //if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Sending null packet to " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + (connectionEstablished ? " (" + ConnectionId + ")." : "."));

                        //Send a single 0 byte
                        tcpClientNetworkStream.Write(new byte[] { 0 }, 0, 1);

                        //Update the traffic time after we have written to netStream
                        LastTrafficTime = DateTime.Now;
                    }
                }
            }
            catch (Exception)
            {
                CloseConnection(true, 19);
                //throw new CommunicationException(ex.ToString());
            }
        }

        /// <summary>
        /// Add a connection specific shutdown delegate.
        /// </summary>
        /// <param name="handlerToAppend"></param>
        public void AppendConnectionSpecificShutdownHandler(NetworkComms.ConnectionShutdownDelegate handlerToAppend)
        {
            lock (delegateLocker)
            {
                if (connectionSpecificShutdownDelegate == null)
                    connectionSpecificShutdownDelegate = handlerToAppend;
                else
                    connectionSpecificShutdownDelegate += handlerToAppend;

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Added connection specific shutdown delegate to connection with id " + (this.ConnectionInfo == null ? "NA" : this.ConnectionInfo.NetworkIdentifier.ToString()));
            }
        }

        /// <summary>
        /// Remove a connection specific shutdown delegate.
        /// </summary>
        /// <param name="handlerToRemove"></param>
        public void RemoveConnectionSpecificShutdownHandler(NetworkComms.ConnectionShutdownDelegate handlerToRemove)
        {
            lock (delegateLocker)
            {
                connectionSpecificShutdownDelegate -= handlerToRemove;
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Removed connection specific shutdown delegate to connection with id " + (this.ConnectionInfo == null ? "NA" : this.ConnectionInfo.NetworkIdentifier.ToString()));
            }
        }

        /// <summary>
        /// Sends a ping packet to the remote end of this connection and returns true if the correct response is received. If any exception occurs returns false.
        /// </summary>
        /// <param name="aliveRespondTimeout"></param>
        /// <returns>True if the connection is active, false otherwise.</returns>
        public bool CheckConnectionAliveState(int aliveRespondTimeout)
        {
            DateTime startTime=DateTime.Now;

            if (ConnectionInfo == null)
            {
                if ((DateTime.Now - tcpConnectionCreationTime).Milliseconds > NetworkComms.connectionEstablishTimeoutMS)
                {
                    CloseConnection(false, -1);
                    return false;
                }
                else
                    return true;
            }
            else
            {
                try
                {
                    bool returnValue = NetworkComms.SendReceiveObject<bool>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), ConnectionId, false, Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), aliveRespondTimeout, false, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("ConnectionAliveTest success, response in " + (DateTime.Now - startTime).TotalMilliseconds + "ms");
                  
                    return returnValue;
                }
                catch (Exception)
                {
                    CloseConnection(true, 4);
                    return false;
                }
            }
        }
    }
}
