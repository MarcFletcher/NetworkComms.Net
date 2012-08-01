//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SerializerBase;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// NetworkComms maintains a top level Connection object for shared methods
    /// </summary>
    public abstract partial class Connection
    {
        protected object delegateLocker = new object();

        /// <summary>
        /// The default send receive options used for this connection
        /// </summary>
        public SendReceiveOptions ConnectionDefaultSendReceiveOptions { get; protected set; }

        /// <summary>
        /// A multicast function delegate for maintaining connection specific shutdown delegates
        /// </summary>
        protected NetworkComms.ConnectionEstablishShutdownDelegate ConnectionSpecificShutdownDelegate { get; set; }

        /// <summary>
        /// By default all incoming objects are serialised and compressed by DefaultSerializer and DefaultCompressor. Should the user want something else
        /// those settings are stored here
        /// </summary>
        protected Dictionary<string, NetworkComms.PacketTypeUnwrapper> incomingPacketUnwrappers = new Dictionary<string, NetworkComms.PacketTypeUnwrapper>();

        /// <summary>
        /// A connection specific incoming packet handler dictionary. These are called before any applicable global handlers
        /// </summary>
        protected Dictionary<string, List<NetworkComms.IPacketTypeHandlerDelegateWrapper>> incomingPacketHandlers = new Dictionary<string, List<NetworkComms.IPacketTypeHandlerDelegateWrapper>>();

        /// <summary>
        /// Append a connection specific packet handler
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="packetTypeStr"></param>
        /// <param name="packetHandlerDelgatePointer"></param>
        /// <param name="packetTypeStrSerializer"></param>
        /// <param name="packetTypeStrCompressor"></param>
        /// <param name="enableAutoListen"></param>
        public void AppendIncomingPacketHandler<T>(string packetTypeStr, NetworkComms.PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer, SendReceiveOptions options, bool enableAutoListen = true)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes the provided delegate for the specified packet type
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public void RemoveIncomingPacketHandler(string packetTypeStr, Delegate packetHandlerDelgatePointer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all delegates for the provided packet type
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which all delegates should be removed</param>
        public void RemoveAllPacketHandlers(string packetTypeStr)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all delegates for all packet types
        /// </summary>
        public void RemoveAllPacketHandlers()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Trigger all packet type delegates with the provided parameters. Providing serializer and compressor will override any defaults.
        /// </summary>
        /// <param name="packetHeader">Packet type for which all delegates should be triggered</param>
        /// <param name="sourceConnectionId">The source connection id</param>
        /// <param name="incomingObjectBytes">The serialised and or compressed bytes to be used</param>
        /// <param name="serializer">Override serializer</param>
        /// <param name="compressor">Override compressor</param>
        public static void TriggerPacketHandler(PacketHeader packetHeader, ConnectionInfo connectionInfo, byte[] incomingObjectBytes, SendReceiveOptions options)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Add a connection specific shutdown delegate
        /// </summary>
        /// <param name="handlerToAppend"></param>
        public void AppendShutdownHandler(NetworkComms.ConnectionEstablishShutdownDelegate handlerToAppend)
        {
            lock (delegateLocker)
            {
                if (ConnectionSpecificShutdownDelegate == null)
                    ConnectionSpecificShutdownDelegate = handlerToAppend;
                else
                    ConnectionSpecificShutdownDelegate += handlerToAppend;

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Added connection specific shutdown delegate to connection with id " + (!ConnectionInfo.ConnectionEstablished ? "NA" : this.ConnectionInfo.NetworkIdentifier.ToString()));
            }
        }

        /// <summary>
        /// Remove a connection specific shutdown delegate.
        /// </summary>
        /// <param name="handlerToRemove"></param>
        public void RemoveShutdownHandler(NetworkComms.ConnectionEstablishShutdownDelegate handlerToRemove)
        {
            lock (delegateLocker)
            {
                ConnectionSpecificShutdownDelegate -= handlerToRemove;
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Removed connection specific shutdown delegate to connection with id " + (!ConnectionInfo.ConnectionEstablished ? "NA" : this.ConnectionInfo.NetworkIdentifier.ToString()));
            }
        }
    }
}
