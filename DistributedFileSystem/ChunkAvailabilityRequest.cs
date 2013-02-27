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
using NetworkCommsDotNet;
using ProtoBuf;

namespace DistributedFileSystem
{
    public enum ChunkReplyState : byte
    {
        DataIncluded,
        ItemOrChunkNotAvailable,
        PeerBusy
    }

    [ProtoContract]
    public class ChunkAvailabilityRequest
    {
        [ProtoMember(1)]
        public string ItemCheckSum { get; private set; }
        [ProtoMember(2)]
        public byte ChunkIndex { get; private set; }

        public DateTime RequestCreationTime { get; private set; }

        public ConnectionInfo PeerConnectionInfo { get; private set; }

        /// <summary>
        /// We are currently processing incoming data for this request.
        /// </summary>
        public bool RequestIncoming { get; set; }

        /// <summary>
        /// We have received data and this request is complete.
        /// </summary>
        public bool RequestComplete { get; set; }

        private ChunkAvailabilityRequest() { }

        public ChunkAvailabilityRequest(string itemCheckSum, byte chunkIndex, ConnectionInfo peerConnectionInfo)
        {
            this.ItemCheckSum = itemCheckSum;
            this.ChunkIndex = chunkIndex;
            this.RequestCreationTime = DateTime.Now;
            this.PeerConnectionInfo = peerConnectionInfo;
            this.RequestIncoming = false;
            this.RequestComplete = false;
        }
    }

    [ProtoContract]
    public class ChunkAvailabilityReply
    {
        [ProtoMember(1)]
        public string ItemCheckSum { get; private set; }
        [ProtoMember(2)]
        public byte ChunkIndex { get; private set; }
        [ProtoMember(3)]
        public ChunkReplyState ReplyState { get; private set; }
        [ProtoMember(4)]
        public long DataSequenceNumber { get; private set; }
        [ProtoMember(5)]
        public string SourceNetworkIdentifier { get; private set; }

        public ConnectionInfo SourceConnectionInfo { get; private set; }

        public byte[] ChunkData { get; private set; }
        public bool ChunkDataSet { get; private set; }

        private ChunkAvailabilityReply() { }

        /// <summary>
        /// Create an empty reply
        /// </summary>
        /// <param name="itemCheckSum"></param>
        /// <param name="chunkIndex"></param>
        public ChunkAvailabilityReply(string sourceNetworkIdentifier, string itemCheckSum, byte chunkIndex, ChunkReplyState replyState)
        {
            this.SourceNetworkIdentifier = sourceNetworkIdentifier;
            this.ItemCheckSum = itemCheckSum;
            this.ChunkIndex = chunkIndex;
            this.ReplyState = replyState;
        }

        /// <summary>
        /// Create a reply with contains data
        /// </summary>
        /// <param name="itemMD5"></param>
        /// <param name="chunkIndex"></param>
        /// <param name="chunkData"></param>
        public ChunkAvailabilityReply(string sourceNetworkIdentifier, string itemCheckSum, byte chunkIndex, long dataSequenceNumber)
        {
            this.SourceNetworkIdentifier = sourceNetworkIdentifier;
            this.ItemCheckSum = itemCheckSum;
            this.ChunkIndex = chunkIndex;
            this.DataSequenceNumber = dataSequenceNumber;
            this.ReplyState = ChunkReplyState.DataIncluded;
        }

        /// <summary>
        /// Set the data for this ChunkAvailabilityReply
        /// </summary>
        /// <param name="chunkData"></param>
        public void SetChunkData(byte[] chunkData)
        {
            this.ChunkData = chunkData;
            ChunkDataSet = true;
        }

        public void SetSourceConnectionInfo(ConnectionInfo info)
        {
            this.SourceConnectionInfo = info;
        }
    }

    /// <summary>
    /// Temporary sotrage for chunk data which is awaiting corresponding info
    /// </summary>
    class ChunkDataWrapper
    {
        public long IncomingSequenceNumber { get; private set; }
        public byte[] Data { get; private set; }
        public DateTime TimeCreated { get; private set; }
        public ChunkAvailabilityReply ChunkAvailabilityReply { get; private set; }

        public ChunkDataWrapper(ChunkAvailabilityReply chunkAvailabilityReply)
        {
            if (chunkAvailabilityReply == null)
                throw new Exception("Unable to create a ChunkDataWrapper with a null ChunkAvailabilityReply reference.");

            this.ChunkAvailabilityReply = chunkAvailabilityReply;
            this.TimeCreated = DateTime.Now;
        }

        public ChunkDataWrapper(long incomingSequenceNumber, byte[] data)
        {
            this.IncomingSequenceNumber = incomingSequenceNumber;
            this.Data = data;
            this.TimeCreated = DateTime.Now;
        }
    }
}
