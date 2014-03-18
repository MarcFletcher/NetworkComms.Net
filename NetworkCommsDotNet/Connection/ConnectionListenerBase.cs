//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;

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
        #region Public Properties
        /// <summary>
        /// The send receive options associated with this listener.
        /// </summary>
        public SendReceiveOptions ListenerDefaultSendReceiveOptions { get; protected set; }

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
        /// True if this listener will be advertised via peer discovery
        /// </summary>
        public bool IsDiscoverable { get; protected set; }

        /// <summary>
        /// The local IPEndPoint that this listener is associated with.
        /// </summary>
        public EndPoint LocalListenEndPoint { get; protected set; }
        #endregion

        #region Private Properties
        /// <summary>
        /// Thread safety locker which is used when accessing <see cref="incomingPacketHandlers"/>
        /// and <see cref="incomingPacketUnwrappers"/>
        /// </summary>
        private object delegateLocker = new object();

        /// <summary>
        /// By default all incoming objects are handled using ListenerDefaultSendReceiveOptions. Should the user want something else
        /// those settings are stored here
        /// </summary>
        private Dictionary<string, PacketTypeUnwrapper> incomingPacketUnwrappers = new Dictionary<string, PacketTypeUnwrapper>();

        /// <summary>
        /// A listener specific incoming packet handler dictionary. These are called before any global handlers
        /// </summary>
        private Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>> incomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();
        #endregion

        /// <summary>
        /// Create a new listener instance
        /// </summary>
        /// <param name="connectionType">The connection type to listen for.</param>
        /// <param name="sendReceiveOptions">The send receive options to use for this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="allowDiscoverable">Determines if the newly created <see cref="ConnectionListenerBase"/> will be discoverable if <see cref="Tools.PeerDiscovery"/> is enabled.</param>
        protected ConnectionListenerBase(ConnectionType connectionType,
            SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol,
            bool allowDiscoverable)
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

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Created new connection listener (" + connectionType.ToString() + "-" + (applicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? "E" : "D") + ").");

            this.ConnectionType = connectionType;
            this.ListenerDefaultSendReceiveOptions = sendReceiveOptions;
            this.ApplicationLayerProtocol = applicationLayerProtocol;
            this.IsDiscoverable = allowDiscoverable;
        }

        /// <summary>
        /// Returns a clean string containing the current listener state
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string returnString = "[" + ConnectionType.ToString() + "-" + (ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? "E" : "D") + "] ";

            if (IsListening && LocalListenEndPoint != null)
                return returnString + "Listening - " + LocalListenEndPoint.ToString();
            else
                return returnString + "Not Listening";
        }

        #region Start and Stop Listening
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
        #endregion

        #region Listener Specific Packet Handlers
        /// <summary>
        /// Append a listener specific packet handler using the listener default SendReceiveOptions
        /// </summary>
        /// <typeparam name="incomingObjectType">The type of incoming object</typeparam>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when a packet of packetTypeStr is received</param>
        public void AppendIncomingPacketHandler<incomingObjectType>(string packetTypeStr, NetworkComms.PacketHandlerCallBackDelegate<incomingObjectType> packetHandlerDelgatePointer)
        {
            AppendIncomingPacketHandler<incomingObjectType>(packetTypeStr, packetHandlerDelgatePointer, ListenerDefaultSendReceiveOptions);
        }

        /// <summary>
        /// Append a listener specific packet handler
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

            lock (delegateLocker)
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

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Added listener specific incoming packetHandler for '" + packetTypeStr + "' packetType on listener " + ToString());
            }
        }

        /// <summary>
        /// Append a listener specific unmanaged packet handler
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when an unmanaged packet is received</param>
        public void AppendIncomingUnmanagedPacketHandler(NetworkComms.PacketHandlerCallBackDelegate<byte[]> packetHandlerDelgatePointer)
        {
            AppendIncomingPacketHandler<byte[]>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetHandlerDelgatePointer, new SendReceiveOptions<NullSerializer>());
        }

        /// <summary>
        /// Returns true if an unmanaged packet handler exists on this listener
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which to check incoming packet handlers</param>
        /// <returns>True if a packet handler exists</returns>
        public bool IncomingPacketHandlerExists(string packetTypeStr)
        {
            lock (delegateLocker)
                return incomingPacketHandlers.ContainsKey(packetTypeStr);
        }

        /// <summary>
        /// Returns true if a listener specific unmanaged packet handler exists, on this listener.
        /// </summary>
        /// <returns>True if an unmanaged packet handler exists</returns>
        public bool IncomingUnmanagedPacketHandlerExists()
        {
            lock (delegateLocker)
                return incomingPacketHandlers.ContainsKey(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged));
        }

        /// <summary>
        /// Returns true if the provided listener specific packet handler has been added for the provided packet type, on this listener.
        /// </summary>
        /// <param name="packetTypeStr">The packet type within which to check packet handlers</param>
        /// <param name="packetHandlerDelgatePointer">The packet handler to look for</param>
        /// <returns>True if a listener specific packet handler exists for the provided packetType</returns>
        public bool IncomingPacketHandlerExists(string packetTypeStr, Delegate packetHandlerDelgatePointer)
        {
            lock (delegateLocker)
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
        /// Returns true if the provided listener specific unmanaged packet handler has been added, on this listener.
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The packet handler to look for</param>
        /// <returns>True if a listener specific unmanaged packet handler exists</returns>
        public bool IncomingUnmanagedPacketHandlerExists(Delegate packetHandlerDelgatePointer)
        {
            lock (delegateLocker)
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
        /// Remove the provided listener specific packet handler for the specified packet type, on this listener.
        /// </summary>
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

                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed a listener specific packetHandler for '" + packetTypeStr + "' packetType. No handlers remain on listener " + ToString());
                    }
                    else
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed a listener specific packetHandler for '" + packetTypeStr + "' packetType. Handlers remain on listener " + ToString());
                }
            }
        }

        /// <summary>
        /// Remove the provided listener specific unmanaged packet handler, on this listener.
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public void RemoveIncomingUnmanagedPacketHandler(Delegate packetHandlerDelgatePointer)
        {
            RemoveIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetHandlerDelgatePointer);
        }

        /// <summary>
        /// Removes all listener specific packet handlers for the provided packet type, on this listener.
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which all delegates should be removed</param>
        public void RemoveIncomingPacketHandler(string packetTypeStr)
        {
            lock (delegateLocker)
            {
                //We don't need to check for potentially removing a critical reserved packet handler here because those cannot be removed.
                if (incomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    incomingPacketHandlers.Remove(packetTypeStr);

                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed all listener specific incoming packetHandlers for '" + packetTypeStr + "' packetType on listener " + ToString());
                }
            }
        }

        /// <summary>
        /// Removes all unmanaged packet handlers, on this listener.
        /// </summary>
        public void RemoveIncomingUnmanagedPacketHandler()
        {
            RemoveIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged));
        }

        /// <summary>
        /// Removes all packet handlers for all packet types, on this listener.
        /// </summary>
        public void RemoveIncomingPacketHandler()
        {
            lock (delegateLocker)
            {
                incomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Removed all listener specific incoming packetHandlers for all packetTypes on listener " + ToString());
            }
        }

        /// <summary>
        /// Add all listener specific packet handlers to the provided connection
        /// </summary>
        /// <param name="connection">The connection to which packet handlers should be added</param>
        internal void AddListenerPacketHandlersToConnection(Connection connection)
        {
            lock (delegateLocker)
            {
                foreach (string packetType in incomingPacketHandlers.Keys)
                {
                    foreach (IPacketTypeHandlerDelegateWrapper handler in incomingPacketHandlers[packetType])
                        connection.AppendIncomingPacketHandler(packetType, handler, incomingPacketUnwrappers[packetType].Options);
                }
            }

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Appended connection specific packet handlers from listener '" + ToString() + "' to connection '" + connection.ToString() + "'.");
        }
        #endregion
    }
}
