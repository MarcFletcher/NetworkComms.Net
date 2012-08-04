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
using SerializerBase;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// When a packet is broken into multiple variable sized chunks this class allows us to rebuild the original unbroken packet
    /// </summary>
    public class ConnectionPacketBuilder
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
}