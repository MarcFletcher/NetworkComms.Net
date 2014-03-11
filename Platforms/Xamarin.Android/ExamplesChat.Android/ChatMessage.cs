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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//We need to add the following two namespaces to this class
using NetworkCommsDotNet;
using ProtoBuf;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.DPSBase;

namespace ExamplesChat.Android
{
    /// <summary>
    /// A wrapper class for the messages that we intend to send and recieve. 
    /// The [ProtoContract] attribute informs NetworkCommsDotNet that we intend to 
    /// serialise (turn into bytes) this object. At the base level the 
    /// serialisation is performed by protobuf.net.
    /// </summary>
    [ProtoContract]
    public class ChatMessage : IExplicitlySerialize
    {
        /// <summary>
        /// The source identifier of this ChatMessage.
        /// We use this variable as the constructor for the ShortGuid.
        /// The [ProtoMember(1)] attribute informs the serialiser that when
        /// an object of type ChatMessage is serialised we want to include this variable
        /// </summary>
        [ProtoMember(1)]
        string _sourceIdentifier;

        /// <summary>
        /// The source identifier is accessible as a ShortGuid
        /// </summary>
        public ShortGuid SourceIdentifier { get { return new ShortGuid(_sourceIdentifier); } }

        /// <summary>
        /// The name of the source of this ChatMessage. 
        /// We use shorthand declaration, get and set.
        /// The [ProtoMember(2)] attribute informs the serialiser that when
        /// an object of type ChatMessage is serialised we want to include this variable 
        /// </summary>
        [ProtoMember(2)]
        public string SourceName { get; private set; }

        /// <summary>
        /// The actual message.
        /// </summary>
        [ProtoMember(3)]
        public string Message { get; private set; }

        /// <summary>
        /// The index of this message. Every message sent by a particular source
        /// has an incrementing index.
        /// </summary>
        [ProtoMember(4)]
        public long MessageIndex { get; private set; }

        /// <summary>
        /// The number of times this message has been relayed.
        /// </summary>
        [ProtoMember(5)]
        public int RelayCount { get; private set; }

        /// <summary>
        /// We must include a private constructor to be used by the deserialisation step.
        /// </summary>
        private ChatMessage() { }

        /// <summary>
        /// Create a new ChatMessage
        /// </summary>
        /// <param name="sourceIdentifier">The source identifier</param>
        /// <param name="sourceName">The source name</param>
        /// <param name="message">The message to be sent</param>
        /// <param name="messageIndex">The index of this message</param>
        public ChatMessage(ShortGuid sourceIdentifier, string sourceName, string message, long messageIndex)
        {
            this._sourceIdentifier = sourceIdentifier;
            this.SourceName = sourceName;
            this.Message = message;
            this.MessageIndex = messageIndex;
            this.RelayCount = 0;
        }

        /// <summary>
        /// Increment the relay count variable
        /// </summary>
        public void IncrementRelayCount()
        {
            RelayCount++;
        }

        public void Serialize(System.IO.Stream outputStream)
        {
            List<byte[]> data = new List<byte[]>();

            byte[] sourceIDData = Encoding.UTF8.GetBytes(_sourceIdentifier);
            byte[] sourceIDLengthData = BitConverter.GetBytes(sourceIDData.Length);

            data.Add(sourceIDLengthData); data.Add(sourceIDData);

            byte[] sourceNameData = Encoding.UTF8.GetBytes(SourceName);
            byte[] sourceNameLengthData = BitConverter.GetBytes(sourceNameData.Length);

            data.Add(sourceNameLengthData); data.Add(sourceNameData);

            byte[] messageData = Encoding.UTF8.GetBytes(Message);
            byte[] messageLengthData = BitConverter.GetBytes(messageData.Length);

            data.Add(messageLengthData); data.Add(messageData);

            byte[] messageIdxData = BitConverter.GetBytes(MessageIndex);

            data.Add(messageIdxData);

            byte[] relayCountData = BitConverter.GetBytes(RelayCount);

            data.Add(relayCountData);

            foreach (byte[] datum in data)
                outputStream.Write(datum, 0, datum.Length);
        }

        public void Deserialize(System.IO.Stream inputStream)
        {
            byte[] sourceIDLengthData = new byte[sizeof(int)]; inputStream.Read(sourceIDLengthData, 0, sizeof(int));
            byte[] sourceIDData = new byte[BitConverter.ToInt32(sourceIDLengthData, 0)]; inputStream.Read(sourceIDData, 0, sourceIDData.Length);
            _sourceIdentifier = new String(Encoding.UTF8.GetChars(sourceIDData));

            byte[] sourceNameLengthData = new byte[sizeof(int)]; inputStream.Read(sourceNameLengthData, 0, sizeof(int));
            byte[] sourceNameData = new byte[BitConverter.ToInt32(sourceNameLengthData, 0)]; inputStream.Read(sourceNameData, 0, sourceNameData.Length);
            SourceName = new String(Encoding.UTF8.GetChars(sourceNameData));

            byte[] messageLengthData = new byte[sizeof(int)]; inputStream.Read(messageLengthData, 0, sizeof(int));
            byte[] messageData = new byte[BitConverter.ToInt32(messageLengthData, 0)]; inputStream.Read(messageData, 0, messageData.Length);
            Message = new String(Encoding.UTF8.GetChars(messageData));

            byte[] messageIdxData = new byte[sizeof(long)]; inputStream.Read(messageIdxData, 0, sizeof(long));
            MessageIndex = BitConverter.ToInt64(messageIdxData, 0);

            byte[] relayCountData = new byte[sizeof(int)]; inputStream.Read(relayCountData, 0, sizeof(int));
            RelayCount = BitConverter.ToInt32(relayCountData, 0);
        }

        public static void Deserialize(System.IO.Stream inputStream, out ChatMessage result)
        {
            result = new ChatMessage();
            result.Deserialize(inputStream);
        }
    }
}