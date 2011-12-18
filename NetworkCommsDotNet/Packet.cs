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
using SerializerBase;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Wrapper for the entire comms packet. Packet consists of header and data in serialised byte[] form.
    /// </summary>
    class Packet
    {
        PacketHeader packetHeader;
        byte[] packetData;

        public Packet(string packetTypeStr, bool recieveConfirmationRequired, object packetObject, ISerialize serializer, ICompress compressor)
        {
            if (packetTypeStr == null)
                throw new ArgumentNullException("packetTypeStr should never be null.");

            //We can gain performance if we are just sending a byte array directly
            bool pureBytesInPayload = false;
            if (packetObject is byte[])
            {
                this.packetData = packetObject as byte[];
                pureBytesInPayload = true;
            }
            else
            {
                IThreadSafeSerialise packetObjectInt = packetObject as IThreadSafeSerialise;
                if (packetObjectInt != null)
                    this.packetData = packetObjectInt.ThreadSafeSerialise();
                else
                    this.packetData = serializer.SerialiseDataObject(packetObject, compressor);
            }

            //We only calculate the md5 if we are going to use it
            long dataHashValue = 0;
            if (NetworkComms.EnablePacketCheckSumValidation)
                dataHashValue = Adler32.GenerateCheckSum(packetData);

            this.packetHeader = new PacketHeader(packetTypeStr, recieveConfirmationRequired, dataHashValue, packetData.Length, pureBytesInPayload);
#if logging
                logger.Debug("... creating comms packet. Total packet size is " + FBPSerialiser.SerialiseDataObject(packetHeader).Length + packetData.Length + " bytes. PacketObject data size is " + packetData.Length + " bytes");
#endif
        }

        /// <summary>
        /// Return the packet header for this packet
        /// </summary>
        public PacketHeader PacketHeader
        {
            get { return packetHeader; }
        }

        /// <summary>
        /// Return the byte[] packet data
        /// </summary>
        public byte[] PacketData
        {
            get { return packetData; }
        }

        /// <summary>
        /// Returns the serialisedbytes of the packet header appended by the serialised header size. This is required to rebuild the header on receive.
        /// </summary>
        /// <returns></returns>
        public byte[] SerialiseHeader(ISerialize serializer, ICompress compressor)
        {
            //We need to start of by serialising the header
            byte[] serialisedHeader = serializer.SerialiseDataObject(packetHeader, compressor);

            //Define our return array which includes byte[0] as the header size
            byte[] returnArray = new byte[1 + serialisedHeader.Length];

            if (serialisedHeader.Length > byte.MaxValue)
                throw new SerialisationException("Unable to send packet as header size is larger than Byte.MaxValue");

            //The first byte now specifies the header size (allows for variable header size)
            returnArray[0] = (byte)serialisedHeader.Length;

            //Copy the bytes for the header in
            Buffer.BlockCopy(serialisedHeader, 0, returnArray, 1, serialisedHeader.Length);

            if (returnArray == null)
                throw new SerialisationException("Serialised header bytes should never be null.");

            return returnArray;
        }
    }
}
