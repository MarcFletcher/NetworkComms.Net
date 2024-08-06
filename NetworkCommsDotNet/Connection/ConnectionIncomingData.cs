﻿// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.IO;

using System.Net.Sockets;

namespace NetworkCommsDotNet.Connections
{
    public abstract partial class Connection
    {
        /// <summary>
        /// The <see cref="PacketBuilder"/> for this connection
        /// </summary>
        protected PacketBuilder packetBuilder;
                
        /// <summary>
        /// The total bytes read so far within dataBuffer
        /// </summary>
        protected int totalBytesRead;

        /// <summary>
        /// The thread listening for incoming data should we be using synchronous methods.
        /// </summary>
        protected Thread incomingDataListenThread = null;

        /// <summary>
        /// True if async listen has started
        /// </summary>
        protected bool asyncListenStarted = false;

        /// <summary>
        /// True if the async listen method is in a beginRead
        /// </summary>
        protected volatile bool asyncListenerInRead = false;

        /// <summary>
        /// A connection specific method which triggers any requisites for accepting incoming data
        /// </summary>
        protected abstract void StartIncomingDataListen();

        /// <summary>
        /// Attempts to use the data provided in packetBuilder to recreate something useful. If we don't have enough data 
        /// yet that value is set in packetBuilder.
        /// </summary>
        /// <param name="packetBuilder">The <see cref="PacketBuilder"/> containing incoming cached data</param>
        protected void IncomingPacketHandleHandOff(PacketBuilder packetBuilder)
        {
            int loopCounter = 0;
            try
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... checking for completed packet with " + packetBuilder.TotalBytesCached.ToString() + " bytes read.");

                if (packetBuilder.TotalPartialPacketCount == 0)
                    throw new Exception("Executing IncomingPacketHandleHandOff when no packets exist in packetbuilder.");

                //Loop until we are finished with this packetBuilder
                while (true)
                {
                    //If we have ended up with a null packet at the front, probably due to some form of concatenation we can pull it off here
                    //It is possible we have concatenation of several null packets along with real data so we loop until the firstByte is greater than 0
                    if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled && packetBuilder.FirstByte() == 0)
                    {
                        #region Ignore Null Packet
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingPacketHandleHandOff() from " + ConnectionInfo + ", loop index - " + loopCounter.ToString());

                        packetBuilder.ClearNTopBytes(1);

                        //Reset the expected bytes to 0 so that the next check starts from scratch
                        packetBuilder.TotalBytesExpected = 0;

                        //If we have run out of data completely then we can return immediately
                        if (packetBuilder.TotalBytesCached == 0) return;
                        #endregion
                    }
                    else
                    {
                        int packetHeaderSize = 0;
                        PacketHeader topPacketHeader;

                        #region Set topPacketHeader
                        if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
                        {
                            //First determine the expected size of a header packet
                            packetHeaderSize = packetBuilder.FirstByte() + 1;

                            //Do we have enough data to build a header?
                            if (packetBuilder.TotalBytesCached < packetHeaderSize)
                            {
                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("     ... require " + packetHeaderSize + " bytes for packet header, only " + packetBuilder.TotalBytesCached + " bytes cached.");

                                //Set the expected number of bytes and then return
                                packetBuilder.TotalBytesExpected = packetHeaderSize;
                                return;
                            }

                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("     ... deserializing header using " + packetHeaderSize + " bytes, " + packetBuilder.TotalBytesCached + " bytes cached.");

                            //We have enough for a header
                            using (MemoryStream headerStream = packetBuilder.ReadDataSection(1, packetHeaderSize - 1))
                                topPacketHeader = new PacketHeader(headerStream, NetworkComms.InternalFixedSendReceiveOptions);
                        }
                        else
                            topPacketHeader = new PacketHeader(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetBuilder.TotalBytesCached);
                        #endregion

                        //Idiot test
                        if (topPacketHeader.PacketType == null)
                            throw new SerialisationException("packetType value in packetHeader should never be null");

                        //We can now use the header to establish if we have enough payload data
                        //First case is when we have not yet received enough data
                        if (packetBuilder.TotalBytesCached < packetHeaderSize + topPacketHeader.TotalPayloadSize)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("     ... more data required for complete packet payload. Expecting " + (packetHeaderSize + topPacketHeader.TotalPayloadSize).ToString() + " total packet bytes.");

                            //Set the expected number of bytes and then return
                            packetBuilder.TotalBytesExpected = packetHeaderSize + topPacketHeader.TotalPayloadSize;
                            return;
                        }
                        //Second case is we have enough data
                        else if (packetBuilder.TotalBytesCached >= packetHeaderSize + topPacketHeader.TotalPayloadSize)
                        {
                            #region Handle Packet
                            //We can either have exactly the right amount or even more than we were expecting
                            //We may have too much data if we are sending high quantities and the packets have been concatenated
                            SendReceiveOptions incomingPacketSendReceiveOptions = IncomingPacketSendReceiveOptions(topPacketHeader);
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Received packet of type '" + topPacketHeader.PacketType + "' from " + ConnectionInfo + ", containing " + packetHeaderSize.ToString() + " header bytes and " + topPacketHeader.TotalPayloadSize.ToString() + " payload bytes.");

                            bool isReservedPacketType = (topPacketHeader.PacketType != Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged) &&
                                NetworkComms.ReservedPacketTypeNames.ContainsKey(topPacketHeader.PacketType));

                            //Get the packet sequence number if logging
                            string packetSeqNumStr = "";
                            if (NetworkComms.LoggingEnabled)
                                packetSeqNumStr = (topPacketHeader.ContainsOption(PacketHeaderLongItems.PacketSequenceNumber) ? ". pSeq#-" + topPacketHeader.GetOption(PacketHeaderLongItems.PacketSequenceNumber).ToString() + "." : "");

                            //Only reserved packet types get completed inline by default
                            if (isReservedPacketType)
                            {
                                QueueItemPriority priority = (QueueItemPriority)Thread.CurrentThread.Priority;

                                PriorityQueueItem item = new PriorityQueueItem(priority, this, topPacketHeader, packetBuilder.ReadDataSection(packetHeaderSize, topPacketHeader.TotalPayloadSize), incomingPacketSendReceiveOptions);
                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... handling packet type '" + topPacketHeader.PacketType + "' inline. Loop index - " + loopCounter.ToString() + packetSeqNumStr);
                                NetworkComms.CompleteIncomingItemTask(item);
                            }
                            else
                            {
                                QueueItemPriority itemPriority = (incomingPacketSendReceiveOptions.Options.ContainsKey("ReceiveHandlePriority") ? (QueueItemPriority)Enum.Parse(typeof(QueueItemPriority), incomingPacketSendReceiveOptions.Options["ReceiveHandlePriority"]) : QueueItemPriority.Normal);
                                PriorityQueueItem item = new PriorityQueueItem(itemPriority, this, topPacketHeader, packetBuilder.ReadDataSection(packetHeaderSize, topPacketHeader.TotalPayloadSize), incomingPacketSendReceiveOptions);

                                //QueueItemPriority.Highest is the only priority that is executed inline
                                if (itemPriority == QueueItemPriority.Highest)
                                {
                                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... handling packet type '" + topPacketHeader.PacketType + "' with priority HIGHEST inline. Loop index - " + loopCounter.ToString() + packetSeqNumStr);
                                    NetworkComms.CompleteIncomingItemTask(item);
                                }
                                else
                                {
                                    int threadId = NetworkComms.CommsThreadPool.EnqueueItem(item.Priority, NetworkComms.CompleteIncomingItemTask, item);
                                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... added completed " + item.PacketHeader.PacketType + " packet to thread pool (Q:" + NetworkComms.CommsThreadPool.QueueCount.ToString() + ", T:" + NetworkComms.CommsThreadPool.CurrentNumTotalThreads.ToString() + ", I:" + NetworkComms.CommsThreadPool.CurrentNumIdleThreads.ToString() + ") with priority " + itemPriority.ToString() + (threadId > 0 ? ". Selected threadId=" + threadId.ToString() : "") + ". Loop index=" + loopCounter.ToString() + packetSeqNumStr);
                                }
                            }
                            
                            //We clear the bytes we have just handed off
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Removing " + (packetHeaderSize + topPacketHeader.TotalPayloadSize).ToString() + " bytes from incoming packet builder from connection with " + ConnectionInfo +".");
                            packetBuilder.ClearNTopBytes(packetHeaderSize + topPacketHeader.TotalPayloadSize);

                            //Reset the expected bytes to 0 so that the next check starts from scratch
                            packetBuilder.TotalBytesExpected = 0;

                            //If we have run out of data completely then we can return immediately
                            if (packetBuilder.TotalBytesCached == 0) return;
                            #endregion
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
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("A fatal exception occurred in IncomingPacketHandleHandOff(), connection with " + ConnectionInfo + " be closed. See log file for more information.");

                if (this is IPConnection)
                {
                    //Log the exception in DOS protection if enabled
                    if (IPConnection.DOSProtection.Enabled && ConnectionInfo.RemoteEndPoint.GetType() == typeof(IPEndPoint))
                        IPConnection.DOSProtection.LogMalformedData(ConnectionInfo.RemoteIPEndPoint.Address);
                }

                LogTools.LogException(ex, "CommsError", "A fatal exception occurred in IncomingPacketHandleHandOff(), connection with " + ConnectionInfo + " be closed. Loop counter " + loopCounter.ToString() + ". Packet builder contained " + packetBuilder.TotalBytesCached + " total cached bytes.");
                CloseConnection(true, 45);
            }
        }

        /// <summary>
        /// Handle an incoming CheckSumFailResend packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        internal void CheckSumFailResendHandler(MemoryStream packetDataSection)
        {
            //If we have been asked to resend a packet then we just go through the list and resend it.
            SentPacket packetToReSend;
            lock (sentPacketsLocker)
            {
                string checkSumRequested = NetworkComms.InternalFixedSendReceiveOptions.DataSerializer.DeserialiseDataObject<string>(packetDataSection, 
                    NetworkComms.InternalFixedSendReceiveOptions.DataProcessors, NetworkComms.InternalFixedSendReceiveOptions.Options);

                if (sentPackets.ContainsKey(checkSumRequested))
                    packetToReSend = sentPackets[checkSumRequested];
                else
                    throw new CheckSumException("There was no packet sent with a matching check sum");
            }

            //If we have already tried resending the packet 10 times something has gone horribly wrong
            if (packetToReSend.SendCount > 10) throw new CheckSumException("Packet sent resulted in a catastrophic checksum check exception.");

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn(" ... resending packet due to MD5 mismatch.");

            //Increment send count and then resend
            packetToReSend.IncrementSendCount();
            SendPacket<object>(packetToReSend.Packet);
        }
    }
}
