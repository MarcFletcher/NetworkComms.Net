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
using System.IO;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Packet data is generally broken into multiple variable sized byte chunks or 'partial packets'. This class provides features to effortlessly rebuild whole packets.
    /// </summary>
    public class PacketBuilder
    {
        List<byte[]> packets = new List<byte[]>();
        List<int> packetActualBytes = new List<int>();

        object locker = new object();

        int totalBytesCached = 0;
        int totalBytesExpected = 0;

        /// <summary>
        /// Create a new instance of the ConnectionPacketBuilder class
        /// </summary>
        public PacketBuilder()
        {

        }

        /// <summary>
        /// The total number of cached bytes. This is the sum of all bytes across all cached partial packets. See <see cref="TotalPartialPacketCount"/>.
        /// </summary>
        public int TotalBytesCount
        {
            get { return totalBytesCached; }
        }

        /// <summary>
        /// The total number of cached partial packets. This is different from <see cref="TotalBytesCount"/> because each partial packet may contain a variable number of bytes.
        /// </summary>
        public int TotalPartialPacketCount
        {
            get { lock (locker) return packets.Count; }
        }

        /// <summary>
        /// The total number of bytes required to rebuild the next whole packet.
        /// </summary>
        public int TotalBytesExpected
        {
            get { lock (locker) return totalBytesExpected; }
            set { lock (locker) totalBytesExpected = value; }
        }

        /// <summary>
        /// Clear N bytes from cache, starting with oldest bytes first.
        /// </summary>
        /// <param name="numBytesToRemove">The total number of bytes to be removed.</param>
        public void ClearNTopBytes(int numBytesToRemove)
        {
            lock (locker)
            {
                if (numBytesToRemove > 0)
                {
                    if (numBytesToRemove > totalBytesCached)
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
                    totalBytesCached -= bytesRemoved;

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
        }

        /// <summary>
        /// Add a partial packet to the end of the cache by reference.
        /// </summary>
        /// <param name="packetBytes">The number of valid bytes in the provided partial packet</param>
        /// <param name="partialPacket">A buffer which may or may not be full with valid bytes</param>
        public void AddPartialPacket(int packetBytes, byte[] partialPacket)
        {
            lock (locker)
            {
                totalBytesCached += packetBytes;

                packets.Add(partialPacket);
                packetActualBytes.Add(packetBytes);
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
            lock (locker)
            {
                if (packets.Count > 0)
                {
                    int lastPacketIndex = packets.Count - 1;

                    lastPacketBytesRead = packetActualBytes[lastPacketIndex];
                    byte[] returnArray = packets[lastPacketIndex];

                    totalBytesCached -= packetActualBytes[lastPacketIndex];

                    packets.RemoveAt(lastPacketIndex);
                    packetActualBytes.RemoveAt(lastPacketIndex);

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
            lock (locker)
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
            lock (locker)
                return packets[0][0];
        }

        /// <summary>
        /// Copies all cached bytes into a single array and returns. Original data is left unchanged.
        /// </summary>
        /// <returns>All cached data as a single byte[]</returns>
        public byte[] GetAllData()
        {
            lock (locker)
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
            lock (locker)
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

                return new MemoryStream(returnArray, 0, returnArray.Length, false, true);
            }
        }
    }
}