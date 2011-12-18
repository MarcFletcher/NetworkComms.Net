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

namespace NetworkCommsDotNet
{
    public enum ReservedPacketType
    {
        /// <summary>
        /// Reserved packetTypeStrs. Removing/modifying these will prevent network comms from working
        /// </summary>
        #region Reserved
        Confirmation,
        CheckSumFailResend,
        PingPacket,
        ConnectionSetup,
        #endregion

        /// <summary>
        /// Distributed File System (DFS) packetTypeStrs. Removing/modifying these will prevent the distributed file system from working
        /// </summary>
        #region DFS
        //DFS_Setup, //Send be client to server to request setup information (for now its comms listening port)
        //DFS_RequestLocalItemBuild, //Build a swarm item locally mofo

        //DFS_ChunkAvailabilityInterestRequest, //Im interested in a chunk pleae
        //DFS_ChunkAvailabilityInterestReply, //Here is the chunk you requested or a reply saying try another chunk
        //DFS_ChunkAvailabilityInterestReplyComplete, //Thank-you for the chunk, I have it now

        //DFS_ChunkAvailabilityRequest, //Please tell me what chunks you have available
        //DFS_PeerChunkAvailabilityUpdate, // Remote peer now has an available chunk

        //DFS_ItemRemovedLocallyUpdate, //Informs swarm peers client no longer has item
        #endregion
    }
}
