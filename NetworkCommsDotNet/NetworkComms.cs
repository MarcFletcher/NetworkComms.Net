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
using System.Text;
using System.Net;
using System.Threading;
using DPSBase;
using System.Collections;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.IO;

#if NETFX_CORE
using NetworkCommsDotNet.XPlatformHelper;
using System.Threading.Tasks;
using Windows.Storage;
#else
using System.Net.Sockets;
#endif

#if !NO_LOGGING
using NLog;
using NLog.Config;
#endif

#if NET35 || NET4
using InTheHand.Net;
#endif

//Assembly marked as CLSCompliant
[assembly: CLSCompliant(true)]

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Top level interface for NetworkComms.Net library. Anything which is not connection specific generally happens 
    /// within the NetworkComms class. e.g. Keeping track of all connections, global defaults and settings, serialisers 
    /// and data processors etc.
    /// </summary>
    public static class NetworkComms
    {
        /// <summary>
        /// Static constructor which sets comm default values
        /// </summary>
        static NetworkComms()
        {
            //NetworkComms.Net base defaults are defined here
            NetworkIdentifier = ShortGuid.NewGuid();

            CheckSumMismatchSentPacketCacheMaxByteLimit = 75000;
            MinimumSentPacketCacheTimeMinutes = 1;

            ConnectionEstablishTimeoutMS = 10000;
            PacketConfirmationTimeoutMS = 5000;
            ConnectionAliveTestTimeoutMS = 1000;

            //Initialise the reserved packet type dictionary
            //this is faster than enumerating Enum.GetNames(typeof(ReservedPacketType)) every time
            ReservedPacketTypeNames = new Dictionary<string, string>();
            foreach(string reservedPacketTypeName in Enum.GetNames(typeof(ReservedPacketType)))
                ReservedPacketTypeNames.Add(reservedPacketTypeName, "");

#if NETFX_CORE
            CurrentRuntimeEnvironment = RuntimeEnvironment.Windows_RT;
            SendBufferSizeBytes = ReceiveBufferSizeBytes = 8000;
#elif SILVERLIGHT || WINDOWS_PHONE
            CurrentRuntimeEnvironment = RuntimeEnvironment.WindowsPhone_Silverlight;
            SendBufferSizeBytes = ReceiveBufferSizeBytes = 8000;
#elif iOS
            CurrentRuntimeEnvironment = RuntimeEnvironment.Xamarin_iOS;
            SendBufferSizeBytes = ReceiveBufferSizeBytes = 8000;
#elif ANDROID
            CurrentRuntimeEnvironment = RuntimeEnvironment.Xamarin_Android;
            SendBufferSizeBytes = ReceiveBufferSizeBytes = 8000;
#elif NET2
            if (Type.GetType("Mono.Runtime") != null)
            {
                CurrentRuntimeEnvironment = RuntimeEnvironment.Mono_Net2;
                //Mono send buffer smaller as different large object heap limit
                SendBufferSizeBytes = ReceiveBufferSizeBytes = 8000;
            }
            else
            {
                CurrentRuntimeEnvironment = RuntimeEnvironment.Native_Net2;
                SendBufferSizeBytes = ReceiveBufferSizeBytes = 80000;
            }
#elif NET35
            if (Type.GetType("Mono.Runtime") != null)
            {
                CurrentRuntimeEnvironment = RuntimeEnvironment.Mono_Net35;
                //Mono send buffer smaller as different large object heap limit
                SendBufferSizeBytes = ReceiveBufferSizeBytes = 8000;
            }
            else
            {
                CurrentRuntimeEnvironment = RuntimeEnvironment.Native_Net35;
                SendBufferSizeBytes = ReceiveBufferSizeBytes = 80000;
            }
#else
            if (Type.GetType("Mono.Runtime") != null)
            {
                CurrentRuntimeEnvironment = RuntimeEnvironment.Mono_Net4;
                //Mono send buffer smaller as different large object heap limit
                SendBufferSizeBytes = ReceiveBufferSizeBytes = 8000;
            }
            else
            {
                CurrentRuntimeEnvironment = RuntimeEnvironment.Native_Net4;
                SendBufferSizeBytes = ReceiveBufferSizeBytes = 80000;
            }
#endif

            //We want to instantiate our own thread pool here
#if NETFX_CORE
            CommsThreadPool = new CommsThreadPool();
#else
            CommsThreadPool = new CommsThreadPool(1, Environment.ProcessorCount*2, Environment.ProcessorCount * 20, new TimeSpan(0, 0, 10));
#endif

            //Initialise the core extensions
            DPSManager.AddDataSerializer<ProtobufSerializer>();

            DPSManager.AddDataSerializer<NullSerializer>();
            DPSManager.AddDataProcessor<SevenZipLZMACompressor.LZMACompressor>();
            DPSManager.GetDataProcessor<DataPadder>();

#if !FREETRIAL
            //Only the full version includes the encrypter
            DPSManager.AddDataProcessor<RijndaelPSKEncrypter>();
#endif

#if !WINDOWS_PHONE && !NETFX_CORE
            DPSManager.AddDataSerializer<BinaryFormaterSerializer>();
#endif

            InternalFixedSendReceiveOptions = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                new List<DataProcessor>(),
                new Dictionary<string, string>());

            DefaultSendReceiveOptions = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                new List<DataProcessor>() { DPSManager.GetDataProcessor<SevenZipLZMACompressor.LZMACompressor>() },
                new Dictionary<string, string>());
        }

        #region NetworkComms.Net Instance Information
        /// <summary>
        /// The local identifier for this instance of NetworkCommsDotNet. This is an application unique identifier.
        /// </summary>
        public static ShortGuid NetworkIdentifier { get; private set; }

        /// <summary>
        /// The current runtime environment. Detected automatically on start up. Performance may be adversely affected if this is changed.
        /// </summary>
        public static RuntimeEnvironment CurrentRuntimeEnvironment { get; set; }

        /// <summary>
        /// An internal random object
        /// </summary>
        internal static Random randomGen = new Random();

        /// <summary>
        /// A single boolean used to control a NetworkCommsDotNet shutdown
        /// </summary>
        internal static volatile bool commsShutdown;

        /// <summary>
        /// A running total of the number of packets sent on all connections. Used to initialise packet sequence counters to ensure 
        /// duplicates can not occur.
        /// </summary>
        internal static long totalPacketSendCount;
        #endregion

        #region Established Connections
        /// <summary>
        /// Locker for connection dictionaries
        /// </summary>
        internal static object globalDictAndDelegateLocker = new object();

        /// <summary>
        /// Primary connection dictionary stored by network identifier
        /// </summary>
        internal static Dictionary<ShortGuid, Dictionary<ConnectionType, List<Connection>>> allConnectionsByIdentifier = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<Connection>>>();

        /// <summary>
        /// Secondary connection dictionary stored by end point. First key is connection type, second key is remote IPEndPoint, third 
        /// key is local IPEndPoint
        /// </summary>
        internal static Dictionary<ConnectionType, Dictionary<EndPoint, Dictionary<EndPoint, Connection>>> allConnectionsByEndPoint = new Dictionary<ConnectionType, Dictionary<EndPoint, Dictionary<EndPoint, Connection>>>();

        /// <summary>
        /// Old connection cache so that requests for connectionInfo can be returned even after a connection has been closed.
        /// </summary>
        internal static Dictionary<ShortGuid, Dictionary<ConnectionType, List<ConnectionInfo>>> oldNetworkIdentifierToConnectionInfo = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<ConnectionInfo>>>();
        #endregion

        #region Incoming Data and Connection Config
        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Default is false. No noticeable performance difference 
        /// between the two modes.
        /// </summary>
        public static bool ConnectionListenModeUseSync { get; set; }

        /// <summary>
        /// Receive data buffer size. Default is 80KB. CAUTION: Changing the default value can lead to performance degradation.
        /// </summary>
        public static int ReceiveBufferSizeBytes { get; set; }

        /// <summary>
        /// Send data buffer size. Default is 80KB. CAUTION: Changing the default value can lead to performance degradation.
        /// </summary>
        public static int SendBufferSizeBytes { get; set; }

        /// <summary>
        /// The thread pool used by networkComms.Net to execute incoming packet handlers.
        /// </summary>
        public static CommsThreadPool CommsThreadPool { get; set; }
        
        /// <summary>
        /// Once we have received all incoming data we handle it further. This is performed at the global level to help support different 
        /// priorities.
        /// </summary>
        /// <param name="priorityQueueItemObj">Possible PriorityQueueItem. If null is provided an item will be removed from the global item queue</param>
        internal static void CompleteIncomingItemTask(object priorityQueueItemObj)
        {
            if (priorityQueueItemObj == null) throw new ArgumentNullException("itemAsObj", "Provided parameter itemAsObj cannot be null.");

            PriorityQueueItem item = null;
            try
            {
                //If the packetBytes are null we need to ask the incoming packet queue for what we should be running
                item = priorityQueueItemObj as PriorityQueueItem;

                if (item == null)
                    throw new InvalidCastException("Cast from object to PriorityQueueItem resulted in null reference, unable to continue.");

                //If this is a nested packet we want to unwrap it here before continuing
                if (item.PacketHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.NestedPacket))
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Unwrapping a " + ReservedPacketType.NestedPacket + " packet from " + item.Connection.ConnectionInfo + " with a priority of " + item.Priority.ToString() + ".");

                    Packet nestedPacket = item.SendReceiveOptions.DataSerializer.DeserialiseDataObject<Packet>((MemoryStream)item.DataStream, item.SendReceiveOptions.DataProcessors, item.SendReceiveOptions.Options);

                    //Add the sequence number to the nested packet header
                    if (item.PacketHeader.ContainsOption(PacketHeaderLongItems.PacketSequenceNumber))
                        nestedPacket.PacketHeader.SetOption(PacketHeaderLongItems.PacketSequenceNumber, item.PacketHeader.GetOption(PacketHeaderLongItems.PacketSequenceNumber));

                    SendReceiveOptions incomingPacketSendReceiveOptions = item.Connection.IncomingPacketSendReceiveOptions(nestedPacket.PacketHeader);
                    QueueItemPriority itemPriority = (incomingPacketSendReceiveOptions.Options.ContainsKey("ReceiveHandlePriority") ? (QueueItemPriority)Enum.Parse(typeof(QueueItemPriority), incomingPacketSendReceiveOptions.Options["ReceiveHandlePriority"]) : QueueItemPriority.Normal);

#if NETFX_CORE
                    MemoryStream wrappedDataStream = new MemoryStream(nestedPacket._payloadObjectBytes, 0, nestedPacket._payloadObjectBytes.Length, false);
#else
                    MemoryStream wrappedDataStream = new MemoryStream(nestedPacket._payloadObjectBytes, 0, nestedPacket._payloadObjectBytes.Length, false, true);
#endif

                    item = new PriorityQueueItem(itemPriority, item.Connection, nestedPacket.PacketHeader, wrappedDataStream, incomingPacketSendReceiveOptions);
                }

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Handling a " + item.PacketHeader.PacketType + " packet from " + item.Connection.ConnectionInfo + " with a priority of " + item.Priority.ToString() + ".");

#if !WINDOWS_PHONE && !NETFX_CORE
                if (Thread.CurrentThread.Priority != (ThreadPriority)item.Priority) Thread.CurrentThread.Priority = (ThreadPriority)item.Priority;
