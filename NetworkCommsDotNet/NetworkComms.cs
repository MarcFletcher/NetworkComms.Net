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
using System.Net.Sockets;
using System.Threading.Tasks;
using SerializerBase;
using SerializerBase.Protobuf;
using System.Collections;
using System.Net.NetworkInformation;
using Common.Logging;
using System.Diagnostics;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// NetworkComms.net. C# networking made easy.
    /// </summary>
    public static class NetworkComms
    {
        #region Local Host Information
        /// <summary>
        /// Returns the current machine hostname
        /// </summary>
        public static string HostName
        {
            get { return Dns.GetHostName(); }
        }

        /// <summary>
        /// The preferred IP prefixs if network comms may try to auto select the ip address
        /// </summary>
        static string[] preferredIPPrefixs = null;

        /// <summary>
        /// Setting preferred IP prefixs will help network comms select the correct listening ip address. An alternative is to set ListenOnAllInterfaces to true.
        /// Correct format is string[] { "192.168", "213.111.10" }.
        /// If multiple prefixs are provided the lower index prefix if found takes priority
        /// </summary>
        public static string[] PreferredIPPrefixs
        {
            get { return preferredIPPrefixs; }
            set { preferredIPPrefixs = value; }
        }

        /// <summary>
        /// Another way of selecting the desired adaptor is by name
        /// </summary>
        static string[] allowedAdaptorNames = null;

        /// <summary>
        /// If a prefered adaptor name is provided, i.e. eth0, en0 etc. networkComms.net will try to listen on that adaptor.
        /// </summary>
        public static string[] AllowedAdaptorNames
        {
            get { return allowedAdaptorNames; }
            set { allowedAdaptorNames = value; }
        }

        /// <summary>
        /// Returns all possible ipV4 addresses. Considers networkComms.PreferredIPPrefix and networkComms.PreferredAdaptorName. If preferredIPPRefix has been set ranks be descending preference. i.e. Most preffered at [0].
        /// </summary>
        /// <returns></returns>
        public static List<IPAddress> AllAvailableLocalIPs()
        {
            //This is probably the most awesome linq expression ever
            //It loops through every known network adaptor and tries to pull out any 
            //ip addresses which match the provided prefixes
            //If multiple matches are found then we rank by prefix order at the end
            //Credit: M.Fletcher & M.Dean

            return (from current in NetworkInterface.GetAllNetworkInterfaces()
                    where
                        //First we need to select interfaces that contain address information
                    (from inside in current.GetIPProperties().UnicastAddresses
                     where (inside.Address.AddressFamily == AddressFamily.InterNetwork || inside.Address.AddressFamily == AddressFamily.InterNetworkV6) &&
                        (allowedAdaptorNames == null ? true :  allowedAdaptorNames.Contains(current.Id))
                     //&& (preferredIPPrefix == null ? true : preferredIPPrefix.Contains(inside.Address.ToString(), new IPComparer()))  
                     select inside).Count() > 0
                    //We only want adaptors which are operational
                    //&& current.OperationalStatus == OperationalStatus.Up //This line causes problems in mono
                    select
                    (
                        //Once we have adaptors that contain address information we are after the address
                    from inside in current.GetIPProperties().UnicastAddresses
                    where (inside.Address.AddressFamily == AddressFamily.InterNetwork || inside.Address.AddressFamily == AddressFamily.InterNetworkV6) &&
                        (allowedAdaptorNames == null ? true : allowedAdaptorNames.Contains(current.Id))
                    //&& (preferredIPPrefix == null ? true : preferredIPPrefix.Contains(inside.Address.ToString(), new IPComparer()))
                    select inside.Address
                    ).ToArray()).Aggregate(new IPAddress[] { IPAddress.None }, (i, j) => { return i.Union(j).ToArray(); }).OrderBy(ip =>
                    {
                        //If we have no preffered addresses we just return a default
                        if (preferredIPPrefixs == null)
                            return int.MaxValue;
                        else
                        {
                            //We can check the preffered and return the index at which the IP occurs
                            for (int i = 0; i < preferredIPPrefixs.Length; i++)
                                if (ip.ToString().StartsWith(preferredIPPrefixs[i])) return i;

                            //If there was no match for this IP in the preffered IP range we just return maxValue
                            return int.MaxValue;
                        }
                    }).Where(ip => { return ip != IPAddress.None; }).ToList();
        }

        /// <summary>
        /// The port networkComms is operating on
        /// </summary>
        static int defaultListenPort = 10000;

        /// <summary>
        /// The port networkComms is operating on
        /// </summary>
        public static int DefaultListenPort
        {
            get { return defaultListenPort; }
            set { defaultListenPort = value; }
        }

        /// <summary>
        /// The local identifier of this instance of network comms
        /// </summary>
        internal static ShortGuid localNetworkIdentifier = ShortGuid.NewGuid();

        /// <summary>
        /// The local identifier of this instance of network comms. This is an application specific identifier.
        /// </summary>
        public static ShortGuid NetworkNodeIdentifier
        {
            get { return localNetworkIdentifier; }
        }

        /// <summary>
        /// Returns the current instance network usage, as a value between 0 and 1. Returns the largest value from either incoming and outgoing data load across any network adaptor. Triggers load analysis upon first call.
        /// </summary>
        public static double CurrentNetworkLoad
        {
            get
            {
                //We start the load thread when we first access the network load
                //this helps cut down on uncessary threads if unrequired
                if (NetworkLoadThread == null)
                {
                    lock (globalDictAndDelegateLocker)
                    {
                        if (NetworkLoadThread == null)
                        {
                            NetworkLoadThread = new Thread(NetworkLoadWorker);
                            NetworkLoadThread.Name = "NetworkLoadThread";
                            NetworkLoadThread.Start();
                        }
                    }
                }

                return currentNetworkLoad;
            }

            private set
            {
                currentNetworkLoad = value;
            }
        }

        /// <summary>
        /// Retuns an averaged version of CurrentNetworkLoad, as a value between 0 and 1, for upto a time window of 254 seconds. Triggers load analysis upon first call.
        /// </summary>
        /// <param name="secondsToAverage"></param>
        /// <returns></returns>
        public static double AverageNetworkLoad(byte secondsToAverage)
        {
            if (NetworkLoadThread == null)
            {
                lock (globalDictAndDelegateLocker)
                {
                    if (NetworkLoadThread == null)
                    {
                        currentNetworkLoadValues = new CommsMath();

                        NetworkLoadThread = new Thread(NetworkLoadWorker);
                        NetworkLoadThread.Name = "NetworkLoadThread";
                        NetworkLoadThread.Start();
                    }
                }
            }

            return currentNetworkLoadValues.CalculateMean((int)((secondsToAverage * 1000.0) / NetworkLoadUpdateWindowMS));
        }

        /// <summary>
        /// The interface link speed in bits/sec to use for load calculations.
        /// </summary>
        public static long InterfaceLinkSpeed { get; set; }

        internal static volatile bool commsShutdown;

        /// <summary>
        /// The number of millisconds over which to take an instance load (CurrentNetworkLoad). Default is 200ms but use atleast 100ms to get reliable values.
        /// </summary>
        public static int NetworkLoadUpdateWindowMS { get; set; }
        private static Thread NetworkLoadThread = null;
        private static double currentNetworkLoad;
        private static CommsMath currentNetworkLoadValues;

        /// <summary>
        /// Calculates the network load every NetworkLoadUpdateWindowMS
        /// </summary>
        private static void NetworkLoadWorker()
        {
            //Get all interfaces
            NetworkInterface[] interfacesToUse = (from outer in NetworkInterface.GetAllNetworkInterfaces()
                                                  select outer).ToArray();

            long[] startSent, startRecieved, endSent, endRecieved;

            do
            {
                try
                {
                    //we need to look at the load across all adaptors, by default we will probably choose the adaptor with the highest usage
                    DateTime startTime = DateTime.Now;

                    IPv4InterfaceStatistics[] stats = (from current in interfacesToUse select current.GetIPv4Statistics()).ToArray();
                    startSent = (from current in stats select current.BytesSent).ToArray();
                    startRecieved = (from current in stats select current.BytesReceived).ToArray();

                    Thread.Sleep(NetworkLoadUpdateWindowMS);

                    stats = (from current in interfacesToUse select current.GetIPv4Statistics()).ToArray();
                    endSent = (from current in stats select current.BytesSent).ToArray();
                    endRecieved = (from current in stats select current.BytesReceived).ToArray();

                    DateTime endTime = DateTime.Now;

                    List<double> outUsage = new List<double>();
                    List<double> inUsage = new List<double>();
                    for(int i=0; i<startSent.Length; i++)
                    {
                        outUsage.Add((double)(endSent[i] - startSent[i]) / ((double)(InterfaceLinkSpeed * (endTime - startTime).TotalMilliseconds) / 8000));
                        inUsage.Add((double)(endRecieved[i] - startRecieved[i]) / ((double)(InterfaceLinkSpeed * (endTime - startTime).TotalMilliseconds) / 8000));
                    }

                    double loadValue = Math.Max(outUsage.Max(), inUsage.Max());

                    //Limit to one
                    CurrentNetworkLoad = (loadValue > 1 ? 1 : loadValue);
                    currentNetworkLoadValues.AddValue(CurrentNetworkLoad);

                    //We can only have upto 255 seconds worth of data in the average list
                    currentNetworkLoadValues.TrimList((int)(255000.0 / NetworkLoadUpdateWindowMS));
                }
                catch (Exception ex)
                {
                    LogError(ex, "NetworkLoadWorker");
                }
            } while (!commsShutdown);
        }

        internal static Random randomGen = new Random();
        #endregion

        #region Established Connections
        /// <summary>
        /// Locker for all connection dictionaries
        /// </summary>
        internal static object globalDictAndDelegateLocker = new object();

        /// <summary>
        /// Primary connection dictionary stored by network indentifier
        /// </summary>
        internal static Dictionary<ShortGuid, Dictionary<ConnectionType, List<Connection>>> allConnectionsById = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<Connection>>>();

        /// <summary>
        /// Secondary connection dictionary stored by ip end point. Allows for quick cross referencing.
        /// </summary>
        internal static Dictionary<IPEndPoint, Dictionary<ConnectionType, Connection>> allConnectionsByEndPoint = new Dictionary<IPEndPoint, Dictionary<ConnectionType, Connection>>();

        /// <summary>
        /// Old connection cache so that requests for connectionInfo can be returned even after a connection has been closed.
        /// </summary>
        internal static Dictionary<ShortGuid, Dictionary<ConnectionType, List<ConnectionInfo>>> oldConnectionIdToConnectionInfo = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<ConnectionInfo>>>();
        #endregion

        #region Incoming Data and Connection Config
        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Sync performs slightly better but spawns a worker thread for each unique connection. Probably best not to use if you are going to have >50 simultaneous connections.
        /// </summary>
        internal static bool connectionListenModeUseSync = false;

        /// <summary>
        /// Networkcomms.net can listen on a single interface (IP) or all interfaces. Default is single interface.
        /// </summary>
        internal static bool listenOnAllAllowedInterfaces = false;

        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Default is false. No noticable performance difference between the two modes.
        /// </summary>
        public static bool ConnectionListenModeUseSync
        {
            get { return connectionListenModeUseSync; }
            set { connectionListenModeUseSync = value; }
        }

        /// <summary>
        /// Used for switching between listening on a single interface or all interfaces. Default is false (single interface).
        /// </summary>
        public static bool ListenOnAllAllowedInterfaces
        {
            get { return listenOnAllAllowedInterfaces; }
            set { listenOnAllAllowedInterfaces = value; }
        }

        /// <summary>
        /// Send and receive buffer sizes. These values are chosen to prevent the buffers ending up on the Large Object Heap
        /// </summary>
        internal static int receiveBufferSizeBytes = 80000;
        internal static int sendBufferSizeBytes = 80000;

        /// <summary>
        /// Receive data buffer size. Default is 80KB. CAUTION: Changing the default value can lead to severe performance degredation.
        /// </summary>
        public static int ReceiveBufferSizeBytes
        {
            get { return receiveBufferSizeBytes; }
            set { receiveBufferSizeBytes = value; }
        }

        /// <summary>
        /// Send data buffer size. Default is 80KB. CAUTION: Changing the default value can lead to severe performance degredation.
        /// </summary>
        public static int SendBufferSizeBytes
        {
            get { return sendBufferSizeBytes; }
            set { sendBufferSizeBytes = value; }
        }
        #endregion

        #region High CPU Usage Tuning
        /// <summary>
        /// In times of high CPU usage we need to ensure that certain time critical functions, like connection handshaking, do not timeout
        /// </summary>
        internal static ThreadPriority timeCriticalThreadPriority = ThreadPriority.AboveNormal;
        #endregion

        #region Checksum Config
        /// <summary>
        /// Determines whether checkSums are used on sends and receive
        /// </summary>
        internal static bool enablePacketCheckSumValidation = false;

        /// <summary>
        /// Set to true to enable checksum validation during communication. Default is false, thereby relying on the basic TCP checksum alone.
        /// </summary>
        public static bool EnablePacketCheckSumValidation
        {
            get { return enablePacketCheckSumValidation; }
            set { enablePacketCheckSumValidation = value; }
        }

        /// <summary>
        /// The maximum packet size which will be maintained for possible future resend requests
        /// </summary>
        internal static int checkSumMismatchSentPacketCacheMaxByteLimit = 75000;

        /// <summary>
        /// Set to true to enable checksum validation during all receives. Default 75KB.
        /// </summary>
        public static int CheckSumMismatchSentPacketCacheMaxByteLimit
        {
            get { return checkSumMismatchSentPacketCacheMaxByteLimit; }
            set { checkSumMismatchSentPacketCacheMaxByteLimit = value; }
        }
        #endregion

        #region PacketType Config and Global Handlers
        /// <summary>
        /// A reference copy of all reservedPacketTypeNames
        /// </summary>
        internal static string[] reservedPacketTypeNames = Enum.GetNames(typeof(ReservedPacketType));

        /// <summary>
        /// Delegate method for all custom incoming packet handlers.
        /// </summary>
        public delegate void PacketHandlerCallBackDelegate<T>(PacketHeader packetHeader, ConnectionInfo connectionInfo, T incomingObject);

        /// <summary>
        /// Dictionary of all custom packetHandlers. Key is packetType.
        /// </summary>
        private static Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>> globalIncomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();
        
        /// <summary>
        /// By default all incoming objects are serialised and compressed by DefaultSerializer and DefaultCompressor. Should the user want something else
        /// those settings are stored here
        /// </summary>
        private static Dictionary<string, PacketTypeUnwrapper> globalIncomingPacketUnwrappers = new Dictionary<string, PacketTypeUnwrapper>();

        /// <summary>
        /// Private class which wraps serializer and compressor information for specific packet types. Used by globalIncomingPacketUnwrappers.
        /// </summary>
        public class PacketTypeUnwrapper
        {
            string packetTypeStr;
            public SendReceiveOptions Options { get; private set; }

            public PacketTypeUnwrapper(string packetTypeStr, SendReceiveOptions options)
            {
                this.packetTypeStr = packetTypeStr;
                this.Options = options;
            }
        }

        /// <summary>
        /// If true any incoming packet types which do not have configured handlers will just be black holed
        /// </summary>
        internal static volatile bool ignoreUnknownPacketTypes = false;

        /// <summary>
        /// If true any unknown incoming packetTypes are simply ignored. Default is false and will record an error is an unknown packet is received.
        /// </summary>
        public static bool IgnoreUnknownPacketTypes
        {
            get { return ignoreUnknownPacketTypes; }
            set { ignoreUnknownPacketTypes = value; }
        }

        /// <summary>
        /// The following packetTypeHandlerDelegateWrappers are required so that we can do the totally general and awesome object cast on deserialise.
        /// If there is a way of achieving the same without these wrappers please let us know.
        /// </summary>
        public interface IPacketTypeHandlerDelegateWrapper : IEquatable<IPacketTypeHandlerDelegateWrapper>
        {
            object DeSerialize(byte[] incomingBytes, SendReceiveOptions options);

            void Process(PacketHeader packetHeader, ConnectionInfo connectionInfo, object obj);
            bool EqualsDelegate(Delegate other);
        }

        public class PacketTypeHandlerDelegateWrapper<T> : IPacketTypeHandlerDelegateWrapper
        {
            PacketHandlerCallBackDelegate<T> innerDelegate;

            public PacketTypeHandlerDelegateWrapper(PacketHandlerCallBackDelegate<T> packetHandlerDelegate)
            {
                this.innerDelegate = packetHandlerDelegate;
            }

            public object DeSerialize(byte[] incomingBytes, SendReceiveOptions options)
            {
                if (incomingBytes == null || incomingBytes.Length == 0) return null;
                else
                    return options.Serializer.DeserialiseDataObject<T>(incomingBytes, options.Compressor);
            }

            public void Process(PacketHeader packetHeader, ConnectionInfo connectionInfo, object obj)
            {
                innerDelegate(packetHeader, connectionInfo, (obj == null ? default(T) : (T)obj));
            }

            public bool Equals(IPacketTypeHandlerDelegateWrapper other)
            {
                if (innerDelegate == (other as PacketTypeHandlerDelegateWrapper<T>).innerDelegate)
                    return true;
                else
                    return false;
            }

            public bool EqualsDelegate(Delegate other)
            {
                return other as PacketHandlerCallBackDelegate<T> == innerDelegate;
            }
        }

        /// <summary>
        /// Add a new incoming packet handler. Multiple handlers for the same packet type are allowed
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be used</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to use</param>
        /// <param name="packetTypeStrSerializer">A specific serializer to use instead of default</param>
        /// <param name="packetTypeStrCompressor">A specific compressor to use instead of default</param>
        /// <param name="enableAutoListen">If true will enable comms listening after delegate has been added</param>
        public static void AppendGlobalIncomingPacketHandler<T>(string packetTypeStr, PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer, SendReceiveOptions sendReceiveOptions)
        {
            lock (globalDictAndDelegateLocker)
            {
                //Add the custom serializer and compressor if necessary
                if (sendReceiveOptions.Serializer != null && sendReceiveOptions.Compressor != null)
                {
                    if (globalIncomingPacketUnwrappers.ContainsKey(packetTypeStr))
                    {
                        //Make sure if we already have an existing entry that it matches with the provided
                        if (globalIncomingPacketUnwrappers[packetTypeStr].Options != sendReceiveOptions)
                            throw new PacketHandlerException("You cannot specify a different compressor or serializer instance if one has already been specified for this packetTypeStr.");
                    }
                    else
                        globalIncomingPacketUnwrappers.Add(packetTypeStr, new PacketTypeUnwrapper(packetTypeStr, sendReceiveOptions));
                }
                else if (sendReceiveOptions.Serializer != null ^ sendReceiveOptions.Compressor != null)
                    throw new PacketHandlerException("You must provide both serializer and compressor or neither.");
                else
                {
                    //If we have not specified the serialiser and compressor we assume to be using defaults
                    //If a handler has already been added for this type and has specified specific serialiser and compressor then so should this call to AppendIncomingPacketHandler
                    if (globalIncomingPacketUnwrappers.ContainsKey(packetTypeStr))
                        throw new PacketHandlerException("A handler already exists for this packetTypeStr with specific serializer and compressor instances. Please ensure the same instances are provided in this call to AppendPacketHandler.");
                }

                //Ad the handler to the list
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Make sure we avoid duplicates
                    PacketTypeHandlerDelegateWrapper<T> toCompareDelegate = new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer);
                    bool delegateAlreadyExists = (from current in globalIncomingPacketHandlers[packetTypeStr] where current == toCompareDelegate select current).Count() > 0;
                    if (delegateAlreadyExists)
                        throw new PacketHandlerException("This specific packet handler delegate already exists for the provided packetTypeStr.");

                    globalIncomingPacketHandlers[packetTypeStr].Add(new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer));
                }
                else
                    globalIncomingPacketHandlers.Add(packetTypeStr, new List<IPacketTypeHandlerDelegateWrapper>() { new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer) });

                if (loggingEnabled) logger.Info("Added incoming packetHandler for '" + packetTypeStr + "' packetType.");
            }
        }

        /// <summary>
        /// Add a new incoming packet handler using default serializer and compressor. Multiple handlers for the same packet type are allowed
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be used</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to use</param>
        /// <param name="enableAutoListen">If true will enable comms listening after delegate has been added</param>
        public static void AppendGlobalIncomingPacketHandler<T>(string packetTypeStr, PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer)
        {
            AppendGlobalIncomingPacketHandler<T>(packetTypeStr, packetHandlerDelgatePointer, DefaultSendReceiveOptions);
        }

        /// <summary>
        /// Removes the provided delegate for the specified packet type
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public static void RemoveGlobalIncomingPacketHandler(string packetTypeStr, Delegate packetHandlerDelgatePointer)
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

                        //Remove any entries in the unwrappers dict as well as we are done with this packetTypeStr
                        if (globalIncomingPacketUnwrappers.ContainsKey(packetTypeStr))
                            globalIncomingPacketUnwrappers.Remove(packetTypeStr);

                        if (loggingEnabled) logger.Info("Removed a packetHandler for '" + packetTypeStr + "' packetType. No handlers remain.");
                    }
                    else
                        if (loggingEnabled) logger.Info("Removed a packetHandler for '" + packetTypeStr + "' packetType. Handlers remain.");
                }
            }
        }

        /// <summary>
        /// Removes all delegates for the provided packet type
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which all delegates should be removed</param>
        public static void RemoveAllCustomGlobalPacketHandlers(string packetTypeStr)
        {
            lock (globalDictAndDelegateLocker)
            {
                //We don't need to check for potentially removing a critical reserved packet handler here because those cannot be removed.
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    globalIncomingPacketHandlers.Remove(packetTypeStr);

                    if (loggingEnabled) logger.Info("Removed all incoming packetHandlers for '" + packetTypeStr + "' packetType.");
                }
            }
        }

        /// <summary>
        /// Removes all delegates for all packet types
        /// </summary>
        public static void RemoveAllCustomGlobalPacketHandlers()
        {
            lock (globalDictAndDelegateLocker)
            {
                globalIncomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();

                if (loggingEnabled) logger.Info("Removed all incoming packetHandlers for all packetTypes");
            }
        }

        /// <summary>
        /// Trigger all packet type delegates with the provided parameters. Providing serializer and compressor will override any defaults.
        /// </summary>
        /// <param name="packetHeader">Packet type for which all delegates should be triggered</param>
        /// <param name="sourceConnectionId">The source connection id</param>
        /// <param name="incomingObjectBytes">The serialised and or compressed bytes to be used</param>
        /// <param name="serializer">Override serializer</param>
        /// <param name="compressor">Override compressor</param>
        public static void TriggerGlobalPacketHandler(PacketHeader packetHeader, ConnectionInfo connectionInfo, byte[] incomingObjectBytes, SendReceiveOptions options)
        {
            try
            {
                //We take a copy of the handlers list incase it is modified outside of the lock
                List<IPacketTypeHandlerDelegateWrapper> handlersCopy = null;
                lock (globalDictAndDelegateLocker)
                    if (globalIncomingPacketHandlers.ContainsKey(packetHeader.PacketType))
                        handlersCopy = new List<IPacketTypeHandlerDelegateWrapper>(globalIncomingPacketHandlers[packetHeader.PacketType]);

                if (handlersCopy == null && !IgnoreUnknownPacketTypes)
                {
                    //We may get here if we have not added any custom delegates for reserved packet types
                    if (!reservedPacketTypeNames.Contains(packetHeader.PacketType))
                    {
                        //Change this to just a log because generally a packet of the wrong type is nothing to really worry about
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn("The received packet type '" + packetHeader.PacketType + "' has no configured handler and network comms is not set to ignore unknown packet types. Set NetworkComms.IgnoreUnknownPacketTypes=true to prevent this error.");
                        LogError(new UnexpectedPacketTypeException("The received packet type '" + packetHeader.PacketType + "' has no configured handler and network comms is not set to ignore unknown packet types. Set NetworkComms.IgnoreUnknownPacketTypes=true to prevent this error."), "PacketHandlerErrorGlobal_" + packetHeader.PacketType);
                    }

                    return;
                }
                else if (handlersCopy == null && IgnoreUnknownPacketTypes)
                    //If we have received and unknown packet type and we are choosing to ignore them we just finish here
                    return;
                else
                {
                    //Idiot check
                    if (handlersCopy.Count == 0)
                        throw new PacketHandlerException("An entry exists in the packetHandlers list but it contains no elements. This should not be possible.");

                    //If we find a global packet unwrapper for this packetType we used those options
                    lock (globalDictAndDelegateLocker)
                    {
                        if (globalIncomingPacketUnwrappers.ContainsKey(packetHeader.PacketType))
                            options = globalIncomingPacketUnwrappers[packetHeader.PacketType].Options;
                    }

                    //Deserialise the object only once
                    object returnObject = handlersCopy[0].DeSerialize(incomingObjectBytes, options);

                    //Pass the data onto the handler and move on.
                    if (loggingEnabled) logger.Trace(" ... passing completed data packet to selected handlers.");

                    //Pass the object to all necessary delgates
                    //We need to use a copy because we may modify the original delegate list during processing
                    foreach (IPacketTypeHandlerDelegateWrapper wrapper in handlersCopy)
                    {
                        try
                        {
                            wrapper.Process(packetHeader, connectionInfo, returnObject);
                        }
                        catch (Exception ex)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An unhandled exception was caught while processing a packet handler for a packet type '" + packetHeader.PacketType + "'. Make sure to catch errors in packet handlers. See error log file for more information.");
                            NetworkComms.LogError(ex, "PacketHandlerErrorGlobal_" + packetHeader.PacketType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An exception occured in TriggerPacketHandler() for a packet type '" + packetHeader.PacketType + "'. See error log file for more information.");
                NetworkComms.LogError(ex, "PacketHandlerErrorGlobal_" + packetHeader.PacketType);
            }
        }
        #endregion

        #region Connection Comms Establish and Shutdown
        /// <summary>
        /// Delegate method for connection shutdown delegates.
        /// </summary>
        public delegate void ConnectionEstablishShutdownDelegate(ConnectionInfo connectionInfo);

        /// <summary>
        /// A multicast delegate pointer for any connection shutdown delegates.
        /// </summary>
        internal static ConnectionEstablishShutdownDelegate globalConnectionShutdownDelegates;
        internal static ConnectionEstablishShutdownDelegate globalConnectionEstablishDelegates;

        public static event EventHandler<EventArgs> OnCommsShutdown;

        /// <summary>
        /// Add a new shutdown delegate which will be called for every connection as it is closes.
        /// </summary>
        /// <param name="connectionShutdownDelegate"></param>
        public static void AppendGlobalConnectionCloseHandler(ConnectionEstablishShutdownDelegate connectionShutdownDelegate)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (globalConnectionShutdownDelegates == null)
                    globalConnectionShutdownDelegates = connectionShutdownDelegate;
                else
                    globalConnectionShutdownDelegates += connectionShutdownDelegate;

                if (loggingEnabled) logger.Info("Added global connection shutdown delegate.");
            }
        }

        /// <summary>
        /// Remove a shutdown delegate which will be called for every connection as it is closes.
        /// </summary>
        /// <param name="connectionShutdownDelegate"></param>
        public static void RemoveGlobalConnectionCloseHandler(ConnectionEstablishShutdownDelegate connectionShutdownDelegate)
        {
            lock (globalDictAndDelegateLocker)
            {
                globalConnectionShutdownDelegates -= connectionShutdownDelegate;

                if (loggingEnabled) logger.Info("Removed global shutdown delegate.");

                if (globalConnectionShutdownDelegates == null)
                {
                    if (loggingEnabled) logger.Info("No handlers remain for shutdown connections.");
                }
                else
                {
                    if (loggingEnabled) logger.Info("Handlers remain for shutdown connections.");
                }
            }
        }

        /// <summary>
        /// Add a new establish delegate which will be called for every connection once it has been succesfully established
        /// </summary>
        /// <param name="connectionShutdownDelegate"></param>
        public static void AppendGlobalConnectionEstablishHandler(ConnectionEstablishShutdownDelegate connectionEstablishDelegate)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (globalConnectionEstablishDelegates == null)
                    globalConnectionEstablishDelegates = connectionEstablishDelegate;
                else
                    globalConnectionEstablishDelegates += connectionEstablishDelegate;

                if (loggingEnabled) logger.Info("Added global connection establish delegate.");
            }
        }

        /// <summary>
        /// Remove a connection establish delegate
        /// </summary>
        /// <param name="connectionShutdownDelegate"></param>
        public static void RemoveGlobalConnectionEstablishHandler(ConnectionEstablishShutdownDelegate connectionEstablishDelegate)
        {
            lock (globalDictAndDelegateLocker)
            {
                globalConnectionEstablishDelegates -= connectionEstablishDelegate;

                if (loggingEnabled) logger.Info("Removed global connection establish delegate.");

                if (globalConnectionEstablishDelegates == null)
                {
                    if (loggingEnabled) logger.Info("No handlers remain for establish connections.");
                }
                else
                {
                    if (loggingEnabled) logger.Info("Handlers remain for establish connections.");
                }
            }
        }

        /// <summary>
        /// Shutdown all connections and clean up communciation objects. If any comms activity has taken place this should be called on application close
        /// </summary>
        public static void Shutdown(int threadShutdownTimeoutMS = 1000)
        {
            commsShutdown = true;

            try
            {
                TCPConnection.Shutdown(threadShutdownTimeoutMS);
                //UDPConnection.Shutdown();
            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            try
            {
                if (OnCommsShutdown != null) OnCommsShutdown(null, new EventArgs());
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
                if (NetworkLoadThread != null)
                {
                    if (!NetworkLoadThread.Join(threadShutdownTimeoutMS))
                    {
                        NetworkLoadThread.Abort();
                        throw new CommsSetupShutdownException("Timeout waiting for NetworkLoadThread thread to shutdown after " + threadShutdownTimeoutMS + " ms. ");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            commsShutdown = false;
            if (loggingEnabled) logger.Info("Network comms has shutdown");
        }
        #endregion

        #region Timeouts
        internal static int connectionEstablishTimeoutMS = 30000;
        internal static int packetConfirmationTimeoutMS = 5000;
        internal static int connectionAliveTestTimeoutMS = 1000;

        /// <summary>
        /// Time to wait in milliseconds before throwing an exception when waiting for a connection to be established
        /// </summary
        public static int ConnectionEstablishTimeoutMS
        {
            get { return connectionEstablishTimeoutMS; }
            set { connectionEstablishTimeoutMS = value; }
        }

        /// <summary>
        /// Time to wait in milliseconds before throwing an exception when waiting for confirmation of packet receipt
        /// </summary>
        public static int PacketConfirmationTimeoutMS
        {
            get { return packetConfirmationTimeoutMS; }
            set { packetConfirmationTimeoutMS = value; }
        }

        /// <summary>
        /// Time to wait in milliseconds before assuming a remote connection is dead when doing a connection test
        /// </summary>
        public static int ConnectionAliveTestTimeoutMS
        {
            get { return connectionAliveTestTimeoutMS; }
            set { connectionAliveTestTimeoutMS = value; }
        }
        #endregion

        #region Logging
        internal static bool loggingEnabled = false;
        internal static ILog logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Access the networkComms logger externally. Allows logging from external sources
        /// </summary>
        public static ILog Logger
        {
            get { return logger; }
        }

        /// <summary>
        /// Enable logging in networkComms using the provided logging adaptor
        /// </summary>
        /// <param name="loggingAdaptor"></param>
        public static void EnableLogging(ILoggerFactoryAdapter loggingAdaptor)
        {
            lock (globalDictAndDelegateLocker)
            {
                loggingEnabled = true;
                Common.Logging.LogManager.Adapter = loggingAdaptor;
                logger = LogManager.GetCurrentClassLogger();
            }
        }

        /// <summary>
        /// Disable logging in networkComms
        /// </summary>
        public static void DisableLogging()
        {
            lock (globalDictAndDelegateLocker)
            {
                loggingEnabled = false;
                Common.Logging.LogManager.Adapter = new Common.Logging.Simple.NoOpLoggerFactoryAdapter();
            }
        }

        /// <summary>
        /// Locker for LogError() which ensures thread safe operation.
        /// </summary>
        static object errorLocker = new object();

        /// <summary>
        /// Appends provided logString to end of fileName.txt
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="logString"></param>
        public static void AppendStringToLogFile(string fileName, string logString)
        {
            try
            {
                lock (errorLocker)
                {
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fileName + ".txt", true))
                        sw.WriteLine(logString);
                }
            }
            catch (Exception)
            {
                //If an error happens here, such as if the file is locked then we lucked out.
            }
        }

        /// <summary>
        /// Logs provided exception to a file to assist troubleshooting.
        /// </summary>
        public static string LogError(Exception ex, string fileAppendStr, string optionalCommentStr = "")
        {
            string fileName;

            lock (errorLocker)
            {
                if (loggingEnabled) logger.Fatal(fileAppendStr + (optionalCommentStr != "" ? " - " + optionalCommentStr : ""), ex);

#if iOS
                fileName = fileAppendStr + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Thread.CurrentContext.ContextID + "]");
#else
                fileName = fileAppendStr + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + System.Diagnostics.Process.GetCurrentProcess().Id + "-" + Thread.CurrentContext.ContextID + "]");
#endif

                try
                {
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fileName + ".txt", false))
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
                }
                catch (Exception)
                {
                    //This should never really happen, but just incase.
                }
            }

            return fileName;
        }
        #endregion

        #region Serializers and Compressors
        private static Dictionary<Type, ISerialize> allKnownSerializers = WrappersHelper.Instance.GetAllSerializes();
        private static Dictionary<Type, ICompress> allKnownCompressors = WrappersHelper.Instance.GetAllCompressors();

        /// <summary>
        /// The following are used for internal comms objects, packet headers, connection establishment etc. 
        /// We generally seem to increase the size of our data if compressing small objects (~50kb)
        /// Given the typical header size is 40kb we might as well not compress these objects.
        /// </summary>
        internal static SendReceiveOptions InternalFixedSendReceiveOptions { get; set; }

        /// <summary>
        /// Default options for sending and receiving in the absence of specific values
        /// </summary>
        public static SendReceiveOptions DefaultSendReceiveOptions { get; set; }
        #endregion

        #region Public Usage Methods

        #region Misc Utility
        /// <summary>
        /// Return the MD5 hash of the provided byte array as a string
        /// </summary>
        /// <param name="bytesToMd5"></param>
        /// <returns></returns>
        public static string MD5Bytes(byte[] bytesToMd5)
        {
            System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(bytesToMd5)).Replace("-","");
        }
        #endregion

        #region Information

        /// <summary>
        /// Returns an array of ConnectionInfo for every currently established connection
        /// </summary>
        /// <returns></returns>
        public static ConnectionInfo[] AllConnectionInfo()
        {
            lock(globalDictAndDelegateLocker)
            {
                return (from current in allConnectionsByEndPoint 
                        select current.Value.Values.Select(connection => 
                        { 
                            return connection.ConnectionInfo; 
                        })).Aggregate(new List<ConnectionInfo> { null }, (left, right) => { return left.Union(right).ToList(); }).Where(entry => { return entry != null; }).ToArray();
            }
        }

        /// <summary>
        /// Return the total current number of connections in network comms  
        /// </summary>
        /// <returns></returns>
        public static int TotalNumConnections()
        {
            lock (globalDictAndDelegateLocker)
                return (from current in allConnectionsByEndPoint select current.Value.Count).Sum();
        }

        /// <summary>
        /// Return the total current number of connections where the remoteEndPoint matches the provided ip address
        /// </summary>
        /// <param name="matchIP">IP address in the format "192.168.0.1"</param>
        /// <returns></returns>
        public static int TotalNumConnections(IPAddress matchIP)
        {
            lock (globalDictAndDelegateLocker)
            {
                return (from current in allConnectionsByEndPoint
                        select current.Value.Count(connection => { return connection.Value.ConnectionInfo.RemoteEndPoint.Address.Equals(matchIP); })).Sum();
            }
        }
        #endregion

        #region SendObjectDefault
        /// <summary>
        /// Send the provided object to the specified destination on the default comms port and sets the connectionId. Uses the network comms default compressor and serializer
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, object sendObject, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port and sets the connectionId. Uses the network comms default compressor and serializer
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, object sendObject, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on the default comms port. Uses the network comms default compressor and serializer
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        public static void SendObject(string packetTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, object sendObject)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port. Uses the network comms default compressor and serializer
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, object sendObject)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified connectionId. Uses the network comms default compressor and serializer
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="connectionId">Destination connection id</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, object sendObject)
        {
            throw new NotImplementedException();
        }

        #endregion SendObjectDefault
        #region SendObjectSpecific
        /// <summary>
        /// Send the provided object to the specified destination on the default comms port and sets the connectionId. Uses the provided compressor and serializer delegates
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        /// <param name="serializer">The specific serializer delegate to use</param>
        /// <param name="compressor">The specific compressor delegate to use</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }
                
        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port and sets the connectionId. Uses the provided compressor and serializer delegates
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        /// <param name="serializer">The specific serializer delegate to use</param>
        /// <param name="compressor">The specific compressor delegate to use</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on the default comms port. Uses the provided compressor and serializer delegates
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        /// <param name="serializer">The specific serializer delegate to use</param>
        /// <param name="compressor">The specific compressor delegate to use</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port. Uses the provided compressor and serializer delegates
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        /// <param name="serializer">The specific serializer delegate to use</param>
        /// <param name="compressor">The specific compressor delegate to use</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified connectionId. Uses the provided compressor and serializer delegates
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="connectionId">Destination connection id</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        /// <param name="serializer">The specific serializer delegate to use</param>
        /// <param name="compressor">The specific compressor delegate to use</param>
        [Obsolete]
        public static void SendObject(string packetTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region SendReceiveObjectDefault

        /// <summary>
        /// Send the provided object to the specified destination on the default comms port, setting the connectionId, and wait for the return object. Uses the network comms default compressor and serializer
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port, setting the connectionId, and wait for the return object. Uses the network comms default compressor and serializer
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on the default comms port and wait for the return object. Uses the network comms default compressor and serializer
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <returns>The expected return object</returns>
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port and wait for the return object. Uses the network comms default compressor and serializer
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <returns>The expected return object</returns>
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified connectionId and wait for the return object. Uses the network comms default compressor and serializer
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="connectionId">Destination connection id</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {
            throw new NotImplementedException();
        }

        #endregion SendReceiveObjectDefault
        #region SendReceiveObjectSpecific
        /// <summary>
        /// Send the provided object to the specified destination on the default comms port, setting the connectionId, and wait for the return object. Uses the provided compressors and serializers
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="serializerOutgoing">Serializer to use for outgoing object</param>
        /// <param name="compressorOutgoing">Compressor to use for outgoing object</param>
        /// <param name="serializerIncoming">Serializer to use for return object</param>
        /// <param name="compressorIncoming">Compressor to use for return object</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port, setting the connectionId, and wait for the return object. Uses the provided compressors and serializers
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="serializerOutgoing">Serializer to use for outgoing object</param>
        /// <param name="compressorOutgoing">Compressor to use for outgoing object</param>
        /// <param name="serializerIncoming">Serializer to use for return object</param>
        /// <param name="compressorIncoming">Compressor to use for return object</param>
        /// <param name="connectionId">The connectionId used to complete the send. Can be used in subsequent sends without requiring ip address</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming, ref ShortGuid connectionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on the default comms port and wait for the return object. Uses the provided compressors and serializers
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="serializerOutgoing">Serializer to use for outgoing object</param>
        /// <param name="compressorOutgoing">Compressor to use for outgoing object</param>
        /// <param name="serializerIncoming">Serializer to use for return object</param>
        /// <param name="compressorIncoming">Compressor to use for return object</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified destination on a specific comms port and wait for the return object. Uses the provided compressors and serializers
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="commsPort">The destination comms port</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="serializerOutgoing">Serializer to use for outgoing object</param>
        /// <param name="compressorOutgoing">Compressor to use for outgoing object</param>
        /// <param name="serializerIncoming">Serializer to use for return object</param>
        /// <param name="compressorIncoming">Compressor to use for return object</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided object to the specified connectionId and wait for the return object. Uses the provided compressors and serializers
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="connectionId">Destination connection id</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="serializerOutgoing">Serializer to use for outgoing object</param>
        /// <param name="compressorOutgoing">Compressor to use for outgoing object</param>
        /// <param name="serializerIncoming">Serializer to use for return object</param>
        /// <param name="compressorIncoming">Compressor to use for return object</param>
        /// <returns>The expected return object</returns>
        [Obsolete]
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {
            throw new NotImplementedException();
        }
        
        #endregion SendReceiveObjectSpecific

        static NetworkComms()
        {
            NetworkLoadUpdateWindowMS = 200;
            InterfaceLinkSpeed = 100000000;

            InternalFixedSendReceiveOptions = new SendReceiveOptions(false, WrappersHelper.Instance.GetSerializer<ProtobufSerializer>(), WrappersHelper.Instance.GetCompressor<NullCompressor>(), ThreadPriority.Normal);
            DefaultSendReceiveOptions = new SendReceiveOptions(false, WrappersHelper.Instance.GetSerializer<ProtobufSerializer>(), WrappersHelper.Instance.GetCompressor<SevenZipLZMACompressor.LZMACompressor>(), ThreadPriority.Normal);
        }

        public static void CloseAllConnections()
        {
            CloseAllConnections(new IPEndPoint[0], ConnectionType.Undefined);
        }

        public static void CloseAllConnections(ConnectionType connectionType)
        {
            CloseAllConnections(new IPEndPoint[0], connectionType);
        }

        public static void CloseAllConnections(IPEndPoint[] closeAllExceptTheseEndPoints, ConnectionType connectionType)
        {
            List<Connection> connectionsToClose;

            lock (globalDictAndDelegateLocker)
            {
                connectionsToClose = (from current in allConnectionsByEndPoint.Values
                                      select (from inner in current
                                              where (connectionType == ConnectionType.Undefined ? true : inner.Key == connectionType)
                                              where !closeAllExceptTheseEndPoints.Contains(inner.Value.ConnectionInfo.RemoteEndPoint)
                                              select inner.Value)).Aggregate(new List<Connection>() { null }, (left, right) => { return left.Union(right).ToList(); }).Where(entry => { return entry != null; }).ToList();
            }

            foreach (Connection connection in connectionsToClose)
                connection.CloseConnection(false, -6);
        }

        /// <summary>
        /// Retrieve all connections of the provided type
        /// </summary>
        /// <param name="connectionType"></param>
        /// <returns></returns>
        public static List<Connection> RetrieveConnection(ConnectionType connectionType)
        {
            lock (globalDictAndDelegateLocker)
            {
                return (from current in allConnectionsByEndPoint.Values
                        select (from inner in current
                                where (connectionType == ConnectionType.Undefined ? true : inner.Key == connectionType)
                                select inner.Value)).Aggregate(new List<Connection>() { null }, (left, right) => { return left.Union(right).ToList(); }).Where(entry => {return entry != null;}).ToList();
            }
        }

        /// <summary>
        /// Retrieve all connections
        /// </summary>
        /// <param name="connectionType"></param>
        /// <returns></returns>
        public static List<Connection> RetrieveConnection()
        {
            return RetrieveConnection(ConnectionType.Undefined);
        }

        /// <summary>
        /// Get an existing connection with the provided connectionId of a provided type. Returns null if a connection does not exist.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="connectionType"></param>
        /// <returns></returns>
        public static List<Connection> RetrieveConnection(ShortGuid connectionId, ConnectionType connectionType)
        {
            lock (globalDictAndDelegateLocker)
                return (from current in NetworkComms.allConnectionsById where current.Key == connectionId && current.Value.ContainsKey(connectionType) select current.Value[connectionType]).FirstOrDefault();
        }

        /// <summary>
        /// Get an existing connection with the provided ipAddress of a provided type. Returns null if a connection does not exist.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="connectionType"></param>
        /// <returns></returns>
        public static Connection RetrieveConnection(IPEndPoint IPEndPoint, ConnectionType connectionType)
        {
            lock (globalDictAndDelegateLocker)
                return (from current in NetworkComms.allConnectionsByEndPoint where current.Key == IPEndPoint && current.Value.ContainsKey(connectionType) select current.Value[connectionType]).FirstOrDefault();
        }

        /// <summary>
        /// Returns true if a network connection exists with the provided remoteNetworkIdentifier, type and endPoint
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public static bool ConnectionExists(ShortGuid networkIdentifier, ConnectionType connectionType)
        {
            if (loggingEnabled) logger.Trace("Checking by identifier and endPoint for existing " + connectionType + " connection to " + networkIdentifier);

            lock (globalDictAndDelegateLocker)
            {
                if (allConnectionsById.ContainsKey(networkIdentifier))
                {
                    if (allConnectionsById[networkIdentifier].ContainsKey(connectionType))
                        return allConnectionsById[networkIdentifier][connectionType].Count() > 0;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if a connection already exists with the provided endPoint and type
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="portNumber"></param>
        /// <returns></returns>
        public static bool ConnectionExists(IPEndPoint remoteEndPoint, ConnectionType connectionType)
        {
            if (loggingEnabled) logger.Trace("Checking by endPoint for existing " + connectionType + " connection to " + remoteEndPoint.Address + ":" + remoteEndPoint.Port);

            lock (globalDictAndDelegateLocker)
            {
                if (allConnectionsByEndPoint.ContainsKey(remoteEndPoint))
                    return allConnectionsByEndPoint[remoteEndPoint].ContainsKey(connectionType);
                else
                    return false;
            }
        }

        /// <summary>
        /// Removes the reference to the provided connection from within networkComms. Returns true if the provided connection reference existed and was removed, false otherwise.
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        internal static bool RemoveConnectionReference(Connection connection, bool maintainConnectionInfoHistory = true)
        {
            //We don't have the connection identifier until the connection has been established.
            if (!connection.ConnectionInfo.ConnectionEstablished && !connection.ConnectionInfo.ConnectionShutdown)
                return false;

            if (connection.ConnectionInfo.ConnectionEstablished && !connection.ConnectionInfo.ConnectionShutdown)
                throw new ConnectionShutdownException("A connection can only be removed once correctly shutdown.");

            bool returnValue = false;

            //Ensure connection references are removed from networkComms
            //Once we think we have closed the connection it's time to get rid of our other references
            lock (globalDictAndDelegateLocker)
            {
                #region Update NetworkComms Connection Dictionaries
                //We establish whether we have already done this step
                if ((allConnectionsById.ContainsKey(connection.ConnectionInfo.NetworkIdentifier) &&
                    allConnectionsById[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType) &&
                    allConnectionsById[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType].Contains(connection))
                    ||
                    (allConnectionsByEndPoint.ContainsKey(connection.ConnectionInfo.RemoteEndPoint) &&
                    allConnectionsByEndPoint[connection.ConnectionInfo.RemoteEndPoint].ContainsKey(connection.ConnectionInfo.ConnectionType)))
                {
                    //Maintain a reference if this is our first connection close
                    returnValue = true;
                }

                //Keep a reference of the connection for possible debugging later
                if (maintainConnectionInfoHistory)
                {
                    if (oldConnectionIdToConnectionInfo.ContainsKey(connection.ConnectionInfo.NetworkIdentifier))
                    {
                        if (oldConnectionIdToConnectionInfo[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType))
                            oldConnectionIdToConnectionInfo[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType].Add(connection.ConnectionInfo);
                        else
                            oldConnectionIdToConnectionInfo[connection.ConnectionInfo.NetworkIdentifier].Add(connection.ConnectionInfo.ConnectionType, new List<ConnectionInfo>() { connection.ConnectionInfo });
                    }
                    else
                        oldConnectionIdToConnectionInfo.Add(connection.ConnectionInfo.NetworkIdentifier, new Dictionary<ConnectionType, List<ConnectionInfo>>() { { connection.ConnectionInfo.ConnectionType, new List<ConnectionInfo>() { connection.ConnectionInfo } } });
                }

                if (allConnectionsById.ContainsKey(connection.ConnectionInfo.NetworkIdentifier) &&
                        allConnectionsById[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType))
                {
                    if (!allConnectionsById[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType].Contains(connection))
                        throw new ConnectionShutdownException("A reference to the connection being closed was not found in the allConnectionsById dictionary.");
                    else
                        allConnectionsById[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType].Remove(connection);
                }

                //We can now remove this connection by end point as well
                if (allConnectionsByEndPoint.ContainsKey(connection.ConnectionInfo.RemoteEndPoint))
                {
                    if (allConnectionsByEndPoint[connection.ConnectionInfo.RemoteEndPoint].ContainsKey(connection.ConnectionInfo.ConnectionType))
                        allConnectionsByEndPoint[connection.ConnectionInfo.RemoteEndPoint].Remove(connection.ConnectionInfo.ConnectionType);

                    //If this was the last connection type for this endpoint we can remove the endpoint reference as well
                    if (allConnectionsByEndPoint[connection.ConnectionInfo.RemoteEndPoint].Count == 0)
                        allConnectionsByEndPoint.Remove(connection.ConnectionInfo.RemoteEndPoint);
                }
                #endregion
            }

            return returnValue;
        }

        /// <summary>
        /// Adds a reference to the provided connection within networkComms. If a connection to the same endPoint already exists 
        /// </summary>
        /// <param name="connection"></param>
        internal static void AddConnectionByEndPointReference(Connection connection, IPEndPoint endPointToUse = null)
        {
            if (connection.ConnectionInfo.ConnectionEstablished || connection.ConnectionInfo.ConnectionShutdown)
                throw new ConnectionSetupException("Connection reference by endPoint should only be added before a connection is established. This is to prevent duplicate connections.");

            if (endPointToUse == null) endPointToUse = connection.ConnectionInfo.RemoteEndPoint;

            //How do we prevent multiple threads from trying to create a duplicate connection??
            lock (globalDictAndDelegateLocker)
            {
                if (ConnectionExists(endPointToUse, connection.ConnectionInfo.ConnectionType))
                {
                    if (RetrieveConnection(endPointToUse, connection.ConnectionInfo.ConnectionType) != connection)
                        throw new ConnectionSetupException("A difference connection already exists with " + connection.ConnectionInfo);
                    else
                    {
                        //We have just tried to add the same reference twice, no need to do anything this time around
                    }
                }
                else
                {
                    //Add reference to the endPoint dictionary
                    if (allConnectionsByEndPoint.ContainsKey(endPointToUse))
                    {
                        if (allConnectionsByEndPoint[endPointToUse].ContainsKey(connection.ConnectionInfo.ConnectionType))
                            throw new Exception("Idiot check fail. The method ConnectionExists should have prevented execution getting here!!");
                        else
                            allConnectionsByEndPoint[endPointToUse].Add(connection.ConnectionInfo.ConnectionType, connection);
                    }
                    else
                        allConnectionsByEndPoint.Add(endPointToUse, new Dictionary<ConnectionType, Connection>() { { connection.ConnectionInfo.ConnectionType, connection } });
                }
            }
        }

        /// <summary>
        /// Update the endPoint reference for the provided connection with the newEndPoint. Just returns if there is no change
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="newEndPoint"></param>
        internal static void UpdateConnectionByEndPointReference(Connection connection, IPEndPoint newEndPoint)
        {
            if (!connection.ConnectionInfo.RemoteEndPoint.Equals(newEndPoint))
            {
                lock (globalDictAndDelegateLocker)
                {
                    RemoveConnectionReference(connection, false);
                    AddConnectionByEndPointReference(connection, newEndPoint);
                }
            }
        }

        /// <summary>
        /// Add a reference (by networkIdentifier) to the provided connection within NetworkComms.
        /// </summary>
        /// <param name="connection"></param>
        internal static void AddConnectionByIdentifierReference(Connection connection)
        {
            if (!connection.ConnectionInfo.ConnectionEstablished || connection.ConnectionInfo.ConnectionShutdown)
                throw new ConnectionSetupException("Connection reference by identifier should only be added once a connection is established. This is to prevent duplicate connections.");

            if (connection.ConnectionInfo.NetworkIdentifier == ShortGuid.Empty)
                throw new ConnectionSetupException("Should not be calling AddConnectionByIdentifierReference unless the connection remote identifier has been set.");

            lock (globalDictAndDelegateLocker)
            {
                //There should already be a reference to this connection in the endPoint dictionary
                if (!ConnectionExists(connection.ConnectionInfo.RemoteEndPoint, connection.ConnectionInfo.ConnectionType))
                    throw new ConnectionSetupException("A reference by identifier should only be added if a reference by endPoint already exists.");

                //Check for an existing reference first, if there is one and it matches this connection then no worries
                if (allConnectionsById.ContainsKey(connection.ConnectionInfo.NetworkIdentifier))
                {
                    if (allConnectionsById[connection.ConnectionInfo.NetworkIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType))
                    {
                        if (!allConnectionsById[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType].Contains(connection))
                        {
                            if ((from current in allConnectionsById[connection.ConnectionInfo.NetworkIdentifier][connection.ConnectionInfo.ConnectionType]
                                 where current.ConnectionInfo.RemoteEndPoint == connection.ConnectionInfo.RemoteEndPoint
                                 select current).Count() > 0)
                                throw new ConnectionSetupException("A different connection to the same remoteEndPoint already exists. Duplicate connections should be prevented elsewhere.");
                        }
                        else
                        {
                            //We are trying to add the same connection twice, so just do nothing here.
                        }
                    }
                    else
                        allConnectionsById[connection.ConnectionInfo.NetworkIdentifier].Add(connection.ConnectionInfo.ConnectionType, new List<Connection>() { connection });
                }
                else
                    allConnectionsById.Add(connection.ConnectionInfo.NetworkIdentifier, new Dictionary<ConnectionType, List<Connection>>() { { connection.ConnectionInfo.ConnectionType, new List<Connection>() {connection}} });
            }
        }
        #endregion
    }
}