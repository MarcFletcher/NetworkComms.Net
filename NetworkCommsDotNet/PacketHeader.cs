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
using System.Linq;
using System.Text;
using ProtoBuf;
using System.IO;
using DPSBase;

using Serializer = DPSBase.DataSerializer;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Any <see cref="PacketHeader"/> options which are stored as a long.
    /// </summary>
    public enum PacketHeaderLongItems : byte
    {
        /// <summary>
        /// The size of the packet data payload in bytes. This is a compulsory option.
        /// </summary>
        PayloadPacketSize,

        /// <summary>
        /// The data serializer and data processor used to unwrap the payload. Used as flags.
        /// </summary>
        SerializerProcessors,

        /// <summary>
        /// The creation time of the packet header.
        /// </summary>
        PacketCreationTime,

        /// <summary>
        /// The sequence number for this packet. Each connection maintains a unique counter which is increments on each sent packet.
        /// </summary>
        PacketSequenceNumber,
    }

    /// <summary>
    /// Any <see cref="PacketHeader"/> options which are stored as a string.
    /// </summary>
    public enum PacketHeaderStringItems : byte
    {
        /// <summary>
        /// The type of the packet. This is a compulsory option which determines how the incoming packet is handled.
        /// </summary>
        PacketType,

        /// <summary>
        /// Specifies if a recieve confirmation is required for this packet. String option as takes up less space for a boolean option.
        /// </summary>
        ReceiveConfirmationRequired,

        /// <summary>
        /// The packet type which should be used for any return packet type.
        /// </summary>
        RequestedReturnPacketType,

        /// <summary>
        /// A checksum corresponding to the payload data.
        /// </summary>
        CheckSumHash,

        /// <summary>
        /// Optional packet identifier.
        /// </summary>
        PacketIdentifier,
    }

    /// <summary>
    /// Contains information required to send, receive and correctly rebuild any objects sent via NetworkCommsDotNet.
    /// Any data sent via NetworkCommsDotNet is always preceeded by a packetHeader.
    /// </summary>
    [ProtoContract]
    public sealed class PacketHeader
    {
        [ProtoMember(1)]
        Dictionary<PacketHeaderLongItems, long> longItems;
        [ProtoMember(2)]
        Dictionary<PacketHeaderStringItems, string> stringItems;
        
        /// <summary>
        /// Blank constructor required for deserialisation
        /// </summary>
        private PacketHeader() { }
        
        /// <summary>
        /// Creates a new packetHeader
        /// </summary>
        /// <param name="packetTypeStr">The packet type to be used.</param>
        /// <param name="payloadPacketSize">The size on bytes of the payload</param>
        /// <param name="requestedReturnPacketTypeStr">An optional field representing the expected return packet type</param>
        /// <param name="receiveConfirmationRequired">An optional boolean stating that a recieve confirmation is required for this packet</param>
        /// <param name="checkSumHash">An optional field representing the payload checksum</param>
        /// <param name="includeConstructionTime">An optional boolean which if true will record the DateTime this packet was created</param>
        public PacketHeader(string packetTypeStr, long payloadPacketSize, string requestedReturnPacketTypeStr = null, bool receiveConfirmationRequired = false, string checkSumHash = null, bool includeConstructionTime = false)
        {
            longItems = new Dictionary<PacketHeaderLongItems, long>();
            stringItems = new Dictionary<PacketHeaderStringItems, string>();

            stringItems.Add(PacketHeaderStringItems.PacketType, packetTypeStr);
            longItems.Add(PacketHeaderLongItems.PayloadPacketSize, payloadPacketSize);
            
            if (requestedReturnPacketTypeStr != null)
                stringItems.Add(PacketHeaderStringItems.RequestedReturnPacketType, requestedReturnPacketTypeStr);

            if (receiveConfirmationRequired)
                stringItems.Add(PacketHeaderStringItems.ReceiveConfirmationRequired, "");

            if (checkSumHash != null)
                stringItems.Add(PacketHeaderStringItems.CheckSumHash, checkSumHash);

            if (includeConstructionTime)
                longItems.Add(PacketHeaderLongItems.PacketCreationTime, DateTime.Now.Ticks);
        }

        internal PacketHeader(MemoryStream packetData, SendReceiveOptions sendReceiveOptions)
        {
            try
            {
                if (packetData.Length == 0)
                    throw new SerialisationException("Attempted to create packetHeader using 0 packetData bytes.");

                PacketHeader tempObject = sendReceiveOptions.DataSerializer.DeserialiseDataObject<PacketHeader>(packetData, sendReceiveOptions.DataProcessors, sendReceiveOptions.Options);
                if (tempObject == null || !tempObject.longItems.ContainsKey(PacketHeaderLongItems.PayloadPacketSize) || !tempObject.stringItems.ContainsKey(PacketHeaderStringItems.PacketType))
                    throw new SerialisationException("Something went wrong when trying to deserialise the packet header object");
                else
                {
                    stringItems = tempObject.stringItems.ToDictionary(s => s.Key, s=> String.Copy(s.Value));
                    longItems = tempObject.longItems.ToDictionary(s => s.Key, s => s.Value);
                }
            }
            catch (Exception ex)
            {
                throw new SerialisationException("Error deserialising packetHeader. " + ex.ToString());
            }
        }

        #region Get & Set
        /// <summary>
        /// The total size in bytes of the payload.
        /// </summary>
        public int PayloadPacketSize
        {
            get { return (int)longItems[PacketHeaderLongItems.PayloadPacketSize]; }
            private set { longItems[PacketHeaderLongItems.PayloadPacketSize] = value; }
        }

        /// <summary>
        /// The packet type.
        /// </summary>
        public string PacketType
        {
            get { return stringItems[PacketHeaderStringItems.PacketType]; }
            private set { stringItems[PacketHeaderStringItems.PacketType] = value; }
        }

        /// <summary>
        /// Check if a string option has been set.
        /// </summary>
        /// <param name="option">The string option to be checked.</param>
        /// <returns>Returns true if the provided string option has been set.</returns>
        public bool ContainsOption(PacketHeaderStringItems option)
        {
            return stringItems.ContainsKey(option);
        }

        /// <summary>
        /// Check if a long option has been set.
        /// </summary>
        /// <param name="option">The long option to be checked.</param>
        /// <returns>Returns true if the provided long option has been set.</returns>
        public bool ContainsOption(PacketHeaderLongItems option)
        {
            return longItems.ContainsKey(option);
        }

        /// <summary>
        /// Get a long option.
        /// </summary>
        /// <param name="option">The option to get</param>
        /// <returns>The requested long option</returns>
        public long GetOption(PacketHeaderLongItems option)
        {
            return longItems[option];
        }

        /// <summary>
        /// Get a string option
        /// </summary>
        /// <param name="options">The option to get</param>
        /// <returns>The requested string option</returns>
        public string GetOption(PacketHeaderStringItems options)
        {
            return stringItems[options];
        }

        /// <summary>
        /// Set a long option with the provided value.
        /// </summary>
        /// <param name="option">The option to set</param>
        /// <param name="Value">The option value</param>
        public void SetOption(PacketHeaderLongItems option, long Value)
        {
            longItems[option] = Value;
        }

        /// <summary>
        /// Set a string option with the provided value.
        /// </summary>
        /// <param name="option">The option to set</param>
        /// <param name="Value">The option value</param>
        public void SetOption(PacketHeaderStringItems option, string Value)
        {
            stringItems[option] = Value;
        }
        #endregion
    }
}
