//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

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
        /// Asynchronous incoming connection data delegate
        /// </summary>
        /// <param name="ar">The call back state object</param>
        void IncomingTCPPacketHandler(IAsyncResult ar)
        {
            //Incoming data always gets handled in a timeCritical fashion at this point
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
                        ConnectionInfo.UpdateLastTrafficTime();

                        //If we have read a single byte which is 0 and we are not expecting other data
                        if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingPacketHandler() from "+ConnectionInfo+". 1");
                        }
                        else
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead + " bytes added to packetBuilder.");

                            //If there is more data to get then add it to the packets lists;
                            packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);

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
                                    if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                                    {
                                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingPacketHandler() from "+ConnectionInfo+". 2");
                                        //LastTrafficTime = DateTime.Now;
                                    }
                                    else
                                    {
                                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead + " bytes added to packetBuilder.");
                                        packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                                        dataAvailable = netStream.DataAvailable;
                                    }
                                }
                                else
                                    break;
                            }
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

                        netStream.BeginRead(dataBuffer, totalBytesRead, dataBuffer.Length - totalBytesRead, IncomingTCPPacketHandler, netStream);
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
        /// Synchronous incoming connection data worker
        /// </summary>
        void IncomingTCPDataSyncWorker()
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
                    totalBytesRead = tcpClientNetworkStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                    //Check to see if there is more data ready to be read
                    dataAvailable = tcpClientNetworkStream.DataAvailable;

                    //If we read any data it gets handed off to the packetBuilder
                    if (totalBytesRead > 0)
                    {
                        ConnectionInfo.UpdateLastTrafficTime();

                        //If we have read a single byte which is 0 and we are not expecting other data
                        if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingDataSyncWorker() from "+ConnectionInfo+".");
                        }
                        else
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead + " bytes added to packetBuilder.");
                            packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                        }
                    }
                    else if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionState == ConnectionState.Shutdown))
                    {
                        //If we read 0 bytes and there is no data available we should be shutting down
                        CloseConnection(false, -1);
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

            //Clear the listen thread object because the thread is about to end
            incomingDataListenThread = null;

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Incoming data listen thread ending for " + ConnectionInfo);
        }

        /// <summary>
        /// Closes the <see cref="TCPConnection"/>
        /// </summary>
        /// <param name="closeDueToError">Closing a connection due an error possibly requires a few extra steps.</param>
        /// <param name="logLocation">Optional debug parameter.</param>
        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
            //The following attempts to correctly close the connection
            //Try to close the networkStream first
            try
            {
                if (tcpClientNetworkStream != null) tcpClientNetworkStream.Close();
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

            //Try to close the tcpClient
            try
            {
                tcpClient.Close();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Sends the provided packet to the remote end point
        /// </summary>
        /// <param name="packet">Packet to send</param>
        protected override void SendPacketSpecific(Packet packet)
        {
            //To keep memory copies to a minimum we send the header and payload in two calls to networkStream.Write
            byte[] headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Sending a packet of type '" + packet.PacketHeader.PacketType + "' to " + ConnectionInfo + " containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");

            tcpClientNetworkStream.Write(headerBytes, 0, headerBytes.Length);
            //tcpClientNetworkStream.Write(packet.PacketData.ToArray(), 0, packet.PacketData.Length);
            packet.PacketData.ThreadSafeStream.CopyTo(tcpClientNetworkStream, packet.PacketData.Start, packet.PacketData.Length);

            //Correctly dispose the stream if we are finished with it
            if (packet.PacketData.ThreadSafeStream.CloseStreamAfterSend)
                packet.PacketData.ThreadSafeStream.Close();

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + (headerBytes.Length + packet.PacketData.Length).ToString() + " bytes written to TCP netstream.");

            if (!tcpClient.Connected)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
                throw new CommunicationException("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
            }
        }

        /// <summary>
        /// Send a null packet (1 byte) to the remotEndPoint. Helps keep the TCP connection alive while ensuring the bandwidth usage is an absolute minimum. If an exception is thrown the connection will be closed.
        /// </summary>
        protected override void SendNullPacket()
        {
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
                        tcpClientNetworkStream.Write(new byte[] { 0 }, 0, 1);

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
