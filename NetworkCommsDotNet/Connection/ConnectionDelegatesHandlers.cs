﻿// 
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
using System.Text;
using System.Net;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.IO;

using System.Net.Sockets;

namespace NetworkCommsDotNet.Connections
{
    public abstract partial class Connection
    {
        /// <summary>
        /// Thread safety locker which is used when accessing <see cref="incomingPacketHandlers"/>, 
        /// <see cref="incomingPacketUnwrappers"/> and <see cref="ConnectionSpecificShutdownDelegate"/>.
        /// </summary>
        private object _syncRoot = new object();

        /// <summary>
        /// Connection level SyncRoot which can be used to perform multiple thread safe operations on this connection
        /// external to NetworkComms.Net
        /// </summary>
        public object SyncRoot { get { return _syncRoot; } }

        /// <summary>
        /// The default <see cref="SendReceiveOptions"/> used for this connection
        /// </summary>
        public SendReceiveOptions ConnectionDefaultSendReceiveOptions { get; protected set; }

        /// <summary>
        /// A multicast function delegate for maintaining connection specific shutdown delegates
        /// </summary>
        private NetworkComms.ConnectionEstablishShutdownDelegate ConnectionSpecificShutdownDelegate { get; set; }

        /// <summary>
        /// By default all incoming objects are handled using ConnectionDefaultSendReceiveOptions. Should the user want something else
        /// those settings are stored here
        /// </summary>
        private Dictionary<string, PacketTypeUnwrapper> incomingPacketUnwrappers = new Dictionary<string, PacketTypeUnwrapper>();

        /// <summary>
        /// A connection specific incoming packet handler dictionary. These are called before any global handlers
        /// </summary>
        private Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>> incomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();

        /// <summary>
        /// Returns the <see cref="SendReceiveOptions"/> to be used for the provided <see cref="PacketHeader"/>. Ensures there 
        /// will not be a serializer or data processor clash for different delegate levels.
        /// </summary>
        /// <param name="header">The <see cref="PacketHeader"/> options are desired.</param>
        /// <returns>The requested <see cref="SendReceiveOptions"/></returns>
        internal SendReceiveOptions IncomingPacketSendReceiveOptions(PacketHeader header)
        {
            //Are there connection specific or global packet handlers?
            bool connectionSpecificHandlers = false;
            lock (_syncRoot) connectionSpecificHandlers = incomingPacketHandlers.ContainsKey(header.PacketType);

            bool globalHandlers = NetworkComms.GlobalIncomingPacketHandlerExists(header.PacketType);

            //Get connection specific options for this packet type, if there aren't any use the connection default options
            SendReceiveOptions connectionSpecificOptions = PacketTypeUnwrapperOptions(header.PacketType);
            if (connectionSpecificOptions == null) connectionSpecificOptions = ConnectionDefaultSendReceiveOptions;

            //Get global options for this packet type, if there aren't any use the global default options
            SendReceiveOptions globalOptions = NetworkComms.GlobalPacketTypeUnwrapperOptions(header.PacketType);
            if (globalOptions == null) globalOptions = NetworkComms.DefaultSendReceiveOptions;

            if (connectionSpecificHandlers && globalHandlers)
            {
                if (!connectionSpecificOptions.OptionsCompatible(globalOptions))
                    throw new PacketHandlerException("Attempted to determine correct sendReceiveOptions for packet of type '" + header.PacketType + "'. Unable to continue as connection specific and global sendReceiveOptions are not equal.");

                //We need to combine options in this case using the connection specific option in preference if both are present
                var combinedOptions = new Dictionary<string, string>(globalOptions.Options);
                
                foreach (var pair in connectionSpecificOptions.Options)
                    combinedOptions[pair.Key] = pair.Value;

                //If the header specifies a serializer and data processors we will auto detect those
                if (header.ContainsOption(PacketHeaderLongItems.SerializerProcessors))
                {
                    DataSerializer serializer;
                    List<DataProcessor> dataProcessors;

                    DPSManager.GetSerializerDataProcessorsFromIdentifier(header.GetOption(PacketHeaderLongItems.SerializerProcessors), out serializer, out dataProcessors);
                    return new SendReceiveOptions(serializer, dataProcessors, combinedOptions);
                }

                //Otherwise we will use options that were specified
                return new SendReceiveOptions(connectionSpecificOptions.DataSerializer, connectionSpecificOptions.DataProcessors, combinedOptions);
            }
            //////////////////////////////////////////////
            //// Changed 20/05/14 to fix bug when encryption options is set on the listener and not the packet handler
            //////////////////////////////////////////////
            else //if (connectionSpecificHandlers)
            {
                //If the header specifies a serializer and data processors we will auto detect those
                if (header.ContainsOption(PacketHeaderLongItems.SerializerProcessors))
                {
                    DataSerializer serializer;
                    List<DataProcessor> dataProcessors;

                    DPSManager.GetSerializerDataProcessorsFromIdentifier(header.GetOption(PacketHeaderLongItems.SerializerProcessors), out serializer, out dataProcessors);
                    return new SendReceiveOptions(serializer, dataProcessors, connectionSpecificOptions.Options);
                }

                return connectionSpecificOptions;
            }
            //else
            //{
            //    //If the header specifies a serializer and data processors we will auto detect those
            //    if (header.ContainsOption(PacketHeaderLongItems.SerializerProcessors))
            //    {
            //        DataSerializer serializer;
            //        List<DataProcessor> dataProcessors;

            //        DPSManager.GetSerializerDataProcessorsFromIdentifier(header.GetOption(PacketHeaderLongItems.SerializerProcessors), out serializer, out dataProcessors);
            //        return new SendReceiveOptions(serializer, dataProcessors, globalOptions.Options);
            //    }

            //    //If just globalHandlers is set (or indeed no handlers at all we just return the global options
            //    return globalOptions;
            //}
        }

