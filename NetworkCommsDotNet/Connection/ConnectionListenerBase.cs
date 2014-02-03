//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NetworkCommsDotNet.DPSBase;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#endif

namespace NetworkCommsDotNet.Connections
{
    /// <summary>
    /// A base class that the listener of each connection type inherits from.
    /// This allows NetworkComms.Net to manage listeners at the general connection level.
    /// </summary>
    public abstract class ConnectionListenerBase
    {
        /// <summary>
        /// The send receive options associated with this listener.
        /// </summary>
        public SendReceiveOptions SendReceiveOptions { get; protected set; }

        /// <summary>
        /// The connection type that this listener supports.
        /// </summary>
        public ConnectionType ConnectionType { get; protected set; }

        /// <summary>
        /// The application layer protocol status for this listener.
        /// </summary>
        public ApplicationLayerProtocolStatus ApplicationLayerProtocol { get; protected set; }

        /// <summary>
        /// True if this listener is listening.
        /// </summary>
        public bool IsListening { get; protected set; }

        /// <summary>
        /// True if this listenner will be advertised via peer discovery
        /// </summary>
        public bool IsDiscoverable { get; protected set; }

        /// <summary>
        /// The local IPEndPoint that this listener is associated with.
        /// </summary>
        public EndPoint LocalListenEndPoint { get; protected set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="connectionType">The connection type to listen for.</param>
        /// <param name="sendReceiveOptions">The send receive options to use for this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        protected ConnectionListenerBase(ConnectionType connectionType,
            SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol,
            bool isDiscoverable)
        {
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined is not valid when calling this method.", "connectionType");
            if (sendReceiveOptions == null) throw new ArgumentNullException("sendReceiveOptions", "Provided send receive option may not be null.");
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined) throw new ArgumentException("ApplicationLayerProtocolStatus.Undefined is not valid when calling this method.", "applicationLayerProtocol");

            //Validate SRO options if the application layer protocol is disabled
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
            {
                if (sendReceiveOptions.Options.ContainsKey("ReceiveConfirmationRequired"))
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options specified the ReceiveConfirmationRequired option. Please provide compatible send receive options in order to successfully" +
                        " instantiate this unmanaged connection.", "sendReceiveOptions");

                if (sendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options serialiser was not NullSerializer. Please provide compatible send receive options in order to successfully" +
                        " instantiate this unmanaged connection.", "sendReceiveOptions");

                if (sendReceiveOptions.DataProcessors.Count > 0)
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options contains data processors. Data processors may not be used with unmanaged connections." +
                        " Please provide compatible send receive options in order to successfully instantiate this unmanaged connection.", "sendReceiveOptions");
            }

            this.ConnectionType = connectionType;
            this.SendReceiveOptions = sendReceiveOptions;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
            this.IsDiscoverable = isDiscoverable;
        }

        /// <summary>
        /// Returns a clean string containing the current listener state
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            if (IsListening && LocalListenEndPoint != null)
                return "Listening ("+LocalListenEndPoint.ToString() + ")";
            else
                return "Not Listening";
        }

        /// <summary>
        /// Start listening for incoming connections.
        /// </summary>
        /// <param name="desiredLocalListenEndPoint">Try to start listening on this EndPoint.</param>
        /// <param name="useRandomPortFailOver">If the request EndPoint is unavailable fail over to a random port.</param>
        internal abstract void StartListening(EndPoint desiredLocalListenEndPoint, bool useRandomPortFailOver);

        /// <summary>
        /// Stop listening for incoming connections.
        /// </summary>
        internal abstract void StopListening();
    }
}
