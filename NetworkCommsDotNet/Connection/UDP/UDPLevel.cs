using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    [Flags]
    public enum UDPLevel
    {
        None = 0x0,
        EstablishHandshake = 0x1,
    }
}