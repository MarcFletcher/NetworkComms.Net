using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A wrapper object for keeping track of sent packets. These are used if a resend is requested due to a checksum validation failure.
    /// </summary>
    class SentPacket
    {
        public int SendCount { get; private set; }
        public Packet Packet { get; private set; }

        public SentPacket(Packet packet)
        {
            this.Packet = packet;
            this.SendCount = 1;
        }

        public void IncrementSendCount()
        {
            SendCount++;
        }

        public override string ToString()
        {
            if (Packet.PacketHeader.ContainsOption(PacketHeaderLongItems.PacketCreationTime))
                return "[" + (new DateTime(Packet.PacketHeader.GetOption(PacketHeaderLongItems.PacketCreationTime))).ToShortTimeString() + "] " +
                    Packet.PacketHeader.PacketType + " - " + Packet.PacketData.Length + " bytes.";
            else
                return "[Unknown] " + Packet.PacketHeader.PacketType + " - " + Packet.PacketData.Length + " bytes.";
        }
    }
}
