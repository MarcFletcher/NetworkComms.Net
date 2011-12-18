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
using SerializerBase;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Definintion of the network comms packet header.
    /// </summary>
    [ProtoContract]
    public class PacketHeader
    {
        [ProtoMember(1)]
        int payloadPacketSize;
        [ProtoMember(2)]
        string packetTypeStr;
        [ProtoMember(3)]
        bool recieveConfirmationRequired;
        [ProtoMember(4)]
        long checkSumHash;
        [ProtoMember(5)]
        DateTime packetCreationTime;

        [ProtoMember(6)]
        bool pureBytesInPayload;

        /// <summary>
        /// Blank constructor for deserialisation using protobuf
        /// </summary>
        private PacketHeader() { }

        public PacketHeader(string packetTypeStr, bool recieveConfirmationRequired, long checkSumHash, int payloadPacketSize, bool pureBytesInPayload)
        {
            this.packetTypeStr = packetTypeStr;
            this.recieveConfirmationRequired = recieveConfirmationRequired;
            this.checkSumHash = checkSumHash;
            this.payloadPacketSize = payloadPacketSize;
            this.pureBytesInPayload = pureBytesInPayload;
            this.packetCreationTime = DateTime.Now;
        }

        internal PacketHeader(byte[] packetData, ISerialize serializer, ICompress compressor)
        {
            try
            {
                PacketHeader tempObject = serializer.DeserialiseDataObject<PacketHeader>(packetData, compressor);
                if (tempObject == null)
                    throw new SerialisationException("Something went wrong when trying to deserialise the packet header object");
                else
                {
                    payloadPacketSize = tempObject.PayloadPacketSize;
                    packetTypeStr = tempObject.PacketType;
                    recieveConfirmationRequired = tempObject.RecieveConfirmationRequired;
                    checkSumHash = tempObject.CheckSumHash;
                    pureBytesInPayload = tempObject.PureBytesInPayload;
                    packetCreationTime = tempObject.PacketCreationTime;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Error deserialising packetHeader. " + ex.ToString());
            }
        }

        #region Get & Set
        public int PayloadPacketSize
        {
            get { return payloadPacketSize; }
        }

        public string PacketType
        {
            get { return packetTypeStr; }
        }

        public bool RecieveConfirmationRequired
        {
            get { return recieveConfirmationRequired; }
        }

        public long CheckSumHash
        {
            get { return checkSumHash; }
        }

        public bool PureBytesInPayload
        {
            get { return pureBytesInPayload; }
        }

        public DateTime PacketCreationTime
        {
            get { return packetCreationTime; }
        }

        #endregion
    }
}
