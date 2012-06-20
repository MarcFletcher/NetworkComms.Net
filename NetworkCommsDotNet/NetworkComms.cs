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
using System.Collections.Specialized;
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
        /// The IP networkComms is operating on
        /// </summary>
        static string localIP = null;

        /// <summary>
        /// The preferred IP prefixs if network comms may try to auto select the ip address
        /// </summary>
        static string[] preferredIPPrefix = null;

        /// <summary>
        /// Setting preferred IP prefixs will help network comms select the correct listening ip address. An alternative is to set ListenOnAllInterfaces to true.
        /// Correct format is string[] { "192.168", "213.111.10" }.
        /// If multiple prefixs are provided the lower index prefix if found takes priority
        /// </summary>
        public static string[] PreferredIPPrefix
        {
            get { return preferredIPPrefix; }
            set
            {
                if (isListening || commsInitialised)
                    throw new CommsSetupException("Unable to change PreferredIPPRefix once already initialised. Shutdown comms, change value and restart.");

                preferredIPPrefix = value;
            }
        }

        /// <summary>
        /// Another way of selecting the desired adaptor is by name
        /// </summary>
        static string preferredAdaptorName = null;

        /// <summary>
        /// If a prefered adaptor name is provided, i.e. eth0, en0 etc. networkComms.net will try to listen on that adaptor.
        /// </summary>
        public static string PreferredAdaptorName
        {
            get { return preferredAdaptorName; }
            set
            {
                if (isListening || commsInitialised)
                    throw new CommsSetupException("Unable to change PreferredIPPRefix once already initialised. Shutdown comms, change value and restart.");

                preferredAdaptorName = value;
            }
        }

        /// <summary>
        /// Get the localIP as detected by network comms. If an incorrect IP is being returned
        /// set the IP on startup, specify PreferredIPPrefix or PreferredAdaptorName. If listening on multiple adaptors
        /// this getter on returns whatever is chosen as the primary ip, consider AllLocalIPs().
        /// </summary>
        public static string LocalIP
        {
            get
            {
                if (localIP == null)
                {
                    //If we are listening on all interfaces or we have specific prefereces we try to get an ip that way
                    if (listenOnAllInterfaces || preferredAdaptorName != null || (preferredIPPrefix != null && preferredIPPrefix.Length > 0))
                    {
                        string[] possibleIPs = AllLocalIPs();
                        if (possibleIPs.Length > 0)
                        {
                            localIP = possibleIPs[0];
                            return localIP;
                        }
                    }

#if !iOS
                    //If we did not get an IP by using the above method we try using an external ping
                    //This will only work in windows
                    localIP = AttemptBestIPAddressGuess();
                    if (localIP != null) return localIP;
#endif

                    //If we have got to this point we should probably throw an exception and let the user decide on the best course of action
                    if (preferredAdaptorName != null || preferredIPPrefix !=null)
                        throw new CommsSetupException("Unable to determine LocalIP address using provided prefixes. Specifiy LocalIP explicity before using comms, try different preferred prefixes or listenOnAllInterfaces.");
                    else
                        throw new CommsSetupException("Unable to determine LocalIP address. Specifiy LocalIP explicity, provide preferred prefixes or listenOnAllInterfaces.");
                }
                else
                    return localIP;
            }
            set
            {
                if (isListening || commsInitialised)
                    throw new CommsSetupException("Unable to change LocalIP once comms has been initialised. Shutdown comms, change IP and restart.");

                //If we want to set the localIP we can validate it here
                if (AllLocalIPs().Contains(value))
                {
                    localIP = value;
                    return;
                }

                throw new CommsSetupException("Unable to confirm validity of provided IP.");
            }
        }

        /// <summary>
        /// Returns all possible ipV4 addresses. Considers networkComms.PreferredIPPrefix and networkComms.PreferredAdaptorName. If preferredIPPRefix has been set ranks be descending preference. i.e. Most preffered at [0].
        /// </summary>
        /// <returns></returns>
        public static string[] AllLocalIPs()
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
                     where inside.Address.AddressFamily == AddressFamily.InterNetwork
                        && (preferredAdaptorName == null ? true : current.Id == preferredAdaptorName)
                        //&& (preferredIPPrefix == null ? true : preferredIPPrefix.Contains(inside.Address.ToString(), new IPComparer()))  
                     select inside).Count() > 0 &&
                     //We only want adaptors which are operational
                     current.OperationalStatus == OperationalStatus.Up
                    select
                    (
                    //Once we have adaptors that contain address information we are after the address
                    from inside in current.GetIPProperties().UnicastAddresses
                    where inside.Address.AddressFamily == AddressFamily.InterNetwork
                        && (preferredAdaptorName == null ? true : current.Id == preferredAdaptorName)
                        //&& (preferredIPPrefix == null ? true : preferredIPPrefix.Contains(inside.Address.ToString(), new IPComparer()))
                    select inside.Address.ToString()
                    ).ToArray()).Aggregate(new string[] { "" }, (i, j) => { return i.Union(j).ToArray(); }).OrderBy(ip =>
                    {
                        //If we have no preffered addresses we just return a default
                        if (preferredIPPrefix == null)
                            return int.MaxValue;
                        else
                        {
                            //We can check the preffered and return the index at which the IP occurs
                            for (int i = 0; i < preferredIPPrefix.Length; i++)
                                if (ip.StartsWith(preferredIPPrefix[i])) return i;

                            //If there was no match for this IP in the preffered IP range we just return maxValue
                            return int.MaxValue;
                        }
                    }).Where(ip => { return ip != ""; }).ToArray();
        }

        /// <summary>
        /// Custom comparer for v4 IPs. Used to determine localIP
        /// </summary>
        class IPComparer : IEqualityComparer<string>
        {
            // Products are equal if their names and product numbers are equal.
            public bool Equals(string x, string y)
            {
                //Check whether the compared objects reference the same data.
                if (Object.ReferenceEquals(x, y)) return true;

                //Check whether any of the compared objects is null.
                if (Object.ReferenceEquals(x, null) || Object.ReferenceEquals(y, null))
                    return false;

                return (y.StartsWith(x) || x.StartsWith(y));
            }

            // If Equals() returns true for a pair of objects 
            // then GetHashCode() must return the same value for these objects.

            public int GetHashCode(string ipAddress)
            {
                return ipAddress.GetHashCode();
            }

        }

        /// <summary>
        /// The port networkComms is operating on
        /// </summary>
        static int commsPort = 4000;

        /// <summary>
        /// The port networkComms is operating on
        /// </summary>
        public static int CommsPort
        {
            get { return commsPort; }
            set
            {
                //Need a quick check to see if we are already listening before trying to change the comms port.
                lock (globalDictAndDelegateLocker)
                {
                    //If we are trying to set the port to it's current value then nothing needs to happen.
                    if (value != commsPort)
                    {
                        if (isListening)
                            throw new CommsSetupException("Unable to change CommsPort once already listening. Shutdown comms, change port and restart.");

                        commsPort = value;
                    }
                }
            }
        }

        static bool enableNagleAlgorithmForEstablishedConnections = false;

        /// <summary>
        /// By default networkComms.net disables all usage of the nagle algorithm. If you wish it to be used for established connections set this property to true.
        /// </summary>
        public static bool EnableNagleAlgorithmForEstablishedConnections
        {
            get { return enableNagleAlgorithmForEstablishedConnections; }
            set
            {
                //Need a quick check to see if we are already listening before trying to change the comms port.
                lock (globalDictAndDelegateLocker)
                {
                    //If we are trying to set the port to it's current value then nothing needs to happen.
                    if (value != enableNagleAlgorithmForEstablishedConnections)
                    {
                        if (isListening)
                            throw new CommsSetupException("Unable to set EnableNagleAlgorithmForEstablishedConnections once already listening. Shutdown comms, set and restart.");

                        enableNagleAlgorithmForEstablishedConnections = value;
                    }
                }
            }
        }

        /// <summary>
        /// The local identifier of this instance of network comms
        /// </summary>
        internal static ShortGuid localNetworkIdentifier = ShortGuid.NewGuid();

        /// <summary>
        /// The local identifier of this instance of network comms
        /// </summary>
        public static string NetworkNodeIdentifier
        {
            get { return localNetworkIdentifier.ToString(); }
        }

        /// <summary>
        /// Returns the current instance network usage, as a value between 0 and 1. Outgoing and incoming usage are investigated and the larger of the two is used. Triggers load analysis upon first call.
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
        /// Retuns an averaged version of CurrentNetworkLoad, as a value between 0 and 1, upto a maximum window of 254 seconds. Triggers load analysis upon first call.
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

        public static long InterfaceLinkSpeed { get; set; }

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
            //Get the right interface
            NetworkInterface interfaceToUse = (from outer in NetworkInterface.GetAllNetworkInterfaces()
                                               where (from inner in outer.GetIPProperties().UnicastAddresses where inner.Address.ToString() == LocalIP select inner).Count() > 0
                                               select outer).FirstOrDefault();

            //We need to make sure we have managed to get an adaptor
            if (interfaceToUse == null) throw new CommunicationException("Unable to locate correct network adaptor.");

            do
            {
                try
                {
                    //If we are not running in Mono we can correctly get the link speed from the Speed property
                    //Other environment do not always implement this property correctly
                    //This is inside loop if for whatever reason the linkSpeed is changed during application execution
                    if (Type.GetType("Mono.Runtime") == null) InterfaceLinkSpeed = interfaceToUse.Speed;

                    //Get the usage over numMillisecsToAverage
                    long startSent, startRecieved, endSent, endRecieved;
                    DateTime startTime = DateTime.Now;

                    //We need to pull the stat numbers out here because different environments return different things on a call to interfaceToUse.GetIPv4Statistics();
                    IPv4InterfaceStatistics currentStats = interfaceToUse.GetIPv4Statistics();
                    startSent = currentStats.BytesSent;
                    startRecieved = currentStats.BytesReceived;

                    Thread.Sleep(NetworkLoadUpdateWindowMS);

                    currentStats = interfaceToUse.GetIPv4Statistics();
                    endSent = currentStats.BytesSent;
                    endRecieved = currentStats.BytesReceived;

                    DateTime endTime = DateTime.Now;

                    //Calculate both the out and in usage
                    decimal outUsage = (decimal)(endSent - startSent) / ((decimal)(InterfaceLinkSpeed * (endTime - startTime).TotalMilliseconds) / 8000);
                    decimal inUsage = (decimal)(endRecieved - startRecieved) / ((decimal)(InterfaceLinkSpeed * (endTime - startTime).TotalMilliseconds) / 8000);

                    //Take the maximum value
                    double returnValue = (double)Math.Max(outUsage, inUsage);

                    //Limit to one
                    CurrentNetworkLoad = (returnValue > 1 ? 1 : returnValue);
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
        #endregion

        #region Current Comms State
        internal static volatile bool commsInitialised = false;
        internal static volatile bool endListen = false;
        internal static volatile bool commsShutdown = false;
        internal static volatile bool isListening = false;

        /// <summary>
        /// An internal random number object for any randomisation requirements
        /// </summary>
        internal static Random randomGen = new Random();

        /// <summary>
        /// Returns true if network comms is currently accepting new incoming connections
        /// </summary>
        public static bool IsListening
        {
            get { return isListening; }
            private set { isListening = value; }
        }
        #endregion

        #region Established Connections
        /// <summary>
        /// Locker for all connection dictionaries
        /// </summary>
        internal static object globalDictAndDelegateLocker = new object();

        /// <summary>
        /// Primary connection dictionary stored by network indentifier
        /// </summary>
        internal static Dictionary<ShortGuid, TCPConnection> allConnectionsById = new Dictionary<ShortGuid, TCPConnection>();

        /// <summary>
        /// Secondary connection dictionary stored by ip end point. Allows for quick cross referencing.
        /// </summary>
        internal static Dictionary<IPEndPoint, TCPConnection> allConnectionsByEndPoint = new Dictionary<IPEndPoint, TCPConnection>();

        /// <summary>
        /// Old connection cache so that requests for connectionInfo can be returned even after a connection has been closed.
        /// </summary>
        internal static Dictionary<ShortGuid, ConnectionInfo> oldConnectionIdToConnectionInfo = new Dictionary<ShortGuid, ConnectionInfo>();

        /// <summary>
        /// The interval between keep alive polls of all serverside connections
        /// </summary>
        internal static int connectionKeepAlivePollIntervalSecs = 30;

        private static Thread ConnectionKeepAliveThread;

        /// <summary>
        /// The last time serverside connections were polled
        /// </summary>
        internal static DateTime lastConnectionKeepAlivePoll;

        /// <summary>
        /// The interval between keep alive polls of all connections. Set to int.MaxValue to disable keep alive
        /// </summary>
        public static int ConnectionKeepAlivePollIntervalSecs
        {
            get 
            {
                lock (globalDictAndDelegateLocker)
                    return connectionKeepAlivePollIntervalSecs; 
            }
            set
            {
                lock (globalDictAndDelegateLocker)
                    connectionKeepAlivePollIntervalSecs = value;
            }
        }
        #endregion

        #region Incoming Data and Connection Config
        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Sync performs slightly better but spawns a worker thread for each unique connection. Probably best not to use if you are going to have >50 simultaneous connections.
        /// </summary>
        internal static bool connectionListenModeUseSync = false;

        /// <summary>
        /// Networkcomms.net can listen on a single interface (IP) or all interfaces. Default is single interface.
        /// </summary>
        internal static bool listenOnAllInterfaces = false;

        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Default is false. No noticable performance difference between the two modes.
        /// </summary>
        public static bool ConnectionListenModeUseSync
        {
            get { return connectionListenModeUseSync; }
            set
            {
                if (isListening || commsInitialised)
                    throw new CommsSetupException("Unable to change connectionListenModeUseSync once already initialised. Shutdown comms, change mode and restart.");

                connectionListenModeUseSync = value;
            }
        }

        /// <summary>
        /// Used for switching between listening on a single interface or all interfaces. Default is false (single interface).
        /// </summary>
        public static bool ListenOnAllInterfaces
        {
            get { return listenOnAllInterfaces; }
            set
            {
                if (isListening || commsInitialised)
                    throw new CommsSetupException("Unable to change listenOnAllInterfaces once already initialised. Shutdown comms, change mode and restart.");

                listenOnAllInterfaces = value;
            }
        }

        /// <summary>
        /// New incoming connection listeners
        /// </summary>
        static Thread newIncomingListenThread;
        //static TcpListener tcpListener;

        /// <summary>
        /// Lists which handle the incoming connections. Originally this was used as a single but this allows networkComms.net to listen across multiple adaptors
        /// </summary>
        //static List<Thread> incomingListenThreadList;
        static List<TcpListener> tcpListenerList;

        /// <summary>
        /// Send and receive buffer sizes. These values are chosen to prevent the buffers ending up on the Large Object Heap
        /// </summary>
        internal static int receiveBufferSizeBytes = 80000;
        internal static int sendBufferSizeBytes = 80000;

        /// <summary>
        /// Receive data buffer size. Default is 256KB. CAUTION: Changing the default value can lead to severe performance degredation.
        /// </summary>
        public static int ReceiveBufferSizeBytes
        {
            get { return receiveBufferSizeBytes; }
            set
            {
                if (isListening || commsInitialised)
                    throw new CommsSetupException("Unable to change receiveBufferSizeBytes once already initialised. Shutdown comms, change and restart.");

                receiveBufferSizeBytes = value;
            }
        }

        /// <summary>
        /// Send data buffer size. Default is 256KB. CAUTION: Changing the default value can lead to severe performance degredation.
        /// </summary>
        public static int SendBufferSizeBytes
        {
            get { return sendBufferSizeBytes; }
            set
            {
                if (isListening || commsInitialised)
                    throw new CommsSetupException("Unable to change sendBufferSizeBytes once already initialised. Shutdown comms, change and restart.");

                sendBufferSizeBytes = value;
            }
        }
        #endregion

        #region High CPU Usage Tuning
        //In times of high CPU usage we need to ensure that certain time critical functions of networkComms happen quickly
        internal static ThreadPriority timeCriticalThreadPriority = ThreadPriority.AboveNormal;
        #endregion

        #region Checksum Config
        /// <summary>
        /// Determines whether incoming data is checkSumed
        /// </summary>
        internal static bool enablePacketCheckSumValidation = false;

        /// <summary>
        /// Set to true to enable checksum validation during all receives. Default is false, relying on the basic TCP checksum alone.
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
        /// Set to true to enable checksum validation during all receives. Default 150KB.
        /// </summary>
        public static int CheckSumMismatchSentPacketCacheMaxByteLimit
        {
            get { return checkSumMismatchSentPacketCacheMaxByteLimit; }
            set { checkSumMismatchSentPacketCacheMaxByteLimit = value; }
        }
        #endregion

        #region PacketType Config and Handlers
        /// <summary>
        /// A reference copy of all reservedPacketTypeNames
        /// </summary>
        internal static string[] reservedPacketTypeNames = Enum.GetNames(typeof(ReservedPacketType));

        /// <summary>
        /// Delegate method for all custom incoming packet handlers.
        /// </summary>
        public delegate void PacketHandlerCallBackDelegate<T>(PacketHeader packetHeader, ShortGuid sourceConnectionId, T incomingObject);

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
        private class PacketTypeUnwrapper
        {
            string packetTypeStr;
            public ICompress Compressor { get; private set; }
            public ISerialize Serializer { get; private set; }

            public PacketTypeUnwrapper(string packetTypeStr, ICompress compressor, ISerialize serializer)
            {
                this.packetTypeStr = packetTypeStr;
                this.Compressor = compressor;
                this.Serializer = serializer;
            }
        }

        /// <summary>
        /// If true any incoming packet types which do not have configured handlers will just be black holed
        /// </summary>
        internal static volatile bool ignoreUnknownPacketTypes = false;

        /// <summary>
        /// If true any unknown incoming packetTypes are simply ignored. Default is false.
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
        interface IPacketTypeHandlerDelegateWrapper : IEquatable<IPacketTypeHandlerDelegateWrapper>
        {
            object DeSerialize(byte[] incomingBytes, ISerialize serializer, ICompress compressor);

            void Process(PacketHeader packetHeader, ShortGuid sourceConnectionId, object obj);
            bool EqualsDelegate(Delegate other);
        }
        class PacketTypeHandlerDelegateWrapper<T> : IPacketTypeHandlerDelegateWrapper
        {
            PacketHandlerCallBackDelegate<T> innerDelegate;

            public PacketTypeHandlerDelegateWrapper(PacketHandlerCallBackDelegate<T> packetHandlerDelegate)
            {
                this.innerDelegate = packetHandlerDelegate;
            }

            public object DeSerialize(byte[] incomingBytes, ISerialize serializer, ICompress compressor)
            {
                return serializer.DeserialiseDataObject<T>(incomingBytes, compressor);
            }

            public void Process(PacketHeader packetHeader, ShortGuid sourceConnectionId, object obj)
            {
                innerDelegate(packetHeader, sourceConnectionId, (T)obj);
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
                return other == innerDelegate;
            }
        }
        #endregion

        #region Connection Shutdown
        /// <summary>
        /// Delegate method for connection shutdown delegates.
        /// </summary>
        public delegate void ConnectionShutdownDelegate(ShortGuid connectionId);

        /// <summary>
        /// A multicast delegate pointer for any connection shutdown delegates.
        /// </summary>
        internal static ConnectionShutdownDelegate globalConnectionShutdownDelegates;
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
        #endregion

        #region Serializers and Compressors
        /// <summary>
        /// The following are used for internal comms objects, packet headers, connection establishment etc. 
        /// We generally seem to increase the size of our data if compressing small objects (~50kb)
        /// Given the typical header size is 40kb we might as well not compress these objects.
        /// </summary>
        internal static readonly ISerialize internalFixedSerializer = ProtobufSerializer.Instance;
        internal static readonly ICompress internalFixedCompressor = NullCompressor.Instance;

        /// <summary>
        /// Default serializer and compressor for sending and receiving in the absence of specific values
        /// </summary>
        static ISerialize defaultSerializer = ProtobufSerializer.Instance;
#if !iOS
        static ICompress defaultCompressor = SevenZipLZMACompressor.LZMACompressor.Instance;
#else
        static ICompress defaultCompressor = NullCompressor.Instance;
#endif

        /// <summary>
        /// Get or set the default serializer for sending and receiving objects
        /// </summary>
        public static ISerialize DefaultSerializer
        {
            get { return defaultSerializer; }
            set { defaultSerializer = value; }
        }

        /// <summary>
        /// Get or set the default compressor for sending and receiving objects
        /// </summary>
        public static ICompress DefaultCompressor
        {
            get { return defaultCompressor; }
            set { defaultCompressor = value; }
        }
        #endregion

        #region Public Usage Methods

        #region Misc Utility
        /// <summary>
        /// Opens a comms port and waits for incoming connections
        /// </summary>
        public static void StartListening()
        {
            lock (globalDictAndDelegateLocker)
            {
                InitialiseComms();

                //We only start a new thread if we are currently not listening and endListen is false
                if (!endListen && !isListening)
                {
                    OpenIncomingPorts();

                    newIncomingListenThread = new Thread(IncomingConnectionListenThread);
                    newIncomingListenThread.Priority = timeCriticalThreadPriority;
                    newIncomingListenThread.Name = "NetworkCommsIncomingListen";

                    IsListening = true;

                    newIncomingListenThread.Start();
                }
            }
        }

        /// <summary>
        /// Close all established connections
        /// </summary>
        public static void CloseAllConnections()
        {
            //We need to create a copy because we need to avoid a collection modifed error and possible deadlocks from within closeConnection
            //Dictionary<IPEndPoint, TCPConnection> dictCopy;
            //lock (globalDictAndDelegateLocker)
            //    dictCopy = new Dictionary<IPEndPoint, TCPConnection>(allConnectionsByEndPoint);

            ////Any connections created after the above line will not be closed, a subsequent call to shutdown will get those.
            //if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Closing all connections, currently " + dictCopy.Count + " are active.");

            //foreach (TCPConnection client in dictCopy.Values)
            //    client.CloseConnection(false, -3);

            //if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... all connections have been closed.");
            CloseAllConnections(new string[] { });
        }

        /// <summary>
        /// Close all established connections with some provided exceptions.
        /// </summary>
        /// <param name="closeAllExceptTheseConnectionsByIP">If a connection has a matching ip address it wont be closed</param>
        public static void CloseAllConnections(string[] closeAllExceptTheseConnectionsByIP)
        {
            //We need to create a copy because we need to avoid a collection modifed error and possible deadlocks from within closeConnection
            Dictionary<IPEndPoint, TCPConnection> dictCopy;
            lock (globalDictAndDelegateLocker)
                dictCopy = new Dictionary<IPEndPoint, TCPConnection>(allConnectionsByEndPoint);

            //Any connections created after the above line will not be closed, a subsequent call to shutdown will get those.
            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Closing all connections (with "+closeAllExceptTheseConnectionsByIP.Length+" exceptions), currently " + dictCopy.Count + " are active.");

            foreach (TCPConnection client in dictCopy.Values)
            {
                if (closeAllExceptTheseConnectionsByIP.Length > 0)
                {
                    if (!closeAllExceptTheseConnectionsByIP.Contains(client.RemoteClientIP))
                        client.CloseConnection(false, -3);
                }
                else
                    client.CloseConnection(false, -3);
            }

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... all connections have been closed.");
        }

        /// <summary>
        /// Closes the specified connection. Any global or connection specific shutdown delegates will be executed.
        /// </summary>
        /// <param name="connectionId"></param>
        public static void CloseConnection(ShortGuid connectionId)
        {
            TCPConnection targetConnection = CheckForConnection(connectionId);
            targetConnection.CloseConnection(false, -1);
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

        /// <summary>
        /// Checks all current connections to make sure they are active. If any problems occur the connection will be closed.
        /// </summary>
        /// <param name="returnImmediately">If true method will run as task and return immediately, otherwise return time is proportional to total number of connections.</param>
        /// <param name="lastTrafficTimePassSeconds">Will not test connections which have a lastSeen time within provided number of seconds</param>
        public static void TestAllConnectionsAliveStatus(bool returnImmediately = false, int lastTrafficTimePassSeconds=0)
        {
            //Loop through all connections and test the alive state
            List<TCPConnection> dictCopy;
            lock (globalDictAndDelegateLocker)
                dictCopy = new Dictionary<IPEndPoint, TCPConnection>(allConnectionsByEndPoint).Values.ToList();

            List<Task> connectionCheckTasks = new List<Task>();

            for (int i = 0; i < dictCopy.Count; i++)
            {
                int innerIndex = i;
                connectionCheckTasks.Add(Task.Factory.StartNew(new Action(() => 
                {
                    if ((DateTime.Now - dictCopy[innerIndex].LastTrafficTime).TotalSeconds > lastTrafficTimePassSeconds)
                        dictCopy[innerIndex].CheckConnectionAliveState(connectionAliveTestTimeoutMS); 
                })));
            }

            if (!returnImmediately) Task.WaitAll(connectionCheckTasks.ToArray());
        }

        /// <summary>
        /// Pings the provided connection and returns true if the ping was succesfull, returns false otherwise. Usefull to see if an ip address is listening for connections.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// <param name="pingTimeoutMS"></param>
        /// <returns></returns>
        public static bool PingConnection(string ipAddress, int port, int pingTimeoutMS)
        {
            try
            {
                return NetworkComms.SendReceiveObject<bool>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), ipAddress, port, false, Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), pingTimeoutMS, false, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
            }
            catch (CommsException)
            {
                return false;
            }
        }

        /// <summary>
        /// Pings the provided connection and returns true if the ping was succesfull after setting pingTimeMS, returns false otherwise. Usefull to see if an ip address is listening for connections.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// <param name="pingTimeoutMS"></param>
        /// <param name="pingTimeMS"></param>
        /// <returns></returns>
        public static bool PingConnection(string ipAddress, int port, int pingTimeoutMS, out long pingTimeMS)
        {
            try
            {
                Stopwatch timer = new Stopwatch();
                timer.Start();
                bool result = NetworkComms.SendReceiveObject<bool>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), ipAddress, port, false, Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), pingTimeoutMS, false, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                timer.Stop();
                pingTimeMS = timer.ElapsedMilliseconds;
                return result;
            }
            catch (CommsException)
            {
                pingTimeMS = long.MaxValue;
                return false;
            }
        }

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

        #region PacketHandlers
        /// <summary>
        /// Add a new shutdown delegate which will be called for every connection as it is closes.
        /// </summary>
        /// <param name="connectionShutdownDelegate"></param>
        public static void AppendGlobalConnectionCloseHandler(ConnectionShutdownDelegate connectionShutdownDelegate)
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
        public static void RemoveGlobalConnectionCloseHandler(ConnectionShutdownDelegate connectionShutdownDelegate)
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
        /// Add a new incoming packet handler. Multiple handlers for the same packet type are allowed
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be used</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to use</param>
        /// <param name="packetTypeStrSerializer">A specific serializer to use instead of default</param>
        /// <param name="packetTypeStrCompressor">A specific compressor to use instead of default</param>
        /// <param name="enableAutoListen">If true will enable comms listening after delegate has been added</param>
        public static void AppendIncomingPacketHandler<T>(string packetTypeStr, PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer, ISerialize packetTypeStrSerializer, ICompress packetTypeStrCompressor, bool enableAutoListen = true)
        {
            lock (globalDictAndDelegateLocker)
            {
                //Add the custom serializer and compressor if necessary
                if (packetTypeStrSerializer != null && packetTypeStrCompressor != null)
                {
                    if (globalIncomingPacketUnwrappers.ContainsKey(packetTypeStr))
                    {
                        //Make sure if we already have an existing entry that it matches with the provided
                        if (globalIncomingPacketUnwrappers[packetTypeStr].Compressor != packetTypeStrCompressor || globalIncomingPacketUnwrappers[packetTypeStr].Serializer != packetTypeStrSerializer)
                            throw new PacketHandlerException("You cannot specify a different compressor or serializer instance if one has already been specified for this packetTypeStr.");
                    }
                    else
                        globalIncomingPacketUnwrappers.Add(packetTypeStr, new PacketTypeUnwrapper(packetTypeStr, packetTypeStrCompressor, packetTypeStrSerializer));
                }
                else if (packetTypeStrSerializer != null ^ packetTypeStrCompressor != null)
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

                //Start listening if we have not already.
                if (enableAutoListen)
                    StartListening();
            }
        }

        /// <summary>
        /// Add a new incoming packet handler using default serializer and compressor. Multiple handlers for the same packet type are allowed
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be used</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to use</param>
        /// <param name="enableAutoListen">If true will enable comms listening after delegate has been added</param>
        public static void AppendIncomingPacketHandler<T>(string packetTypeStr, PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer, bool enableAutoListen = true)
        {
            AppendIncomingPacketHandler<T>(packetTypeStr, packetHandlerDelgatePointer, null, null, enableAutoListen);
        }

        /// <summary>
        /// Removes the provided delegate for the specified packet type
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public static void RemoveIncomingPacketHandler(string packetTypeStr, Delegate packetHandlerDelgatePointer)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Create the compare object
                    //PacketTypeHandlerDelegateWrapper<T> toCompareDelegate = new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer);

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
        public static void RemoveAllCustomPacketHandlers(string packetTypeStr)
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
        public static void RemoveAllCustomPacketHandlers()
        {
            lock (globalDictAndDelegateLocker)
            {
                globalIncomingPacketHandlers = new Dictionary<string, List<IPacketTypeHandlerDelegateWrapper>>();

                if (loggingEnabled) logger.Info("Removed all incoming packetHandlers for all packetTypes");
           } 
        }

        /// <summary>
        /// Trigger all packet type delegates with the provided parameters
        /// </summary>
        /// <param name="packetHeader">Packet type for which all delegates should be triggered</param>
        /// <param name="sourceConnectionId">The source connection id</param>
        /// <param name="incomingObjectBytes">The serialised and or compressed bytes to be used</param>
        public static void TriggerPacketHandler(PacketHeader packetHeader, ShortGuid sourceConnectionId, byte[] incomingObjectBytes)
        {
            TriggerPacketHandler(packetHeader, sourceConnectionId, incomingObjectBytes, null, null);
        }

        /// <summary>
        /// Trigger all packet type delegates with the provided parameters. Providing serializer and compressor will override any defaults.
        /// </summary>
        /// <param name="packetHeader">Packet type for which all delegates should be triggered</param>
        /// <param name="sourceConnectionId">The source connection id</param>
        /// <param name="incomingObjectBytes">The serialised and or compressed bytes to be used</param>
        /// <param name="serializer">Override serializer</param>
        /// <param name="compressor">Override compressor</param>
        public static void TriggerPacketHandler(PacketHeader packetHeader, ShortGuid sourceConnectionId, byte[] incomingObjectBytes, ISerialize serializer, ICompress compressor)
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
                        LogError(new UnexpectedPacketTypeException("The received packet type '" + packetHeader.PacketType + "' has no configured handler and network comms is not set to ignore unknown packet types. Set NetworkComms.IgnoreUnknownPacketTypes=true to prevent this error."), "PacketHandlerError_" + packetHeader.PacketType);
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

                    //We decide which serializer and compressor to use
                    if (globalIncomingPacketUnwrappers.ContainsKey(packetHeader.PacketType))
                    {
                        if (serializer == null) serializer = globalIncomingPacketUnwrappers[packetHeader.PacketType].Serializer;
                        if (compressor == null) compressor = globalIncomingPacketUnwrappers[packetHeader.PacketType].Compressor;
                    }
                    else
                    {
                        if (serializer == null) serializer = DefaultSerializer;
                        if (compressor == null) compressor = DefaultCompressor;
                    }

                    //Deserialise the object only once
                    object returnObject = handlersCopy[0].DeSerialize(incomingObjectBytes, serializer, compressor);

                    //Pass the data onto the handler and move on.
                    if (loggingEnabled) logger.Trace(" ... passing completed data packet to selected handlers.");

                    //Pass the object to all necessary delgates
                    //We need to use a copy because we may modify the original delegate list during processing
                    foreach (IPacketTypeHandlerDelegateWrapper wrapper in handlersCopy)
                    {
                        try
                        {
                            wrapper.Process(packetHeader, sourceConnectionId, returnObject);
                        }
                        catch (Exception ex)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An unhandled exception was caught while processing a packet handler for a packet type '" + packetHeader.PacketType + "'. Make sure to catch errors in packet handlers. See error log file for more information.");
                            NetworkComms.LogError(ex, "PacketHandlerError_" + packetHeader.PacketType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An exception occured in TriggerPacketHandler() for a packet type '" + packetHeader.PacketType + "'. See error log file for more information.");
                NetworkComms.LogError(ex, "PacketHandlerError_" + packetHeader.PacketType);
            }
        }
        #endregion

        #region Information

        public static event EventHandler<EventArgs> OnCommsShutdown;

        /// <summary>
        /// Shutdown all connections and clean up communciation objects. If any comms activity has taken place this should be called on application close
        /// </summary>
        public static void ShutdownComms()
        {
            if (OnCommsShutdown != null)
                OnCommsShutdown(null, new EventArgs());

            //Signal everything we are shutting down
            commsShutdown = true;
            endListen = true;

            try
            {
                //We need to start by closing all connections before we stop the incoming listen thread to try and prevent the TIME_WAIT problem
                RemoveAllCustomPacketHandlers();
                CloseAllConnections();

                try
                {
                    //lock (globalDictAndDelegateLocker)
                    //{
                        //We need to make sure everything has shutdown before this method returns
                        if (isListening && newIncomingListenThread != null)// && (newIncomingListenThread.ThreadState == System.Threading.ThreadState.Running || newIncomingListenThread.ThreadState == System.Threading.ThreadState.WaitSleepJoin))
                        {
                            if (!newIncomingListenThread.Join(PacketConfirmationTimeoutMS))
                            {
                                //If we are still waiting for a close it may be stuck on AcceptTCPClient
                                if (tcpListenerList != null)
                                {
                                    try
                                    {
                                        foreach (var listener in tcpListenerList)
                                        {
                                            try
                                            {
                                                if (listener != null) listener.Stop();
                                            }
                                            catch (Exception) { }
                                        }
                                    }
                                    catch (Exception) { }
                                    finally
                                    {
                                        //Once we have stopped all listeners we set the list to null incase we want to resart listening
                                        tcpListenerList = null;
                                    }
                                }

                                //We now wait for a further time. If it is still stuck it gets nuked
                                if (!newIncomingListenThread.Join(PacketConfirmationTimeoutMS * 10))
                                {
                                    newIncomingListenThread.Abort();
                                    globalDictAndDelegateLocker = new object();

                                    throw new TimeoutException("Timeout waiting for network comms to shutdown after " + PacketConfirmationTimeoutMS * 10 + " ms. endListen state is " + endListen.ToString() + ", isListening stats is " + isListening.ToString() + ". Incoming connection listen thread aborted.");
                                }
                            }
                        }
                    //}
                }
                finally
                {
                    //One last attempt at closing any still existing connections
                    CloseAllConnections();
                }

                if (loggingEnabled) logger.Info("Network comms has shutdown");
            }
            catch (CommsException)
            {

            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            //We need to wait for the polling thread to close here
            try
            {
                if (ConnectionKeepAliveThread != null)
                {
                    if (!ConnectionKeepAliveThread.Join(PacketConfirmationTimeoutMS * 10))
                    {
                        ConnectionKeepAliveThread.Abort();
                        throw new TimeoutException("Timeout waiting for connectionKeepAlivePollThread thread to shutdown after " + PacketConfirmationTimeoutMS * 10 + " ms. commsShutdown state is " + commsShutdown.ToString() + ", endListen state is " + endListen.ToString() + ", isListening stats is " + isListening.ToString() + ". connectionKeepAlivePollThread status = "+ ConnectionKeepAliveThreadState() +" connectionKeepAlivePollThread thread aborted. Number of connections = "+TotalNumConnections()+".");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            try
            {
                if (NetworkLoadThread != null)
                {
                    if (!NetworkLoadThread.Join(NetworkLoadUpdateWindowMS * 2))
                    {
                        NetworkLoadThread.Abort();
                        throw new TimeoutException("Timeout waiting for NetworkLoadThread thread to shutdown after " + PacketConfirmationTimeoutMS * 10 + " ms. commsShutdown state is " + commsShutdown.ToString() + ", endListen state is " + endListen.ToString() + ", isListening stats is " + isListening.ToString() + ". NetworkLoadThread thread aborted.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            commsInitialised = false;
            commsShutdown = false;
            endListen = false;
        }

        /// <summary>
        /// Returns true if a network connection exists with the provided remoteNetworkIdentifier
        /// </summary>
        /// <param name="remoteNetworkIdentifier"></param>
        /// <returns></returns>
        public static bool ConnectionExists(string remoteNetworkIdentifier)
        {
            lock (globalDictAndDelegateLocker)
            {
                return (allConnectionsById.ContainsKey(new ShortGuid(remoteNetworkIdentifier)));
            }
        }

        /// <summary>
        /// Returns true if a connection already exists with the provided ip and port number
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="portNumber"></param>
        /// <returns></returns>
        public static bool ConnectionExists(string ipAddress, int portNumber)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), portNumber);

            lock (globalDictAndDelegateLocker)
            {
                if (allConnectionsByEndPoint.ContainsKey(endPoint))
                    return true;
                else
                    return false;
            }
        }

        /// <summary>
        /// Converts a connectionId into connectionInfo if a connection with the corresponding id exists
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public static ConnectionInfo ConnectionIdToConnectionInfo(ShortGuid connectionId)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (allConnectionsById.ContainsKey(connectionId))
                    return allConnectionsById[connectionId].ConnectionInfo;
                else
                {
                    if (oldConnectionIdToConnectionInfo.ContainsKey(connectionId))
                        return oldConnectionIdToConnectionInfo[connectionId];
                    else
                        throw new InvalidConnectionIdException("Unable to locate connection with provided connectionId.");
                }
            }
        }

        /// <summary>
        /// Returns an array of ConnectionInfo for every currently established connection
        /// </summary>
        /// <returns></returns>
        public static ConnectionInfo[] AllEstablishedConnections()
        {
            List<ConnectionInfo> returnArray = new List<ConnectionInfo>();

            lock (globalDictAndDelegateLocker)
            {
                foreach (var connection in allConnectionsById)
                    returnArray.Add(connection.Value.ConnectionInfo);
            }

            return returnArray.ToArray();
        }

        /// <summary>
        /// Return the total current number of connections in network comms  
        /// </summary>
        /// <returns></returns>
        public static int TotalNumConnections()
        {
            lock (globalDictAndDelegateLocker)
            {
                return allConnectionsByEndPoint.Count;
            }
        }

        /// <summary>
        /// Return the total current number of connections in network comms which originate from the provided ip
        /// </summary>
        /// <param name="matchIP">IP address in the format "192.168.0.1"</param>
        /// <returns></returns>
        public static int TotalNumConnections(string matchIP)
        {
            lock (globalDictAndDelegateLocker)
            {
                return (from current in allConnectionsByEndPoint
                        where current.Value.RemoteClientIP == matchIP
                        select current).Count();
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
        public static void SendObject(string packetTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, object sendObject, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);
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
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, object sendObject, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);
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
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);
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
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);
        }

        /// <summary>
        /// Send the provided object to the specified connectionId. Uses the network comms default compressor and serializer
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use during send</param>
        /// <param name="connectionId">Destination connection id</param>
        /// <param name="receiveConfirmationRequired">If true will return only when object is successfully received at destination</param>
        /// <param name="sendObject">The obect to send</param>
        public static void SendObject(string packetTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, object sendObject)
        {
            TCPConnection targetConnection = CheckForConnection(connectionId);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);
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
        public static void SendObject(string packetTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);
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
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);
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
        public static void SendObject(string packetTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);
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
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);
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
        public static void SendObject(string packetTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
            TCPConnection targetConnection = CheckForConnection(connectionId);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {
            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, connectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, null, null, null, null);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming, ref ShortGuid connectionId)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, CommsPort);
            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {
            TCPConnection targetConnection = EstablishTCPConnection(destinationIPAddress, commsPort);
            return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {
            TCPConnection targetConnection = CheckForConnection(connectionId);

            returnObjectType returnObject = default(returnObjectType);

            bool remotePeerDisconnectedDuringWait = false;
            AutoResetEvent returnWaitSignal = new AutoResetEvent(false);

            #region SendReceiveDelegate
            PacketHandlerCallBackDelegate<returnObjectType> SendReceiveDelegate = (packetHeader, sourceConnectionId, incomingObject) =>
            {
                if (sourceConnectionId == connectionId)
                {
                    returnObject = incomingObject;
                    returnWaitSignal.Set();
                }
            };

            //We use the following delegate to quickly force a response timeout if the remote end disconnects
            ConnectionShutdownDelegate SendReceiveShutDownDelegate = (sourceConnectionId) =>
            {
                if (sourceConnectionId == connectionId)
                {
                    remotePeerDisconnectedDuringWait = true;
                    returnObject = default(returnObjectType);
                    returnWaitSignal.Set();
                }
            };
            #endregion

            targetConnection.AppendConnectionSpecificShutdownHandler(SendReceiveShutDownDelegate);
            AppendIncomingPacketHandler(expectedReturnPacketTypeStr, SendReceiveDelegate, serializerIncoming, compressorIncoming, false);

            if (serializerOutgoing == null) serializerOutgoing = DefaultSerializer;
            if (compressorOutgoing == null) compressorOutgoing = DefaultCompressor;

            Packet sendPacket = new Packet(sendingPacketTypeStr, receiveConfirmationRequired, sendObject, serializerOutgoing, compressorOutgoing);
            targetConnection.SendPacket(sendPacket);

            //We wait for the return data here
            if (!returnWaitSignal.WaitOne(returnPacketTimeOutMilliSeconds))
            {
                RemoveIncomingPacketHandler(expectedReturnPacketTypeStr, SendReceiveDelegate);
                throw new ExpectedReturnTimeoutException("Timeout occurred after " + returnPacketTimeOutMilliSeconds + "ms waiting for response packet of type '" + expectedReturnPacketTypeStr + "'.");
            }

            RemoveIncomingPacketHandler(expectedReturnPacketTypeStr, SendReceiveDelegate);
            targetConnection.RemoveConnectionSpecificShutdownHandler(SendReceiveShutDownDelegate);

            if (remotePeerDisconnectedDuringWait)
                throw new ExpectedReturnTimeoutException("Remote end closed connection before data was successfully returned.");
            else
                return returnObject;
        }
        
        #endregion SendReceiveObjectSpecific

        #endregion Public Usage Methods

        #region Private Setup, Connection and Shutdown

        static NetworkComms()
        {
            NetworkLoadUpdateWindowMS = 200;
            InterfaceLinkSpeed = 100000000;
        }

        /// <summary>
        /// Initialise comms items on startup
        /// </summary>
        private static void InitialiseComms()
        {
            lock (globalDictAndDelegateLocker)
            {
                if (!commsInitialised)
                {
                    ConnectionKeepAliveThread = new Thread(ConnectionKeepAliveThreadWorker);
                    ConnectionKeepAliveThread.Name = "ConnectionKeepAliveThread";
                    ConnectionKeepAliveThread.Start();

                    commsInitialised = true;
                    if (loggingEnabled) logger.Info("networkComms.net has been initialised");
                }
            }
        }

        /// <summary>
        /// Attempts to guess the primary ip address of this machine using dll hooks in Windows API.
        /// </summary>
        /// <returns>IP address or null if failed.</returns>
        private static string AttemptBestIPAddressGuess()
        {
            try
            {
                //We work out the best interface for connecting with the outside world
                //If we are going to try and choose an ip address this one makes the most sense
                //Using Google DNS server as reference IP
                UInt32 ipaddr = BitConverter.ToUInt32(new byte[] { 8, 8, 8, 8 }, 0);

                UInt32 interfaceindex = 0;
                IPExtAccess.GetBestInterface(ipaddr, out interfaceindex);

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                var bestInterface = (from current in interfaces
                                     where current.GetIPProperties().GetIPv4Properties().Index == interfaceindex
                                     select current).First();

                var ipAddressBest = (from current in bestInterface.GetIPProperties().UnicastAddresses
                                     where current.Address.AddressFamily == AddressFamily.InterNetwork
                                     select current.Address).First().ToString();

                if (ipAddressBest != null)
                {
                    localIP = ipAddressBest;
                    return localIP;
                }
            }
            catch (Exception)
            {

            }

            return null;
        }

        /// <summary>
        /// Send the provided object to the specified connection. Uses the provided compressor and serializer delegates.
        /// This is only used during initial connection establish
        /// </summary>
        /// <param name="packetTypeStr"></param>
        /// <param name="targetConnection"></param>
        /// <param name="receiveConfirmationRequired"></param>
        /// <param name="sendObject"></param>
        /// <param name="serializer"></param>
        /// <param name="compressor"></param>
        internal static void SendObject(string packetTypeStr, TCPConnection targetConnection, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);
        }

        /// <summary>
        /// New incoming connection listen worker thread
        /// </summary>
        private static void IncomingConnectionListenThread()
        {
            lock (globalDictAndDelegateLocker) lastConnectionKeepAlivePoll = DateTime.Now;

            if (loggingEnabled) logger.Info("networkComms.net is now waiting for new connections.");

            try
            {
                do
                {
                    try
                    {
                        bool pickedUpNewConnection = false;
                        foreach (var listener in tcpListenerList)
                        {
                            if (listener.Pending() && !commsShutdown)
                            {
                                pickedUpNewConnection = true;

                                //Pick up the new connection
                                TcpClient newClient = listener.AcceptTcpClient();

                                //Build the endPoint object based on available information at the current moment
                                IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(newClient.Client.RemoteEndPoint.ToString().Split(':')[0]), int.Parse(newClient.Client.RemoteEndPoint.ToString().Split(':')[1]));

                                TCPConnection newConnection = new TCPConnection(true, newClient);

                                //Once we have the connection we want to check if we already have an existing one
                                //If we already have a connection with this remote end point we close it
                                TCPConnection existingConnection = null;
                                lock (globalDictAndDelegateLocker)
                                {
                                    if (allConnectionsByEndPoint.ContainsKey(endPoint))
                                        existingConnection = allConnectionsByEndPoint[endPoint];
                                }
                                //GAP START

                                //If we had an existing connection we will try to close it here
                                //Do this outside the locker above to ensure the closeConnection works
                                if (existingConnection != null) existingConnection.CloseConnection(false, -4);

                                //In the commented GAP another thread may have established a new outgoing connection
                                //If we have then there is no need to finish establishing this one
                                bool establishNewConnection = false;

                                //GAP END
                                lock (globalDictAndDelegateLocker)
                                {
                                    if (!allConnectionsByEndPoint.ContainsKey(endPoint))
                                    {
                                        allConnectionsByEndPoint.Add(endPoint, newConnection);
                                        establishNewConnection = true;
                                    }
                                }

                                //If we have made it this far and establish connection is true we can proceed with the handshake
                                if (establishNewConnection) newConnection.EstablishConnection();
                            }
                        }

                        //We will only pause if we didnt get any new connections
                        if (!pickedUpNewConnection)
                            Thread.Sleep(200);
                    }
                    catch (ConfirmationTimeoutException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (CommunicationException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (ConnectionSetupException)
                    {
                        //If we are the server end and we did not pick the incoming connection up then tooo bad!
                    }
                    catch (SocketException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (Exception ex)
                    {
                        //For some odd reason SocketExceptions don't always get caught above, so another check
                        if (ex.GetBaseException().GetType() != typeof(SocketException))
                        {
                            //Can we catch the socketException by looking at the string error text?
                            if (ex.ToString().StartsWith("System.Net.Sockets.SocketException"))
                                LogError(ex, "CommsSetupError_SE");
                            else
                                LogError(ex, "CommsSetupError");
                        }
                    }
                } while (!endListen);
            }
            catch (Exception ex)
            {
                LogError(ex, "CriticalCommsError");
            }
            finally
            {
                //We try to close all of the tcpListeners
                if (tcpListenerList != null)
                {
                    try
                    {
                        foreach (var listener in tcpListenerList)
                        {
                            try
                            {
                                if (listener != null) listener.Stop();
                            }
                            catch (Exception) { }
                        }
                    }
                    catch (Exception) { }
                    finally
                    {
                        //Once we have stopped all listeners we set the list to null incase we want to resart listening
                        tcpListenerList = null;
                    }
                }

                //If we get this far we have definately stopped accepting new connections
                endListen = false;
                isListening = false;
            }

            //newIncomingListenThread = null;
            if (loggingEnabled) logger.Info("networkComms.net is no longer accepting new connections.");
        }

        /// <summary>
        /// A thread that ensures all established connections are maintained
        /// </summary>
        private static void ConnectionKeepAliveThreadWorker()
        {
            if (loggingEnabled) logger.Debug("Connection keep alive polling thread has started.");
            DateTime lastPollCheck = DateTime.Now;

            do
            {
                try
                {
                    //We have a short sleep here so that we can exit the thread fairly quickly if we need too
                    Thread.Sleep(100);

                    //Any connections which we have not seen in the last poll interval get tested using a null packet
                    if ((DateTime.Now - lastPollCheck).TotalSeconds > (double)ConnectionKeepAlivePollIntervalSecs)
                    {
                        ConnectionKeepAlivePoll();
                        lastPollCheck = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex, "KeepAlivePollError");
                }
            } while (!commsShutdown);

            //connectionKeepAlivePollThread = null;
        }

        /// <summary>
        /// Returns the threadState of the IncomingConnectionListenThread
        /// </summary>
        /// <returns></returns>
        public static System.Threading.ThreadState IncomingConnectionListenThreadState()
        {
            lock(globalDictAndDelegateLocker)
            {
                if (newIncomingListenThread != null)
                    return newIncomingListenThread.ThreadState;
                else
                    return System.Threading.ThreadState.Unstarted;
            }
        }

        /// <summary>
        /// Returns the threadState of the ConnectionKeepAlivePollThread
        /// </summary>
        /// <returns></returns>
        public static System.Threading.ThreadState ConnectionKeepAliveThreadState()
        {
            lock (globalDictAndDelegateLocker)
            {
                if (ConnectionKeepAliveThread != null)
                    return ConnectionKeepAliveThread.ThreadState;
                else
                    return System.Threading.ThreadState.Unstarted;
            }
        }

        /// <summary>
        /// Polls all existing connections based on ConnectionKeepAlivePollIntervalSecs value. Serverside connections are polled slightly earlier than client side to help reduce potential congestion.
        /// </summary>
        /// <param name="returnImmediately"></param>
        private static void ConnectionKeepAlivePoll(bool returnImmediately = false)
        {
            if (ConnectionKeepAlivePollIntervalSecs < int.MaxValue)
            {
                //Loop through all connections and test the alive state
                List<TCPConnection> dictCopy;
                lock (globalDictAndDelegateLocker)
                    dictCopy = new Dictionary<IPEndPoint, TCPConnection>(allConnectionsByEndPoint).Values.ToList();

                List<Task> connectionCheckTasks = new List<Task>();

                for (int i = 0; i < dictCopy.Count; i++)
                {
                    int innerIndex = i;

                    connectionCheckTasks.Add(Task.Factory.StartNew(new Action(() =>
                    {
                        try
                        {
                            //If the connection is server side we poll preferentially
                            if (dictCopy[innerIndex] != null)
                            {
                                if (dictCopy[innerIndex].ServerSide)
                                {
                                    //We check the last incoming traffic time
                                    //In scenarios where the client is sending us lots of data there is no need to poll
                                    if ((DateTime.Now - dictCopy[innerIndex].LastTrafficTime).TotalSeconds > ConnectionKeepAlivePollIntervalSecs)
                                        dictCopy[innerIndex].SendNullPacket();
                                }
                                else
                                {
                                    //If we are client side we wait upto an additional 3 seconds to do the poll
                                    //This means the server will probably beat us
                                    if ((DateTime.Now - dictCopy[innerIndex].LastTrafficTime).TotalSeconds > ConnectionKeepAlivePollIntervalSecs + 1.0 + (NetworkComms.randomGen.NextDouble() * 2.0))
                                        dictCopy[innerIndex].SendNullPacket();
                                }
                            }
                        }
                        catch (Exception) { }
                    })));
                }

                if (!returnImmediately) Task.WaitAll(connectionCheckTasks.ToArray());
            }
        }

        /// <summary>
        /// Opens a local port for incoming connections
        /// </summary>
        private static void OpenIncomingPorts()
        {
            //If we already have open listeners we need to make sure they are closed correctly
            if (tcpListenerList != null && tcpListenerList.Count > 0)
                throw new CommunicationException("Port Listeners have already been defined. Unable to open further ports.");

            //Make sure we have a list of listeners
            tcpListenerList = new List<TcpListener>();
            TcpListener newListenerInstance = null;

            if (listenOnAllInterfaces)
            {
                //We need a list of all IP address and then open a listener for each one
                IPAddress[] allIPs = (from current in AllLocalIPs() select System.Net.IPAddress.Parse(current)).ToArray();

                foreach (var ipAddress in allIPs)
                {
                    try
                    {
                        newListenerInstance = new TcpListener(ipAddress, CommsPort);
                        newListenerInstance.Start();
                        tcpListenerList.Add(newListenerInstance);
                    }
                    catch (SocketException)
                    {
                        //We need to ensure the same port is used across all interfaces otherwise someone will end up pulling their hair out trying to troubleshoot this particular problem.
                        //We won't throw an exception on 169.x.x.x addresses as they are generally non routable anyway
                        if (!ipAddress.ToString().StartsWith("169"))
                            throw new CommsSetupException("Port " + CommsPort + " was already in use on IP " + ipAddress.ToString() + ". Ensure port is available across all interfaces, change CommsPort, or only enable listening on a single interface.");
                    }
                }
            }
            else
            {
                System.Net.IPAddress localAddress = System.Net.IPAddress.Parse(LocalIP);

                try
                {
                    newListenerInstance = new TcpListener(localAddress, CommsPort);
                    newListenerInstance.Start();
                }
                catch (SocketException)
                {
                    //The port we wanted is not available so we need to let .net choose one for us
                    newListenerInstance = new TcpListener(localAddress, 0);
                    newListenerInstance.Start();

                    //Need to jump commsInitialised otherwise we won't be able to change the port
                    CommsPort = ((IPEndPoint)newListenerInstance.LocalEndpoint).Port;
                }
                finally
                {
                    tcpListenerList.Add(newListenerInstance);
                }
            }

            if (loggingEnabled) logger.Info("networkComms.net has opened port "+CommsPort+".");
        }

        /// <summary>
        /// Checks for a connection and if it does not exist creates a new one. If the connection fails throws ConnectionSetupException.
        /// </summary>
        /// <param name="targetIPAddress"></param>
        public static TCPConnection EstablishTCPConnection(string targetIPAddress, int commsPort)
        {
            if (loggingEnabled) logger.Trace("Checking for connection to " + targetIPAddress + ":" + commsPort);

            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(targetIPAddress), commsPort);

            TCPConnection connection = null;
            try
            {
                if (commsShutdown)
                    throw new Exception("Attempting to access comms after shutdown has been initiated.");

                if (targetIPAddress == "")
                    throw new Exception("targetIPAddress provided was empty, i.e. '', clearly not a valid IP.");

                if (commsPort < 1)
                    throw new Exception("Invalid commsPort specified. Must be greater than 0.");

                if (targetIPAddress == LocalIP && commsPort == CommsPort && isListening)
                    throw new ConnectionSetupException("Attempting to connect local network comms instance to itself.");

                bool newConnectionEstablish = false;
                TcpClient targetClient = null;

                lock (globalDictAndDelegateLocker)
                {
                    InitialiseComms();

                    if (allConnectionsByEndPoint.ContainsKey(endPoint))
                        connection = allConnectionsByEndPoint[endPoint];
                    else
                    {
                        allConnectionsByEndPoint.Add(endPoint, connection);
                        newConnectionEstablish = true;
                    }
                }

                if (newConnectionEstablish)
                {
                    if (loggingEnabled) logger.Trace(" ... establishing a new connection");

                    //We now connect to our target
                    targetClient = new TcpClient();
                    targetClient.Connect(targetIPAddress, commsPort);

                    connection = new TCPConnection(false, targetClient);
                    connection.EstablishConnection();
                }
                else
                    if (loggingEnabled) logger.Trace(" ... using an existing connection");

                if (!connection.WaitForConnectionEstablish(connectionEstablishTimeoutMS))
                {
                    if (newConnectionEstablish)
                        throw new ConnectionSetupException("Timeout after connectionEstablishTimeoutMS waiting for connection to finish establish.");
                    else
                        throw new ConnectionSetupException("Timeout after connectionEstablishTimeoutMS waiting for another thread to finish establishing connection.");
                }

                return connection;
            }
            catch (Exception ex)
            {
                //If the connection failed we need to remove the endPoint, duh!
                allConnectionsByEndPoint.Remove(endPoint);

                //If there was an exception we need to close the connection
                if (connection != null)
                {
                    connection.CloseConnection(true, 17);
                    throw new ConnectionSetupException("Error during connection to destination (" + targetIPAddress + ":" + commsPort + ") from (" + connection.LocalConnectionIP + "). Destination may not be listening. " + ex.ToString());
                }

                throw new ConnectionSetupException("Error during connection to destination (" + targetIPAddress + ":" + commsPort + "). Destination may not be listening. " + ex.ToString());
            }
        }

        /// <summary>
        /// Returns the connection object assocaited with the provided connectionId
        /// </summary>
        /// <param name="targetIPAddress"></param>
        private static TCPConnection CheckForConnection(ShortGuid connectionId)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (allConnectionsById.ContainsKey(connectionId))
                    return allConnectionsById[connectionId];
                else
                    throw new InvalidConnectionIdException("Unable to locate a connection with the provided id - " + connectionId + ".");
            }
        }
        #endregion Private Setup, Connection and Shutdown
    }
}