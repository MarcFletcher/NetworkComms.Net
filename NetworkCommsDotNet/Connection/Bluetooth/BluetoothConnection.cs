#if !NET2

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using DPSBase;
using InTheHand.Net.Sockets;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A connection type that uses Bluetooth RFCOMM to communicate 
    /// </summary>
    public sealed partial class BluetoothConnection : Connection
    {
        /// <summary>
        /// The Bluetooth equivalent of TCPClient
        /// </summary>
        BluetoothClient btClient;

        /// <summary>
        /// The network stream associated with btClient
        /// </summary>
        NetworkStream btClientNetworkStream;

        /// <summary>
        /// Bluetooth connection constructor
        /// </summary>
        private BluetoothConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, BluetoothClient btClient)
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            if (btClient != null)
                this.btClient = btClient;
        }

        /// <inheritdoc />
        protected override void EstablishConnectionSpecific()
        {
            if (btClient == null) ConnectSocket();

            //We should now be able to set the connectionInfo localEndPoint
            var localEndPoint = btClient.Client.LocalEndPoint as BluetoothEndPoint;
            localEndPoint = new BluetoothEndPoint(localEndPoint.Address, BluetoothService.SerialPort, localEndPoint.Port);

            NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteEndPoint, localEndPoint);
            ConnectionInfo.UpdateLocalEndPointInfo(localEndPoint);

            btClient.Client.ReceiveBufferSize = NetworkComms.ReceiveBufferSizeBytes;
            btClient.Client.SendBufferSize = NetworkComms.SendBufferSizeBytes;

            //We are going to be using the networkStream quite a bit so we pull out a reference once here
            btClientNetworkStream = btClient.GetStream();
                        
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
        }

        /// <summary>
        /// If we were not provided with a btClient on creation we need to create one
        /// </summary>
        private void ConnectSocket()
        {
            try
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Connecting bluetooth client with " + ConnectionInfo);

                bool connectSuccess = true;

                //We now connect to our target
                btClient = new BluetoothClient();

                //Start the connection using the async version
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

                if (!connectSuccess) throw new ConnectionSetupException("Timeout waiting for remoteEndPoint to accept bluetooth connection.");
            }
            catch (Exception ex)
            {
                CloseConnection(true, 17);
                throw new ConnectionSetupException("Error during bluetooth connection establish with destination (" + ConnectionInfo + "). Destination may not be listening or connect timed out. " + ex.ToString());
            }
        }

        /// <inheritdoc />
        protected override void StartIncomingDataListen()
        {
            if (!NetworkComms.ConnectionExists(ConnectionInfo.RemoteEndPoint, ConnectionInfo.LocalEndPoint, ConnectionType.Bluetooth, ConnectionInfo.ApplicationLayerProtocol))
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
                        incomingDataListenThread.Name = "BT_IncomingDataListener";
                        incomingDataListenThread.IsBackground = true;
                        incomingDataListenThread.Start();
                    }
                }
                else
                    btClientNetworkStream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingBluetoothPacketHandler), btClientNetworkStream);
            }

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Listening for incoming data from " + ConnectionInfo);
        }

        /// <summary>
        /// Asynchronous incoming connection data delegate
        /// </summary>
        /// <param name="ar">The call back state object</param>
        void IncomingBluetoothPacketHandler(IAsyncResult ar)
        {
            //Initialised with true so that logic still works in WP8
            bool dataAvailable = true;

#if !WINDOWS_PHONE
            //Incoming data always gets handled in a timeCritical fashion at this point
            Thread.CurrentThread.Priority = NetworkComms.timeCriticalThreadPriority;
            //int bytesRead;
#endif

            try
            {
#if WINDOWS_PHONE
                var stream = ar.AsyncState as Stream;
                var count = stream.EndRead(ar);
                totalBytesRead = count + totalBytesRead;
#else
                var netStream = (NetworkStream)ar.AsyncState;
                if (!netStream.CanRead)
                    throw new ObjectDisposedException("Unable to read from stream.");

                totalBytesRead = netStream.EndRead(ar) + totalBytesRead;
                dataAvailable = netStream.DataAvailable;
#endif
                if (totalBytesRead > 0)
                {
                    ConnectionInfo.UpdateLastTrafficTime();

                    //If we have read a single byte which is 0 and we are not expecting other data
                    if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled &&
                        totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingPacketHandler() from " + ConnectionInfo + ". 1");
                    }
                    else
                    {
                        //if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder.");

                        //If there is more data to get then add it to the packets lists;
                        packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);

#if !WINDOWS_PHONE
                        //If we have more data we might as well continue reading synchronously
                        //In order to deal with data as soon as we think we have sufficient we will leave this loop
                        while (dataAvailable && packetBuilder.TotalBytesCached < packetBuilder.TotalBytesExpected)
                        {
                            int bufferOffset = 0;

                            //We need a buffer for our incoming data
                            //First we try to reuse a previous buffer
                            if (packetBuilder.TotalPartialPacketCount > 0 && packetBuilder.NumUnusedBytesMostRecentPartialPacket() > 0)
                                dataBuffer = packetBuilder.RemoveMostRecentPartialPacket(ref bufferOffset);
                            else
                                //If we have nothing to reuse we allocate a new buffer
                                dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];

                            totalBytesRead = netStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                            if (totalBytesRead > 0)
                            {
                                ConnectionInfo.UpdateLastTrafficTime();

                                //If we have read a single byte which is 0 and we are not expecting other data
                                if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled && totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                                {
                                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet ignored in IncomingPacketHandler() from " + ConnectionInfo + ". 2");
                                    //LastTrafficTime = DateTime.Now;
                                }
                                else
                                {
                                    //if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder for connection with " + ConnectionInfo + ". Cached " + packetBuilder.TotalBytesCached.ToString() + "B, expecting " + packetBuilder.TotalBytesExpected.ToString() + "B.");
                                    packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                                    dataAvailable = netStream.DataAvailable;
                                }
                            }
                            else
                                break;
                        }
#endif
                    }
                }

                if (packetBuilder.TotalBytesCached > 0 && packetBuilder.TotalBytesCached >= packetBuilder.TotalBytesExpected)
                {
                    //Once we think we might have enough data we call the incoming packet handle hand off
                    //Should we have a complete packet this method will start the appropriate task
                    //This method will now clear byes from the incoming packets if we have received something complete.
                    IncomingPacketHandleHandOff(packetBuilder);
                }

                if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionState == ConnectionState.Shutdown))
                    CloseConnection(false, -2);
                else
                {
                    //We need a buffer for our incoming data
                    //First we try to reuse a previous buffer
                    if (packetBuilder.TotalPartialPacketCount > 0 && packetBuilder.NumUnusedBytesMostRecentPartialPacket() > 0)
                        dataBuffer = packetBuilder.RemoveMostRecentPartialPacket(ref totalBytesRead);
                    else
                    {
                        //If we have nothing to reuse we allocate a new buffer
                        dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];
                        totalBytesRead = 0;
                    }

