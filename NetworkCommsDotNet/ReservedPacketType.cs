//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Reserved packetTypeStrs. Removing or modifying these will prevent NetworkComms.Net from working
    /// </summary>
    enum ReservedPacketType
    {
        Confirmation,
        CheckSumFailResend,
        AliveTestPacket,
        ConnectionSetup,
        Unmanaged,
        NestedPacket,
    }
}
