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
        public long ItemCheckSum { get; private set; }
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

        public ChunkAvailabilityRequest(long itemCheckSum, byte chunkIndex, ConnectionInfo peerConnectionInfo)
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
        public long ItemCheckSum { get; private set; }
        [ProtoMember(2)]
        public byte ChunkIndex { get; private set; }
        [ProtoMember(3)]
        public ChunkReplyState ReplyState { get; private set; }
        [ProtoMember(4)]
        public byte[] ChunkData { get; private set; }

        private ChunkAvailabilityReply() { }

        /// <summary>
        /// Create an empty reply
        /// </summary>
        /// <param name="itemCheckSum"></param>
        /// <param name="chunkIndex"></param>
        public ChunkAvailabilityReply(long itemCheckSum, byte chunkIndex, ChunkReplyState replyState)
        {
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
        public ChunkAvailabilityReply(long itemCheckSum, byte chunkIndex, byte[] chunkData)
        {
            this.ItemCheckSum = itemCheckSum;
            this.ChunkIndex = chunkIndex;
            this.ChunkData = chunkData;
            this.ReplyState = ChunkReplyState.DataIncluded;
        }
    }
}
