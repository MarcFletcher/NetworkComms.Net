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
using System.Net.Sockets;
using System.Net;

namespace NetworkCommsDotNet
{
    //Maximum packet size is theoretically 65,507 bytes
    public partial class UDPConnection : Connection
    {
        UdpClient udpClient;

        public UDPConnection CreateConnection(ConnectionInfo connectionInfo, UDPLevel level, bool establishIfRequired = true)
        {
            throw new NotImplementedException();
        }

        protected UDPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions)
            : base(connectionInfo, defaultSendReceiveOptions)
        {

        }

        protected override void EstablishConnectionSpecific()
        {
            //There is generally no establish for a UDP connection
        }

        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
            throw new NotImplementedException();
        }

        protected override void SendPacketSpecific(Packet packet)
        {
            throw new NotImplementedException();
        }

        protected override void StartIncomingDataListen()
        {
            throw new NotImplementedException();
        }

        public static void CloseAllConnections(EndPoint[] closeAllExceptTheseEndPoints)
        {
            throw new NotImplementedException();
        }

        internal static void Shutdown()
        {
            //Close any established udp listeners

            throw new NotImplementedException();
        }
    }
}
