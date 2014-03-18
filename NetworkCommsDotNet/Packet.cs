//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using NetworkCommsDotNet.DPSBase;
using System.IO;
using System.Reflection;
using NetworkCommsDotNet.Tools;

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
        internal int _payloadSize;

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

            //Check for security critical data processors
            //There may be performance issues here
            bool containsSecurityCritialDataProcessors = false;
            if (!options.Options.ContainsKey("UseNestedPacketType") && //We only need to perform this check if we are not already using a nested packet
                !isNested) //We do not perform this check within a nested packet
            {
                foreach (DataProcessor processor in options.DataProcessors)
                {
                    if (processor.IsSecurityCritical)
                    {
                        containsSecurityCritialDataProcessors = true;
                        break;
                    }
                }
            }

            //By default the object to serialise will be the payloadObject
            object objectToSerialise = payloadObject;
            bool objectToSerialiseIsNull = false;

            //We only replace the null with an empty stream if this is either in the nested packet
            //or we will not be nesting
            if (objectToSerialise == null && 
                ((!options.Options.ContainsKey("UseNestedPacketType") && 
                !containsSecurityCritialDataProcessors) || isNested))
            {
#if NETFX_CORE
                var emptyStream = new MemoryStream(new byte[0], 0, 0, false);
#else
                var emptyStream = new MemoryStream(new byte[0], 0, 0, false, true);
#endif
                //If the sending object is null we set objectToSerialiseIsNull and create a zero length StreamSendWrapper
                //The zero length StreamSendWrapper can then be passed to any data serializers
                objectToSerialiseIsNull = true;
                objectToSerialise = new StreamTools.StreamSendWrapper(new StreamTools.ThreadSafeStream(emptyStream, true));
            }

            //If we need to nest this packet
            if ((containsSecurityCritialDataProcessors || options.Options.ContainsKey("UseNestedPacketType")) && !isNested)
            {
                //We set the objectToSerialise to a nested packet
                objectToSerialise = new Packet(sendingPacketTypeStr, requestReturnPacketTypeStr, payloadObject, options, true);
            }
            else if (isNested)
            {
                //Serialise the payload object into byte[]
                //We do not use any data processors at this stage as the object will be processed again one level higher.
#if NETFX_CORE
                 _payloadObjectBytes = options.DataSerializer.SerialiseDataObject(payloadObject).ThreadSafeStream.ToArray();
                _payloadSize = _payloadObjectBytes.Length;
#else
                NetworkCommsDotNet.Tools.StreamTools.ThreadSafeStream tempStream = options.DataSerializer.SerialiseDataObject(objectToSerialise).ThreadSafeStream;
                _payloadObjectBytes = tempStream.GetBuffer();
                _payloadSize = (int)tempStream.Length;
#endif
                //Set the packet header
                //THe nulls represent internal SendReceiveOptions and no checksum
                this._packetHeader = new PacketHeader(sendingPacketTypeStr, _payloadSize, null, requestReturnPacketTypeStr, null);

                //Set the deserialiser information in the nested packet header, excluding data processors
                this._packetHeader.SetOption(PacketHeaderLongItems.SerializerProcessors, DPSManager.CreateSerializerDataProcessorIdentifier(options.DataSerializer, null));
            }

            //If we are at the top level packet we can finish off the serialisation
            if (!isNested)
            {
                //Set the payload stream data.
                if (objectToSerialiseIsNull && options.DataProcessors.Count == 0)
                    //Only if there are no data processors can we use a zero length array for nulls
                    //This ensures that should there be any required padding we can include it
                    this.payloadStream = (StreamTools.StreamSendWrapper)objectToSerialise;
                else
                {
                    if (objectToSerialise is Packet)
                        //We have to use the internal explicit serializer for nested packets (the nested data is already byte[])
                        this.payloadStream = NetworkComms.InternalFixedSendReceiveOptions.DataSerializer.SerialiseDataObject(objectToSerialise, options.DataProcessors, options.Options);
                    else
                        this.payloadStream = options.DataSerializer.SerialiseDataObject(objectToSerialise, options.DataProcessors, options.Options);
                }

                //We only calculate the checkSum if we are going to use it
                string hashStr = null;
                if (NetworkComms.EnablePacketCheckSumValidation)
                    hashStr = StreamTools.MD5(payloadStream.ThreadSafeStream.ToArray(payloadStream.Start, payloadStream.Length));

                //Choose the sending and receiving packet type depending on if it is being used with a nested packet
                string _sendingPacketTypeStr;
                string _requestReturnPacketTypeStr = null;
                if (containsSecurityCritialDataProcessors || options.Options.ContainsKey("UseNestedPacketType"))
                    _sendingPacketTypeStr = Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.NestedPacket);
                else
                {
                    _sendingPacketTypeStr = sendingPacketTypeStr;
                    _requestReturnPacketTypeStr = requestReturnPacketTypeStr;
                }

                this._packetHeader = new PacketHeader(_sendingPacketTypeStr, payloadStream.Length, options, _requestReturnPacketTypeStr, hashStr);

                //Add an identifier specifying the serialisers and processors we have used
                if (objectToSerialise is Packet)
                    this._packetHeader.SetOption(PacketHeaderLongItems.SerializerProcessors, DPSManager.CreateSerializerDataProcessorIdentifier(NetworkComms.InternalFixedSendReceiveOptions.DataSerializer, options.DataProcessors));
                else
                    this._packetHeader.SetOption(PacketHeaderLongItems.SerializerProcessors, DPSManager.CreateSerializerDataProcessorIdentifier(options.DataSerializer, options.DataProcessors));
            }

            //Set the null data header section if required
            if (objectToSerialiseIsNull && 
                ((!containsSecurityCritialDataProcessors && !options.Options.ContainsKey("UseNestedPacketType")) || isNested))
                this._packetHeader.SetOption(PacketHeaderStringItems.NullDataSection, "");

            if (NetworkComms.LoggingEnabled)
            {
                if (isNested)
                    NetworkComms.Logger.Trace(" ... created nested packet of type " + sendingPacketTypeStr);
                else
                    NetworkComms.Logger.Trace(" ... created packet of type " + sendingPacketTypeStr + ". PacketObject data size is " + payloadStream.Length.ToString() + " bytes");
            }
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

        /// <inheritdoc />
        public void Serialize(Stream outputStream)
        {
            _packetHeader.Serialize(outputStream);
            outputStream.Write(BitConverter.GetBytes(_payloadSize), 0, sizeof(int));
            outputStream.Write(_payloadObjectBytes, 0, _payloadSize);
        }

        /// <inheritdoc />
        public void Deserialize(Stream inputStream)
        {
            PacketHeader.Deserialize(inputStream, out _packetHeader);

            byte[] payloadLengthData = new byte[sizeof(int)];
            inputStream.Read(payloadLengthData, 0, sizeof(int));

            _payloadSize = BitConverter.ToInt32(payloadLengthData, 0);
            _payloadObjectBytes = new byte[_payloadSize];
            inputStream.Read(_payloadObjectBytes, 0, _payloadSize);
        }

        #endregion

        /// <summary>
        /// Deserializes from a memory stream to a <see cref="Packet"/> object
        /// </summary>
        /// <param name="inputStream">The memory stream containing the serialized <see cref="Packet"/></param>
        /// <param name="result">The deserialized <see cref="Packet"/></param>
        public static void Deserialize(Stream inputStream, out Packet result)
        {
            result = new Packet();
            result.Deserialize(inputStream);
        }
    }
}