        /// <summary>
        /// Gets packet handler wrappers for a given packet type
        /// </summary>
        /// <param name="packetTypeStr">The packet type to get handler wrappers for</param>
        /// <returns>The packet handler wrappers associated with the packet type supplied</returns>
        internal List<IPacketTypeHandlerDelegateWrapper> GetPacketHandlerWrappers(string packetTypeStr)
        {
            List<IPacketTypeHandlerDelegateWrapper> handlers = null;
            if (incomingPacketHandlers.TryGetValue(packetTypeStr, out handlers) && handlers != null)
                return new List<IPacketTypeHandlerDelegateWrapper>(handlers);
            else
                return null; 
        }

        /// <summary>
        /// Trigger connection specific packet delegates with the provided parameters. Returns true if connection specific handlers were executed.
        /// </summary>
        /// <param name="packetHeader">The packetHeader for which all delegates should be triggered with</param>
        /// <param name="returnObject">The deserialised payload object</param>
        /// <returns>Returns true if connection specific handlers were executed.</returns>
        public bool TriggerSpecificPacketHandlers(PacketHeader packetHeader, object returnObject)
        {
            try
            {
                if (packetHeader == null) throw new ArgumentNullException("packetHeader", "Provided PacketHeader cannot not be null.");

                //We take a copy of the handlers list in case it is modified outside of the lock
                List<IPacketTypeHandlerDelegateWrapper> handlersCopy = null;
                lock (_syncRoot)
                    if (incomingPacketHandlers.ContainsKey(packetHeader.PacketType))
                        handlersCopy = new List<IPacketTypeHandlerDelegateWrapper>(incomingPacketHandlers[packetHeader.PacketType]);

                if (handlersCopy == null)
                    //If we have received an unknown packet type we ignore them on this connection specific level and just finish here
                    return false;
                else
                {
                    //Idiot check
                    if (handlersCopy.Count == 0) throw new PacketHandlerException("An entry exists in the packetHandlers list but it contains no elements. This should not be possible.");

                    //Pass the data onto the handler and move on.
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... passing completed data packet to selected connection specific handlers.");

                    //Pass the object to all necessary delegates
                    //We need to use a copy because we may modify the original delegate list during processing
                    foreach (IPacketTypeHandlerDelegateWrapper wrapper in handlersCopy)
                    {
                        try
                        {
                            wrapper.Process(packetHeader, this, returnObject);
                        }
                        catch (Exception ex)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("An unhandled exception was caught while processing a packet handler for a packet type '" + packetHeader.PacketType + "'. Make sure to catch errors in packet handlers. See error log file for more information.");
                            LogTools.LogException(ex, "PacketHandlerErrorSpecific_" + packetHeader.PacketType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("An exception occured in TriggerPacketHandler() for a packet type '" + packetHeader.PacketType + "'. See error log file for more information.");
                LogTools.LogException(ex, "PacketHandlerErrorSpecific_" + packetHeader.PacketType);
            }

            return true;
        }

        /// <summary>
        /// Returns the packet type sendReceiveOptions possibly used to unwrap incoming data. If no specific options are registered returns null
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which the <see cref="SendReceiveOptions"/> are required.</param>
        /// <returns>The requested <see cref="SendReceiveOptions"/> otherwise null</returns>
        public SendReceiveOptions PacketTypeUnwrapperOptions(string packetTypeStr)
        {
            SendReceiveOptions options = null;

            //If we find a global packet unwrapper for this packetType we used those options
            lock (_syncRoot)
            {
                if (incomingPacketUnwrappers.ContainsKey(packetTypeStr))
                    options = incomingPacketUnwrappers[packetTypeStr].Options;
            }

            return options;
        }

        /// <summary>
        /// Append a connection specific packet handler using the connection default SendReceiveOptions
        /// </summary>
        /// <typeparam name="incomingObjectType">The type of incoming object</typeparam>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when a packet of packetTypeStr is received</param>
        public void AppendIncomingPacketHandler<incomingObjectType>(string packetTypeStr, NetworkComms.PacketHandlerCallBackDelegate<incomingObjectType> packetHandlerDelgatePointer)
        {
            AppendIncomingPacketHandler<incomingObjectType>(packetTypeStr, packetHandlerDelgatePointer, ConnectionDefaultSendReceiveOptions);
        }

        /// <summary>
        /// Append a connection specific packet handler
        /// </summary>
        /// <typeparam name="incomingObjectType">The type of incoming object</typeparam>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when a packet of packetTypeStr is received</param>
        /// <param name="options">The <see cref="SendReceiveOptions"/> to be used for the provided packet type</param>
        public void AppendIncomingPacketHandler<incomingObjectType>(string packetTypeStr, NetworkComms.PacketHandlerCallBackDelegate<incomingObjectType> packetHandlerDelgatePointer, SendReceiveOptions options)
        {
            if (packetTypeStr == null) throw new ArgumentNullException("packetTypeStr", "Provided packetType string cannot be null.");
            if (packetHandlerDelgatePointer == null) throw new ArgumentNullException("packetHandlerDelgatePointer", "Provided NetworkComms.PacketHandlerCallBackDelegate<incomingObjectType> cannot be null.");
            if (options == null) throw new ArgumentNullException("options", "Provided SendReceiveOptions cannot be null.");

            //If we are adding a handler for an unmanaged packet type the data serializer must be NullSerializer
            //Checks for unmanaged packet types
            if (packetTypeStr == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
            {
                if (options.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new ArgumentException("Attempted to add packet handler for an unmanaged packet type when the provided send receive options serializer was not NullSerializer.");

                if (options.DataProcessors.Count > 0)
                    throw new ArgumentException("Attempted to add packet handler for an unmanaged packet type when the provided send receive options contains data processors. Data processors may not be used inline with unmanaged packet types.");
            }

            lock (_syncRoot)
            {
                if (incomingPacketUnwrappers.ContainsKey(packetTypeStr))
                {
                    //Make sure if we already have an existing entry that it matches with the provided
                    if (!incomingPacketUnwrappers[packetTypeStr].Options.OptionsCompatible(options))
                        throw new PacketHandlerException("The provided SendReceiveOptions are not compatible with existing SendReceiveOptions already specified for this packetTypeStr.");
                }
                else
                    incomingPacketUnwrappers.Add(packetTypeStr, new PacketTypeUnwrapper(packetTypeStr, options));             

                //Ad the handler to the list
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Make sure we avoid duplicates
                    PacketTypeHandlerDelegateWrapper<incomingObjectType> toCompareDelegate = new PacketTypeHandlerDelegateWrapper<incomingObjectType>(packetHandlerDelgatePointer);
                    bool delegateAlreadyExists = false;
                    foreach (var handler in incomingPacketHandlers[packetTypeStr])
                    {
                        if (handler == toCompareDelegate)
                        {
                            delegateAlreadyExists = true;
                            break;
                        }
                    }
                        
                    if (delegateAlreadyExists)
                        throw new PacketHandlerException("This specific packet handler delegate already exists for the provided packetTypeStr.");

                    incomingPacketHandlers[packetTypeStr].Add(new PacketTypeHandlerDelegateWrapper<incomingObjectType>(packetHandlerDelgatePointer));
                }
                else
                    incomingPacketHandlers.Add(packetTypeStr, new List<IPacketTypeHandlerDelegateWrapper>() { new PacketTypeHandlerDelegateWrapper<incomingObjectType>(packetHandlerDelgatePointer) });

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Added connection specific incoming packetHandler for '" + packetTypeStr + "' packetType with " + ConnectionInfo);
            }
        }

        /// <summary>
        /// Append a connection specific packet handler which has already been wrapped by IPacketTypeHandlerDelegateWrapper
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgateWrapper">The IPacketTypeHandlerDelegateWrapper to be executed when a packet of packetTypeStr is received</param>
        /// <param name="options">The <see cref="SendReceiveOptions"/> to be used for the provided packet type</param>
        internal void AppendIncomingPacketHandler(string packetTypeStr, IPacketTypeHandlerDelegateWrapper packetHandlerDelgateWrapper, SendReceiveOptions options)
        {
            if (packetTypeStr == null) throw new ArgumentNullException("packetTypeStr", "Provided packetType string cannot be null.");
            if (packetHandlerDelgateWrapper == null) throw new ArgumentNullException("packetHandlerDelgateWrapper", "Provided packetHandlerDelgateWrapper cannot be null.");
            if (options == null) throw new ArgumentNullException("options", "Provided SendReceiveOptions cannot be null.");

            //If we are adding a handler for an unmanaged packet type the data serializer must be NullSerializer
            //Checks for unmanaged packet types
            if (packetTypeStr == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
            {
                if (options.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new ArgumentException("Attempted to add packet handler for an unmanaged packet type when the provided send receive options serializer was not NullSerializer.");

                if (options.DataProcessors.Count > 0)
                    throw new ArgumentException("Attempted to add packet handler for an unmanaged packet type when the provided send receive options contains data processors. Data processors may not be used inline with unmanaged packet types.");
            }

            lock (_syncRoot)
            {
                if (incomingPacketUnwrappers.ContainsKey(packetTypeStr))
                {
                    //Make sure if we already have an existing entry that it matches with the provided
                    if (!incomingPacketUnwrappers[packetTypeStr].Options.OptionsCompatible(options))
                        throw new PacketHandlerException("The provided SendReceiveOptions are not compatible with existing SendReceiveOptions already specified for this packetTypeStr.");
                }
                else
                    incomingPacketUnwrappers.Add(packetTypeStr, new PacketTypeUnwrapper(packetTypeStr, options));

                //Ad the handler to the list
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Make sure we avoid duplicates
                    bool delegateAlreadyExists = false;
                    foreach (var handler in incomingPacketHandlers[packetTypeStr])
                    {
                        if (handler == packetHandlerDelgateWrapper)
                        {
                            delegateAlreadyExists = true;
                            break;
                        }
                    }

                    if (delegateAlreadyExists)
                        throw new PacketHandlerException("This specific packet handler delegate already exists for the provided packetTypeStr.");

                    incomingPacketHandlers[packetTypeStr].Add(packetHandlerDelgateWrapper);
                }
                else
                    incomingPacketHandlers.Add(packetTypeStr, new List<IPacketTypeHandlerDelegateWrapper>() { packetHandlerDelgateWrapper });

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Added connection specific incoming packetHandler for '" + packetTypeStr + "' packetType with " + ConnectionInfo);
            }
        }

        /// <summary>
        /// Append a connection specific unmanaged packet handler
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when an unmanaged packet is received</param>
        public void AppendIncomingUnmanagedPacketHandler(NetworkComms.PacketHandlerCallBackDelegate<byte[]> packetHandlerDelgatePointer)
        {
            AppendIncomingPacketHandler<byte[]>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged),packetHandlerDelgatePointer,new SendReceiveOptions<NullSerializer>());
        }

        /// <summary>
        /// Returns true if an unmanaged packet handler exists on this connection
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which to check incoming packet handlers</param>
        /// <returns>True if a packet handler exists</returns>
        public bool IncomingPacketHandlerExists(string packetTypeStr)
        {
            lock (_syncRoot)
                return incomingPacketHandlers.ContainsKey(packetTypeStr);
        }

        /// <summary>
        /// Returns true if a connection specific unmanaged packet handler exists, on this connection.
        /// </summary>
        /// <returns>True if an unmanaged packet handler exists</returns>
        public bool IncomingUnmanagedPacketHandlerExists()
        {
            lock (_syncRoot)
                return incomingPacketHandlers.ContainsKey(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged));
        }

        /// <summary>
        /// Returns true if the provided connection specific packet handler has been added for the provided packet type, on this connection.
        /// </summary>
        /// <param name="packetTypeStr">The packet type within which to check packet handlers</param>
        /// <param name="packetHandlerDelgatePointer">The packet handler to look for</param>
        /// <returns>True if a connection specific packet handler exists for the provided packetType</returns>
        public bool IncomingPacketHandlerExists(string packetTypeStr, Delegate packetHandlerDelgatePointer)
        {
            lock (_syncRoot)
            {
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    foreach (var handler in incomingPacketHandlers[packetTypeStr])
                        if (handler.EqualsDelegate(packetHandlerDelgatePointer))
                            return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the provided connection specific unmanaged packet handler has been added, on this connection.
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The packet handler to look for</param>
        /// <returns>True if a connection specific unmanaged packet handler exists</returns>
        public bool IncomingUnmanagedPacketHandlerExists(Delegate packetHandlerDelgatePointer)
        {
            lock (_syncRoot)
            {
                if (incomingPacketHandlers.ContainsKey(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged)))
                {
                    foreach (var handler in incomingPacketHandlers[Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged)])
                        if (handler.EqualsDelegate(packetHandlerDelgatePointer))
                            return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Remove the provided connection specific packet handler for the specified packet type, on this connection.
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which this delegate should be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public void RemoveIncomingPacketHandler(string packetTypeStr, Delegate packetHandlerDelgatePointer)
        {
            lock (_syncRoot)
            {
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Remove any instances of this handler from the delegates
                    //The bonus here is if the delegate has not been added we continue quite happily
                    IPacketTypeHandlerDelegateWrapper toRemove = null;

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

                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed a connection specific packetHandler for '" + packetTypeStr + "' packetType. No handlers remain with " + ConnectionInfo);
                    }
                    else
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed a connection specific packetHandler for '" + packetTypeStr + "' packetType. Handlers remain with " + ConnectionInfo);
                }
            }
        }

        /// <summary>
        /// Remove the provided connection specific unmanaged packet handler, on this connection.
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public void RemoveIncomingUnmanagedPacketHandler(Delegate packetHandlerDelgatePointer)
        {
            RemoveIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetHandlerDelgatePointer);
        }

        /// <summary>
        /// Removes all connection specific packet handlers for the provided packet type, on this connection.
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which all delegates should be removed</param>
        public void RemoveIncomingPacketHandler(string packetTypeStr)
        {
            lock (_syncRoot)
            {
                //We don't need to check for potentially removing a critical reserved packet handler here because those cannot be removed.
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    incomingPacketHandlers.Remove(packetTypeStr);

                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed all connection specific incoming packetHandlers for '" + packetTypeStr + "' packetType with " + ConnectionInfo);
                }
            }
        }

        /// <summary>
        /// Removes all unmanaged packet handlers, on this connection.
        /// </summary>
        public void RemoveIncomingUnmanagedPacketHandler()
        {
            RemoveIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged));
        }

