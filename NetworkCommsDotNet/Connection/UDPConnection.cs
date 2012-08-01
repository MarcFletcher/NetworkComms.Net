using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    //Maximum packet size is theoretically 65,507 bytes
    public class UDPConnection : Connection
    {
        UdpClient udpClient;

        public UDPConnection CreateConnection(ConnectionInfo connectionInfo, UDPLevel level, bool establishIfRequired = true)
        {
            throw new NotImplementedException();
        }

        protected UDPConnection(ConnectionInfo connectionInfo)
            : base(connectionInfo)
        {

        }

        protected override void EstablishConnectionInternal()
        {
            //There is generally no establish for a UDP connection
        }

        protected override void CloseConnectionInternal(bool closeDueToError, int logLocation = 0)
        {
            throw new NotImplementedException();
        }

        protected override void SendPacketInternal(Packet packet)
        {
            throw new NotImplementedException();
        }

        internal override void SendNullPacket()
        {
            throw new NotImplementedException();
        }
    }

    [Flags]
    public enum UDPLevel
    {
        None = 0x0,
	    EstablishHandshake = 0x1,
    }
}