#endif

                //Check for a shutdown connection
                if (item.Connection.ConnectionInfo.ConnectionState == ConnectionState.Shutdown) return;

                //We only look at the check sum if we want to and if it has been set by the remote end
                if (NetworkComms.EnablePacketCheckSumValidation && item.PacketHeader.ContainsOption(PacketHeaderStringItems.CheckSumHash))
                {
                    var packetHeaderHash = item.PacketHeader.GetOption(PacketHeaderStringItems.CheckSumHash);

                    //Validate the checkSumhash of the data
                    string packetDataSectionMD5 = StreamTools.MD5(item.DataStream);
                    if (packetHeaderHash != packetDataSectionMD5)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn(" ... corrupted packet detected, expected " + packetHeaderHash + " but received " + packetDataSectionMD5 + ".");

                        //We have corruption on a resend request, something is very wrong so we throw an exception.
                        if (item.PacketHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend)) throw new CheckSumException("Corrupted md5CheckFailResend packet received.");

                        if (item.PacketHeader.TotalPayloadSize < NetworkComms.CheckSumMismatchSentPacketCacheMaxByteLimit)
                        {
                            //Instead of throwing an exception we can request the packet to be resent
                            Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend), packetHeaderHash, NetworkComms.InternalFixedSendReceiveOptions);
                            item.Connection.SendPacket<string>(returnPacket);
                            //We need to wait for the packet to be resent before going further
                            return;
                        }
                        else
                            throw new CheckSumException("Corrupted packet detected from " + item.Connection.ConnectionInfo + ", expected " + packetHeaderHash + " but received " + packetDataSectionMD5 + ".");
                    }
                }

                //Remote end may have requested packet receive confirmation so we send that now
                if (item.PacketHeader.ContainsOption(PacketHeaderStringItems.ReceiveConfirmationRequired))
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... sending requested receive confirmation packet.");

                    long packetSequenceNumber;
                    if (item.PacketHeader.ContainsOption(PacketHeaderLongItems.PacketSequenceNumber))
                        packetSequenceNumber = item.PacketHeader.GetOption(PacketHeaderLongItems.PacketSequenceNumber);
                    else
                        throw new InvalidOperationException("Attempted to access packet header sequence number when non was set.");

                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), packetSequenceNumber, NetworkComms.InternalFixedSendReceiveOptions);
                    
                    //Should an error occur while sending the confirmation it should not prevent the handling of this packet
                    try
                    {
                        item.Connection.SendPacket<long>(returnPacket);
                    }
                    catch (CommsException) 
                    { 
                        //Do nothing
                    }
                }

                //We can now pass the data onto the correct delegate
                //First we have to check for our reserved packet types
                //The following large sections have been factored out to make reading and debugging a little easier
                if (item.PacketHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend))
                    item.Connection.CheckSumFailResendHandler(item.DataStream);
                else if (item.PacketHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup))
                    item.Connection.ConnectionSetupHandler(item.DataStream);
                else if (item.PacketHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket) &&
                    (NetworkComms.InternalFixedSendReceiveOptions.DataSerializer.DeserialiseDataObject<byte[]>(item.DataStream,
                        NetworkComms.InternalFixedSendReceiveOptions.DataProcessors,
                        NetworkComms.InternalFixedSendReceiveOptions.Options))[0] == 0)
                {
                    //If we have received a ping packet from the originating source we reply with true
                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), new byte[1] { 1 }, NetworkComms.InternalFixedSendReceiveOptions);
                    item.Connection.SendPacket<byte[]>(returnPacket);
                }

                //We allow users to add their own custom handlers for reserved packet types here
                //else
                if (true)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Triggering handlers for packet of type '" + item.PacketHeader.PacketType + "' from " + item.Connection.ConnectionInfo);

                    //We trigger connection specific handlers first
                    bool connectionSpecificHandlersTriggered = item.Connection.TriggerSpecificPacketHandlers(item.PacketHeader, item.DataStream, item.SendReceiveOptions);

                    //We trigger global handlers second
                    NetworkComms.TriggerGlobalPacketHandlers(item.PacketHeader, item.Connection, item.DataStream, item.SendReceiveOptions, connectionSpecificHandlersTriggered);

                    //This is a really bad place to put a garbage collection, comment left in so that it doesn't get added again at some later date
                    //We don't want the CPU to JUST be trying to garbage collect the WHOLE TIME
                    //GC.Collect();
                }
            }
            catch (CommunicationException)
            {
                if (item != null)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("A communication exception occurred in CompleteIncomingPacketWorker(), connection with " + item.Connection.ConnectionInfo + " be closed.");
                    item.Connection.CloseConnection(true, 2);
                }
            }
            catch (DuplicateConnectionException ex)
            {
                if (item != null)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn(ex.Message != null ? ex.Message : "A possible duplicate connection was detected with " + item.Connection + ". Closing connection.");
                    item.Connection.CloseConnection(true, 42);
                }
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "CompleteIncomingItemTaskError");

                if (item != null)
                {
                    //If anything goes wrong here all we can really do is log the exception
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("An unhanded exception occurred in CompleteIncomingPacketWorker(), connection with " + item.Connection.ConnectionInfo + " be closed. See log file for more information.");
                    item.Connection.CloseConnection(true, 3);
                }
            }
            finally
            {
                //We need to dispose the data stream correctly
#if NETFX_CORE
                if (item != null) item.DataStream.Dispose();
#else
                if (item!=null) item.DataStream.Close();
#endif

#if !WINDOWS_PHONE && !NETFX_CORE
                //Ensure the thread returns to the pool with a normal priority
                if (Thread.CurrentThread.Priority != ThreadPriority.Normal) Thread.CurrentThread.Priority = ThreadPriority.Normal;
#endif
            }
        }
        #endregion

#if !WINDOWS_PHONE && !NETFX_CORE
        #region High CPU Usage Tuning
        /// <summary>
        /// In times of high CPU usage we need to ensure that certain time critical functions, like connection handshaking do not timeout.
        /// This sets the thread priority for those processes.
        /// </summary>
        internal static ThreadPriority timeCriticalThreadPriority = ThreadPriority.AboveNormal;
        #endregion
