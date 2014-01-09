//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
using DPSBase;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A base class that the listener of each connection type inherts from.
    /// This allows us to manage listeners at the general connection level.
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
        /// The local IPEndPoint that this listener is associated with.
        /// </summary>
        public EndPoint LocalListenEndPoint { get; protected set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="connectionType">The connection type to listen for.</param>
        /// <param name="sendReceiveOptions">The send receive options to use for this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide usefull features such as inline serialisation, 
        /// transparent packet tranmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        public ConnectionListenerBase(ConnectionType connectionType,
            SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol)
        {
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined is not valid when calling this method.", "connectionType");
            if (sendReceiveOptions == null) throw new ArgumentNullException("sendReceiveOptions", "Provided send receive option may not be null.");
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined) throw new ArgumentException("ApplicationLayerProtocolStatus.Undefined is not valid when calling this method.", "applicationLayerProtocol");

            //Validate SRO options if the application layer protocol is disabled
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
            {
                if (sendReceiveOptions.Options.ContainsKey("ReceiveConfirmationRequired"))
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options specified the ReceiveConfirmationRequired option. Please provide compatible send receive options in order to succesfully" +
                        " instantiate this unmanaged connection.", "defaultSendReceiveOptions");

                if (sendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options serializer was not NullSerializer. Please provide compatible send receive options in order to succesfully" +
                        " instantiate this unmanaged connection.", "defaultSendReceiveOptions");

                if (sendReceiveOptions.DataProcessors.Count > 0)
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options contains data processors. Data processors may not be used with unmanaged connections." +
                        " Please provide compatible send receive options in order to succesfully instantiate this unmanaged connection.", "defaultSendReceiveOptions");
            }

            this.ConnectionType = connectionType;
            this.SendReceiveOptions = sendReceiveOptions;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
        }

        /// <summary>
        /// If listening returns the local listen IPEndPoint.
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
        /// Start listening
        /// </summary>
        /// <param name="desiredLocalListenEndPoint">Try to start listening on this IPEndPoint</param>
        /// <param name="useRandomPortFailOver">If the request EndPoint.Port is unavailable fail over to a random port</param>
        internal abstract void StartListening(EndPoint desiredLocalListenEndPoint, bool useRandomPortFailOver);

        /// <summary>
        /// Stop listening
        /// </summary>
        internal abstract void StopListening();
    }
}
