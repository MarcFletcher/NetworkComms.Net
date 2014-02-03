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
using System.Linq;
using System.Text;

using ProtoBuf;

namespace Examples.ExamplesFileTransfer.WPF
{
    /// <summary>
    /// Information class used to associate incoming data with the correct ReceivedFile
    /// </summary>
    [ProtoContract]
    class SendInfo
    {
        /// <summary>
        /// Corresponding filename
        /// </summary>
        [ProtoMember(1)]
        public string Filename { get; private set; }

        /// <summary>
        /// The starting point for the associated data
        /// </summary>
        [ProtoMember(2)]
        public long BytesStart { get; private set; }

        /// <summary>
        /// The total number of bytes expected for the whole ReceivedFile
        /// </summary>
        [ProtoMember(3)]
        public long TotalBytes { get; private set; }

        /// <summary>
        /// The packet sequence number corresponding to the associated data
        /// </summary>
        [ProtoMember(4)]
        public long PacketSequenceNumber { get; private set; }

        /// <summary>
        /// Private constructor required for deserialisation
        /// </summary>
        private SendInfo() { }

        /// <summary>
        /// Create a new instance of SendInfo
        /// </summary>
        /// <param name="filename">Filename corresponding to data</param>
        /// <param name="totalBytes">Total bytes of the whole ReceivedFile</param>
        /// <param name="bytesStart">The starting point for the associated data</param>
        /// <param name="packetSequenceNumber">Packet sequence number corresponding to the associated data</param>
        public SendInfo(string filename, long totalBytes, long bytesStart, long packetSequenceNumber)
        {
            this.Filename = filename;
            this.TotalBytes = totalBytes;
            this.BytesStart = bytesStart;
            this.PacketSequenceNumber = packetSequenceNumber;
        }
    }
}