#endif

        #region Checksum Config
        /// <summary>
        /// When enabled uses an MD5 checksum to validate all received packets. Default is false, relying on any possible connection 
        /// checksum alone. Also when enabled any packets sent less than CheckSumMismatchSentPacketCacheMaxByteLimit will be cached 
        /// for a duration to ensure successful delivery. Default false.
        /// </summary>
        public static bool EnablePacketCheckSumValidation { get; set; }

        /// <summary>
        /// When checksum validation is enabled sets the limit below which sent packets are cached to ensure successful delivery. Default 75KB.
        /// </summary>
        public static int CheckSumMismatchSentPacketCacheMaxByteLimit { get; set; }

        /// <summary>
        /// When a sent packet has been cached for a possible resend this is the minimum length of time it will be retained. 
        /// Default is 1.0 minutes.
        /// </summary>
        public static double MinimumSentPacketCacheTimeMinutes { get; set; }

        /// <summary>
        /// Records the last sent packet cache clean up time. Prevents the sent packet cache from being checked too frequently.
        /// </summary>
        internal static DateTime LastSentPacketCacheCleanup { get; set; }
        #endregion

        #region PacketType Config and Global Handlers
        /// <summary>
        /// An internal reference copy of all reservedPacketTypeNames, key is packet type name
        /// </summary>
        internal static Dictionary<string, string> ReservedPacketTypeNames;

        /// <summary>
        /// Dictionary of all custom packetHandlers. Key is packetType.
        /// </summary>
        static Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>> globalIncomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();
        
        /// <summary>
        /// Dictionary of any non default custom packet unwrappers. Key is packetType.
        /// </summary>
        static Dictionary<string, PacketTypeUnwrapper> globalIncomingPacketUnwrappers = new Dictionary<string, PacketTypeUnwrapper>();

        /// <summary>
        /// Delegate for handling incoming packets. See AppendGlobalIncomingPacketHandler members.
        /// </summary>
        /// <typeparam name="incomingObjectType">The type of object which is expected for this handler</typeparam>
        /// <param name="packetHeader">The <see cref="PacketHeader"/> of the incoming packet</param>
        /// <param name="connection">The connection with which this packet was received</param>
        /// <param name="incomingObject">The incoming object of specified type T</param>
        public delegate void PacketHandlerCallBackDelegate<incomingObjectType>(PacketHeader packetHeader, Connection connection, incomingObjectType incomingObject);

        /// <summary>
        /// If true any unknown incoming packet types are ignored. Default is false and will result in an error file being created if 
        /// an unknown packet type is received.
        /// </summary>
        public static bool IgnoreUnknownPacketTypes { get; set; }

        /// <summary>
        /// Add an incoming packet handler using default SendReceiveOptions. Multiple handlers for the same packet type will be 
        /// executed in the order they are added.
        /// </summary>
        /// <typeparam name="incomingObjectType">The type of incoming object</typeparam>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when a packet of packetTypeStr is received</param>
        public static void AppendGlobalIncomingPacketHandler<incomingObjectType>(string packetTypeStr, PacketHandlerCallBackDelegate<incomingObjectType> packetHandlerDelgatePointer)
        {
            //Checks for unmanaged packet types
            if (packetTypeStr == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
            {
                if (DefaultSendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new InvalidOperationException("Attempted to add packet handler for an unmanaged packet type when the global send receive options serializer was not NullSerializer.");

                if (DefaultSendReceiveOptions.DataProcessors.Count > 0)
                    throw new InvalidOperationException("Attempted to add packet handler for an unmanaged packet type when the global send receive options contains data processors. Data processors may not be used inline with unmanaged packet types.");
            }

            AppendGlobalIncomingPacketHandler<incomingObjectType>(packetTypeStr, packetHandlerDelgatePointer, DefaultSendReceiveOptions);
        }

        /// <summary>
        /// Add an incoming packet handler using the provided SendReceiveOptions. Multiple handlers for the same packet type will be executed in the order they are added.
        /// </summary>
        /// <typeparam name="incomingObjectType">The type of incoming object</typeparam>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when a packet of packetTypeStr is received</param>
        /// <param name="sendReceiveOptions">The SendReceiveOptions to be used for the provided packet type</param>
        public static void AppendGlobalIncomingPacketHandler<incomingObjectType>(string packetTypeStr, PacketHandlerCallBackDelegate<incomingObjectType> packetHandlerDelgatePointer, SendReceiveOptions sendReceiveOptions)
        {
            if (packetTypeStr == null) throw new ArgumentNullException("packetTypeStr", "Provided packetType string cannot be null.");
            if (packetHandlerDelgatePointer == null) throw new ArgumentNullException("packetHandlerDelgatePointer", "Provided PacketHandlerCallBackDelegate<incomingObjectType> cannot be null.");
            if (sendReceiveOptions == null) throw new ArgumentNullException("sendReceiveOptions", "Provided SendReceiveOptions cannot be null.");

            //If we are adding a handler for an unmanaged packet type the data serializer must be NullSerializer
            //Checks for unmanaged packet types
            if (packetTypeStr == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
            {
                if (sendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new ArgumentException("Attempted to add packet handler for an unmanaged packet type when the provided send receive options serializer was not NullSerializer.");

                if (sendReceiveOptions.DataProcessors.Count > 0)
                    throw new ArgumentException("Attempted to add packet handler for an unmanaged packet type when the provided send receive options contains data processors. Data processors may not be used inline with unmanaged packet types.");
            }

            lock (globalDictAndDelegateLocker)
            {
                if (globalIncomingPacketUnwrappers.ContainsKey(packetTypeStr))
                {
                    //Make sure if we already have an existing entry that it matches with the provided
                    if (!globalIncomingPacketUnwrappers[packetTypeStr].Options.OptionsCompatible(sendReceiveOptions))
                        throw new PacketHandlerException("The provided SendReceiveOptions are not compatible with existing SendReceiveOptions already specified for this packetTypeStr.");
                }
                else
                    globalIncomingPacketUnwrappers.Add(packetTypeStr, new PacketTypeUnwrapper(packetTypeStr, sendReceiveOptions));

                //Ad the handler to the list
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Make sure we avoid duplicates
                    PacketTypeHandlerDelegateWrapper<incomingObjectType> toCompareDelegate = new PacketTypeHandlerDelegateWrapper<incomingObjectType>(packetHandlerDelgatePointer);

                    bool delegateAlreadyExists = false;
                    foreach (var handler in globalIncomingPacketHandlers[packetTypeStr])
                    {
                        if (handler == toCompareDelegate)
                        {
                            delegateAlreadyExists = true;
                            break;
                        }
                    }
                                        
                    if (delegateAlreadyExists)
                        throw new PacketHandlerException("This specific packet handler delegate already exists for the provided packetTypeStr.");

                    globalIncomingPacketHandlers[packetTypeStr].Add(new PacketTypeHandlerDelegateWrapper<incomingObjectType>(packetHandlerDelgatePointer));
                }
                else
                    globalIncomingPacketHandlers.Add(packetTypeStr, new List<IPacketTypeHandlerDelegateWrapper>() { new PacketTypeHandlerDelegateWrapper<incomingObjectType>(packetHandlerDelgatePointer) });

                if (LoggingEnabled) logger.Info("Added incoming packetHandler for '" + packetTypeStr + "' packetType.");
            }
        }

        /// <summary>
        /// Add an incoming packet handler for unmanaged packets. Multiple handlers will be executed in the order they are added.
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when an unmanaged packet is received</param>
        public static void AppendGlobalIncomingUnmanagedPacketHandler(PacketHandlerCallBackDelegate<byte[]> packetHandlerDelgatePointer)
        {
            AppendGlobalIncomingPacketHandler<byte[]>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetHandlerDelgatePointer, new SendReceiveOptions<NullSerializer>());
        }

        /// <summary>
        /// Removes the provided delegate for unmanaged packet types. If the provided delegate does not exist for this packet type just returns.
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The delegate to be removed</param>
        public static void RemoveGlobalIncomingUnmanagedPacketHandler<packetHandlerIncomingObjectType>(PacketHandlerCallBackDelegate<packetHandlerIncomingObjectType> packetHandlerDelgatePointer)
        {
            RemoveGlobalIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetHandlerDelgatePointer);
        }

        /// <summary>
        /// Removes the provided delegate for the specified packet type. If the provided delegate does not exist for this packet type just returns.
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which the delegate will be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be removed</param>
        public static void RemoveGlobalIncomingPacketHandler<packetHandlerIncomingObjectType>(string packetTypeStr, PacketHandlerCallBackDelegate<packetHandlerIncomingObjectType> packetHandlerDelgatePointer)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Remove any instances of this handler from the delegates
                    //The bonus here is if the delegate has not been added we continue quite happily
                    IPacketTypeHandlerDelegateWrapper toRemove = null;

                    foreach (var handler in globalIncomingPacketHandlers[packetTypeStr])
                    {
                        if (handler.EqualsDelegate(packetHandlerDelgatePointer))
                        {
                            toRemove = handler;
                            break;
                        }
                    }

                    if (toRemove != null)
                        globalIncomingPacketHandlers[packetTypeStr].Remove(toRemove);

                    if (globalIncomingPacketHandlers[packetTypeStr] == null || globalIncomingPacketHandlers[packetTypeStr].Count == 0)
                    {
                        globalIncomingPacketHandlers.Remove(packetTypeStr);
                        globalIncomingPacketUnwrappers.Remove(packetTypeStr);

                        if (LoggingEnabled) logger.Info("Removed a packetHandler for '" + packetTypeStr + "' packetType. No handlers remain.");
                    }
                    else
                        if (LoggingEnabled) logger.Info("Removed a packetHandler for '" + packetTypeStr + "' packetType. Handlers remain.");
                }
            }
        }

        /// <summary>
        /// Removes all delegates for the provided packet type.
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which all delegates should be removed</param>
        public static void RemoveGlobalIncomingPacketHandler(string packetTypeStr)
        {
            lock (globalDictAndDelegateLocker)
            {
                //We don't need to check for potentially removing a critical reserved packet handler here because those cannot be removed.
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    globalIncomingPacketHandlers.Remove(packetTypeStr);
                    globalIncomingPacketUnwrappers.Remove(packetTypeStr);

                    if (LoggingEnabled) logger.Info("Removed all incoming packetHandlers for '" + packetTypeStr + "' packetType.");
                }
            }
        }

        /// <summary>
        /// Removes all unmanaged packet handlers.
        /// </summary>
        public static void RemoveGlobalIncomingUnmanagedPacketHandler()
        {
            RemoveGlobalIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged));
        }

        /// <summary>
        /// Removes all delegates for all packet types
        /// </summary>
        public static void RemoveGlobalIncomingPacketHandler()
        {
            lock (globalDictAndDelegateLocker)
            {
                globalIncomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();
                globalIncomingPacketUnwrappers = new Dictionary<string, PacketTypeUnwrapper>();

                if (LoggingEnabled) logger.Info("Removed all incoming packetHandlers for all packetTypes");
            }
        }

        /// <summary>
        /// Trigger incoming packet delegates for the provided parameters.
        /// </summary>
        /// <param name="packetHeader">The packet header</param>
        /// <param name="connection">The incoming connection</param>
        /// <param name="incomingDataStream">The bytes corresponding to the incoming object</param>
        /// <param name="options">The SendReceiveOptions to be used to convert incomingObjectBytes back to the desired object</param>
        public static void TriggerGlobalPacketHandlers(PacketHeader packetHeader, Connection connection, MemoryStream incomingDataStream, SendReceiveOptions options)
        {
            TriggerGlobalPacketHandlers(packetHeader, connection, incomingDataStream, options, IgnoreUnknownPacketTypes);
        }

        /// <summary>
        /// Trigger incoming packet delegates for the provided parameters.
        /// </summary>
        /// <param name="packetHeader">The packet header</param>
        /// <param name="connection">The incoming connection</param>
        /// <param name="incomingDataStream">The bytes corresponding to the incoming object</param>
        /// <param name="options">The SendReceiveOptions to be used to convert incomingObjectBytes back to the desired object</param>
        /// <param name="ignoreUnknownPacketTypeOverride">Used to potentially override NetworkComms.IgnoreUnknownPacketTypes property</param>
        internal static void TriggerGlobalPacketHandlers(PacketHeader packetHeader, Connection connection, MemoryStream incomingDataStream, SendReceiveOptions options, bool ignoreUnknownPacketTypeOverride = false)
        {
            try
            {
                if (options == null) throw new PacketHandlerException("Provided sendReceiveOptions should not be null for packetType " + packetHeader.PacketType);

                //We take a copy of the handlers list in case it is modified outside of the lock
                List<IPacketTypeHandlerDelegateWrapper> handlersCopy = null;
                lock (globalDictAndDelegateLocker)
                    if (globalIncomingPacketHandlers.ContainsKey(packetHeader.PacketType))
                        handlersCopy = new List<IPacketTypeHandlerDelegateWrapper>(globalIncomingPacketHandlers[packetHeader.PacketType]);

                if (handlersCopy == null && !IgnoreUnknownPacketTypes && !ignoreUnknownPacketTypeOverride)
                {
                    //We may get here if we have not added any custom delegates for reserved packet types
                    if (!ReservedPacketTypeNames.ContainsKey(packetHeader.PacketType))
                    {
                        //Change this to just a log because generally a packet of the wrong type is nothing to really worry about
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn("The received packet type '" + packetHeader.PacketType + "' has no configured handler and NetworkComms.Net is not set to ignore unknown packet types. Set NetworkComms.IgnoreUnknownPacketTypes=true to prevent this error.");
                        LogError(new UnexpectedPacketTypeException("The received packet type '" + packetHeader.PacketType + "' has no configured handler and NetworkComms.Net is not set to ignore unknown packet types. Set NetworkComms.IgnoreUnknownPacketTypes=true to prevent this error."), "PacketHandlerErrorGlobal_" + packetHeader.PacketType);
                    }

                    return;
                }
                else if (handlersCopy == null && (IgnoreUnknownPacketTypes || ignoreUnknownPacketTypeOverride))
                    //If we have received an unknown packet type and we are choosing to ignore them we just finish here
                    return;
                else
                {
                    //Idiot check
                    if (handlersCopy.Count == 0)
                        throw new PacketHandlerException("An entry exists in the packetHandlers list but it contains no elements. This should not be possible.");

                    //Deserialise the object only once
                    object returnObject = handlersCopy[0].DeSerialize(incomingDataStream, options);

                    //Pass the data onto the handler and move on.
                    if (LoggingEnabled) logger.Trace(" ... passing completed data packet of type '" + packetHeader.PacketType + "' to " + handlersCopy.Count.ToString() + " selected global handlers.");

                    //Pass the object to all necessary delegates
                    //We need to use a copy because we may modify the original delegate list during processing
                    foreach (IPacketTypeHandlerDelegateWrapper wrapper in handlersCopy)
                    {
                        try
                        {
                            wrapper.Process(packetHeader, connection, returnObject);
                        }
                        catch (Exception ex)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("An unhandled exception was caught while processing a packet handler for a packet type '" + packetHeader.PacketType + "'. Make sure to catch errors in packet handlers. See error log file for more information.");
                            NetworkComms.LogError(ex, "PacketHandlerErrorGlobal_" + packetHeader.PacketType);
                        }
                    }

                    if (LoggingEnabled) logger.Trace(" ... all handlers for packet of type '" + packetHeader.PacketType + "' completed.");
                }
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("An exception occurred in TriggerPacketHandler() for a packet type '" + packetHeader.PacketType + "'. See error log file for more information.");
                NetworkComms.LogError(ex, "PacketHandlerErrorGlobal_" + packetHeader.PacketType);
            }
        }

        /// <summary>
        /// Returns the unwrapper <see cref="SendReceiveOptions"/> for the provided packet type. If no specific options are registered returns null.
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which the <see cref="SendReceiveOptions"/> are required</param>
        /// <returns>The requested <see cref="SendReceiveOptions"/> otherwise null</returns>
        public static SendReceiveOptions GlobalPacketTypeUnwrapperOptions(string packetTypeStr)
        {
            SendReceiveOptions options = null;

            //If we find a global packet unwrapper for this packetType we used those options
            lock (globalDictAndDelegateLocker)
            {
                if (globalIncomingPacketUnwrappers.ContainsKey(packetTypeStr))
                    options = globalIncomingPacketUnwrappers[packetTypeStr].Options;
            }

            return options;
        }

        /// <summary>
        /// Returns true if a global packet handler exists for the provided packet type.
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which to check incoming packet handlers</param>
        /// <returns>True if a global packet handler exists</returns>
        public static bool GlobalIncomingPacketHandlerExists(string packetTypeStr)
        {
            lock (globalDictAndDelegateLocker)
                return globalIncomingPacketHandlers.ContainsKey(packetTypeStr);
        }

        /// <summary>
        /// Returns true if a global unmanaged packet handler exists
        /// </summary>
        /// <returns>True if a global unmanaged packet handler exists</returns>
        public static bool GlobalIncomingUnmanagedPacketHandlerExists()
        {
            lock (globalDictAndDelegateLocker)
                return globalIncomingPacketHandlers.ContainsKey(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged));
        }

        /// <summary>
        /// Returns true if the provided global packet handler has been added for the provided packet type.
        /// </summary>
        /// <param name="packetTypeStr">The packet type within which to check packet handlers</param>
        /// <param name="packetHandlerDelgatePointer">The packet handler to look for</param>
        /// <returns>True if a global packet handler exists for the provided packetType</returns>
        public static bool GlobalIncomingPacketHandlerExists<packetHandlerIncomingObjectType>(string packetTypeStr, PacketHandlerCallBackDelegate<packetHandlerIncomingObjectType> packetHandlerDelgatePointer)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    foreach (var handler in globalIncomingPacketHandlers[packetTypeStr])
                    {
                        if (handler.EqualsDelegate(packetHandlerDelgatePointer))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the provided global unmanaged packet handler has been added.
        /// </summary>
        /// <param name="packetHandlerDelgatePointer">The packet handler to look for.</param>
        /// <returns>True if a global unmanaged packet handler exists.</returns>
        public static bool GlobalIncomingUnmanagedPacketHandlerExists<packetHandlerIncomingObjectType>(PacketHandlerCallBackDelegate<packetHandlerIncomingObjectType> packetHandlerDelgatePointer)
        {
            return GlobalIncomingPacketHandlerExists(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetHandlerDelgatePointer);
        }
        #endregion

        #region Connection Establish and Shutdown
        /// <summary>
        /// Delegate which is executed when a connection is established or shutdown. See <see cref="AppendGlobalConnectionEstablishHandler"/> and <see cref="AppendGlobalConnectionCloseHandler"/>.
        /// </summary>
        /// <param name="connection">The connection which has been established or shutdown.</param>
        public delegate void ConnectionEstablishShutdownDelegate(Connection connection);

        /// <summary>
        /// Multicast delegate pointer for connection shutdowns.
        /// </summary>
        internal static ConnectionEstablishShutdownDelegate globalConnectionShutdownDelegates;

        /// <summary>
        /// Delegate counter for debugging.
        /// </summary>
        internal static int globalConnectionShutdownDelegateCount = 0;

        /// <summary>
        /// Multicast delegate pointer for connection establishments, run asynchronously.
        /// </summary>
        internal static ConnectionEstablishShutdownDelegate globalConnectionEstablishDelegatesAsync;

        /// <summary>
        /// Multicast delegate pointer for connection establishments, run synchronously.
        /// </summary>
        internal static ConnectionEstablishShutdownDelegate globalConnectionEstablishDelegatesSync;

        /// <summary>
        /// Delegate counter for debugging.
        /// </summary>
        internal static int globalConnectionEstablishDelegateCount = 0;

        /// <summary>
        /// Comms shutdown event. This will be triggered when calling NetworkComms.Shutdown
        /// </summary>
        public static event EventHandler<EventArgs> OnCommsShutdown;

        /// <summary>
        /// Add a new connection shutdown delegate which will be called for every connection as it is closes.
        /// </summary>
        /// <param name="connectionShutdownDelegate">The delegate to call on all connection shutdowns</param>
        public static void AppendGlobalConnectionCloseHandler(ConnectionEstablishShutdownDelegate connectionShutdownDelegate)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (globalConnectionShutdownDelegates == null)
                    globalConnectionShutdownDelegates = connectionShutdownDelegate;
                else
                    globalConnectionShutdownDelegates += connectionShutdownDelegate;

                globalConnectionShutdownDelegateCount++;

                if (LoggingEnabled) logger.Info("Added globalConnectionShutdownDelegates. " + globalConnectionShutdownDelegateCount.ToString());
            }
        }

        /// <summary>
        /// Remove a connection shutdown delegate.
        /// </summary>
        /// <param name="connectionShutdownDelegate">The delegate to remove from connection shutdown events</param>
        public static void RemoveGlobalConnectionCloseHandler(ConnectionEstablishShutdownDelegate connectionShutdownDelegate)
        {
            lock (globalDictAndDelegateLocker)
            {
                globalConnectionShutdownDelegates -= connectionShutdownDelegate;
                globalConnectionShutdownDelegateCount--;

                if (LoggingEnabled) logger.Info("Removed globalConnectionShutdownDelegates. " + globalConnectionShutdownDelegateCount.ToString());
            }
        }

        /// <summary>
        /// Add a new connection establish delegate which will be called for every connection once it has been successfully established.
        /// </summary>
        /// <param name="connectionEstablishDelegate">The delegate to call after all connection establishments.</param>
        /// <param name="runSynchronously">If true this ConnectionEstablishShutdownDelegate will be called synchronously during the 
        /// connection establish. The connection will not be considered established until the ConnectionEstablishShutdownDelegate has completed.</param>
        public static void AppendGlobalConnectionEstablishHandler(ConnectionEstablishShutdownDelegate connectionEstablishDelegate, bool runSynchronously = false)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (runSynchronously)
                {
                    if (globalConnectionEstablishDelegatesSync == null)
                        globalConnectionEstablishDelegatesSync = connectionEstablishDelegate;
                    else
                        globalConnectionEstablishDelegatesSync += connectionEstablishDelegate;
                }
                else
                {
                    if (globalConnectionEstablishDelegatesAsync == null)
                        globalConnectionEstablishDelegatesAsync = connectionEstablishDelegate;
                    else
                        globalConnectionEstablishDelegatesAsync += connectionEstablishDelegate;
                }

                globalConnectionEstablishDelegateCount++;

                if (LoggingEnabled) logger.Info("Added globalConnectionEstablishDelegates. " + globalConnectionEstablishDelegateCount.ToString());
            }
        }

        /// <summary>
        /// Remove a connection establish delegate.
        /// </summary>
        /// <param name="connectionEstablishDelegate">The delegate to remove from connection establish events</param>
        public static void RemoveGlobalConnectionEstablishHandler(ConnectionEstablishShutdownDelegate connectionEstablishDelegate)
        {
            lock (globalDictAndDelegateLocker)
            {
                //Remove from either async or sync delegates
                globalConnectionEstablishDelegatesAsync -= connectionEstablishDelegate;
                globalConnectionEstablishDelegatesSync -= connectionEstablishDelegate;

                globalConnectionEstablishDelegateCount--;

                if (LoggingEnabled) logger.Info("Removed globalConnectionEstablishDelegates. " + globalConnectionEstablishDelegateCount.ToString());
            }
        }

        /// <summary>
        /// Shutdown all connections, threads and execute OnCommsShutdown event. Any packet handlers are left unchanged. If any network
        /// activity has taken place this should be called on application close.
        /// </summary>
        /// <param name="threadShutdownTimeoutMS">The time to wait for worker threads to close before attempting a thread abort.</param>
        public static void Shutdown(int threadShutdownTimeoutMS = 1000)
        {
            if (LoggingEnabled) logger.Trace("NetworkComms.Net shutdown initiated.");
            commsShutdown = true;

            CommsThreadPool.BeginShutdown();
            Connection.Shutdown(threadShutdownTimeoutMS);
            HostInfo.IP.ShutdownThreads(threadShutdownTimeoutMS);

            try
            {
                CloseAllConnections();
            }
            catch (CommsException)
            {

            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            try
            {
                if (OnCommsShutdown != null)
                {
                    //This copy is requested by Gendarme
                    EventHandler<EventArgs> eventToRun = OnCommsShutdown;
                    eventToRun(null, new EventArgs());
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            CommsThreadPool.EndShutdown(threadShutdownTimeoutMS);

            commsShutdown = false;
            if (LoggingEnabled) logger.Info("NetworkComms.Net has shutdown");

#if !WINDOWS_PHONE && !NO_LOGGING && !NETFX_CORE
            //Mono bug fix
            //Sometimes NLog ends up in a deadlock on close, workaround provided on NLog website
            if (Logger != null)
            {
                LogManager.Flush();
                Logger.Factory.Flush();

                if (NetworkComms.CurrentRuntimeEnvironment == RuntimeEnvironment.Mono_Net2 ||
                    NetworkComms.CurrentRuntimeEnvironment == RuntimeEnvironment.Mono_Net35 ||
                    NetworkComms.CurrentRuntimeEnvironment == RuntimeEnvironment.Mono_Net4)
                    LogManager.Configuration = null;
            }
#endif
        }
        #endregion
         
        #region Timeouts
        /// <summary>
        /// Time to wait in milliseconds before throwing an exception when waiting for a connection to be established. Default is 30000.
        /// </summary>
        public static int ConnectionEstablishTimeoutMS { get; set; }

        /// <summary>
        /// Time to wait in milliseconds before throwing an exception when waiting for confirmation of packet receipt. Default is 5000.
        /// </summary>
        public static int PacketConfirmationTimeoutMS { get; set; }

        /// <summary>
        /// Time to wait in milliseconds before assuming a remote connection is dead when doing a connection test. Default is 1000.
        /// </summary>
        public static int ConnectionAliveTestTimeoutMS { get; set; }

        /// <summary>
        /// By default NetworkComms.Net closes connections for which sends take a long time. The timeout is calculated based on previous connection send performances. Set this to true to disable this feature.
        /// </summary>
        public static bool DisableConnectionSendTimeouts { get; set; }
        #endregion

        #region Logging
        /// <summary>
        /// Returns true if NetworkComms.Net logging has been enabled.
        /// </summary>
        public static bool LoggingEnabled { get; private set; }

        private static Logger logger = null;

        /// <summary>
        /// Access the NetworkCommsDotNet logger externally.
        /// </summary>
        public static Logger Logger
        {
            get { return logger; }
        }

#if NO_LOGGING
        /// <summary>
        /// Enable basic logging using the provided logFileLocation
        /// </summary>        
        /// <param name="loggingConfiguration"></param>
        public static void EnableLogging(string logFileLocation)
        {
            lock (globalDictAndDelegateLocker)
            {
                LoggingEnabled = true;
                logger = new Logger();
                logger.LogFileLocation = logFileLocation;
            }
        }
        
        /// <summary>
        /// Disable all logging in NetworkCommsDotNet
        /// </summary>
        public static void DisableLogging()
        {
            lock (globalDictAndDelegateLocker)
            {
                LoggingEnabled = false;
                logger = null;
            }
        }
#else
        /// <summary>
        /// Enable logging using a default config. All log output is written directly to the local console.
        /// </summary>
        public static void EnableLogging()
        {
            LoggingConfiguration logConfig = new LoggingConfiguration();
            NLog.Targets.ConsoleTarget consoleTarget = new NLog.Targets.ConsoleTarget();
            consoleTarget.Layout = "${date:format=HH\\:MM\\:ss} [${level}] - ${message}";
            logConfig.AddTarget("console", consoleTarget);
            logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, consoleTarget));
            EnableLogging(logConfig);
        }

        /// <summary>
        /// Enable logging using the provided config. See examples for usage.
        /// </summary>        
        /// <param name="loggingConfiguration"></param>
        public static void EnableLogging(LoggingConfiguration loggingConfiguration)
        {
            lock (globalDictAndDelegateLocker)
            {
                LoggingEnabled = true;
                LogManager.Configuration = loggingConfiguration;                
                logger = LogManager.GetCurrentClassLogger();
                LogManager.EnableLogging();
            }
        }

        /// <summary>
        /// Disable all logging in NetworkCommsDotNet
        /// </summary>
        public static void DisableLogging()
        {
            lock (globalDictAndDelegateLocker)
            {
                LoggingEnabled = false;
                LogManager.DisableLogging();
            }
        }
#endif

        /// <summary>
        /// Locker for LogError() which ensures thread safe saves.
        /// </summary>
        static object errorLocker = new object();

        /// <summary>
        /// Appends the provided logString to end of fileName.txt. If the file does not exist it will be created.
        /// </summary>
        /// <param name="fileName">The filename to use. The extension .txt will be appended automatically</param>
        /// <param name="logString">The string to append.</param>
        public static void AppendStringToLogFile(string fileName, string logString)
        {
            try
            {
                lock (errorLocker)
                {
#if NETFX_CORE
                    Func<Task> writeTask = new Func<Task>(async () =>
                        {
                            StorageFolder folder = ApplicationData.Current.LocalFolder;
                            StorageFile file = await folder.CreateFileAsync(fileName + ".txt", CreationCollisionOption.OpenIfExists);
                            await FileIO.AppendTextAsync(file, logString);
                        });

                    writeTask().Wait();
#else
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fileName + ".txt", true))
                        sw.WriteLine(logString);
#endif
                }
            }
            catch (Exception)
            {
                //If an error happens here, such as if the file is locked then we lucked out.
            }
        }

        /// <summary>
        /// Logs the provided exception to a file to assist troubleshooting.
        /// </summary>
        /// <param name="ex">The exception to be logged</param>
        /// <param name="fileName">The filename to use. A time stamp and extension .txt will be appended automatically</param>
        /// <param name="optionalCommentStr">An optional string which will appear at the top of the error file</param>
        /// <returns>The entire fileName used.</returns>
        public static string LogError(Exception ex, string fileName, string optionalCommentStr = "")
        {
            string entireFileName;

            lock (errorLocker)
            {
                
#if iOS
                //We need to ensure we add the correct document path for iOS
                entireFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "]"));