#if WINDOWS_PHONE
                    stream.BeginRead(dataBuffer, totalBytesRead, dataBuffer.Length - totalBytesRead, IncomingTCPPacketHandler, stream);
#else
                    netStream.BeginRead(dataBuffer, totalBytesRead, dataBuffer.Length - totalBytesRead, IncomingBluetoothPacketHandler, netStream);
#endif
                }

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
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_BluetoothConnectionIncomingPacketHandler");
                CloseConnection(true, 31);
            }

#if !WINDOWS_PHONE
            Thread.CurrentThread.Priority = ThreadPriority.Normal;
#endif
        }

        /// <summary>
        /// Synchronous incoming connection data worker
        /// </summary>
        void IncomingBluetoothDataSyncWorker()
        {
            bool dataAvailable = false;

            try
            {
                while (true)
                {
                    if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                        break;

                    int bufferOffset = 0;

                    //We need a buffer for our incoming data
                    //First we try to reuse a previous buffer
                    if (packetBuilder.TotalPartialPacketCount > 0 && packetBuilder.NumUnusedBytesMostRecentPartialPacket() > 0)
                        dataBuffer = packetBuilder.RemoveMostRecentPartialPacket(ref bufferOffset);
                    else
                        //If we have nothing to reuse we allocate a new buffer
                        dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];

                    //We block here until there is data to read
                    //When we read data we read until method returns or we fill the buffer length
                    totalBytesRead = btClientNetworkStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                    //Check to see if there is more data ready to be read
                    dataAvailable = btClientNetworkStream.DataAvailable;

                    //If we read any data it gets handed off to the packetBuilder
                    if (totalBytesRead > 0)
                    {
                        ConnectionInfo.UpdateLastTrafficTime();

                        //If we have read a single byte which is 0 and we are not expecting other data
                        if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled && totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingDataSyncWorker() from " + ConnectionInfo + ".");
                        }
                        else
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder for connection with " + ConnectionInfo + ". Cached " + packetBuilder.TotalBytesCached.ToString() + "B, expecting " + packetBuilder.TotalBytesExpected.ToString() + "B.");

                            packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                        }
                    }
                    else if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionState == ConnectionState.Shutdown))
                    {
                        //If we read 0 bytes and there is no data available we should be shutting down
                        CloseConnection(false, -10);
                        break;
                    }

                    //If we have read some data and we have more or equal what was expected we attempt a data hand off
                    if (packetBuilder.TotalBytesCached > 0 && packetBuilder.TotalBytesCached >= packetBuilder.TotalBytesExpected)
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
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_TCPConnectionIncomingPacketHandler");
                CloseConnection(true, 39);
            }

            //Clear the listen thread object because the thread is about to end
            incomingDataListenThread = null;

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Incoming data listen thread ending for " + ConnectionInfo);
        }

        /// <inheritdoc />
        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
            //The following attempts to correctly close the connection
            //Try to close the networkStream first
            try
            {
                if (btClientNetworkStream != null) btClientNetworkStream.Close();
            }
            catch (Exception)
            {
            }
            finally
            {
                btClientNetworkStream = null;
            }

            //Try to close the tcpClient
            try
            {
                btClient.Client.Disconnect(false);
                btClient.Client.Close();
            }
            catch (Exception)
            {
            }

            //Try to close the tcpClient
            try
            {
                btClient.Close();
            }
            catch (Exception)
            {
            }

        }

        /// <inheritdoc />
        protected override double[] SendStreams(StreamTools.StreamSendWrapper[] streamsToSend, double maxSendTimePerKB, long totalBytesToSend)
        {
            double[] timings = new double[streamsToSend.Length];

            Stream sendingStream = btClientNetworkStream;

            for (int i = 0; i < streamsToSend.Length; i++)
            {
                if (streamsToSend[i].Length > 0)
                {
                    //Write each stream
                    timings[i] = streamsToSend[i].ThreadSafeStream.CopyTo(sendingStream, streamsToSend[i].Start, streamsToSend[i].Length, NetworkComms.SendBufferSizeBytes, maxSendTimePerKB, MinSendTimeoutMS);

                    //Finish by checking if the stream needs closing
                    if (streamsToSend[i].ThreadSafeStream.CloseStreamAfterSend)
                        streamsToSend[i].ThreadSafeStream.Close();
                }
                else
                    timings[i] = 0;
            }

            if (!btClient.Connected)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("BTClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");

                throw new CommunicationException("BTClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
            }

            return timings;
        }
    }
}

#endif