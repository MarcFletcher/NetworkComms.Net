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

    }
}
