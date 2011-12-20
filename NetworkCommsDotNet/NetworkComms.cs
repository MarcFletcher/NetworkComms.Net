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
using QuickLZCompressor;
using SharpZipLibCompressor;

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
        static string[] preferredIPPRefix = null;

        /// <summary>
        /// The preferred IP prefixs if network comms may try to help select the correct listening ip address.
        /// Correct format is string[] { "192.168", "213.111.10" }.
        /// If multiple prefixs are provided the lower index prefix if found takes priority
        /// </summary>
        public static string[] PreferredIPPrefix
        {
            get { return preferredIPPRefix; }
            set
            {
                if (isListening || commsInitialised)
                    throw new ConnectionSetupException("Unable to change PreferredIPPRefix once already initialised. Shutdown comms, change value and restart.");

                preferredIPPRefix = value;
            }
        }

        /// <summary>
        /// Get the localIP as detected by network comms. If an incorrect IP is being returned
        /// set the cirrect IP on application startup or specify PreferredIPPrefix to aid detection
        /// </summary>
        public static string LocalIP
        {
            get
            {
                if (localIP == null)
                {
                    //If we have not specified a preffered ip prefix we attempt to guess the ip.
                    if (preferredIPPRefix == null)
                    {
                        localIP = AttemptBestIPAddressGuess();
                        if (localIP != null) return localIP;
                    }
                    
                    //If the cool detection above did not work or we provided a preffered prefix we iterate through all addresses
                    //In order to do that the user must have provided some ip prefixs
                    if (preferredIPPRefix == null || preferredIPPRefix.Length == 0)
                        throw new ConnectionSetupException("Unable to determine LocalIP address. Either specifiy LocalIP explicity before using comms or provide suitable preferred prefixes.");

                    //Using host name, get the IP address list (we will have more than one for sure)
                    IPHostEntry ipEntry = Dns.GetHostEntry(HostName);
                    IPAddress[] addr = ipEntry.AddressList;

                    //We are going to look for a preffered ip
                    int bestIndexMatch = int.MaxValue;
                    for (int i = 0; i < addr.Length; i++)
                    {
                        string ipAddressStr = addr[i].ToString();

                        for (int j = 0; j < preferredIPPRefix.Length; j++)
                        {
                            if (ipAddressStr.Contains(preferredIPPRefix[j]))
                            {
                                //We only choose this match if it beats our previous index
                                if (j < bestIndexMatch)
                                {
                                    bestIndexMatch = j;
                                    localIP = ipAddressStr;
                                }

                                //If we have a match to index 0 then we are done
                                if (j == 0) return localIP;
                            }
                            //If we have not yet matched anything from the preferred prefix list we can try common private ranges as well
                            else if (ipAddressStr.Split('.')[0] == "192" && bestIndexMatch == int.MaxValue)
                                localIP = ipAddressStr;
                            else if (ipAddressStr.Split('.')[0] == "10" && bestIndexMatch == int.MaxValue)
                                localIP = ipAddressStr;
                        }
                    }

                    //If we did not match index 0 preffered ip prefix but have something we will go with that.
                    if (localIP != null) return localIP;
                    else
                    {
                        //If we still have nothing it may be we provided some prefixs which were not located
                        //Try to auto detect as last attempt
                        localIP = AttemptBestIPAddressGuess();
                        if (localIP != null) return localIP;
                    }

                    throw new ConnectionSetupException("Unable to determine LocalIP address using provided prefixes. Either specifiy LocalIP explicity before using comms or provide suitable preferred prefixes.");
                }
                else
                    return localIP;
            }
            set
            {
                if (isListening || commsInitialised)
                    throw new ConnectionSetupException("Unable to change LocalIP once comms has been initialised. Shutdown comms, change IP and restart.");

                //If we want to set the localIP we must first validate it
                String strHostName = Dns.GetHostName();

                // Then using host name, get the IP address list (we will have more than one for sure)
                IPHostEntry ipEntry = Dns.GetHostEntry(strHostName);
                IPAddress[] addr = ipEntry.AddressList;

                for (int i = 0; i < addr.Length; i++)
                {
                    if (addr[i].ToString() == value)
                    {
                        localIP = value;
                        return;
                    }
                }

                throw new ConnectionSetupException("Unable to confirm validity of provided IP.");
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
                            throw new ConnectionSetupException("Unable to change CommsPort once already listening. Shutdown comms, change port and restart.");

                        commsPort = value;
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
        #endregion

        #region Current Comms State
        internal static volatile bool commsInitialised = false;
        internal static volatile bool endListen = false;
        internal static volatile bool commsShutdown = false;
        internal static volatile bool isListening = false;

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
        internal static Dictionary<ShortGuid, Connection> allConnectionsById = new Dictionary<ShortGuid, Connection>();

        /// <summary>
        /// Secondary connection dictionary stored by ip end point. Allows for quick cross referencing.
        /// </summary>
        internal static Dictionary<IPEndPoint, Connection> allConnectionsByEndPoint = new Dictionary<IPEndPoint, Connection>();

        /// <summary>
        /// Old connection cache so that requests for connectionInfo can be returned even after a connection has been closed.
        /// </summary>
        internal static Dictionary<ShortGuid, ConnectionInfo> oldConnectionIdToConnectionInfo = new Dictionary<ShortGuid, ConnectionInfo>();
        #endregion

        #region Incoming Data and Connection Config
        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Sync is slightly better performing but spawns a worker thread for each unique connection.
        /// </summary>
        internal static bool connectionListenModeUseSync = false;

        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Default is false. No noticable performance difference between the two modes.
        /// </summary>
        public static bool ConnectionListenModeUseSync
        {
            get { return connectionListenModeUseSync; }
            set
            {
                if (isListening || commsInitialised)
                    throw new ConnectionSetupException("Unable to change connectionListenModeUseSync once already initialised. Shutdown comms, change mode and restart.");

                connectionListenModeUseSync = value;
            }
        }

        /// <summary>
        /// New incoming connection listeners
        /// </summary>
        static Thread newIncomingListenThread;
        static TcpListener tcpListener;

        /// <summary>
        /// Send and receive buffer sizes
        /// </summary>
        internal static int recieveBufferSizeBytes = 256000;
        internal static int sendBufferSizeBytes = 256000;

        /// <summary>
        /// Recieve data buffer size. Default is 256KB. CAUTION: Changing the default value can lead to severe performance degredation.
        /// </summary>
        public static int RecieveBufferSizeBytes
        {
            get { return recieveBufferSizeBytes; }
            set
            {
                if (isListening || commsInitialised)
                    throw new ConnectionSetupException("Unable to change recieveBufferSizeBytes once already initialised. Shutdown comms, change and restart.");

                recieveBufferSizeBytes = value;
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
                    throw new ConnectionSetupException("Unable to change sendBufferSizeBytes once already initialised. Shutdown comms, change and restart.");

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
        /// Set to true to enable checksum validation during all recieves. Default is false, relying on the basic TCP checksum alone.
        /// </summary>
        public static bool EnablePacketCheckSumValidation
        {
            get { return enablePacketCheckSumValidation; }
            set { enablePacketCheckSumValidation = value; }
        }

        /// <summary>
        /// The maximum packet size which will be maintained for possible future resend requests
        /// </summary>
        internal static int checkSumMismatchSentPacketCacheMaxByteLimit = 153750;

        /// <summary>
        /// Set to true to enable checksum validation during all recieves. Default 150KB.
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
        internal static int connectionAliveTestTimeoutMS = 5000;

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
#if logging
        private static readonly ILog logger = LogManager.GetLogger(typeof(NetworkComms));
        private static volatile bool loggerConfigured = false;
#endif

        /// <summary>
        /// Delegate method for writing out network comms log information
        /// </summary>
        /// <param name="textToWrite">The text to write to the log</param>
        public delegate void WriteLineToLogDelegate(string textToWrite);

        /// <summary>
        /// Internal pointer to a provided writeLineToLog delegate.
        /// </summary>
        static WriteLineToLogDelegate writeLineToLogMethod = null;

        /// <summary>
        /// A locker for writing to log to ensure thread safety
        /// </summary>
        static object logWriteLocker = new object();

        /// <summary>
        /// Set a network comms WriteLineToLogMethod delegate
        /// </summary>
        public static WriteLineToLogDelegate WriteLineToLogMethod
        {
            set { writeLineToLogMethod = value; }
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
        static ICompress defaultCompressor = SharpZipLibGzipCompressor.Instance;

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
#if logging
                ConfigureLogger();
#endif

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
            //Console.WriteLine("Entering CloseConnections.");

            //We need to create a copy because we need to avoid a collection modifed error and possible deadlocks from within closeConnection
            Dictionary<IPEndPoint, Connection> dictCopy;
            lock (globalDictAndDelegateLocker)
                //dictCopy = allConnectionsByEndPoint.ToDictionary(dict => dict.Key, dict => dict.Value);
                dictCopy = new Dictionary<IPEndPoint, Connection>(allConnectionsByEndPoint);

            //Any connections created after the above line will not be closed, a subsequent call to shutdown will get those.
            //Console.WriteLine("Closing {0} connections.", dictCopy.Count);
            foreach (Connection client in dictCopy.Values)
                client.CloseConnection(false, -3);
        }

        /// <summary>
        /// Locker for LogError() which ensures thread safe operation.
        /// </summary>
        static object errorLocker = new object();

        /// <summary>
        /// Logs provided exception to a file to assist troubleshooting.
        /// </summary>
        public static string LogError(Exception ex, string fileAppendStr, string optionalCommentStr = "")
        {
            string fileName;

            lock (errorLocker)
            {
                fileName = fileAppendStr + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + System.Diagnostics.Process.GetCurrentProcess().Id + "-" + Thread.CurrentContext.ContextID + "]");
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

            return fileName;
        }

        /// <summary>
        /// Checks all current connections to make sure they are active. If any problems occur the connection will be closed.
        /// </summary>
        /// <param name="returnImmediately">If true method will run as task and return immediately, otherwise return time depends on total number of connections.</param>
        public static void CheckConnectionAliveStatus(bool returnImmediately = false)
        {
            //Loop through all connections and test the alive state
            List<Connection> dictCopy;
            lock (globalDictAndDelegateLocker)
                dictCopy = new Dictionary<IPEndPoint, Connection>(allConnectionsByEndPoint).Values.ToList();

            List<Task> connectionCheckTasks = new List<Task>();

            for (int i = 0; i < dictCopy.Count; i++)
            {
                int innerIndex = i;
                connectionCheckTasks.Add(Task.Factory.StartNew(new Action(() => { dictCopy[innerIndex].CheckConnectionAliveState(connectionAliveTestTimeoutMS); })));
            }

            if (!returnImmediately)
                Task.WaitAll(connectionCheckTasks.ToArray());
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

                WriteToLog("Added global connection shutdown delegate.");
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
                WriteToLog("Removed global shutdown delegate.");

                if (globalConnectionShutdownDelegates == null)
                    WriteToLog("No handlers remain for shutdown connections.");
                else
                    WriteToLog("Handlers remain for shutdown connections.");
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

                WriteToLog("Added incoming packetHandler for '" + packetTypeStr + "' packetType.");

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
        public static void RemoveIncomingPacketHandler<T>(string packetTypeStr, PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Create the compare object
                    PacketTypeHandlerDelegateWrapper<T> toCompareDelegate = new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer);

                    //Remove any instances of this handler from the delegates
                    //The bonus here is if the delegate has not been added we continue quite happily
                    globalIncomingPacketHandlers[packetTypeStr].Remove(toCompareDelegate);

                    //WriteToLog("Removed a single packetHandler for '" + packetTypeStr + "' packetType.");

                    if (globalIncomingPacketHandlers[packetTypeStr] == null || globalIncomingPacketHandlers[packetTypeStr].Count == 0)
                    {
                        globalIncomingPacketHandlers.Remove(packetTypeStr);

                        //Remove any entries in the unwrappers dict as well as we are done with this packetTypeStr
                        if (globalIncomingPacketUnwrappers.ContainsKey(packetTypeStr))
                            globalIncomingPacketUnwrappers.Remove(packetTypeStr);

                        WriteToLog("Removed a packetHandler for '" + packetTypeStr + "' packetType. No handlers remain.");
                    }
                    else
                        WriteToLog("Removed a packetHandler for '" + packetTypeStr + "' packetType. Handlers remain.");
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
                    WriteToLog("Removed all incoming packetHandlers for '" + packetTypeStr + "' packetType.");
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
                WriteToLog("Removed all incoming packetHandlers for all packetTypes");
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
            //We take a copy of the handlers list incase it is modified outside of the lock
            List<IPacketTypeHandlerDelegateWrapper> handlersCopy = null;
            lock (globalDictAndDelegateLocker)
                if (globalIncomingPacketHandlers.ContainsKey(packetHeader.PacketType))
                    handlersCopy = new List<IPacketTypeHandlerDelegateWrapper>(globalIncomingPacketHandlers[packetHeader.PacketType]);

            if (handlersCopy == null && !IgnoreUnknownPacketTypes)
            {
                //We may get here if we have not added any custom delegates for reserved packet types
                if (!reservedPacketTypeNames.Contains(packetHeader.PacketType))
                    //Change this to just a log because generally a packet of the wrong type is nothing to really worry about
                    LogError(new UnexpectedPacketTypeException("The received packetTypeStr, " + packetHeader.PacketType + ", has no configured handler and network comms is not set to ignore unkown packet type. Use NetworkComms.IgnoreUnknownPacketTypes as an optional work around."), "CommsPacketError");

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

                //Pass the data onto the handler and move on.
#if logging
                        logger.Debug("... passing completed data packet to selected handler.");
#endif

                //We decide which serializer and compressor to use
                if (globalIncomingPacketUnwrappers.ContainsKey(packetHeader.PacketType))
                {
                    if (serializer==null) serializer = globalIncomingPacketUnwrappers[packetHeader.PacketType].Serializer;
                    if (compressor==null) compressor = globalIncomingPacketUnwrappers[packetHeader.PacketType].Compressor;
                }
                else
                {
                    if (serializer == null) serializer = DefaultSerializer;
                    if (compressor == null) compressor = DefaultCompressor;
                }

                //Deserialise the object only once
                object returnObject = handlersCopy[0].DeSerialize(incomingObjectBytes, serializer, compressor);

                //Pass the object to all necessary delgates
                //We need to use a copy because we may modify the original delegate list during processing
                foreach (IPacketTypeHandlerDelegateWrapper wrapper in handlersCopy)
                    wrapper.Process(packetHeader, sourceConnectionId, returnObject);
            }
        }
        #endregion

        #region Information
        /// <summary>
        /// Shutdown all connections and clean up communciation objects. If any comms activity has taken place this should be called on application close
        /// </summary>
        public static void ShutdownComms()
        {
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
                    lock (globalDictAndDelegateLocker)
                    {
                        //We need to make sure everything has shutdown before this method returns
                        if (isListening && newIncomingListenThread != null && (newIncomingListenThread.ThreadState == System.Threading.ThreadState.Running || newIncomingListenThread.ThreadState == System.Threading.ThreadState.WaitSleepJoin))
                        {
                            if (!newIncomingListenThread.Join(PacketConfirmationTimeoutMS))
                            {
                                //If we are still waiting for a close it may be stuck on AcceptTCPClient
                                try
                                {
                                    if (tcpListener != null) tcpListener.Stop();
                                }
                                catch (Exception)
                                {
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
                    }
                }
                finally
                {
                    //One last attempt at closing any still existing connections
                    CloseAllConnections();
                }

                WriteToLog("Network comms has shutdown");
            }
            catch (CommsException)
            {

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
                        throw new ConnectionSetupException("Unable to locate connection with provided connectionId.");
                }
            }
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(connectionId);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, DefaultSerializer, DefaultCompressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
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
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            Connection targetConnection = CheckForConnection(connectionId);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
        }
        #endregion

        #region SendRecieveObjectDefault

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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ref ShortGuid connectionId)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ref ShortGuid connectionId)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {
            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, connectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, null, null, null, null);
        }

        #endregion SendRecieveObjectDefault
        #region SendRecieveObjectSpecific
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming, ref ShortGuid connectionId)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            connectionId = targetConnection.ConnectionId;

            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming, ref ShortGuid connectionId)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            connectionId = targetConnection.ConnectionId;

            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, CommsPort);
            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int commsPort, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(destinationIPAddress, commsPort);
            return SendRecieveObject<returnObjectType>(sendingPacketTypeStr, targetConnection.ConnectionId, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, serializerOutgoing, compressorOutgoing, serializerIncoming, compressorIncoming);
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
        public static returnObjectType SendRecieveObject<returnObjectType>(string sendingPacketTypeStr, ShortGuid connectionId, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, ISerialize serializerOutgoing, ICompress compressorOutgoing, ISerialize serializerIncoming, ICompress compressorIncoming)
        {

#if logging
            ConfigureLogger();
#endif

            Connection targetConnection = CheckForConnection(connectionId);

            returnObjectType returnObject = default(returnObjectType);

            bool remotePeerDisconnectedDuringWait = false;
            AutoResetEvent returnWaitSignal = new AutoResetEvent(false);

            #region SendRecieveDelegate
            PacketHandlerCallBackDelegate<returnObjectType> SendRecieveDelegate = (packetHeader, sourceConnectionId, incomingObject) =>
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
            AppendIncomingPacketHandler(expectedReturnPacketTypeStr, SendRecieveDelegate, serializerIncoming, compressorIncoming, false);

            if (serializerOutgoing == null) serializerOutgoing = DefaultSerializer;
            if (compressorOutgoing == null) compressorOutgoing = DefaultCompressor;

            Packet sendPacket = new Packet(sendingPacketTypeStr, receiveConfirmationRequired, sendObject, serializerOutgoing, compressorOutgoing);
            //Console.WriteLine("... starting send {0}.", DateTime.Now.ToString("HH:mm:ss.fff"));
            targetConnection.SendPacket(sendPacket);
            //Console.WriteLine("... send complete {0}.", DateTime.Now.ToString("HH:mm:ss.fff"));

            //We wait for the return data here
            if (!returnWaitSignal.WaitOne(returnPacketTimeOutMilliSeconds))
            {
                RemoveIncomingPacketHandler(expectedReturnPacketTypeStr, SendRecieveDelegate);
                throw new ExpectedReturnTimeoutException("Timeout occurred waiting for response packet.");
            }

            RemoveIncomingPacketHandler(expectedReturnPacketTypeStr, SendRecieveDelegate);
            targetConnection.RemoveConnectionSpecificShutdownHandler(SendReceiveShutDownDelegate);

            if (remotePeerDisconnectedDuringWait)
                throw new ExpectedReturnTimeoutException("Remote end closed connection before data was successfully returned.");
            else
                return returnObject;
        }
        
        #endregion SendRecieveObjectSpecific

        #endregion Public Usage Methods

        #region Private Setup, Connection and Shutdown

