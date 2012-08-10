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
        /// Trigger all packet type delegates with the provided parameters. Providing options will override any defaults.
        /// </summary>
        /// <param name="packetHeader">The packetHeader for which all delegates should be triggered with</param>
        /// <param name="connection">The source connectionInfo</param>
        /// <param name="incomingObjectBytes">The serialised and or compressed bytes to be used</param>
        /// <param name="options">The incoming sendReceiveOptions to use overriding defaults</param>
        /// <returns>Returns true if connection specific handlers were executed.</returns>
        public bool TriggerPacketHandler(PacketHeader packetHeader, Connection connection, byte[] incomingObjectBytes, SendReceiveOptions options)
        {
            try
            {
                //We take a copy of the handlers list incase it is modified outside of the lock
                List<NetworkComms.IPacketTypeHandlerDelegateWrapper> handlersCopy = null;
                lock (delegateLocker)
                    if (incomingPacketHandlers.ContainsKey(packetHeader.PacketType))
                        handlersCopy = new List<NetworkComms.IPacketTypeHandlerDelegateWrapper>(incomingPacketHandlers[packetHeader.PacketType]);

                if (handlersCopy == null)
                    //If we have received an unknown packet type we ignore them on this connection specific level and just finish here
                    return false;
                else
                {
                    //Idiot check
                    if (handlersCopy.Count == 0)
                        throw new PacketHandlerException("An entry exists in the packetHandlers list but it contains no elements. This should not be possible.");

                    //If we find a global packet unwrapper for this packetType we used those options
                    lock (delegateLocker)
                    {
                        if (incomingPacketUnwrappers.ContainsKey(packetHeader.PacketType))
                            options = incomingPacketUnwrappers[packetHeader.PacketType].Options;
                    }

                    if (options == null) options = ConnectionDefaultSendReceiveOptions;

                    //Deserialise the object only once
                    object returnObject = handlersCopy[0].DeSerialize(incomingObjectBytes, options);

                    //Pass the data onto the handler and move on.
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... passing completed data packet to selected handlers.");

                    //Pass the object to all necessary delgates
                    //We need to use a copy because we may modify the original delegate list during processing
                    foreach (NetworkComms.IPacketTypeHandlerDelegateWrapper wrapper in handlersCopy)
                    {
                        try
                        {
                            wrapper.Process(packetHeader, connection, returnObject);
                        }
                        catch (Exception ex)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An unhandled exception was caught while processing a packet handler for a packet type '" + packetHeader.PacketType + "'. Make sure to catch errors in packet handlers. See error log file for more information.");
                            NetworkComms.LogError(ex, "PacketHandlerErrorSpecific_" + packetHeader.PacketType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An exception occured in TriggerPacketHandler() for a packet type '" + packetHeader.PacketType + "'. See error log file for more information.");
                NetworkComms.LogError(ex, "PacketHandlerErrorSpecific_" + packetHeader.PacketType);
            }

            return true;
        }

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
            lock (delegateLocker)
            {
                //Add the custom serializer and compressor if necessary
                if (options.Serializer != null && options.Compressor != null)
                {
                    if (incomingPacketUnwrappers.ContainsKey(packetTypeStr))
                    {
                        //Make sure if we already have an existing entry that it matches with the provided
                        if (incomingPacketUnwrappers[packetTypeStr].Options != options)
                            throw new PacketHandlerException("You cannot specify a different compressor or serializer instance if one has already been specified for this packetTypeStr.");
                    }
                    else
                        incomingPacketUnwrappers.Add(packetTypeStr, new NetworkComms.PacketTypeUnwrapper(packetTypeStr, options));
                }
                else if (options.Serializer != null ^ options.Compressor != null)
                    throw new PacketHandlerException("You must provide both serializer and compressor or neither.");
                else
                {
                    //If we have not specified the serialiser and compressor we assume to be using defaults
                    //If a handler has already been added for this type and has specified specific serialiser and compressor then so should this call to AppendIncomingPacketHandler
                    if (incomingPacketUnwrappers.ContainsKey(packetTypeStr))
                        throw new PacketHandlerException("A handler already exists for this packetTypeStr with specific serializer and compressor instances. Please ensure the same instances are provided in this call to AppendPacketHandler.");
                }

                //Ad the handler to the list
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Make sure we avoid duplicates
                    NetworkComms.PacketTypeHandlerDelegateWrapper<T> toCompareDelegate = new NetworkComms.PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer);
                    bool delegateAlreadyExists = (from current in incomingPacketHandlers[packetTypeStr] where current == toCompareDelegate select current).Count() > 0;
                    if (delegateAlreadyExists)
                        throw new PacketHandlerException("This specific packet handler delegate already exists for the provided packetTypeStr.");

                    incomingPacketHandlers[packetTypeStr].Add(new NetworkComms.PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer));
                }
                else
                    incomingPacketHandlers.Add(packetTypeStr, new List<NetworkComms.IPacketTypeHandlerDelegateWrapper>() { new NetworkComms.PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer) });

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Added connection specific incoming packetHandler for '" + packetTypeStr + "' packetType with " + ConnectionInfo);
            }
        }

        /// <summary>
        /// Removes the provided delegate for the specified packet type
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public void RemoveIncomingPacketHandler(string packetTypeStr, Delegate packetHandlerDelgatePointer)
        {
            lock (delegateLocker)
            {
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Remove any instances of this handler from the delegates
                    //The bonus here is if the delegate has not been added we continue quite happily
                    NetworkComms.IPacketTypeHandlerDelegateWrapper toRemove = null;

                    foreach (var handler in incomingPacketHandlers[packetTypeStr])
                    {
                        if (handler.EqualsDelegate(packetHandlerDelgatePointer))
                        {
                            toRemove = handler;
                            break;
                        }
                    }

                    if (toRemove != null)
                        incomingPacketHandlers[packetTypeStr].Remove(toRemove);

                    if (incomingPacketHandlers[packetTypeStr] == null || incomingPacketHandlers[packetTypeStr].Count == 0)
                    {
                        incomingPacketHandlers.Remove(packetTypeStr);

                        //Remove any entries in the unwrappers dict as well as we are done with this packetTypeStr
                        if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                            incomingPacketHandlers.Remove(packetTypeStr);

                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Removed a connection specific packetHandler for '" + packetTypeStr + "' packetType. No handlers remain with " + ConnectionInfo);
                    }
                    else
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Removed a connection specific packetHandler for '" + packetTypeStr + "' packetType. Handlers remain with " + ConnectionInfo);
                }
            }
        }

        /// <summary>
        /// Removes all delegates for the provided packet type
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which all delegates should be removed</param>
        public void RemoveAllPacketHandlers(string packetTypeStr)
        {
            lock (delegateLocker)
            {
                //We don't need to check for potentially removing a critical reserved packet handler here because those cannot be removed.
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    incomingPacketHandlers.Remove(packetTypeStr);

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Removed all connection specific incoming packetHandlers for '" + packetTypeStr + "' packetType with " + ConnectionInfo);
                }
            }
        }

        /// <summary>
        /// Removes all delegates for all packet types
        /// </summary>
        public void RemoveAllPacketHandlers()
        {
            lock (delegateLocker)
            {
                incomingPacketHandlers = new Dictionary<string, List<NetworkComms.IPacketTypeHandlerDelegateWrapper>>();

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Removed all connection specific incoming packetHandlers for all packetTypes with " + ConnectionInfo);
            }
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

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Added a connection specific shutdown delegate to connection with " + ConnectionInfo);
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
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Removed ConnectionSpecificShutdownDelegate to connection with " + ConnectionInfo);

                if (ConnectionSpecificShutdownDelegate == null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("No handlers remain for ConnectionSpecificShutdownDelegate with " + ConnectionInfo);
                }
                else
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Handlers remain for ConnectionSpecificShutdownDelegate with " + ConnectionInfo);
                }
            }
        }
    }
}
