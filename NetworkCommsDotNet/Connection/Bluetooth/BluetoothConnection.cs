#if !NET2

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using DPSBase;

namespace NetworkCommsDotNet
{
    public sealed partial class BluetoothConnection : Connection
    {
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
                        //If we have more data we might as well continue reading syncronously
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
                    //Once we think we might have enough data we call the incoming packet handle handoff
                    //Should we have a complete packet this method will start the appriate task
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

                    //If we have read some data and we have more or equal what was expected we attempt a data handoff
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

        protected override void SendPacketSpecific(Packet packet)
        {
            byte[] headerBytes;

            //If this connection does not use the applicationlayerprotocol we need to check a few things
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
            {
                //Serialise the header
                headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);
            }
            else
            {
                headerBytes = new byte[0];

                if (packet.PacketHeader.PacketType != Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
                    throw new UnexpectedPacketTypeException("Only 'Unmanaged' packet types can be used if the NetworkComms.Net application layer protocol is disabled.");

                if (packet.PacketData.Length == 0)
                    throw new NotSupportedException("Sending a zero length array if the NetworkComms.Net application layer protocol is disabled is not supported.");
            }

            double maxSendTimePerKB = double.MaxValue;
            if (!NetworkComms.DisableConnectionSendTimeouts)
            {
                if (SendTimesMSPerKBCache.Count > MinNumSendsBeforeConnectionSpecificSendTimeout)
                    maxSendTimePerKB = Math.Max(MinimumMSPerKBSendTimeout, SendTimesMSPerKBCache.CalculateMean() + NumberOfStDeviationsForWriteTimeout * SendTimesMSPerKBCache.CalculateStdDeviation());
                else
                    maxSendTimePerKB = DefaultMSPerKBSendTimeout;
            }

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Sending a packet of type '" + packet.PacketHeader.PacketType + "' to " +
                ConnectionInfo + " containing " + headerBytes.Length.ToString() + " header bytes and " + packet.PacketData.Length.ToString() + " payload bytes. Allowing " +
                maxSendTimePerKB.ToString("0.0##") + " ms/KB for send.");

            DateTime startTime = DateTime.Now;

            Stream sendingStream;

            sendingStream = btClientNetworkStream;

            //We only need to write the header if we have prepared the necessary bytes
            double headerWriteTime = 0;
            if (headerBytes.Length > 0)
            {
                headerWriteTime = StreamWriteWithTimeout.Write(headerBytes, headerBytes.Length, sendingStream, NetworkComms.SendBufferSizeBytes, maxSendTimePerKB, MinSendTimeoutMS);
                SendTimesMSPerKBCache.AddValue(headerWriteTime, headerBytes.Length);
            }

            //We can now write the payload data
            double dataWriteTime = 0;
            if (packet.PacketData.Length > 0)
                dataWriteTime = packet.PacketData.ThreadSafeStream.CopyTo(sendingStream, packet.PacketData.Start, packet.PacketData.Length, NetworkComms.SendBufferSizeBytes, maxSendTimePerKB, MinSendTimeoutMS);

            //We record each send independantly as if one is considerably larger than 
            //the other it will provide a much more reliable rate
            SendTimesMSPerKBCache.AddValue(dataWriteTime, packet.PacketData.Length);
            SendTimesMSPerKBCache.TrimList(MaxNumSendTimes);

            //Correctly dispose the stream if we are finished with it
            if (packet.PacketData.ThreadSafeStream.CloseStreamAfterSend)
                packet.PacketData.ThreadSafeStream.Close();

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + ((headerBytes.Length + packet.PacketData.Length) / 1024.0).ToString("0.000") + "KB written to TCP netstream at average of " + (((headerBytes.Length + packet.PacketData.Length) / 1024.0) / (DateTime.Now - startTime).TotalSeconds).ToString("0.000") + "KB/s. Current:" + ((headerWriteTime + dataWriteTime) / 2).ToString("0.00") + " ms/KB, AVG:" + SendTimesMSPerKBCache.CalculateMean().ToString("0.00") + " ms/KB.");

            if (!btClient.Connected)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
                throw new CommunicationException("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
            }
        }

        protected override void SendNullPacket()
        {
            //We can't send null packets if the application layer is disabled
            //as we have no way to distinquish them on the receiving side
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Ignoring null packet send to " + ConnectionInfo + " as the application layer protocol is disabled.");
                return;
            }

            try
            {
                //Only once the connection has been established do we send null packets
                if (ConnectionInfo.ConnectionState == ConnectionState.Established)
                {
                    //Multiple threads may try to send packets at the same time so we need this lock to prevent a thread cross talk
                    lock (sendLocker)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Sending null packet to " + ConnectionInfo);

                        //Send a single 0 byte
                        double maxSendTimePerKB = double.MaxValue;
                        if (!NetworkComms.DisableConnectionSendTimeouts)
                        {
                            if (SendTimesMSPerKBCache.Count > MinNumSendsBeforeConnectionSpecificSendTimeout)
                                maxSendTimePerKB = Math.Max(MinimumMSPerKBSendTimeout, SendTimesMSPerKBCache.CalculateMean() + NumberOfStDeviationsForWriteTimeout * SendTimesMSPerKBCache.CalculateStdDeviation());
                            else
                                maxSendTimePerKB = DefaultMSPerKBSendTimeout;
                        }

                        StreamWriteWithTimeout.Write(new byte[] { 0 }, 1, btClientNetworkStream, 1, maxSendTimePerKB, MinSendTimeoutMS);

                        //Update the traffic time after we have written to netStream
                        ConnectionInfo.UpdateLastTrafficTime();
                    }
                }

                //If the connection is shutdown we should call close
                if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown) CloseConnection(false, -8);
            }
            catch (Exception)
            {
                CloseConnection(true, 19);
            }
        }
    }
}

#endif