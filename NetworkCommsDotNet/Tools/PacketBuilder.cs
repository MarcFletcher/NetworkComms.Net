//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using System.IO;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Packet data is generally broken into multiple variable sized byte chunks or 'partial packets'. 
    /// This class provides features to effortlessly rebuild whole packets.
    /// </summary>
    public class PacketBuilder
    {
        List<byte[]> packets = new List<byte[]>();
        List<int> packetActualBytes = new List<int>();

        /// <summary>
        /// Locker object used for performing thread safe operations over this packet builder
        /// </summary>
        public object Locker { get; private set; }

        int totalBytesCached = 0;
        int totalBytesExpected = 0;

        /// <summary>
        /// Create a new instance of the ConnectionPacketBuilder class
        /// </summary>
        public PacketBuilder()
        {
            Locker = new object();
        }

        /// <summary>
        /// The total number of cached bytes. This is the sum of all bytes across all cached partial packets. See <see cref="TotalPartialPacketCount"/>.
        /// </summary>
        public int TotalBytesCached
        {
            get { return totalBytesCached; }
        }

        /// <summary>
        /// The total number of cached partial packets. This is different from <see cref="TotalBytesCached"/> because each partial packet may contain a variable number of bytes.
        /// </summary>
        public int TotalPartialPacketCount
        {
            get { lock (Locker) return packets.Count; }
        }

        /// <summary>
        /// The total number of bytes required to rebuild the next whole packet.
        /// </summary>
        public int TotalBytesExpected
        {
            get { lock (Locker) return totalBytesExpected; }
            set { lock (Locker) totalBytesExpected = value; }
        }

        /// <summary>
        /// Clear N bytes from cache, starting with oldest bytes first.
        /// </summary>
        /// <param name="numBytesToRemove">The total number of bytes to be removed.</param>
        public void ClearNTopBytes(int numBytesToRemove)
        {
            lock (Locker)
            {
                if (numBytesToRemove > 0)
                {
                    if (numBytesToRemove > totalBytesCached)
                        throw new CommunicationException("Attempting to remove " + numBytesToRemove.ToString() + " bytes when ConnectionPacketBuilder only contains " + totalBytesCached.ToString());

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
                    totalBytesCached -= bytesRemoved;

                    if (totalBytesCached > 0)
                    {
                        //Get rid of any null packets
                        List<byte[]> newPackets = new List<byte[]>(packets.Count);
                        for (int i = 0; i < packets.Count; i++)
                        {
                            if (packets[i] != null)
                                newPackets.Add(packets[i]);
                        }
                        packets = newPackets;

                        //Remove any -1 entries
                        List<int> newPacketActualBytes = new List<int>(packetActualBytes.Count);
                        for (int i = 0; i < packetActualBytes.Count; i++)
                        {
                            if (packetActualBytes[i] > -1)
                                newPacketActualBytes.Add(packetActualBytes[i]);
                        }
                        packetActualBytes = newPacketActualBytes;
                    }
                    else
                    {
                        //This is faster if we have removed everything
                        packets = new List<byte[]>();
                        packetActualBytes = new List<int>();
                    }

                    //This is a really bad place to put a garbage collection as it hammers the CPU
                    //GC.Collect();
                }

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... removed " + numBytesToRemove + " bytes from packetBuilder.");
            }
        }

        /// <summary>
        /// Add a partial packet to the end of the cache by reference.
        /// </summary>
        /// <param name="packetBytes">The number of valid bytes in the provided partial packet</param>
        /// <param name="partialPacket">A buffer which may or may not be full with valid bytes</param>
        public void AddPartialPacket(int packetBytes, byte[] partialPacket)
        {
            if (packetBytes > partialPacket.Length)
                throw new ArgumentException("packetBytes cannot be greater than the length of the provided partialPacket data.");
            if (packetBytes < 0)
                throw new ArgumentException("packetBytes cannot be negative.");

            lock (Locker)
            {
                totalBytesCached += packetBytes;

                packets.Add(partialPacket);
                packetActualBytes.Add(packetBytes);

                if (NetworkComms.LoggingEnabled)
                {
                    if (TotalBytesExpected == 0 && totalBytesCached > (10 * 1024 * 1024))
                        NetworkComms.Logger.Warn("Packet builder cache contains " + (totalBytesCached / 1024.0).ToString("0.0") + "KB when 0KB are currently expected.");
                    else if (TotalBytesExpected > 0 && totalBytesCached > totalBytesExpected * 2)
                        NetworkComms.Logger.Warn("Packet builder cache contains " + (totalBytesCached / 1024.0).ToString("0.0") + "KB when only " + (TotalBytesExpected / 1024.0).ToString("0.0") + "KB were expected.");
                }

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... added " + packetBytes + " bytes to packetBuilder.");
            }
        }

        /// <summary>
        /// Returns the most recently cached partial packet and removes it from the cache.
        /// Used to more efficiently utilise allocated memory space.
        /// </summary>
        /// <param name="lastPacketBytesRead">The number of valid bytes in the last partial packet added</param>
        /// <returns>A byte[] corresponding with the last added partial packet</returns>
        public byte[] RemoveMostRecentPartialPacket(ref int lastPacketBytesRead)
        {
            lock (Locker)
            {
                if (packets.Count > 0)
                {
                    int lastPacketIndex = packets.Count - 1;

                    lastPacketBytesRead = packetActualBytes[lastPacketIndex];
                    byte[] returnArray = packets[lastPacketIndex];

                    totalBytesCached -= packetActualBytes[lastPacketIndex];

                    packets.RemoveAt(lastPacketIndex);
                    packetActualBytes.RemoveAt(lastPacketIndex);

                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... reusing byte[" + returnArray.Length + "] from packetBuilder which contains " + lastPacketBytesRead + " existing bytes.");

                    return returnArray;
                }
                else
                    throw new Exception("Unable to remove most recent packet as packet list is empty.");
            }
        }

        /// <summary>
        /// Returns the number of unused bytes in the most recently cached partial packet.
        /// </summary>
        /// <returns>The number of unused bytes in the most recently cached partial packet.</returns>
        public int NumUnusedBytesMostRecentPartialPacket()
        {
            lock (Locker)
            {
                if (packets.Count > 0)
                {
                    int lastPacketIndex = packets.Count - 1;
                    return packets[lastPacketIndex].Length - packetActualBytes[lastPacketIndex];
                }
                else
                    throw new Exception("Unable to return requested size as packet list is empty.");
            }
        }

        /// <summary>
        /// Returns the value of the first cached byte.
        /// </summary>
        /// <returns>The value of the first cached byte.</returns>
        public byte FirstByte()
        {
            lock (Locker)
                return packets[0][0];
        }

        /// <summary>
        /// Copies all cached bytes into a single array and returns. Original data is left unchanged.
        /// </summary>
        /// <returns>All cached data as a single byte[]</returns>
        public byte[] GetAllData()
        {
            lock (Locker)
            {
                byte[] returnArray = new byte[totalBytesCached];

                int currentStart = 0;
                for (int i = 0; i < packets.Count; i++)
                {
                    Buffer.BlockCopy(packets[i], 0, returnArray, currentStart, packetActualBytes[i]);
                    currentStart += packetActualBytes[i];
                }

                return returnArray;
            }
        }

        /// <summary>
        /// Copies the requested cached bytes into a single array and returns. Original data is left unchanged.
        /// </summary>
        /// <param name="startIndex">The inclusive byte index to use as the starting position.</param>
        /// <param name="length">The total number of desired bytes.</param>
        /// <returns>The requested bytes as a single array.</returns>
        public MemoryStream ReadDataSection(int startIndex, int length)
        {
            lock (Locker)
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

                if (writeTotal != length) throw new Exception("Not enough data available in packetBuilder to complete request. Requested " + length.ToString() + " bytes but only " + writeTotal.ToString() + " bytes were copied.");

#if NETFX_CORE
                return new MemoryStream(returnArray, 0, returnArray.Length, false);
#else
                return new MemoryStream(returnArray, 0, returnArray.Length, false, true);
#endif
            }
        }
    }
}