#if logging
        private static void ConfigureLogger()
        {
            lock (globalLocker)
            {
                if (!loggerConfigured)
                {
                    loggerConfigured = true;
                    //If we are logging configure the logger
                    ILoggerRepository repository = LogManager.GetRepository(Assembly.GetCallingAssembly());
                    IBasicRepositoryConfigurator configurableRepository = repository as IBasicRepositoryConfigurator;

                    PatternLayout layout = new PatternLayout();
                    layout.ConversionPattern = "%timestamp% - %level% [%thread%] - %message%newline";
                    layout.ActivateOptions();

                    FileAppender appender = new FileAppender();
                    appender.Layout = layout;
                    appender.File = "commsLog.txt";
                    appender.AppendToFile = false;
                    appender.ActivateOptions();
                    configurableRepository.Configure(appender);
                }
            }
        }
#endif

        /// <summary>
        /// Initialise comms items on startup
        /// </summary>
        private static void InitialiseComms()
        {
            lock (globalDictAndDelegateLocker)
            {
                if (!commsInitialised)
                {
                    commsInitialised = true;
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
        internal static void SendObject(string packetTypeStr, Connection targetConnection, bool receiveConfirmationRequired, object sendObject, ISerialize serializer, ICompress compressor)
        {
#if logging
            ConfigureLogger();
#endif

#if logging
            logger.Debug("Start send of " + packetTypeStr + " packetTypeStr.");
#endif

            //Connection targetConnection = CheckForConnection(destinationIPAddress);
            Packet sendPacket = new Packet(packetTypeStr, receiveConfirmationRequired, sendObject, serializer, compressor);
            targetConnection.SendPacket(sendPacket);

#if logging
            logger.Debug("Completed send of " + packetTypeStr + " packetTypeStr.");
#endif
        }

        /// <summary>
        /// New incoming connection listen worker thread
        /// </summary>
        private static void IncomingConnectionListenThread()
        {
            WriteToLog("Network comms is now accepting connections.");
            //LogError(new Exception(""), "CommsStartup", "This is just a notice that comms has started up. No exception really occured.");

            try
            {
                do
                {
                    try
                    {
                        if (tcpListener.Pending() && !commsShutdown)
                        {
                            //Pick up the new conneciton
                            TcpClient newClient = tcpListener.AcceptTcpClient();

                            //Build the endPoint object based on available information at the current moment
                            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(newClient.Client.RemoteEndPoint.ToString().Split(':')[0]), int.Parse(newClient.Client.RemoteEndPoint.ToString().Split(':')[1]));
                            Connection newConnection = new Connection(true, endPoint);

                            //Once we have the connection we want to check if we already have an existing one
                            //If we already have a connection with this remote end point we close it
                            Connection existingConnection = null;
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
                            if (establishNewConnection) newConnection.EstablishConnection(newClient);
                        }
                        else
                            Thread.Sleep(500);
                    }
                    catch (ConfirmationTimeoutException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (CommunicationException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, "CommsSetupError");
                    }
                } while (!endListen);
            }
            catch (Exception ex)
            {
                LogError(ex, "CriticalCommsError");
            }
            finally
            {
                //If we get this far we have definately stopped accepting new connections
                endListen = false;
                isListening = false;
            }

            //LogError(new Exception(""), "CommsShutdown", "This is just a notice that comms has shutdown. No exception really occured.");
        }

        /// <summary>
        /// Opens a local port for incoming connections
        /// </summary>
        private static void OpenIncomingPorts()
        {
            System.Net.IPAddress localAddress = System.Net.IPAddress.Parse(LocalIP);

            try
            {
                tcpListener = new TcpListener(localAddress, CommsPort);
                tcpListener.Start();
            }
            catch (SocketException)
            {
                //The port we wanted is not available so we need to let .net choose one for us
                tcpListener = new TcpListener(localAddress, 0);
                tcpListener.Start();

                //Need to jump commsInitialised otherwise we won't be able to change the port
                CommsPort = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            }
        }

        /// <summary>
        /// Checks for a connection and if it does not exist creates a new one
        /// </summary>
        /// <param name="targetIPAddress"></param>
        private static Connection CheckForConnection(string targetIPAddress, int commsPort)
        {
            Connection connection = null;
            try
            {
                if (commsShutdown)
                    throw new Exception("Attempting to access comms after shutdown has been initiated.");

                if (commsPort < 1)
                    throw new Exception("Invalid commsPort specified. Must be greater than 0.");

                if (targetIPAddress == LocalIP && commsPort == CommsPort && isListening)
                    throw new ConnectionSetupException("Attempting to connect local network comms instance to itself.");

                bool newConnectionEstablish = false;
                lock (globalDictAndDelegateLocker)
                {
                    InitialiseComms();

                    IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(targetIPAddress), commsPort);
                    if (allConnectionsByEndPoint.ContainsKey(endPoint))
                        connection = allConnectionsByEndPoint[endPoint];
                    else
                    {
                        connection = new Connection(false, endPoint);
                        allConnectionsByEndPoint.Add(endPoint, connection);
                        newConnectionEstablish = true;
                    }
                }

                if (newConnectionEstablish)
                {
                    TcpClient targetClient = new TcpClient();
                    targetClient.Connect(targetIPAddress, commsPort);
                    connection.EstablishConnection(targetClient);
                }

                if (!connection.WaitForConnectionEstablish(connectionEstablishTimeoutMS))
                {
                    if (newConnectionEstablish)
                        throw new ConnectionSetupException("Timeout after 60 secs waiting for connection to finish establish.");
                    else
                        throw new ConnectionSetupException("Timeout after 60 secs waiting for another thread to finish establishing connection.");
                }

                return connection;
            }
            catch (Exception ex)
            {
                //If there was an exception we need to close the connection
                if (connection != null)
                    connection.CloseConnection(true, 17);

                throw new ConnectionSetupException("Error during connection to destination (" + targetIPAddress + ":" + commsPort + ") from (" + LocalIP + "). Destination may not be listening. " + ex.ToString());
            }
        }

        /// <summary>
        /// Returns the connection object assocaited with the provided connectionId
        /// </summary>
        /// <param name="targetIPAddress"></param>
        private static Connection CheckForConnection(ShortGuid connectionId)
        {
            lock (globalDictAndDelegateLocker)
            {
                if (allConnectionsById.ContainsKey(connectionId))
                    return allConnectionsById[connectionId];
                else
                    throw new InvalidConnectionIdException("Unable to locate a connection with the provided id - " + connectionId + ".");
            }
        }

        /// <summary>
        /// Internal wrapper for writing strings to log targets
        /// </summary>
        /// <param name="lineToWrite"></param>
        internal static void WriteToLog(string lineToWrite)
        {
            try
            {
                if (writeLineToLogMethod != null)
                    lock (logWriteLocker)
                        writeLineToLogMethod(lineToWrite);
            }
            catch (Exception)
            {
                writeLineToLogMethod = null;
            }

        }

        #endregion Private Setup, Connection and Shutdown
    }
}