#elif ANDROID
                entireFileName = Path.Combine(global::Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "]"));
#elif WINDOWS_PHONE
                entireFileName = fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "]");
#elif NETFX_CORE
                entireFileName = fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Environment.CurrentManagedThreadId.ToString() + "]");
#else
                using (Process currentProcess = System.Diagnostics.Process.GetCurrentProcess())
                    entireFileName = fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + currentProcess.Id.ToString() + "-" + Thread.CurrentContext.ContextID.ToString() + "]");
#endif

                if (LoggingEnabled) logger.Fatal(entireFileName, ex);

                try
                {
#if NETFX_CORE
                    Func<Task> writeTask = new Func<Task>(async () =>
                        {
                            List<string> lines = new List<string>();

                            if (optionalCommentStr != "")
                            {
                                lines.Add("Comment: " + optionalCommentStr);
                                lines.Add("");
                            }

                            if (ex.GetBaseException() != null)
                                lines.Add("Base Exception Type: " + ex.GetBaseException().ToString());

                            if (ex.InnerException != null)
                                lines.Add("Inner Exception Type: " + ex.InnerException.ToString());

                            if (ex.StackTrace != null)
                            {
                                lines.Add("");
                                lines.Add("Stack Trace: " + ex.StackTrace.ToString());
                            }

                            StorageFolder folder = ApplicationData.Current.LocalFolder;
                            StorageFile file = await folder.CreateFileAsync(fileName + ".txt", CreationCollisionOption.OpenIfExists);
                            await FileIO.WriteLinesAsync(file, lines);
                        });

                    writeTask().Wait();
#else
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(entireFileName + ".txt", false))
                    {
                        if (optionalCommentStr != "")
                        {
                            sw.WriteLine("Comment: " + optionalCommentStr);
                            sw.WriteLine("");
                        }

                        if (ex.GetBaseException() != null)
                            sw.WriteLine("Base Exception Type: " + ex.GetBaseException().ToString());

                        if (ex.InnerException != null)
                            sw.WriteLine("Inner Exception Type: " + ex.InnerException.ToString());

                        if (ex.StackTrace != null)
                        {
                            sw.WriteLine("");
                            sw.WriteLine("Stack Trace: " + ex.StackTrace.ToString());
                        }
                    }
