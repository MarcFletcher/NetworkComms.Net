//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Text;
using NetworkCommsDotNet.DPSBase;
using System.IO;
using System.Reflection;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Interface for defining Application Layer Protocol packets
    /// </summary>
    public interface IPacket
    {
        /// <summary>
        /// The packet header for this packet
        /// </summary>
        PacketHeader PacketHeader { get; }

        /// <summary>
        /// The payload data stream
        /// </summary>
        StreamTools.StreamSendWrapper PacketData { get; }

        /// <summary>
        /// Returns the serialised bytes of the packet header appended by the serialised header size. This is required to 
        /// rebuild the header on receive.
        /// </summary>
        /// <returns>The serialised header as byte[]</returns>
        byte[] SerialiseHeader(SendReceiveOptions options);

        /// <summary>
        /// Dispose of internal packet resources
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Wrapper for <see cref="PacketHeader"/> and packetData.
    /// </summary>
    public class Packet : IDisposable, IPacket, IExplicitlySerialize
    {
        /// <summary>
        /// If we serialise a whole packet we include the packet header
        /// </summary>
        PacketHeader _packetHeader;

        /// <summary>
        /// And the payload object as byte[]. We cannot use type T here because we do not know the type of T
        /// on deserialisation until we have the nested packet header.
        /// </summary>
        internal byte[] _payloadObjectBytes;

        StreamTools.StreamSendWrapper payloadStream;

        /// <summary>
        /// Parameterless constructor for deserialisation
        /// </summary>
        private Packet()
        {
        }

        /// <summary>
        /// Create a new packet
        /// </summary>
        /// <param name="sendingPacketTypeStr">The sending packet type</param>
        /// <param name="payloadObject">The object to be sent</param>
        /// <param name="options">The <see cref="SendReceiveOptions"/> to be used to create this packet</param>
        public Packet(string sendingPacketTypeStr, object payloadObject, SendReceiveOptions options)
        {
            Constructor(sendingPacketTypeStr, null, payloadObject, options, false);
        }

        /// <summary>
        /// Create a new packet
        /// </summary>
        /// <param name="sendingPacketTypeStr">The sending packet type</param>
        /// <param name="requestReturnPacketTypeStr">The expected return packet type</param>
        /// <param name="payloadObject">The object to be sent</param>
        /// <param name="options">The <see cref="SendReceiveOptions"/> to be used to create this packet</param>
        public Packet(string sendingPacketTypeStr, string requestReturnPacketTypeStr, object payloadObject, SendReceiveOptions options)
        {
            Constructor(sendingPacketTypeStr, requestReturnPacketTypeStr, payloadObject, options, false);
        }

        /// <summary>
        /// Private constructor used for nesting packets
        /// </summary>
        /// <param name="sendingPacketTypeStr"></param>
        /// <param name="requestReturnPacketTypeStr"></param>
        /// <param name="payloadObject"></param>
        /// <param name="options"></param>
        /// <param name="isNested"></param>
        private Packet(string sendingPacketTypeStr, string requestReturnPacketTypeStr, object payloadObject, SendReceiveOptions options, bool isNested)
        {
            Constructor(sendingPacketTypeStr, requestReturnPacketTypeStr, payloadObject, options, isNested);
        }

        private void Constructor<payloadObjectType>(string sendingPacketTypeStr, string requestReturnPacketTypeStr, payloadObjectType payloadObject, SendReceiveOptions options, bool isNested)
        {
            if (sendingPacketTypeStr == null || sendingPacketTypeStr == "") throw new ArgumentNullException("sendingPacketTypeStr", "The provided string can not be null or zero length.");
            if (options == null) throw new ArgumentNullException("options", "The provided SendReceiveOptions cannot be null.");
            if (options.DataSerializer == null) throw new ArgumentNullException("options", "The provided SendReceiveOptions.DataSerializer cannot be null. Consider using NullSerializer instead.");

            object objectToSerialise;
            if (options.Options.ContainsKey("UseNestedPacketType") && !isNested)
            {
                //We need to create a nested packet
                objectToSerialise = new Packet(sendingPacketTypeStr, requestReturnPacketTypeStr, payloadObject, options, true);

                //Serialise the nested packet
                this.payloadStream = options.DataSerializer.SerialiseDataObject(objectToSerialise, options.DataProcessors, options.Options);

                //We only calculate the checkSum if we are going to use it
                string hashStr = null;
                if (NetworkComms.EnablePacketCheckSumValidation)
                    hashStr = StreamTools.MD5(payloadStream.ThreadSafeStream.ToArray(payloadStream.Start, payloadStream.Length));

                //Set the packet header
                this._packetHeader = new PacketHeader(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.NestedPacket), payloadStream.Length, options, null, hashStr);

                //Add an identifier specifying the serialisers and processors we have used
                this._packetHeader.SetOption(PacketHeaderLongItems.SerializerProcessors, DPSManager.CreateSerializerDataProcessorIdentifier(options.DataSerializer, options.DataProcessors));
            }
            else if (isNested)
            {
                //Serialise the payload object into byte[]
                //We do not use any data processors at this stage as the object will be processed again at the nested packet level.
#if NETFX_CORE
                 _payloadObjectBytes = options.DataSerializer.SerialiseDataObject(payloadObject).ThreadSafeStream.ToArray();
#else
                _payloadObjectBytes = options.DataSerializer.SerialiseDataObject(payloadObject).ThreadSafeStream.GetBuffer();
#endif
                //Set the packet header
                this._packetHeader = new PacketHeader(sendingPacketTypeStr, _payloadObjectBytes.Length, null, requestReturnPacketTypeStr, null);

                //Set the deserialiser option in the nested packet header
                this._packetHeader.SetOption(PacketHeaderLongItems.SerializerProcessors, DPSManager.CreateSerializerDataProcessorIdentifier(options.DataSerializer, null));
            }
            else
            {
                objectToSerialise = payloadObject;
                if (objectToSerialise == null)
                {
#if NETFX_CORE
                var emptyStream = new MemoryStream(new byte[0], 0, 0, false);
#else
                    var emptyStream = new MemoryStream(new byte[0], 0, 0, false, true);
#endif
                    objectToSerialise = new StreamTools.StreamSendWrapper(new StreamTools.ThreadSafeStream(emptyStream, true));
                }

                //Set the packet data
                this.payloadStream = options.DataSerializer.SerialiseDataObject(objectToSerialise, options.DataProcessors, options.Options);

                //We only calculate the checkSum if we are going to use it
                string hashStr = null;
                if (NetworkComms.EnablePacketCheckSumValidation)
                    hashStr = StreamTools.MD5(payloadStream.ThreadSafeStream.ToArray(payloadStream.Start, payloadStream.Length));

                //Set the packet header
                this._packetHeader = new PacketHeader(sendingPacketTypeStr, payloadStream.Length, options, requestReturnPacketTypeStr, hashStr);

                //Add an identifier specifying the serialisers and processors we have used
                this._packetHeader.SetOption(PacketHeaderLongItems.SerializerProcessors, DPSManager.CreateSerializerDataProcessorIdentifier(options.DataSerializer, options.DataProcessors));
            }

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... creating comms packet. PacketObject data size is " + payloadStream.Length.ToString() + " bytes");
        }

        /// <inheritdoc />
        public PacketHeader PacketHeader
        {
            get { return _packetHeader; }
        }

        /// <inheritdoc />
        public StreamTools.StreamSendWrapper PacketData
        {
            get { return payloadStream; }
        }

        /// <inheritdoc />
        public byte[] SerialiseHeader(SendReceiveOptions options)
        {
            if (options == null) throw new ArgumentNullException("options", "Provided SendReceiveOptions cannot be null.");

            //We need to start of by serialising the header
            byte[] serialisedHeader;
            using (StreamTools.StreamSendWrapper sendWrapper = options.DataSerializer.SerialiseDataObject(_packetHeader, options.DataProcessors, null))
                serialisedHeader = sendWrapper.ThreadSafeStream.ToArray(1);

            if (serialisedHeader.Length - 1 > byte.MaxValue)
                throw new SerialisationException("Unable to send packet as header size is larger than Byte.MaxValue. Try reducing the length of provided packetTypeStr or turning off checkSum validation.");

            //The first byte now specifies the header size (allows for variable header size)
            serialisedHeader[0] = (byte)(serialisedHeader.Length - 1);

            if (serialisedHeader == null)
                throw new SerialisationException("Serialised header bytes should never be null.");

            return serialisedHeader;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            payloadStream.Dispose();
        }

        #region IExplicitlySerialize Members

        public void Serialize(Stream outputStream)
        {
            _packetHeader.Serialize(outputStream);
            outputStream.Write(BitConverter.GetBytes(_payloadObjectBytes.Length), 0, sizeof(int));
            outputStream.Write(_payloadObjectBytes, 0, _payloadObjectBytes.Length);
        }

        public void Deserialize(Stream inputStream)
        {
            var packetHeaderConstructor = typeof(PacketHeader).GetConstructor(BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            _packetHeader = (PacketHeader)packetHeaderConstructor.Invoke(new object[] { });

            _packetHeader.Deserialize(inputStream);

            byte[] payloadLengthData = new byte[sizeof(int)];
            inputStream.Read(payloadLengthData, 0, sizeof(int));

            _payloadObjectBytes = new byte[BitConverter.ToInt32(payloadLengthData, 0)];
            inputStream.Read(_payloadObjectBytes, 0, _payloadObjectBytes.Length);
        }

        #endregion
    }
}
