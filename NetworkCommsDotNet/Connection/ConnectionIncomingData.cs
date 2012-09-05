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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DPSBase;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// NetworkComms maintains a top level Connection object for shared methods
    /// </summary>
    public abstract partial class Connection
    {
        /// <summary>
        /// The packet builder for this connection
        /// </summary>
        protected ConnectionPacketBuilder packetBuilder;

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
        /// <param name="packetBuilder"></param>
        protected void IncomingPacketHandleHandOff(ConnectionPacketBuilder packetBuilder)
        {
            //ThreadPriority is NetworkComms.timeCriticalThreadPriority

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
                        PacketHeader topPacketHeader = new PacketHeader(packetBuilder.ReadDataSection(1, packetHeaderSize - 1), NetworkComms.InternalFixedSendReceiveOptions);

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

                            SendReceiveOptions incomingPacketSendReceiveOptions = IncomingPacketSendReceiveOptions(topPacketHeader);

                            //Build the necessary task input data
                            object[] completedData = new object[3];
                            completedData[0] = topPacketHeader;
                            completedData[1] = packetBuilder.ReadDataSection(packetHeaderSize, topPacketHeader.PayloadPacketSize);
                            completedData[2] = incomingPacketSendReceiveOptions;

                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Received packet of type '" + topPacketHeader.PacketType + "' from " + ConnectionInfo + ", containing " + packetHeaderSize + " header bytes and " + topPacketHeader.PayloadPacketSize + " payload bytes.");

                            //If this is a reserved packetType we call the method inline so that it gets dealt with immediately
                            if (NetworkComms.reservedPacketTypeNames.Contains(topPacketHeader.PacketType))
                            {
                                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... handling packet type '" + topPacketHeader.PacketType + "' inline. Loop index - " + loopCounter);
                                CompleteIncomingPacketWorker(completedData);
                            }
                            else if (incomingPacketSendReceiveOptions.Options.ContainsKey("ReceiveHandlePriority") && 
                                incomingPacketSendReceiveOptions["ReceiveHandlePriority"] != Enum.GetName(typeof(ThreadPriority), ThreadPriority.Normal))
                            {
                                Thread newHandleThread = new Thread(CompleteIncomingPacketWorker);

                                newHandleThread.Priority = incomingPacketSendReceiveOptions.Options.ContainsKey("ReceiveHandlePriority") ?
                                    (ThreadPriority)Enum.Parse(typeof(ThreadPriority), incomingPacketSendReceiveOptions.Options["ReceiveHandlePriority"]) :
                                    ThreadPriority.Normal;
                                
                                newHandleThread.Name = "CompleteIncomingPacketWorker-" + topPacketHeader.PacketType;
                                newHandleThread.Start(completedData);
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
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("A fatal exception occured in IncomingPacketHandleHandOff(), connection with " + ConnectionInfo + " be closed. See log file for more information.");

                NetworkComms.LogError(ex, "CommsError");
                CloseConnection(true, 16);
            }
        }

        /// <summary>
        /// Once we have received all incoming data we can handle it further.
        /// </summary>
        /// <param name="packetBytes"></param>
        protected void CompleteIncomingPacketWorker(object packetBytes)
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... packet hand off task started.");

                //Check for a shutdown connection
                if (ConnectionInfo.ConnectionShutdown) return;

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

                SendReceiveOptions packetSendReceiveOptions = completedData[2] as SendReceiveOptions;
                if (packetSendReceiveOptions == null) throw new NullReferenceException("Type cast to SendReceiveOptions failed in CompleteIncomingPacketWorker.");

                //We only look at the check sum if we want to and if it has been set by the remote end
                if (NetworkComms.EnablePacketCheckSumValidation && packetHeader.ContainsOption(PacketHeaderStringItems.CheckSumHash))
                {
                    var packetHeaderHash = packetHeader.GetOption(PacketHeaderStringItems.CheckSumHash);

                    //Validate the checkSumhash of the data
                    if (packetHeaderHash != NetworkComms.MD5Bytes(packetDataSection))
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn(" ... corrupted packet header detected.");

                        //We have corruption on a resend request, something is very wrong so we throw an exception.
                        if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend)) throw new CheckSumException("Corrupted md5CheckFailResend packet received.");

                        //Instead of throwing an exception we can request the packet to be resent
                        Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend), packetHeaderHash, NetworkComms.InternalFixedSendReceiveOptions);
                        SendPacket(returnPacket);

                        //We need to wait for the packet to be resent before going further
                        return;
                    }
                }

                //Remote end may have requested packet receive confirmation so we send that now
                if (packetHeader.ContainsOption(PacketHeaderStringItems.ReceiveConfirmationRequired))
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... sending requested receive confirmation packet.");

                    var hash = packetHeader.ContainsOption(PacketHeaderStringItems.CheckSumHash) ? packetHeader.GetOption(PacketHeaderStringItems.CheckSumHash) : "";


                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), hash, NetworkComms.InternalFixedSendReceiveOptions);
                    SendPacket(returnPacket);
                }

                //We can now pass the data onto the correct delegate
                //First we have to check for our reserved packet types
                //The following large sections have been factored out to make reading and debugging a little easier
                if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend))
                    CheckSumFailResendHandler(packetDataSection);
                else if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup))
                    ConnectionSetupHandler(packetDataSection);
                else if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket) && 
                    (NetworkComms.InternalFixedSendReceiveOptions.Serializer.DeserialiseDataObject<bool>(packetDataSection, 
                        NetworkComms.InternalFixedSendReceiveOptions.DataProcessors, 
                        NetworkComms.InternalFixedSendReceiveOptions.Options)) == false)
                {
                    //If we have received a ping packet from the originating source we reply with true
                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), true, NetworkComms.InternalFixedSendReceiveOptions);
                    SendPacket(returnPacket);
                }

                //We allow users to add their own custom handlers for reserved packet types here
                //else
                if (true)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Triggering handlers for packet of type '" + packetHeader.PacketType + "' from " + ConnectionInfo);

                    //We trigger connection specific handlers first
                    bool connectionSpecificHandlersTriggered = TriggerSpecificPacketHandlers(packetHeader, packetDataSection, packetSendReceiveOptions);

                    //We trigger global handlers second
                    NetworkComms.TriggerGlobalPacketHandlers(packetHeader, this, packetDataSection, packetSendReceiveOptions, connectionSpecificHandlersTriggered);

                    //This is a really bad place to put a garbage collection, comment left in so that it doesn't get added again at some later date
                    //We don't want the CPU to JUST be trying to garbage collect the WHOLE TIME
                    //GC.Collect();
                }
            }
            catch (CommunicationException)
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("A communcation exception occured in CompleteIncomingPacketWorker(), connection with " + ConnectionInfo + " be closed.");
                CloseConnection(true, 2);
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An unhandled exception occured in CompleteIncomingPacketWorker(), connection with " + ConnectionInfo + " be closed. See log file for more information.");

                NetworkComms.LogError(ex, "CommsError");
                CloseConnection(true, 3);
            }
        }

        /// <summary>
        /// Handle an incoming CheckSumFailResend packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        protected void CheckSumFailResendHandler(byte[] packetDataSection)
        {
            //If we have been asked to resend a packet then we just go through the list and resend it.
            OldSentPacket packetToReSend;
            lock (sentPacketsLocker)
            {
                string checkSumRequested = NetworkComms.InternalFixedSendReceiveOptions.Serializer.DeserialiseDataObject<string>(packetDataSection, 
                    NetworkComms.InternalFixedSendReceiveOptions.DataProcessors, NetworkComms.InternalFixedSendReceiveOptions.Options);

                if (sentPackets.ContainsKey(checkSumRequested))
                    packetToReSend = sentPackets[checkSumRequested];
                else
                    throw new CheckSumException("There was no packet sent with a matching check sum");
            }

            //If we have already tried resending the packet 10 times something has gone horribly wrong
            if (packetToReSend.SendCount > 10) throw new CheckSumException("Packet sent resulted in a catastropic checksum check exception.");

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn(" ... resending packet due to MD5 mismatch.");

            //Increment send count and then resend
            packetToReSend.IncrementSendCount();
            SendPacket(packetToReSend.Packet);
        }
    }
}
