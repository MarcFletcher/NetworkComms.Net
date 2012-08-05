using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;

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
            throw new NotImplementedException();
        }
    }
}
