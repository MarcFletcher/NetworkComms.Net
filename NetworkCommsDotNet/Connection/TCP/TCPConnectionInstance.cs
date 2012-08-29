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
        /// Asynchronous incoming connection data delegate
        /// </summary>
        /// <param name="ar"></param>
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
                        if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesRead == 0)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... null packet removed in IncomingPacketHandler().");
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
                                    dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];

                                totalBytesRead = netStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                                if (totalBytesRead > 0)
                                {
                                    ConnectionInfo.UpdateLastTrafficTime();

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

                    if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionShutdown))
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
                    if (ConnectionInfo.ConnectionShutdown)
                        break;

                    int bufferOffset = 0;

                    //We need a buffer for our incoming data
                    //First we try to reuse a previous buffer
                    if (packetBuilder.CurrentPacketCount() > 0 && packetBuilder.NumUnusedBytesMostRecentPacket() > 0)
                        dataBuffer = packetBuilder.RemoveMostRecentPacket(ref bufferOffset);
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
                        if (totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesRead == 0)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... null packet removed in IncomingDataSyncWorker().");
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

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Incoming data listen thread ending for " + ConnectionInfo);
        }

        /// <summary>
        /// Closes a connection
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
        /// Send the provided packet to the remote peer
        /// </summary>
        /// <param name="packetTypeStr"></param>
        /// <param name="packetData"></param>
        /// <param name="destinationIPAddress"></param>
        /// <param name="receiveConfirmationRequired"></param>
        /// <returns></returns>
        protected override void SendPacketSpecific(Packet packet)
        {
            //To keep memory copies to a minimum we send the header and payload in two calls to networkStream.Write
            byte[] headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending a packet of type '" + packet.PacketHeader.PacketType + "' to " + ConnectionInfo + " containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");

            tcpClientNetworkStream.Write(headerBytes, 0, headerBytes.Length);
            tcpClientNetworkStream.Write(packet.PacketData, 0, packet.PacketData.Length);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... " + (headerBytes.Length + packet.PacketData.Length).ToString() + " bytes written to TCP netstream.");

            if (!tcpClient.Connected)
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Error("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
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
                if (ConnectionInfo.ConnectionEstablished)
                {
                    //Multiple threads may try to send packets at the same time so we need this lock to prevent a thread cross talk
                    lock (sendLocker)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Sending null packet to " + ConnectionInfo);

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
            }
        }
    }
}
