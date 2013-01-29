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
