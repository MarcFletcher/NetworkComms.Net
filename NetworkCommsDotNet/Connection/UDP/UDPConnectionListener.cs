// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.Net.Sockets;

namespace NetworkCommsDotNet.Connections.UDP
{
    /// <summary>
    /// A UDP connection listener
    /// </summary>
    public class UDPConnectionListener : ConnectionListenerBase
    {
        /// <summary>
        /// The UDPOptions to be used for this listener
        /// </summary>
        public UDPOptions UDPOptions { get; private set; }

        /// <summary>
        /// The UDP listener is a UDP connection
        /// </summary>
        internal UDPConnection UDPConnection { get; set; }

        /// <summary>
        /// Create a new instance of a UDP listener
        /// </summary>
        /// <param name="sendReceiveOptions">The SendReceiveOptions to use with incoming data on this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="udpOptions">The UDPOptions to use with this listener</param>
        /// <param name="allowDiscoverable">Determines if the newly created <see cref="ConnectionListenerBase"/> will be discoverable if <see cref="Tools.PeerDiscovery"/> is enabled.</param>
        public UDPConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol, 
            UDPOptions udpOptions, bool allowDiscoverable = false)
            :base(ConnectionType.UDP, sendReceiveOptions, applicationLayerProtocol, allowDiscoverable)
        {
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled && udpOptions != UDPOptions.None)
                throw new ArgumentException("If the application layer protocol has been disabled the provided UDPOptions can only be UDPOptions.None.");

            UDPOptions = udpOptions;
        }

        /// <inheritdoc />
        internal override void StartListening(EndPoint desiredLocalListenEndPoint, bool useRandomPortFailOver)
        {
            if (desiredLocalListenEndPoint.GetType() != typeof(IPEndPoint)) throw new ArgumentException("Invalid desiredLocalListenEndPoint type provided.", "desiredLocalListenEndPoint");
            if (IsListening) throw new InvalidOperationException("Attempted to call StartListening when already listening.");

            IPEndPoint desiredLocalListenIPEndPoint = (IPEndPoint)desiredLocalListenEndPoint;

            try
            {
                UDPConnection = new UDPConnection(new ConnectionInfo(ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), desiredLocalListenIPEndPoint, ApplicationLayerProtocol, this), ListenerDefaultSendReceiveOptions, UDPOptions, true);
            }
            catch (SocketException)
            {
                if (useRandomPortFailOver)
                {
                    try
                    {
                        UDPConnection = new UDPConnection(new ConnectionInfo(ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(desiredLocalListenIPEndPoint.Address, 0), ApplicationLayerProtocol, this), ListenerDefaultSendReceiveOptions, UDPOptions, true);
                    }
                    catch (SocketException)
                    {
                        //If we get another socket exception this appears to be a bad IP. We will just ignore this IP
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to open a random port on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                        throw new CommsSetupShutdownException("It was not possible to open a random port on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }
                else
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to open port #" + desiredLocalListenIPEndPoint.Port.ToString() + " on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    throw new CommsSetupShutdownException("It was not possible to open port #" + desiredLocalListenIPEndPoint.Port.ToString() + " on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                }
            }

            this.LocalListenEndPoint = (IPEndPoint)UDPConnection.udpClient.LocalIPEndPoint;

            this.IsListening = true;
        }

        /// <inheritdoc />
        internal override void StopListening()
        {
            this.IsListening = false;
            UDPConnection.CloseConnection(false, -16);
        }
    }
}
