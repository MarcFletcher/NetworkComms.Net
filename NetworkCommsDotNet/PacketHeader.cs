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
using ProtoBuf;
using System.IO;
using DPSBase;

using Serializer = DPSBase.DataSerializer;

namespace NetworkCommsDotNet
{
    public enum PacketHeaderLongItems : byte
    {
        PayloadPacketSize,
        SerializerProcessors,
        PacketCreationTime,
    }

    public enum PacketHeaderStringItems : byte
    {
        PacketType,
        ReceiveConfirmationRequired,
        RequestedReturnPactetType,
        CheckSumHash,
    }

    /// <summary>
    /// Definintion of the network comms packet header.
    /// </summary>
    [ProtoContract]
    public sealed class PacketHeader
    {
        [ProtoMember(1)]
        Dictionary<PacketHeaderLongItems, long> longItems;
        [ProtoMember(2)]
        Dictionary<PacketHeaderStringItems, string> stringItems;
        
        /// <summary>
        /// Blank constructor for deserialisation using protobuf
        /// </summary>
        private PacketHeader() { }
        
        public PacketHeader(string packetTypeStr, long payloadPacketSize, string requestedReturnPacketTypeStr = null, bool receiveConfirmationRequired = false, string checkSumHash = null, bool includeConstructionTime = false)
        {
            longItems = new Dictionary<PacketHeaderLongItems, long>();
            stringItems = new Dictionary<PacketHeaderStringItems, string>();

            stringItems.Add(PacketHeaderStringItems.PacketType, packetTypeStr);
            longItems.Add(PacketHeaderLongItems.PayloadPacketSize, payloadPacketSize);
            
            if (requestedReturnPacketTypeStr != null)
                stringItems.Add(PacketHeaderStringItems.RequestedReturnPactetType, requestedReturnPacketTypeStr);

            if (receiveConfirmationRequired)
                stringItems.Add(PacketHeaderStringItems.ReceiveConfirmationRequired, "");

            if (checkSumHash != null)
                stringItems.Add(PacketHeaderStringItems.CheckSumHash, checkSumHash);

            if (includeConstructionTime)
                longItems.Add(PacketHeaderLongItems.PacketCreationTime, DateTime.Now.Ticks);
        }

        internal PacketHeader(byte[] packetData, SendReceiveOptions sendReceiveOptions)
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

        public int PayloadPacketSize
        {
            get { return (int)longItems[PacketHeaderLongItems.PayloadPacketSize]; }
            private set { longItems[PacketHeaderLongItems.PayloadPacketSize] = value; }
        }

        public string PacketType
        {
            get { return stringItems[PacketHeaderStringItems.PacketType]; }
            private set { stringItems[PacketHeaderStringItems.PacketType] = value; }
        }

        public bool ContainsOption(PacketHeaderStringItems option)
        {
            return stringItems.ContainsKey(option);
        }

        public bool ContainsOption(PacketHeaderLongItems option)
        {
            return longItems.ContainsKey(option);
        }

        public long GetOption(PacketHeaderLongItems option)
        {
            return longItems[option];
        }

        public string GetOption(PacketHeaderStringItems options)
        {
            return stringItems[options];
        }

        public void SetOption(PacketHeaderLongItems option, long Value)
        {
            longItems[option] = Value;
        }

        public void SetOption(PacketHeaderStringItems option, string Value)
        {
            stringItems[option] = Value;
        }

        #endregion
    }
}
