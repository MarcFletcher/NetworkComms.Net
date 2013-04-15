using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using ProtoBuf;
using NetworkCommsDotNet;

namespace ExamplesChat.Android
{
    /// <summary>
    /// A wrapper class for the messages that we intend to send and recieve. 
    /// The [ProtoContract] attribute informs NetworkCommsDotNet that we intend to 
    /// serialise (turn into bytes) this object. At the base level the 
    /// serialisation is performed by protobuf.net.
    /// </summary>
    [ProtoContract]
    class ChatMessage
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
        public ChatMessage() { }

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
    }
}