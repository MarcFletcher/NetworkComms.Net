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

        public override void SendObject(string sendingPacketType, object objectToSend, SendReceiveOptions options)
        {
            throw new NotImplementedException();
        }

        public override returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, SendReceiveOptions options)
        {
            throw new NotImplementedException();
        }

        protected override void SendPacket(Packet packet)
        {
            throw new NotImplementedException();
        }

        internal override void SendNullPacket()
        {
            throw new NotImplementedException();
        }
    }
}
