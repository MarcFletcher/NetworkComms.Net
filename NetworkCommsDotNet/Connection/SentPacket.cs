//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// A wrapper object for keeping track of sent packets. These are used if a resend is requested due to a checksum validation failure.
    /// </summary>
    class SentPacket
    {
        public int SendCount { get; private set; }
        public IPacket Packet { get; private set; }
        public DateTime SentPacketCreationTime { get; private set; }

        public SentPacket(IPacket packet)
        {
            this.SentPacketCreationTime = DateTime.Now;
            this.Packet = packet;
            this.SendCount = 1;
        }

        public void IncrementSendCount()
        {
            SendCount++;
        }

        public override string ToString()
        {
            string timeString;
#if NETFX_CORE
            timeString = NetworkCommsDotNet.Tools.XPlatformHelper.DateTimeExtensions.ToShortTimeString(SentPacketCreationTime);
#else
            timeString = (SentPacketCreationTime).ToShortTimeString();
#endif

            return "[" + timeString + "] " + Packet.PacketHeader.PacketType + " - " + Packet.PacketData.Length.ToString() + " bytes.";
        }
    }
}
