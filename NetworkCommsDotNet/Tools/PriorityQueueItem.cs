//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
using System.Text;
using System.Threading;
using System.IO;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Private wrapper class used for passing data to the incoming packet item worker
    /// </summary>
    class PriorityQueueItem
    {
        public QueueItemPriority Priority { get; private set; }
        public Connection Connection { get; private set; }
        public PacketHeader PacketHeader { get; private set; }
        public MemoryStream DataStream { get; private set; }
        public SendReceiveOptions SendReceiveOptions { get; private set; }

        public PriorityQueueItem(QueueItemPriority priority, Connection connection, PacketHeader packetHeader, MemoryStream dataStream, SendReceiveOptions sendReceiveOptions)
        {
            //Nullreference checks
            if (connection == null) throw new NullReferenceException("Provided connection parameter can not be null.");
            if (packetHeader == null) throw new NullReferenceException("Provided packetHeader parameter can not be null.");
            if (dataStream == null) throw new NullReferenceException("Provided dataStream parameter can not be null.");
            if (sendReceiveOptions == null) throw new NullReferenceException("Provided sendReceiveOptions parameter can not be null.");

            this.Priority = priority;
            this.Connection = connection;
            this.PacketHeader = packetHeader;
            this.DataStream = dataStream;
            this.SendReceiveOptions = sendReceiveOptions;
        }
    }
}
