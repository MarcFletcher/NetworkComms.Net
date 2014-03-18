//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// The type of <see cref="Connection"/>.
    /// </summary>
    public enum ConnectionType
    {
        /// <summary>
        /// An undefined connection type. This is used as the default value.
        /// </summary>
        Undefined,

        /// <summary>
        /// A TCP connection type. Used by <see cref="NetworkCommsDotNet.Connections.TCP.TCPConnection"/>.
        /// </summary>
        TCP,

        /// <summary>
        /// A UDP connection type. Used by <see cref="NetworkCommsDotNet.Connections.UDP.UDPConnection"/>.
        /// </summary>
        UDP,

#if !NET2 && !WINDOWS_PHONE
        /// <summary>
        /// A Bluetooth RFCOMM connection. Used by <see cref="NetworkCommsDotNet.Connections.Bluetooth.BluetoothConnection"/> 
        /// </summary>
        Bluetooth,
#endif

        //We may support others in future such as SSH, FTP, SCP etc.
    }

    /// <summary>
    /// The connections application layer protocol status.
    /// </summary>
    public enum ApplicationLayerProtocolStatus
    {
        /// <summary>
        /// Useful for selecting or searching connections when the ApplicationLayerProtocolStatus
        /// is unimportant.
        /// </summary>
        Undefined,

        /// <summary>
        /// Default value. NetworkComms.Net will use a custom application layer protocol to provide 
        /// useful features such as inline serialisation, transparent packet send and receive, 
        /// connection handshakes and remote information etc. We strongly recommend you enable the 
        /// NetworkComms.Net application layer protocol.
        /// </summary>
        Enabled,

        /// <summary>
        /// No application layer protocol will be used. TCP packets may fragment or be concatenated 
        /// with other packets. A large number of library features will be unavailable.
        /// </summary>
        Disabled
    }
}