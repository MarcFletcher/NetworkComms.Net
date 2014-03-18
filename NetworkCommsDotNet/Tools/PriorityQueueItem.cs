//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;
using NetworkCommsDotNet.Connections;

namespace NetworkCommsDotNet.Tools
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

        /// <summary>
        /// Initialise a new PriorityQueueItem
        /// </summary>
        /// <param name="priority"></param>
        /// <param name="connection"></param>
        /// <param name="packetHeader"></param>
        /// <param name="dataStream"></param>
        /// <param name="sendReceiveOptions"></param>
        public PriorityQueueItem(QueueItemPriority priority, Connection connection, PacketHeader packetHeader, MemoryStream dataStream, SendReceiveOptions sendReceiveOptions)
        {
            if (connection == null) throw new ArgumentNullException("connection", "Provided Connection parameter cannot be null.");
            if (packetHeader == null) throw new ArgumentNullException("packetHeader", "Provided PacketHeader parameter cannot be null.");
            if (dataStream == null) throw new ArgumentNullException("dataStream", "Provided MemoryStream parameter cannot be null.");
            if (sendReceiveOptions == null) throw new ArgumentNullException("sendReceiveOptions", "Provided sendReceiveOptions cannot be null.");

            this.Priority = priority;
            this.Connection = connection;
            this.PacketHeader = packetHeader;
            this.DataStream = dataStream;
            this.SendReceiveOptions = sendReceiveOptions;
        }
    }
}