#endif
                }
                catch (Exception)
                {
                    //This should never really happen, but just incase.
                }
            }

            return entireFileName;
        }
        #endregion

        #region Serializers and Compressors
        
        /// <summary>
        /// The following are used for internal NetworkComms.Net objects, packet headers, connection establishment etc. 
        /// We generally seem to increase the size of our data if compressing small objects (~50 bytes)
        /// Given the typical header size is 40 bytes we might as well not compress these objects.
        /// </summary>
        internal static SendReceiveOptions InternalFixedSendReceiveOptions { get; set; }

        /// <summary>
        /// Default options for sending and receiving in the absence of specific values
        /// </summary>
        public static SendReceiveOptions DefaultSendReceiveOptions { get; set; }
        #endregion

        #region Connection Access
        /// <summary>
        /// Send the provided object to the specified destination using TCP. Uses default sendReceiveOptions. For more control over 
        /// options see connection specific methods.
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use for send</param>
        /// <param name="destinationIPAddress">The destination IP address</param>
        /// <param name="destinationPort">The destination listen port</param>
        /// <param name="sendObject">The object to send</param>
        public static void SendObject<sendObjectType>(string packetTypeStr, string destinationIPAddress, int destinationPort, sendObjectType sendObject)
        {
            TCPConnection conn = TCPConnection.GetConnection(new ConnectionInfo(destinationIPAddress, destinationPort));
            conn.SendObject(packetTypeStr, sendObject);
        }

        /// <summary>
        /// Send the provided object to the specified destination and wait for a return object using TCP. Uses default sendReceiveOptions. 
        /// For more control over options see connection specific methods.
        /// </summary>
        /// <typeparam name="sendObjectType">The sending object type, i.e. string, int[], etc</typeparam>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination IP address</param>
        /// <param name="destinationPort">The destination listen port</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <returns>The expected return object</returns>
        public static returnObjectType SendReceiveObject<sendObjectType, returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int destinationPort, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, sendObjectType sendObject)
        {
            TCPConnection conn = TCPConnection.GetConnection(new ConnectionInfo(destinationIPAddress, destinationPort));
            return conn.SendReceiveObject<sendObjectType, returnObjectType>(sendingPacketTypeStr, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
        }

        /// <summary>
        /// Returns a ConnectionInfo array containing information for all connections
        /// </summary>
        /// <param name="includeClosedConnections">If true information for closed connections will also be included</param>
        /// <returns>List of ConnectionInfo containing information for all requested connections</returns>
        public static List<ConnectionInfo> AllConnectionInfo(bool includeClosedConnections = false)
        {
            List<ConnectionInfo> returnList = new List<ConnectionInfo>();

            lock (globalDictAndDelegateLocker)
            {
                List<Connection> currentConnections  = GetExistingConnection();
                foreach (Connection conn in currentConnections)
                    returnList.Add(conn.ConnectionInfo);

                if (includeClosedConnections)
                {
                    foreach (var pair in oldNetworkIdentifierToConnectionInfo)
                    {
                        foreach (var infoList in pair.Value.Values)
                        {
                            returnList.AddRange(infoList);
                        }
                    }
                }
            }

            List<ConnectionInfo> distinctList = new List<ConnectionInfo>();
            foreach (var info in returnList)
                if (!distinctList.Contains(info))
                    distinctList.Add(info);

            return distinctList;
        }

        /// <summary>
        /// Returns a ConnectionInfo array containing information for all connections which have the provided networkIdentifier. 
        /// It is also possible to include information for closed connections.
        /// </summary>
        /// <param name="networkIdentifier">The networkIdentifier corresponding to the desired connectionInfo information</param>
        /// <param name="includeClosedConnections">If true will include information for connections which are closed. Otherwise only 
        /// active connections will be included.</param>
        /// <returns>List of ConnectionInfo containing information for matching connections</returns>
        public static List<ConnectionInfo> AllConnectionInfo(ShortGuid networkIdentifier, bool includeClosedConnections = false)
        {
            List<ConnectionInfo> returnList = new List<ConnectionInfo>();

            lock (globalDictAndDelegateLocker)
            {
                List<Connection> allCurrentConnections = GetExistingConnection(networkIdentifier, ConnectionType.Undefined);
                foreach (Connection conn in allCurrentConnections)
                    returnList.Add(conn.ConnectionInfo);

                if (includeClosedConnections)
                {
                    foreach (var pair in oldNetworkIdentifierToConnectionInfo)
                    {
                        if (pair.Key == networkIdentifier)
                        {
                            foreach (var infoList in pair.Value.Values)
                                foreach (var info in infoList)
                                        returnList.Add(info);

                            break;
                        }
                    }                    
                }
            }

            List<ConnectionInfo> distinctList = new List<ConnectionInfo>();
            foreach (var info in returnList)
                if (!distinctList.Contains(info))
                    distinctList.Add(info);

            return distinctList;
        }

        /// <summary>
        /// Returns the total number of connections
        /// </summary>
        /// <returns>Total number of connections</returns>
        public static int TotalNumConnections()
        {
            lock (globalDictAndDelegateLocker)
                return GetExistingConnection().Count;
        }

        /// <summary>
        /// Returns the total number of connections where the <see cref="ConnectionInfo.RemoteEndPoint"/> matches the provided 
        /// <see cref="IPAddress"/>
        /// </summary>
        /// <param name="matchRemoteEndPointIP">The <see cref="IPAddress"/> to match</param>
        /// <returns>Total number of connections where the <see cref="ConnectionInfo.RemoteEndPoint "/> matches the provided 
        /// <see cref="IPAddress"/></returns>
        public static int TotalNumConnections(IPAddress matchRemoteEndPointIP)
        {
            lock (globalDictAndDelegateLocker)
            {
                List<Connection> allCurrentConnections = GetExistingConnection(new IPEndPoint(matchRemoteEndPointIP, 0), 
                    new IPEndPoint(IPAddress.Any, 0), 
                    ConnectionType.Undefined, 
                    ApplicationLayerProtocolStatus.Undefined);

                return allCurrentConnections.Count;
            }
        }

        /// <summary>
        /// Close all connections
        /// </summary>
        public static void CloseAllConnections()
        {
            CloseAllConnections(ConnectionType.Undefined, new EndPoint[0]);
        }

        /// <summary>
        /// Close all connections of the provided <see cref="ConnectionType"/>
        /// </summary>
        /// <param name="connectionType">The type of connections to be closed</param>
        public static void CloseAllConnections(ConnectionType connectionType)
        {
            CloseAllConnections(connectionType, new EndPoint[0]);
        }

        /// <summary>
        /// Close all connections of the provided <see cref="ConnectionType"/> except to provided <see cref="EndPoint"/> array.
        /// </summary>
        /// <param name="connectionTypeToClose">The type of connections to be closed. ConnectionType.<see cref="ConnectionType.Undefined"/> matches all types.</param>
        /// <param name="closeAllExceptTheseRemoteEndPoints">Close all except those with remote EndPoint that is provided in <see cref="EndPoint"/> array</param>
        public static void CloseAllConnections(ConnectionType connectionTypeToClose, EndPoint[] closeAllExceptTheseRemoteEndPoints)
        {
            List<Connection> connectionsToClose = new List<Connection>();

            lock (globalDictAndDelegateLocker)
            {
                List<Connection> allConnectionsOfType = GetExistingConnection(connectionTypeToClose);
                foreach (Connection conn in allConnectionsOfType)
                {
                    bool dontClose = false;
                    foreach (IPEndPoint endPointToNotClose in closeAllExceptTheseRemoteEndPoints)
                    {
                        if (conn.ConnectionInfo.RemoteEndPoint.Equals(endPointToNotClose))
                        {
                            dontClose = true;
                            break;
                        }
                    }

                    if (!dontClose) connectionsToClose.Add(conn);
                }                
            }

            if (LoggingEnabled) logger.Trace("Closing " + connectionsToClose.Count.ToString() + " connections.");

            foreach (Connection connection in connectionsToClose)
                connection.CloseConnection(false, -6);
        }

        /// <summary>
        /// Returns a list of all connections which match the provided parameters. If no parameter are provided returns all connections.
        /// </summary>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>A list of requested connections. If no matching connections exist returns empty list.</returns>
        public static List<Connection> GetExistingConnection(ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Undefined)
        {
            return GetExistingConnection(ConnectionType.Undefined, applicationLayerProtocol);
        }

        /// <summary>
        /// Returns a list of all connections matching the provided parameters.
        /// </summary>
        /// <param name="connectionType">The type of connections to return. ConnectionType.<see cref="ConnectionType.Undefined"/> matches 
        /// all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>A list of requested connections. If no matching connections exist returns empty list.</returns>
        public static List<Connection> GetExistingConnection(ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Undefined)
        {
            return GetExistingConnection(new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(IPAddress.Any, 0), connectionType, applicationLayerProtocol);
        }

        /// <summary>
        /// Returns a list of all connections matching the provided parameters.
        /// </summary>
        /// <param name="networkIdentifier">The <see cref="ShortGuid"/> corresponding with the desired peer networkIdentifier</param>
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/> 
        /// matches all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>A list of connections to the desired peer. If no matching connections exist returns empty list.</returns>
        public static List<Connection> GetExistingConnection(ShortGuid networkIdentifier, ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Undefined)
        {
            List<Connection> resultList = new List<Connection>();
            lock (globalDictAndDelegateLocker)
            {
                if (allConnectionsByIdentifier.ContainsKey(networkIdentifier))
                {
                    if (connectionType != ConnectionType.Undefined && allConnectionsByIdentifier[networkIdentifier].ContainsKey(connectionType))
                    {
                        //We have connections of the correct type to the provided identifier
                        //need to check if the application layer protocol has been enabled
                        foreach (Connection connection in allConnectionsByIdentifier[networkIdentifier][connectionType])
                        {
                            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined || connection.ConnectionInfo.ApplicationLayerProtocol == applicationLayerProtocol)
                                resultList.Add(connection);
                        }
                    }
                    else
                    {
                        foreach (ConnectionType connType in allConnectionsByIdentifier[networkIdentifier].Keys)
                        {
                            foreach (Connection connection in allConnectionsByIdentifier[networkIdentifier][connType])
                            {
                                if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined || connection.ConnectionInfo.ApplicationLayerProtocol == applicationLayerProtocol)
                                    resultList.Add(connection);
                            }
                        }
                    }
                }
            }

            if (LoggingEnabled) logger.Trace("RetrieveConnection by networkIdentifier='" + networkIdentifier + "' and connectionType='" + connectionType.ToString() + "'. Returning list of " + resultList.Count.ToString() + " connections.");

            return resultList;
        }

        /// <summary>
        /// Returns a list of all connections matching the provided parameters.
        /// </summary>
        /// <param name="remoteEndPoint">Remote EndPoint corresponding with the desired connection. Use IPAddress.Any to match all 
        /// IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="localEndPoint">Local EndPoint corresponding with the desired connection. Use IPAddress.Any to match all 
        /// IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/> 
        /// matches all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>A list of connections to the desired peer. If no matching connections exists returns empty list.</returns>
        public static List<Connection> GetExistingConnection(EndPoint remoteEndPoint, EndPoint localEndPoint, ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Undefined)
        {
            if (remoteEndPoint == null) throw new ArgumentNullException("remoteEndPoint", "remoteEndPoint may not be null.");
            if (localEndPoint == null) throw new ArgumentNullException("localEndPoint", "localEndPoint may not be null.");

            List<Connection> result = new List<Connection>();

            lock (globalDictAndDelegateLocker)
            {
                foreach (ConnectionType currentConnectionType in allConnectionsByEndPoint.Keys)
                {
                    if ((connectionType == ConnectionType.Undefined || connectionType == currentConnectionType) &&
                        allConnectionsByEndPoint[currentConnectionType].Count > 0)
                    {
                        //For each connection type we create a list of matching IPEndPoints. 
                        //[0] is remoteEndPoint, [1] will be localEndPoint
                        Dictionary<EndPoint, List<EndPoint>> matchedEndPoints = new Dictionary<EndPoint, List<EndPoint>>();
                        List<EndPoint> connectionTypeRemoteEndPointKeys = new List<EndPoint>(allConnectionsByEndPoint[currentConnectionType].Keys);

                        //We can only use the match if we can successfully cast to IPEndPoint
                        if (connectionTypeRemoteEndPointKeys.Count > 0 &&
                            connectionTypeRemoteEndPointKeys[0].GetType() == typeof(IPEndPoint) &&
                            remoteEndPoint.GetType() == typeof(IPEndPoint) &&
                            localEndPoint.GetType() == typeof(IPEndPoint))
                        {
                            IPEndPoint remoteIPEndPoint = remoteEndPoint as IPEndPoint;
                            IPEndPoint localIPEndPoint = localEndPoint as IPEndPoint;

                            #region Match Remote IPEndPoint
                            //If the remoteEndPoint only has a port specified
                            if ((remoteIPEndPoint.Address == IPAddress.Any || remoteIPEndPoint.Address == IPAddress.IPv6Any) &&
                                remoteIPEndPoint.Port > 0)
                            {
                                //If the provided IP is match any then we look for matching ports
                                foreach (IPEndPoint endPoint in connectionTypeRemoteEndPointKeys)
                                {
                                    if (endPoint.Port == remoteIPEndPoint.Port)
                                        matchedEndPoints.Add(endPoint, new List<EndPoint>());
                                }
                            }
                            else if ((remoteIPEndPoint.Address == IPAddress.Any || remoteIPEndPoint.Address == IPAddress.IPv6Any) &&
                                remoteIPEndPoint.Port == 0)
                            {
                                foreach (IPEndPoint endPoint in connectionTypeRemoteEndPointKeys)
                                    matchedEndPoints.Add(endPoint, new List<EndPoint>());
                            }
                            else if ((remoteIPEndPoint.Address != IPAddress.Any && remoteIPEndPoint.Address != IPAddress.IPv6Any) &&
                                remoteIPEndPoint.Port == 0)
                            {
                                //If the provided IP is set but the port is 0 we aim to match the IPAddress
                                foreach (IPEndPoint endPoint in connectionTypeRemoteEndPointKeys)
                                {
                                    if (endPoint.Address.Equals(remoteIPEndPoint.Address))
                                        matchedEndPoints.Add(endPoint, new List<EndPoint>());
                                }
                            }
                            else
                            {
                                if (allConnectionsByEndPoint[currentConnectionType].ContainsKey(remoteIPEndPoint))
                                    matchedEndPoints.Add(remoteIPEndPoint, new List<EndPoint>());
                            }
                            #endregion

                            #region Match Local IPEndPoint
                            foreach (KeyValuePair<EndPoint, List<EndPoint>> keyPair in matchedEndPoints)
                            {
                                //If the localEndPoint only has a port specified
                                if ((localIPEndPoint.Address == IPAddress.Any || localIPEndPoint.Address == IPAddress.IPv6Any) &&
                                    localIPEndPoint.Port > 0)
                                {
                                    //If the provided IP is match any then we look for matching ports
                                    foreach (IPEndPoint endPoint in allConnectionsByEndPoint[currentConnectionType][keyPair.Key].Keys)
                                    {
                                        if (endPoint.Port == localIPEndPoint.Port)
                                            keyPair.Value.Add(endPoint);
                                    }
                                }
                                else if ((localIPEndPoint.Address == IPAddress.Any || localIPEndPoint.Address == IPAddress.IPv6Any) &&
                                    localIPEndPoint.Port == 0)
                                {
                                    foreach (IPEndPoint endPoint in allConnectionsByEndPoint[currentConnectionType][keyPair.Key].Keys)
                                        keyPair.Value.Add(endPoint);
                                }
                                else if ((localIPEndPoint.Address != IPAddress.Any && localIPEndPoint.Address != IPAddress.IPv6Any) &&
                                    localIPEndPoint.Port == 0)
                                {
                                    //If the provided IP is set but the port is 0 we aim to match the IPAddress
                                    foreach (IPEndPoint endPoint in allConnectionsByEndPoint[currentConnectionType][keyPair.Key].Keys)
                                    {
                                        if (endPoint.Address.Equals(localIPEndPoint.Address))
                                            keyPair.Value.Add(endPoint);
                                    }
                                }
                                else
                                {
                                    if (allConnectionsByEndPoint[currentConnectionType][keyPair.Key].ContainsKey(localIPEndPoint))
                                        keyPair.Value.Add(localIPEndPoint);
                                }
                            }
                            #endregion
                        }
#if NET35 || NET4
                        else if (connectionTypeRemoteEndPointKeys.Count > 0 &&
                            connectionTypeRemoteEndPointKeys[0].GetType() == typeof(BluetoothEndPoint) &&
                            connectionType == ConnectionType.Bluetooth)
                        {
                            //////////////////////////////////////////////////////////////////////////
                            //                  IMPORTANT!!!                                        //
                            //Using BLuetoothEndPOint.HasPort to define any port. When we move away //
                            //from 32Feet change this!!                                             //
                            //////////////////////////////////////////////////////////////////////////
                            BluetoothEndPoint remoteBTEndPoint = remoteEndPoint as BluetoothEndPoint;
                            BluetoothEndPoint localBTEndPoint = localEndPoint as BluetoothEndPoint;

                            #region Match Remote IPEndPoint
                            //If the remoteEndPoint only has a port specified
                            if (remoteBTEndPoint.Address == BluetoothAddress.None && remoteBTEndPoint.HasPort)
                            {
                                //If the provided IP is match any then we look for matching ports
                                foreach (BluetoothEndPoint endPoint in connectionTypeRemoteEndPointKeys)
                                {
                                    if (endPoint.Port == remoteBTEndPoint.Port)
                                        matchedEndPoints.Add(endPoint, new List<EndPoint>());
                                }
                            }
                            else if (remoteBTEndPoint.Address == BluetoothAddress.None && !remoteBTEndPoint.HasPort)
                            {
                                foreach (BluetoothEndPoint endPoint in connectionTypeRemoteEndPointKeys)
                                    matchedEndPoints.Add(endPoint, new List<EndPoint>());
                            }
                            else if (remoteBTEndPoint.Address != BluetoothAddress.None && !remoteBTEndPoint.HasPort)
                            {
                                //If the provided IP is set but the port is 0 we aim to match the IPAddress
                                foreach (BluetoothEndPoint endPoint in connectionTypeRemoteEndPointKeys)
                                {
                                    if (endPoint.Address.Equals(remoteBTEndPoint.Address))
                                        matchedEndPoints.Add(endPoint, new List<EndPoint>());
                                }
                            }
                            else
                            {
                                if (allConnectionsByEndPoint[currentConnectionType].ContainsKey(remoteBTEndPoint))
                                    matchedEndPoints.Add(remoteBTEndPoint, new List<EndPoint>());
                            }
                            #endregion

                            #region Match Local IPEndPoint
                            foreach (KeyValuePair<EndPoint, List<EndPoint>> keyPair in matchedEndPoints)
                            {
                                //If the localEndPoint only has a port specified
                                if (localBTEndPoint.Address == BluetoothAddress.None && localBTEndPoint.HasPort)
                                {
                                    //If the provided IP is match any then we look for matching ports
                                    foreach (BluetoothEndPoint endPoint in allConnectionsByEndPoint[currentConnectionType][keyPair.Key].Keys)
                                    {
                                        if (endPoint.Port == localBTEndPoint.Port)
                                            keyPair.Value.Add(endPoint);
                                    }
                                }
                                else if (localBTEndPoint.Address == BluetoothAddress.None && !localBTEndPoint.HasPort)
                                {
                                    foreach (BluetoothEndPoint endPoint in allConnectionsByEndPoint[currentConnectionType][keyPair.Key].Keys)
                                        keyPair.Value.Add(endPoint);
                                }
                                else if (localBTEndPoint.Address != BluetoothAddress.None && !localBTEndPoint.HasPort)
                                {
                                    //If the provided IP is set but the port is 0 we aim to match the IPAddress
                                    foreach (BluetoothEndPoint endPoint in allConnectionsByEndPoint[currentConnectionType][keyPair.Key].Keys)
                                    {
                                        if (endPoint.Address.Equals(localBTEndPoint.Address))
                                            keyPair.Value.Add(endPoint);
                                    }
                                }
                                else
                                {
                                    if (allConnectionsByEndPoint[currentConnectionType][keyPair.Key].ContainsKey(localBTEndPoint))
                                        keyPair.Value.Add(localBTEndPoint);
                                }
                            }
                            #endregion
                        }
#endif
                        else if (allConnectionsByEndPoint[currentConnectionType].ContainsKey(remoteEndPoint) &&
                            allConnectionsByEndPoint[currentConnectionType][remoteEndPoint].ContainsKey(localEndPoint))
                        {
                            matchedEndPoints.Add(remoteEndPoint, new List<EndPoint>() { localEndPoint });
                        }

                        //Now pick out all of the matched IPEndPoints and see if there are matched connections
                        foreach (EndPoint currentRemoteEndPoint in matchedEndPoints.Keys)
                        {
                            foreach (EndPoint currentLocalEndPoint in matchedEndPoints[currentRemoteEndPoint])
                            {
                                if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined ||
                                    allConnectionsByEndPoint[currentConnectionType][currentRemoteEndPoint][currentLocalEndPoint].ConnectionInfo.ApplicationLayerProtocol == applicationLayerProtocol)
                                    result.Add(allConnectionsByEndPoint[currentConnectionType][currentRemoteEndPoint][currentLocalEndPoint]);
                            }
                        }
                    }
                }
            }

            if (LoggingEnabled)
            { 
                if (result.Count == 0)
                    logger.Trace("RetrieveConnection by remoteEndPoint='" + remoteEndPoint.ToString() + "', localEndPoint='"+localEndPoint.ToString()+"', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol.ToString() + "'. No matching connections found.");
                else
                    logger.Trace("RetrieveConnection by remoteEndPoint='" + remoteEndPoint.ToString() + "', localEndPoint='" + localEndPoint.ToString() + "', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol.ToString() + "'. " + result.Count.ToString() + " matching connections found.");
            }

            return result;
        }

        /// <summary>
        /// Retrieve an existing connection with the provided ConnectionInfo. Internally matches connection based on IPEndPoint, ConnectionType,
        /// NetworkIdentifier and ApplicationLayerProtocol status.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo corresponding with the desired connection.</param>
        /// <returns>The desired connection. If no matching connection exists returns null.</returns>
        public static Connection GetExistingConnection(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null) throw new ArgumentNullException("Provided ConnectionInfo cannot be null.", "connectionInfo");
            if (connectionInfo.ConnectionType == ConnectionType.Undefined) throw new ArgumentException("Provided ConnectionInfo does not specify a connection type.", "connectionInfo");
            if (connectionInfo.RemoteEndPoint == null && connectionInfo.LocalEndPoint == null) throw new ArgumentNullException("connectionInfo", "Provided ConnectionInfo must specify either RemoteEndPoint or LocalEndPoint.");

            List<Connection> result;
            lock (globalDictAndDelegateLocker)
            {
                //If the remote end point has not yet been set for the provided info we use the localEndPoint value
                //and match all localEndPoints
                if (connectionInfo.RemoteEndPoint == null)
                    result = GetExistingConnection(connectionInfo.LocalEndPoint, new IPEndPoint(IPAddress.Any, 0), connectionInfo.ConnectionType, connectionInfo.ApplicationLayerProtocol);
                else
                    result = GetExistingConnection(connectionInfo.RemoteEndPoint, connectionInfo.LocalEndPoint, connectionInfo.ConnectionType, connectionInfo.ApplicationLayerProtocol);
            }

            if (LoggingEnabled)
            {
                if (result.Count == 0)
                    logger.Trace("RetrieveConnection by connectionInfo='" + connectionInfo + "'. No matching connection was found.");
                else
                    logger.Trace("RetrieveConnection by connectionInfo='" + connectionInfo + "'. Matching connection was found.");
            }

            if (result.Count > 0 && result[0].ConnectionInfo.NetworkIdentifier == connectionInfo.NetworkIdentifier)
                return result[0];
            else
                return null;
        }

        /// <summary>
        /// Check if a connection with the provided ConnectionInfo exists. Internally matches connection based on IPEndPoint, ConnectionType,
        /// NetworkIdentifier and ApplicationLayerProtocol status.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo corresponding with the desired connection</param>
        /// <returns>True if a matching connection exists, otherwise false</returns>
        public static bool ConnectionExists(ConnectionInfo connectionInfo)
        {
            if (LoggingEnabled) logger.Trace("Checking for existing connection by connectionInfo='" + connectionInfo +"'");

            return GetExistingConnection(connectionInfo) != null;
        }

        /// <summary>
        /// Check if a connection exists with the provided parameters.
        /// </summary>
        /// <param name="networkIdentifier">The <see cref="ShortGuid"/> corresponding with the desired peer networkIdentifier</param>
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/> 
        /// matches all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>True if a matching connection exists, otherwise false</returns>
        public static bool ConnectionExists(ShortGuid networkIdentifier, ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled)
        {
            if (LoggingEnabled)
                logger.Trace("Checking for existing connection by identifier='" + networkIdentifier + "', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol.ToString() + "'.");

            return GetExistingConnection(networkIdentifier, connectionType, applicationLayerProtocol).Count > 0;
        }

        /// <summary>
        /// Check if a connection exists with the provided parameters.
        /// </summary>
        /// <param name="remoteEndPoint">Remote EndPoint corresponding with the desired connection. Use IPAddress.Any to match all 
        /// IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="localEndPoint">Local EndPoint corresponding with the desired connection. Use IPAddress.Any to match all 
        /// IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/>
        /// matches all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>True if a matching connection exists, otherwise false</returns>
        public static bool ConnectionExists(EndPoint remoteEndPoint, EndPoint localEndPoint, ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled)
        {
            if (remoteEndPoint == null) throw new ArgumentNullException("remoteEndPoint");
            if (localEndPoint == null) throw new ArgumentNullException("localEndPoint");

            if (LoggingEnabled)
                logger.Trace("Checking for existing connection by remoteEndPoint='" + remoteEndPoint.ToString() +
                    "', localEndPoint='" + localEndPoint.ToString() + "', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol.ToString() + "'.");

            return GetExistingConnection(remoteEndPoint, localEndPoint, connectionType, applicationLayerProtocol).Count > 0;
        }

        /// <summary>
        /// Removes the reference to the provided connection from within networkComms. DOES NOT CLOSE THE CONNECTION. Returns true if 
        /// the provided connection reference existed and was removed, false otherwise.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="maintainConnectionInfoHistory"></param>
        /// <returns></returns>
        internal static bool RemoveConnectionReference(Connection connection, bool maintainConnectionInfoHistory = true)
        {
            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Entering RemoveConnectionReference for " + connection.ConnectionInfo);

            if (connection.ConnectionInfo.ConnectionState == ConnectionState.Established && !(connection.ConnectionInfo.ConnectionState == ConnectionState.Shutdown))
                throw new ConnectionShutdownException("A connection can only be removed once correctly shutdown.");

            bool returnValue = false;

            //Ensure connection references are removed from networkComms
            //Once we think we have closed the connection it's time to get rid of our other references
            lock (globalDictAndDelegateLocker)
            {
                #region Update NetworkComms Connection Dictionaries
                ShortGuid currentNetworkIdentifier = connection.ConnectionInfo.NetworkIdentifier;

                //We establish whether we have already done this step
                if ((allConnectionsByIdentifier.ContainsKey(currentNetworkIdentifier) &&
                    allConnectionsByIdentifier[currentNetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType) &&
                    allConnectionsByIdentifier[currentNetworkIdentifier][connection.ConnectionInfo.ConnectionType].Contains(connection))
                    ||
                    (allConnectionsByEndPoint.ContainsKey(connection.ConnectionInfo.ConnectionType) &&
                    allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType].ContainsKey(connection.ConnectionInfo.RemoteEndPoint) &&
                    allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][connection.ConnectionInfo.RemoteEndPoint].ContainsKey(connection.ConnectionInfo.LocalEndPoint) &&
                    allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint] == connection))
                {
                    //Maintain a reference if this is our first connection close
                    returnValue = true;
                }

                //Keep a reference of the connection for possible debugging later
                if (maintainConnectionInfoHistory)
                {
                    if (oldNetworkIdentifierToConnectionInfo.ContainsKey(currentNetworkIdentifier))
                    {
                        if (oldNetworkIdentifierToConnectionInfo[currentNetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType))
                            oldNetworkIdentifierToConnectionInfo[currentNetworkIdentifier][connection.ConnectionInfo.ConnectionType].Add(connection.ConnectionInfo);
                        else
                            oldNetworkIdentifierToConnectionInfo[currentNetworkIdentifier].Add(connection.ConnectionInfo.ConnectionType, new List<ConnectionInfo>() { connection.ConnectionInfo });
                    }
                    else
                        oldNetworkIdentifierToConnectionInfo.Add(currentNetworkIdentifier, new Dictionary<ConnectionType, List<ConnectionInfo>>() { { connection.ConnectionInfo.ConnectionType, new List<ConnectionInfo>() { connection.ConnectionInfo } } });
                }

                if (allConnectionsByIdentifier.ContainsKey(currentNetworkIdentifier) &&
                        allConnectionsByIdentifier[currentNetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType))
                {
                    if (allConnectionsByIdentifier[currentNetworkIdentifier][connection.ConnectionInfo.ConnectionType].Contains(connection))
                        allConnectionsByIdentifier[currentNetworkIdentifier][connection.ConnectionInfo.ConnectionType].Remove(connection);

                    //Remove the connection type reference if it is empty
                    if (allConnectionsByIdentifier[currentNetworkIdentifier][connection.ConnectionInfo.ConnectionType].Count == 0)
                        allConnectionsByIdentifier[currentNetworkIdentifier].Remove(connection.ConnectionInfo.ConnectionType);

                    //Remove the identifier reference
                    if (allConnectionsByIdentifier[currentNetworkIdentifier].Count == 0)
                        allConnectionsByIdentifier.Remove(currentNetworkIdentifier);

                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Removed connection reference by ID for " + connection.ConnectionInfo);
                }

                //We can now remove this connection by end point as well
                if (allConnectionsByEndPoint.ContainsKey(connection.ConnectionInfo.ConnectionType))
                {
                    if (allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType].ContainsKey(connection.ConnectionInfo.RemoteEndPoint) &&
                        allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][connection.ConnectionInfo.RemoteEndPoint].ContainsKey(connection.ConnectionInfo.LocalEndPoint) &&
                        allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint] == connection)
                    {
                        allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][connection.ConnectionInfo.RemoteEndPoint].Remove(connection.ConnectionInfo.LocalEndPoint);

                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Removed connection reference by endPoint for " + connection.ConnectionInfo);
                    }

                    if (allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType].ContainsKey(connection.ConnectionInfo.RemoteEndPoint) &&
                        allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][connection.ConnectionInfo.RemoteEndPoint].Count == 0)
                        allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType].Remove(connection.ConnectionInfo.RemoteEndPoint);

                    //If this was the last connection type for this endpoint we can remove the endpoint reference as well
                    if (allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType].Count == 0)
                        allConnectionsByEndPoint.Remove(connection.ConnectionInfo.ConnectionType);
                }
                #endregion
            }

            return returnValue;
        }

        /// <summary>
        /// Adds a reference by EndPoint to the provided connection within networkComms.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="remoteEndPointToUse">An optional override which forces a specific remote EndPoint</param>
        /// <param name="localEndPointToUse">An optional override which forces a specific local EndPoint</param>
        internal static void AddConnectionReferenceByRemoteEndPoint(Connection connection, EndPoint remoteEndPointToUse = null, EndPoint localEndPointToUse = null)
        {
            if (NetworkComms.LoggingEnabled)
                NetworkComms.Logger.Trace("Adding connection reference by endPoint. Connection='"+connection.ConnectionInfo+"'." +
                    (remoteEndPointToUse != null ? " Provided override endPoint of " + remoteEndPointToUse.ToString() : ""));

            //If the remoteEndPoint is IPAddress.Any we don't record it by endPoint
            #region Unset remote endPoint
            if ((connection.ConnectionInfo.RemoteEndPoint.GetType() == typeof(IPEndPoint) &&
                (connection.ConnectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.Any) || connection.ConnectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.IPv6Any))) ||
                (remoteEndPointToUse!=null && remoteEndPointToUse.GetType() == typeof(IPEndPoint) &&
                (((IPEndPoint)remoteEndPointToUse).Address.Equals(IPAddress.Any) ||
                ((IPEndPoint)remoteEndPointToUse).Address.Equals(IPAddress.IPv6Any))))
                return;
            #endregion

            //Validate incoming remote endPoint address if AllowedIncomingIPRanges is set
            #region IPConnection Security Features
            if (connection is IPConnection)
            {
                #region Check AllowedIncomingIPRanges
                if (IPConnection.AllowedIncomingIPRanges != null && connection.ConnectionInfo.ServerSide)
                {
                    //If remoteEndPointToUse != null validate using that
                    if (remoteEndPointToUse != null && remoteEndPointToUse.GetType() == typeof(IPEndPoint) &&
                        !IPRange.Contains(IPConnection.AllowedIncomingIPRanges, ((IPEndPoint)remoteEndPointToUse).Address))
                        throw new ConnectionSetupException("Connection remoteEndPoint (" + remoteEndPointToUse.ToString() + ") refused as it is not authorised based upon the AllowedIncomingIPRanges.");
                    //Otherwise use connection.ConnectionInfo.RemoteEndPoint
                    else if (connection.ConnectionInfo.RemoteEndPoint.GetType() == typeof(IPEndPoint) &&
                        !IPRange.Contains(IPConnection.AllowedIncomingIPRanges, connection.ConnectionInfo.RemoteIPEndPoint.Address))
                        throw new ConnectionSetupException("Connection remoteEndPoint (" + connection.ConnectionInfo.RemoteIPEndPoint.ToString() + ") refused as it is not authorised based upon the AllowedIncomingIPRanges.");
                }
                #endregion

                //Check for connection initialise in DOS protection
                #region DOS Protection
                if (IPConnection.DOSProtection.Enabled)
                {
                    //If remoteEndPointToUse != null validate using that
                    if (remoteEndPointToUse != null && remoteEndPointToUse.GetType() == typeof(IPEndPoint))
                    {
                        IPEndPoint remoteIPEndPoint = (IPEndPoint)remoteEndPointToUse;

                        //This may well be an updated endPoint. We only log the connection initialise if the endpoint addresses are different
                        if (!remoteIPEndPoint.Address.Equals(connection.ConnectionInfo.RemoteIPEndPoint.Address) &&
                            IPConnection.DOSProtection.LogConnectionInitialise(remoteIPEndPoint.Address))
                            throw new ConnectionSetupException("Connection remoteEndPoint (" + remoteIPEndPoint.ToString() + ") has been banned for " + IPConnection.DOSProtection.BanTimeout + " due to a high number of connection initialisations.");
                        else if (IPConnection.DOSProtection.RemoteIPAddressBanned(remoteIPEndPoint.Address))
                            throw new ConnectionSetupException("Connection remoteEndPoint (" + remoteIPEndPoint.ToString() + ") is currently banned by DOS protection.");
                    }
                    //Otherwise use connection.ConnectionInfo.RemoteEndPoint
                    else if (connection.ConnectionInfo.RemoteEndPoint.GetType() == typeof(IPEndPoint))
                    {
                        if (IPConnection.DOSProtection.LogConnectionInitialise(connection.ConnectionInfo.RemoteIPEndPoint.Address))
                            throw new ConnectionSetupException("Connection remoteEndPoint (" + connection.ConnectionInfo.RemoteIPEndPoint.ToString() + ") has been banned for " + IPConnection.DOSProtection.BanTimeout + " due to a high number of connection initialisations.");
                        else if (IPConnection.DOSProtection.RemoteIPAddressBanned(connection.ConnectionInfo.RemoteIPEndPoint.Address))
                            throw new ConnectionSetupException("Connection remoteEndPoint (" + connection.ConnectionInfo.RemoteIPEndPoint.ToString() + ") is currently banned by DOS protection.");
                    }
                }
                #endregion
            }
            #endregion

            if (connection.ConnectionInfo.ConnectionState == ConnectionState.Established || connection.ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                throw new ConnectionSetupException("Connection reference by endPoint should only be added before a connection is established. This is to prevent duplicate connections.");

            if (remoteEndPointToUse == null) remoteEndPointToUse = connection.ConnectionInfo.RemoteEndPoint;
            if (localEndPointToUse == null) localEndPointToUse = connection.ConnectionInfo.LocalEndPoint;

            //We can double check for an existing connection here first so that it occurs outside the lock
            //We look for a connection with either ApplicationProtocolStatus as the endPoint should not be in use
            List<Connection> existingConnection = GetExistingConnection(remoteEndPointToUse, connection.ConnectionInfo.LocalEndPoint, connection.ConnectionInfo.ConnectionType, ApplicationLayerProtocolStatus.Undefined);
            if (existingConnection.Count > 0 && connection != existingConnection[0] &&
                ((existingConnection[0].ConnectionInfo.ConnectionType == ConnectionType.UDP && existingConnection[0].ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled) || 
                existingConnection[0].ConnectionInfo.ConnectionState == ConnectionState.Established)) 
                existingConnection[0].ConnectionAlive();

            //For UDP connections which do not enable the application protocol we can't check the remote
            //peer. We choose here to assume the new connection is the better choice, so we close the existing connection
            if (existingConnection.Count > 0 && connection != existingConnection[0] &&
                existingConnection[0].ConnectionInfo.ConnectionType == ConnectionType.UDP &&
                existingConnection[0].ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
                existingConnection[0].CloseConnection(false, -12);

            //How do we prevent multiple threads from trying to create a duplicate connection??
            lock (globalDictAndDelegateLocker)
            {
                existingConnection = GetExistingConnection(remoteEndPointToUse, connection.ConnectionInfo.LocalEndPoint, connection.ConnectionInfo.ConnectionType, ApplicationLayerProtocolStatus.Undefined);
                //We now check for an existing connection again from within the lock
                if (existingConnection.Count > 0)
                {
                    //If a connection still exist we don't assume it is the same as above
                    if (existingConnection[0] != connection)
                    {
                        throw new DuplicateConnectionException("A different connection already exists with the desired endPoint (" + remoteEndPointToUse.ToString() + "). This can occur if the connections have different ApplicationProtocolLayer statuses or two peers try to connect to each other simultaneously. New connection is " + (existingConnection[0].ConnectionInfo.ServerSide ? "server side" : "client side") + " - " + connection.ConnectionInfo +
                            ". Existing connection is " + (existingConnection[0].ConnectionInfo.ServerSide ? "server side" : "client side") + ", ConnState:" + existingConnection[0].ConnectionInfo.ConnectionState.ToString() + " - " + ((existingConnection[0].ConnectionInfo.ConnectionState == ConnectionState.Establishing || existingConnection[0].ConnectionInfo.ConnectionState == ConnectionState.Undefined) ? "CreationTime:" + existingConnection[0].ConnectionInfo.ConnectionCreationTime.ToString() : "EstablishedTime:" + existingConnection[0].ConnectionInfo.ConnectionEstablishedTime.ToString()) + " - " + existingConnection[0].ConnectionInfo);
                    }
                    else
                    {
                        //We have just tried to add the same reference twice, no need to do anything this time around
                    }
                }
                else
                {
#if FREETRIAL
                    //If this is a free trial we only allow a single connection. We will throw an exception if any connections already exist
                    if (TotalNumConnections() != 0)
                        throw new NotSupportedException("Unable to create connection as this version of NetworkComms.Net is limited to only one connection. Please purchase a commerical license from www.networkcomms.net which supports an unlimited number of connections.");
#endif

                    //Add reference to the endPoint dictionary
                    if (allConnectionsByEndPoint.ContainsKey(connection.ConnectionInfo.ConnectionType))
                    {
                        if (allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType].ContainsKey(remoteEndPointToUse))
                        {
                            if (allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][remoteEndPointToUse].ContainsKey(localEndPointToUse))
                                throw new Exception("Idiot check fail. The method ConnectionExists should have prevented execution getting here!!");
                            else
                                allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType][remoteEndPointToUse].Add(localEndPointToUse, connection);
                        }
                        else
                            allConnectionsByEndPoint[connection.ConnectionInfo.ConnectionType].Add(remoteEndPointToUse, new Dictionary<EndPoint, Connection>() { { localEndPointToUse, connection } });
                    }
                    else
                        allConnectionsByEndPoint.Add(connection.ConnectionInfo.ConnectionType, new Dictionary<EndPoint, Dictionary<EndPoint, Connection>>() { { remoteEndPointToUse, new Dictionary<EndPoint, Connection>() { { localEndPointToUse, connection } } } });

                    if (NetworkComms.LoggingEnabled)
                        NetworkComms.Logger.Trace("Completed adding connection reference by endPoint. Connection='" + connection.ConnectionInfo + "'.");
                }
            }
        }

        /// <summary>
        /// Update the endPoint reference for the provided connection with the newEndPoint. If there is no change just returns
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="newRemoteEndPoint"></param>
        /// <param name="newLocalEndPoint"></param>
        internal static void UpdateConnectionReferenceByEndPoint(Connection connection, EndPoint newRemoteEndPoint, EndPoint newLocalEndPoint)
        {
            if (NetworkComms.LoggingEnabled)
                NetworkComms.Logger.Trace("Updating connection reference by endPoint. Connection='" + connection.ConnectionInfo + "'." + (newRemoteEndPoint != null ? " Provided new endPoint of " + newRemoteEndPoint.ToString() : ""));

            if (!connection.ConnectionInfo.RemoteEndPoint.Equals(newRemoteEndPoint) || !connection.ConnectionInfo.LocalEndPoint.Equals(newLocalEndPoint))
            {
                lock (globalDictAndDelegateLocker)
                {
                    RemoveConnectionReference(connection, false);
                    AddConnectionReferenceByRemoteEndPoint(connection, newRemoteEndPoint, newLocalEndPoint);
                }
            }
        }

        /// <summary>
        /// Add a reference by networkIdentifier to the provided connection within NetworkComms.Net. Requires a reference by 
        /// EndPoint to already exist.
        /// </summary>
        /// <param name="connection"></param>
        internal static void AddConnectionReferenceByIdentifier(Connection connection)
        {
            if (!(connection.ConnectionInfo.ConnectionState == ConnectionState.Established) || connection.ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                throw new ConnectionSetupException("Connection reference by identifier should only be added once a connection is established. This is to prevent duplicate connections.");

            if (connection.ConnectionInfo.NetworkIdentifier == ShortGuid.Empty)
                throw new ConnectionSetupException("Should not be calling AddConnectionByIdentifierReference unless the connection remote identifier has been set.");

            if (NetworkComms.LoggingEnabled)
                NetworkComms.Logger.Trace("Adding connection reference by identifier. Connection=" + connection.ConnectionInfo + ".");

            lock (globalDictAndDelegateLocker)
            {
                //There should already be a reference to this connection in the endPoint dictionary
                if (!ConnectionExists(connection.ConnectionInfo.RemoteEndPoint, connection.ConnectionInfo.LocalEndPoint, connection.ConnectionInfo.ConnectionType, connection.ConnectionInfo.ApplicationLayerProtocol))
                    throw new ConnectionSetupException("A reference by identifier should only be added if a reference by endPoint already exists.");

                //Check for an existing reference first, if there is one and it matches this connection then no worries
                if (allConnectionsByIdentifier.ContainsKey(connection.ConnectionInfo.NetworkIdentifier))
                {
                    if (allConnectionsByIdentifier[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType))
                    {
                        if (!allConnectionsByIdentifier[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType].Contains(connection))
                        {
                            foreach (var current in allConnectionsByIdentifier[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType])
                            {
                                if (current.ConnectionInfo.RemoteEndPoint.Equals(connection.ConnectionInfo.RemoteEndPoint) &&
                                    current.ConnectionInfo.LocalEndPoint.Equals(connection.ConnectionInfo.LocalEndPoint))
                                    throw new ConnectionSetupException("A different connection to the same remoteEndPoint and localEndPoint already exists. Duplicate connections should be prevented elsewhere. Existing connection " + current.ConnectionInfo + ", new connection " + connection.ConnectionInfo);
                            }

                            //Only if we make it this far do we add another connection reference
                            allConnectionsByIdentifier[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType].Add(connection);
                        }
                        else
                        {
                            //We are trying to add the same connection twice, so just do nothing here.
                        }
                    }
                    else
                        allConnectionsByIdentifier[connection.ConnectionInfo.NetworkIdentifier].Add(connection.ConnectionInfo.ConnectionType, new List<Connection>() { connection });
                }
                else
                    allConnectionsByIdentifier.Add(connection.ConnectionInfo.NetworkIdentifier, new Dictionary<ConnectionType, List<Connection>>() { { connection.ConnectionInfo.ConnectionType, new List<Connection>() {connection}} });
            }
        }
        #endregion
    }
}