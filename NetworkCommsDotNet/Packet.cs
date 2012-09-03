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
    public class Packet
    {
        PacketHeader packetHeader;
        byte[] packetData;

        public Packet(string packetTypeStr, object packetObject, SendReceiveOptions options)
        {
            Constructor(packetTypeStr, null, packetObject, options);
        }

        public Packet(string sendingPacketTypeStr, string requestReturnPacketTypeStr, object packetObject, SendReceiveOptions options)
        {
            Constructor(sendingPacketTypeStr, requestReturnPacketTypeStr, packetObject, options);
        }

        private void Constructor(string sendingPacketTypeStr, string requestReturnPacketTypeStr, object packetObject, SendReceiveOptions options)
        {
            if (sendingPacketTypeStr == null || sendingPacketTypeStr == "")
                throw new ArgumentNullException("The provided packetTypeStr can not be zero length or null.");

            //We can gain performance if we are just sending a byte array directly
            bool pureBytesInPayload = false;

            if (packetObject == null)
                this.packetData = new byte[0];
            else
            {
                IThreadSafeSerialise packetObjectInt = packetObject as IThreadSafeSerialise;
                if (packetObjectInt != null)
                    this.packetData = packetObjectInt.ThreadSafeSerialise();
                else
                    this.packetData = options.Serializer.SerialiseDataObject(packetObject, options.DataProcessors, options.Options);
            }

            //We only calculate the checkSum if we are going to use it
            string hashStr = null;
            if (NetworkComms.EnablePacketCheckSumValidation)
                hashStr = NetworkComms.MD5Bytes(packetData);

            this.packetHeader = new PacketHeader(sendingPacketTypeStr, packetData.Length, requestReturnPacketTypeStr,  
                options.Options.ContainsKey("ReceiveConfirmationRequired") ? bool.Parse(options.Options["ReceiveConfirmationRequired"]) : false,
                hashStr);

            //Add an identifier specifying the serializers and processors we have used
            this.packetHeader.SetOption(PacketHeaderLongItems.SerializerProcessors, ProcessorManager.Instance.CreateSerializerDataProcessorIdentifier(options.Serializer, options.DataProcessors));

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... creating comms packet. PacketObject data size is " + packetData.Length + " bytes");
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
        public byte[] SerialiseHeader(SendReceiveOptions options)
        {
            //We need to start of by serialising the header
            byte[] serialisedHeader = options.Serializer.SerialiseDataObject(packetHeader, options.DataProcessors, null);

            //Define our return array which includes byte[0] as the header size
            byte[] returnArray = new byte[1 + serialisedHeader.Length];

            if (serialisedHeader.Length > byte.MaxValue)
                throw new SerialisationException("Unable to send packet as header size is larger than Byte.MaxValue. Try reducing the length of provided packetTypeStr or turning off checkSum validation.");

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
