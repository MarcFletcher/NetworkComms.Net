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
using System.Text;
using System.Net;
using System.Threading;
using DPSBase;
using System.Net.Sockets;
using System.IO;

#if WINDOWS_PHONE
using QueueItemPriority = Windows.System.Threading.WorkItemPriority;
#else
using QueueItemPriority = System.Threading.ThreadPriority;
#endif


namespace NetworkCommsDotNet
{
    public abstract partial class Connection
    {
        /// <summary>
        /// The <see cref="PacketBuilder"/> for this connection
        /// </summary>
        protected PacketBuilder packetBuilder;

        /// <summary>
        /// The current incoming data buffer
        /// </summary>
        protected byte[] dataBuffer;

        /// <summary>
        /// The total bytes read so far within dataBuffer
        /// </summary>
        protected int totalBytesRead;

        /// <summary>
        /// The thread listening for incoming data should we be using synchronous methods.
        /// </summary>
        protected Thread incomingDataListenThread = null;

        /// <summary>
        /// A connection specific method which triggers any requisites for accepting incoming data
        /// </summary>
        protected abstract void StartIncomingDataListen();

        /// <summary>
        /// Attempts to use the data provided in packetBuilder to recreate something usefull. If we don't have enough data yet that value is set in packetBuilder.
        /// </summary>
        /// <param name="packetBuilder">The <see cref="PacketBuilder"/> containing incoming cached data</param>
        protected void IncomingPacketHandleHandOff(PacketBuilder packetBuilder)
        {
            try
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... checking for completed packet with " + packetBuilder.TotalBytesCached + " bytes read.");

                if (packetBuilder.TotalPartialPacketCount == 0)
                    throw new Exception("Executing IncomingPacketHandleHandOff when no packets exist in packetbuilder.");

                //Loop until we are finished with this packetBuilder
                int loopCounter = 0;
                while (true)
                {
                    //If we have ended up with a null packet at the front, probably due to some form of concatentation we can pull it off here
                    //It is possible we have concatenation of several null packets along with real data so we loop until the firstByte is greater than 0
                    if (packetBuilder.FirstByte() == 0)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingPacketHandleHandOff() from "+ConnectionInfo+", loop index - " + loopCounter);

                        packetBuilder.ClearNTopBytes(1);

                        //Reset the expected bytes to 0 so that the next check starts from scratch
                        packetBuilder.TotalBytesExpected = 0;

                        //If we have run out of data completely then we can return immediately
                        if (packetBuilder.TotalBytesCached == 0) return;
                    }
                    else
                    {
                        //First determine the expected size of a header packet
                        int packetHeaderSize = packetBuilder.FirstByte() + 1;

                        //Do we have enough data to build a header?
                        if (packetBuilder.TotalBytesCached < packetHeaderSize)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... ... more data required for complete packet header.");

                            //Set the expected number of bytes and then return
                            packetBuilder.TotalBytesExpected = packetHeaderSize;
                            return;
                        }

                        //We have enough for a header
                        PacketHeader topPacketHeader = new PacketHeader(packetBuilder.ReadDataSection(1, packetHeaderSize - 1), NetworkComms.InternalFixedSendReceiveOptions);

                        //Idiot test
                        if (topPacketHeader.PacketType == null)
                            throw new SerialisationException("packetType value in packetHeader should never be null");

                        //We can now use the header to establish if we have enough payload data
                        //First case is when we have not yet received enough data
                        if (packetBuilder.TotalBytesCached < packetHeaderSize + topPacketHeader.PayloadPacketSize)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... ... more data required for complete packet payload.");

                            //Set the expected number of bytes and then return
                            packetBuilder.TotalBytesExpected = packetHeaderSize + topPacketHeader.PayloadPacketSize;
                            return;
                        }
                        //Second case is we have enough data
                        else if (packetBuilder.TotalBytesCached >= packetHeaderSize + topPacketHeader.PayloadPacketSize)
                        {
                            //We can either have exactly the right amount or even more than we were expecting
                            //We may have too much data if we are sending high quantities and the packets have been concatenated
                            //no problem!!
                            SendReceiveOptions incomingPacketSendReceiveOptions = IncomingPacketSendReceiveOptions(topPacketHeader);
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Received packet of type '" + topPacketHeader.PacketType + "' from " + ConnectionInfo + ", containing " + packetHeaderSize + " header bytes and " + topPacketHeader.PayloadPacketSize + " payload bytes.");

                            //If this is a reserved packetType we call the method inline so that it gets dealt with immediately
                            bool isReservedType = false;
                            foreach (var tName in NetworkComms.reservedPacketTypeNames)
                            {
                                //isReservedType |= topPacketHeader.PacketType == tName;
                                if (topPacketHeader.PacketType == tName)
                                {
                                    isReservedType = true;
                                    break;
                                }
                            }

                            if (isReservedType)
                            {
#if WINDOWS_PHONE
                                var priority = QueueItemPriority.Normal;
#else
                                var priority = Thread.CurrentThread.Priority;
#endif

                                PriorityQueueItem item = new PriorityQueueItem(priority, this, topPacketHeader, packetBuilder.ReadDataSection(packetHeaderSize, topPacketHeader.PayloadPacketSize), incomingPacketSendReceiveOptions);
                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... handling packet type '" + topPacketHeader.PacketType + "' inline. Loop index - " + loopCounter);
                                NetworkComms.CompleteIncomingItemTask(item);
                            }
                            else
                            {
                                QueueItemPriority itemPriority = (incomingPacketSendReceiveOptions.Options.ContainsKey("ReceiveHandlePriority") ? (QueueItemPriority)Enum.Parse(typeof(QueueItemPriority), incomingPacketSendReceiveOptions.Options["ReceiveHandlePriority"]) : QueueItemPriority.Normal);
                                PriorityQueueItem item = new PriorityQueueItem(itemPriority, this, topPacketHeader, packetBuilder.ReadDataSection(packetHeaderSize, topPacketHeader.PayloadPacketSize), incomingPacketSendReceiveOptions);

                                //If not a reserved packetType we run the completion in a seperate task so that this thread can continue to receive incoming data
                                //The tasks that we start here may not run the item we are addeding to the queue. i.e. it may end up running some higher priority item first
                                if (!NetworkComms.IncomingPacketQueue.TryAdd(new KeyValuePair<int, PriorityQueueItem>((int)item.Priority, item)))
                                    throw new PacketHandlerException("Failed to add packet to incoming packet queue.");

                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... added completed packet item to IncomingPacketQueue with priority " + itemPriority + ". Loop index - " + loopCounter);

                                //If we have just added a high priority item we trigger the high priority thread
                                if (itemPriority > QueueItemPriority.Normal) NetworkComms.IncomingPacketQueueHighPrioThreadWait.Set();

                                //We will also trigger a new task below incase we have many higher priority items as we don't care which thread ends up running the item
                                ThreadPool.QueueUserWorkItem(NetworkComms.CompleteIncomingItemTask, null);
                            }

                            //We clear the bytes we have just handed off
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Removing " + (packetHeaderSize + topPacketHeader.PayloadPacketSize).ToString() + " bytes from incoming packet buffer.");
                            packetBuilder.ClearNTopBytes(packetHeaderSize + topPacketHeader.PayloadPacketSize);

                            //Reset the expected bytes to 0 so that the next check starts from scratch
                            packetBuilder.TotalBytesExpected = 0;

                            //If we have run out of data completely then we can return immediately
                            if (packetBuilder.TotalBytesCached == 0) return;
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
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("A fatal exception occured in IncomingPacketHandleHandOff(), connection with " + ConnectionInfo + " be closed. See log file for more information.");

                NetworkComms.LogError(ex, "CommsError");
                CloseConnection(true, 16);
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
            if (packetToReSend.SendCount > 10) throw new CheckSumException("Packet sent resulted in a catastropic checksum check exception.");

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn(" ... resending packet due to MD5 mismatch.");

            //Increment send count and then resend
            packetToReSend.IncrementSendCount();
            SendPacket(packetToReSend.Packet);
        }
    }
}