        /// <summary>
        /// Removes all packet handlers for all packet types, on this connection.
        /// </summary>
        public void RemoveIncomingPacketHandler()
        {
            lock (_syncRoot)
            {
                incomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed all connection specific incoming packetHandlers for all packetTypes with " + ConnectionInfo);
            }
        }

        /// <summary>
        /// Add a connection specific shutdown delegate
        /// </summary>
        /// <param name="handlerToAppend">The delegate to call when a connection is shutdown</param>
        public void AppendShutdownHandler(NetworkComms.ConnectionEstablishShutdownDelegate handlerToAppend)
        {
            lock (_syncRoot)
            {
                if (ConnectionSpecificShutdownDelegate == null)
                    ConnectionSpecificShutdownDelegate = handlerToAppend;
                else
                    ConnectionSpecificShutdownDelegate += handlerToAppend;

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Added a connection specific shutdown delegate to connection with " + ConnectionInfo);
            }
        }

        /// <summary>
        /// Remove a connection specific shutdown delegate.
        /// </summary>
        /// <param name="handlerToRemove">The delegate to remove for shutdown events</param>
        public void RemoveShutdownHandler(NetworkComms.ConnectionEstablishShutdownDelegate handlerToRemove)
        {
            lock (_syncRoot)
            {
                ConnectionSpecificShutdownDelegate -= handlerToRemove;
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Removed ConnectionSpecificShutdownDelegate to connection with " + ConnectionInfo);

                if (ConnectionSpecificShutdownDelegate == null)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("No handlers remain for ConnectionSpecificShutdownDelegate with " + ConnectionInfo);
                }
                else
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Handlers remain for ConnectionSpecificShutdownDelegate with " + ConnectionInfo);
                }
            }
        }
    }
}
