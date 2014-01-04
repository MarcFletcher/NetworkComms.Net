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
using System.Net.Sockets;
using DPSBase;
using System.Collections;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.IO;

#if !NO_LOGGING
using NLog;
using NLog.Config;
#endif

//Assembly marked as CLSCompliant
[assembly: CLSCompliant(true)]

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Top level interface for NetworkCommsDotNet library. Anything which is not connection specific generally happens within the NetworkComms class. e.g. Keeping track of all connections, global defaults and settings, serialisers and data processors etc.
    /// </summary>
    public static class NetworkComms
    {
        /// <summary>
        /// Static constructor which sets comm default values
        /// </summary>
        static NetworkComms()
        {
            //Generally comms defaults are defined here
            NetworkIdentifier = ShortGuid.NewGuid();
            NetworkLoadUpdateWindowMS = 2000;

            InterfaceLinkSpeed = 95000000;

            DefaultListenPort = 10000;
            ListenOnAllAllowedInterfaces = true;

            CheckSumMismatchSentPacketCacheMaxByteLimit = 75000;
            MinimumSentPacketCacheTimeMinutes = 1;

            ConnectionEstablishTimeoutMS = 10000;
            PacketConfirmationTimeoutMS = 5000;
            ConnectionAliveTestTimeoutMS = 1000;

#if SILVERLIGHT || WINDOWS_PHONE
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
            CommsThreadPool = new CommsThreadPool(1, Environment.ProcessorCount*2, Environment.ProcessorCount * 20, new TimeSpan(0, 0, 10));

            //Initialise the core extensions
            DPSManager.AddDataSerializer<ProtobufSerializer>();

            DPSManager.AddDataSerializer<NullSerializer>();
            DPSManager.AddDataProcessor<SevenZipLZMACompressor.LZMACompressor>();

#if !FREETRIAL
            //Only the full version includes the encrypter
            DPSManager.AddDataProcessor<RijndaelPSKEncrypter>();
#endif

#if !WINDOWS_PHONE
            DPSManager.AddDataSerializer<BinaryFormaterSerializer>();
#endif

            InternalFixedSendReceiveOptions = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                new List<DataProcessor>(),
                new Dictionary<string, string>());

            DefaultSendReceiveOptions = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(),
                new List<DataProcessor>() { DPSManager.GetDataProcessor<SevenZipLZMACompressor.LZMACompressor>() },
                new Dictionary<string, string>());
        }

        #region Local Host Information
        /// <summary>
        /// Returns the current machine hostname
        /// </summary>
        public static string HostName
        {
            get 
            {
#if WINDOWS_PHONE
                return Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile().ToString();
#else
                return Dns.GetHostName(); 
#endif
            }
        }

        /// <summary>
        /// If set NetworkCommsDotNet will only operate on matching IP Addresses. Also see <see cref="AllowedAdaptorNames"/>.
        /// Correct format is string[] { "192.168", "213.111.10" }. If multiple prefixes are provided the earlier prefix, if found, takes priority.
        /// </summary>
        public static string[] AllowedIPPrefixes { get; set; }

        /// <summary>
        ///  If set NetworkCommsDotNet will only operate on specified adaptors. Correct format is string[] { "eth0", "en0", "wlan0" }.
        /// </summary>
        public static string[] AllowedAdaptorNames { get; set; }

        /// <summary>
        /// Returns all allowed local IP addresses. 
        /// If <see cref="AllowedAdaptorNames"/> has been set only returns IP addresses corresponding with specified adaptors.
        /// If <see cref="AllowedIPPrefixes"/> has been set only returns matching addresses ordered in descending preference. i.e. Most preffered at [0].
        /// </summary>
        /// <returns></returns>
        public static List<IPAddress> AllAllowedIPs()
        {

#if WINDOWS_PHONE
            //On windows phone we simply ignore ip addresses from the autoassigned range as well as those without a valid prefix
            List<IPAddress> allowedIPs = new List<IPAddress>();

            foreach (var hName in Windows.Networking.Connectivity.NetworkInformation.GetHostNames())
            {
                if (!hName.DisplayName.StartsWith("169.254"))
                {
                    if (AllowedIPPrefixes != null)
                    {
                        bool valid = false;

                        for (int i = 0; i < AllowedIPPrefixes.Length; i++)
                            valid |= hName.DisplayName.StartsWith(AllowedIPPrefixes[i]);
                                
                        if(valid)
                            allowedIPs.Add(IPAddress.Parse(hName.DisplayName));
                    }
                    else
                        allowedIPs.Add(IPAddress.Parse(hName.DisplayName));
                }
            }

            return allowedIPs;
#else

            //We want to ignore IP's that have been autoassigned
            //169.254.0.0
            IPAddress autoAssignSubnetv4 = new IPAddress(new byte[] { 169, 254, 0, 0 });
            //255.255.0.0
            IPAddress autoAssignSubnetMaskv4 = new IPAddress(new byte[] { 255, 255, 0, 0 });

            List<IPAddress> validIPAddresses = new List<IPAddress>();
            IPComparer comparer = new IPComparer();

#if ANDROID

            var iFaces = Java.Net.NetworkInterface.NetworkInterfaces;
            while (iFaces.HasMoreElements)
            {
                bool interfaceValid = false;
                var iFace = iFaces.NextElement() as Java.Net.NetworkInterface;
                var javaAddresses = iFace.InetAddresses;

                while (javaAddresses.HasMoreElements)
                {
                    var javaAddress = javaAddresses.NextElement() as Java.Net.InetAddress;
                    IPAddress address = default(IPAddress);
                    if (IPAddress.TryParse(javaAddress.HostAddress, out address))
                    {
                        if (address.AddressFamily == AddressFamily.InterNetwork || address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            if (AllowedAdaptorNames != null)
                            {
                                foreach (var id in AllowedAdaptorNames)
                                    if (id == iFace.Name)
                                    {
                                        interfaceValid = true;
                                        break;
                                    }
                            }
                            else
                                interfaceValid = true;

                            if (interfaceValid)
                                break;
                        }
                    }
                }

                if (!interfaceValid)
                    continue;

                javaAddresses = iFace.InetAddresses;

                while (javaAddresses.HasMoreElements)
                {
                    var javaAddress = javaAddresses.NextElement() as Java.Net.InetAddress;
                    IPAddress address = default(IPAddress);

                    if (IPAddress.TryParse(javaAddress.HostAddress, out address))
                    {
                        if (address.AddressFamily == AddressFamily.InterNetwork || address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            if (!IsAddressInSubnet(address, autoAssignSubnetv4, autoAssignSubnetMaskv4))
                            {
                                bool allowed = false;

                                if (AllowedAdaptorNames != null)
                                {
                                    foreach (var id in AllowedAdaptorNames)
                                    {
                                        if (id == iFace.Name)
                                        {
                                            allowed = true;
                                            break;
                                        }
                                    }
                                }
                                else
                                    allowed = true;

                                if (!allowed)
                                    continue;

                                allowed = false;

                                if (AllowedIPPrefixes != null)
                                {
                                    foreach (var ip in AllowedIPPrefixes)
                                    {
                                        if (comparer.Equals(address.ToString(), ip))
                                        {
                                            allowed = true;
                                            break;
                                        }
                                    }
                                }
                                else
                                    allowed = true;

                                if (!allowed)
                                    continue;

                                if (address != IPAddress.None)
                                    validIPAddresses.Add(address);
                            }
                        }
                    }
                }    
            }

#else


            foreach (var iFace in NetworkInterface.GetAllNetworkInterfaces())
            {
                bool interfaceValid = false;
                var unicastAddresses = iFace.GetIPProperties().UnicastAddresses;

                foreach (var address in unicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork || address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        if (AllowedAdaptorNames != null)
                        {
                            foreach (var id in AllowedAdaptorNames)
                                if (iFace.Id == id)
                                {
                                    interfaceValid = true;
                                    break;
                                }
                        }
                        else
                            interfaceValid = true;

                        if (interfaceValid)
                            break;
                    }
                }

                if (!interfaceValid)
                    continue;

                foreach (var address in unicastAddresses)
                {
                    var addressInformation = address.Address;
                    if (addressInformation.AddressFamily == AddressFamily.InterNetwork || addressInformation.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        if (!IsAddressInSubnet(addressInformation, autoAssignSubnetv4, autoAssignSubnetMaskv4))
                        {
                            bool allowed = false;

                            if (AllowedAdaptorNames != null)
                            {
                                foreach (var id in AllowedAdaptorNames)
                                {
                                    if(id == iFace.Id)
                                    {
                                        allowed = true;
                                        break;
                                    }
                                }
                            }
                            else
                                allowed = true;

                            if (!allowed)
                                continue;

                            allowed = false;

                            if (AllowedIPPrefixes != null)
                            {
                                foreach (var ip in AllowedIPPrefixes)
                                {
                                    if (comparer.Equals(addressInformation.ToString(), ip))
                                    {
                                        allowed = true;
                                        break;
                                    }
                                }
                            }
                            else
                                allowed = true;

                            if (!allowed)
                                continue;

                            if (addressInformation != IPAddress.None)
                                validIPAddresses.Add(addressInformation);
                        }
                    }
                }               
            }
#endif

            if (AllowedIPPrefixes != null)
            {
                validIPAddresses.Sort((a, b) =>
                {
                    for (int i = 0; i < AllowedIPPrefixes.Length; i++)
                    {
                        if (a.ToString().StartsWith(AllowedIPPrefixes[i]))
                        {
                            if (b.ToString().StartsWith(AllowedIPPrefixes[i]))
                                return 0;
                            else
                                return -1;
                        }
                        else if (b.ToString().StartsWith(AllowedIPPrefixes[i]))
                            return 1;
                    }

                    return 0;
                });
            }

            return validIPAddresses;
#endif
        }

        /// <summary>
        /// Custom comparer for IP addresses. Used by <see cref="AllAllowedIPs"/>
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
        /// Returns true if the provided address exists within the provided subnet.
        /// </summary>
        /// <param name="address">The address to check, i.e. 192.168.0.10</param>
        /// <param name="subnet">The subnet, i.e. 192.168.0.0</param>
        /// <param name="mask">The subnet mask, i.e. 255.255.255.0</param>
        /// <returns>True if address is in the provided subnet</returns>
        public static bool IsAddressInSubnet(IPAddress address, IPAddress subnet, IPAddress mask)
        {
            if (address == null) throw new ArgumentNullException("address", "Provided IPAddress cannot be null.");
            if (subnet == null) throw new ArgumentNullException("subnet", "Provided IPAddress cannot be null.");
            if (mask == null) throw new ArgumentNullException("mask", "Provided IPAddress cannot be null.");

            //Catch for IPv6
            if (subnet.AddressFamily == AddressFamily.InterNetworkV6 || 
                mask.AddressFamily == AddressFamily.InterNetworkV6)
                throw new NotImplementedException("This method does not yet support IPv6. Please contact NetworkComms.Net support if you would like this functionality.");
            //If we have provided IPV4 subnets and masks and we have an ipv6 address then return false
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return false;

            byte[] addrBytes = address.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();
            byte[] maskedAddressBytes = new byte[addrBytes.Length];

            //Catch for IPv6
            if (maskBytes.Length < maskedAddressBytes.Length)
                return false;

            for (int i = 0; i < maskedAddressBytes.Length; ++i)
                maskedAddressBytes[i] = (byte)(addrBytes[i] & maskBytes[i]);

            IPAddress maskedAddress = new IPAddress(maskedAddressBytes);
            bool equal = subnet.Equals(maskedAddress);

            return equal;
        }

        /// <summary>
        /// The default port NetworkCommsDotNet will operate on
        /// </summary>
        public static int DefaultListenPort { get; set; }

        /// <summary>
        /// The local identifier for this instance of NetworkCommsDotNet. This is an application unique identifier.
        /// </summary>
        public static ShortGuid NetworkIdentifier { get; private set; }

        /// <summary>
        /// The current runtime environment. Detected automatically on startup. Performance may be adversly affected if this is changed.
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
        /// A running total of the number of packets sent on all connections. Used to initialise packet sequence counters to ensure duplicates can not occur.
        /// </summary>
        internal static long totalPacketSendCount;

        /// <summary>
        /// The number of millisconds over which to take an instance load (CurrentNetworkLoad) to be used in averaged values (AverageNetworkLoad). 
        /// Default is 2000ms. Shorter values can be used but less than 200ms may cause significant errors in the value of returned value, especially in mono environments.
        /// </summary>
        public static int NetworkLoadUpdateWindowMS { get; set; }

        private static double currentNetworkLoadIncoming;
        private static double currentNetworkLoadOutgoing;
#if !WINDOWS_PHONE && !ANDROID
        private static Thread NetworkLoadThread = null;
        private static CommsMath currentNetworkLoadValuesIncoming;
        private static CommsMath currentNetworkLoadValuesOutgoing;
        private static ManualResetEvent NetworkLoadThreadWait;
#endif

        /// <summary>
        /// The interface link speed in bits/sec used for network load calculations. Default is 100Mb/sec
        /// </summary>
        public static long InterfaceLinkSpeed { get; set; }

        /// <summary>
        /// Returns the current instance network usage, as a value between 0 and 1. Returns the largest value for any available network adaptor. Triggers load analysis upon first call.
        /// </summary>
        public static double CurrentNetworkLoadIncoming
        {
            get
            {
#if !WINDOWS_PHONE && !ANDROID
                //We start the load thread when we first access the network load
                //this helps cut down on uncessary threads if unrequired
                if (!commsShutdown && NetworkLoadThread == null)
                {
                    lock (globalDictAndDelegateLocker)
                    {
                        if (!commsShutdown && NetworkLoadThread == null)
                        {
                            currentNetworkLoadValuesIncoming = new CommsMath();
                            currentNetworkLoadValuesOutgoing = new CommsMath();

                            NetworkLoadThread = new Thread(NetworkLoadWorker);
                            NetworkLoadThread.Name = "NetworkLoadThread";
                            NetworkLoadThread.Start();
                        }
                    }
                }
#endif
                return currentNetworkLoadIncoming;
            }
            private set { currentNetworkLoadIncoming = value; }
        }

        /// <summary>
        /// Returns the current instance network usage, as a value between 0 and 1. Returns the largest value for any available network adaptor. Triggers load analysis upon first call.
        /// </summary>
        public static double CurrentNetworkLoadOutgoing
        {
            get
            {
#if !WINDOWS_PHONE && !ANDROID
                //We start the load thread when we first access the network load
                //this helps cut down on uncessary threads if unrequired
                if (!commsShutdown && NetworkLoadThread == null)
                {
                    lock (globalDictAndDelegateLocker)
                    {
                        if (!commsShutdown && NetworkLoadThread == null)
                        {
                            currentNetworkLoadValuesIncoming = new CommsMath();
                            currentNetworkLoadValuesOutgoing = new CommsMath();

                            NetworkLoadThread = new Thread(NetworkLoadWorker);
                            NetworkLoadThread.Name = "NetworkLoadThread";
                            NetworkLoadThread.Start();
                        }
                    }
                }
#endif
                return currentNetworkLoadOutgoing;
            }
            private set { currentNetworkLoadOutgoing = value; }
        }

        /// <summary>
        /// Returns the averaged value of CurrentNetworkLoadIncoming, as a value between 0 and 1, for a time window of upto 254 seconds. Triggers load analysis upon first call.
        /// </summary>
        /// <param name="secondsToAverage">Number of seconds over which historial data should be used to arrive at an average</param>
        /// <returns>Average network load as a double between 0 and 1</returns>
        public static double AverageNetworkLoadIncoming(byte secondsToAverage)
        {
#if !WINDOWS_PHONE && !ANDROID

            if (!commsShutdown && NetworkLoadThread == null)
            {
                lock (globalDictAndDelegateLocker)
                {
                    if (!commsShutdown && NetworkLoadThread == null)
                    {
                        currentNetworkLoadValuesIncoming = new CommsMath();
                        currentNetworkLoadValuesOutgoing = new CommsMath();

                        NetworkLoadThread = new Thread(NetworkLoadWorker);
                        NetworkLoadThread.Name = "NetworkLoadThread";
                        NetworkLoadThread.Start();
                    }
                }
            }

            return currentNetworkLoadValuesIncoming.CalculateMean((int)((secondsToAverage * 1000.0) / NetworkLoadUpdateWindowMS));
#else
            return 0;
#endif
        }

        /// <summary>
        /// Returns the averaged value of CurrentNetworkLoadIncoming, as a value between 0 and 1, for a time window of upto 254 seconds. Triggers load analysis upon first call.
        /// </summary>
        /// <param name="secondsToAverage">Number of seconds over which historial data should be used to arrive at an average</param>
        /// <returns>Average network load as a double between 0 and 1</returns>
        public static double AverageNetworkLoadOutgoing(byte secondsToAverage)
        {
#if !WINDOWS_PHONE && !ANDROID
            if (!commsShutdown && NetworkLoadThread == null)
            {
                lock (globalDictAndDelegateLocker)
                {
                    if (!commsShutdown && NetworkLoadThread == null)
                    {
                        currentNetworkLoadValuesIncoming = new CommsMath();
                        currentNetworkLoadValuesOutgoing = new CommsMath();

                        NetworkLoadThread = new Thread(NetworkLoadWorker);
                        NetworkLoadThread.Name = "NetworkLoadThread";
                        NetworkLoadThread.Start();
                    }
                }
            }

            return currentNetworkLoadValuesOutgoing.CalculateMean((int)((secondsToAverage * 1000.0) / NetworkLoadUpdateWindowMS));
#else
            return 0;
#endif
        }

        /// <summary>
        /// Determines the most appropriate local end point to contact the provided remote end point. 
        /// Testing shows this method takes on average 1.6ms to return.
        /// </summary>
        /// <param name="remoteIPEndPoint">The remote end point</param>
        /// <returns>The selected local end point</returns>
        public static IPEndPoint BestLocalEndPoint(IPEndPoint remoteIPEndPoint)
        {
            if (remoteIPEndPoint == null) throw new ArgumentNullException("remoteIPEndPoint", "Provided IPEndPoint cannot be null.");

#if WINDOWS_PHONE
            var t = Windows.Networking.Sockets.DatagramSocket.GetEndpointPairsAsync(new Windows.Networking.HostName(remoteIPEndPoint.Address.ToString()), remoteIPEndPoint.Port.ToString()).AsTask();
            if (t.Wait(20) && t.Result.Count > 0)
            {
                var enumerator = t.Result.GetEnumerator();
                enumerator.MoveNext();

                var endpointPair = enumerator.Current;                
                return new IPEndPoint(IPAddress.Parse(endpointPair.LocalHostName.DisplayName.ToString()), int.Parse(endpointPair.LocalServiceName));
            }
            else
                throw new ConnectionSetupException("Unable to determine correct local end point.");
#else
            //We use UDP as its connectionless hence faster
            IPEndPoint result;
            using (Socket testSocket = new Socket(remoteIPEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                testSocket.Connect(remoteIPEndPoint);
                result = (IPEndPoint)testSocket.LocalEndPoint;
            }

            return result;
#endif
        }

#if !WINDOWS_PHONE && !ANDROID
        /// <summary>
        /// Takes a network load snapshot (CurrentNetworkLoad) every NetworkLoadUpdateWindowMS
        /// </summary>
        private static void NetworkLoadWorker()
        {
            NetworkLoadThreadWait = new ManualResetEvent(false);
            
            //Get all interfaces
            NetworkInterface[] interfacesToUse = NetworkInterface.GetAllNetworkInterfaces();
            
            long[] startSent, startReceived, endSent, endReceived;

            while (!commsShutdown)
            {
                try
                {
                    //we need to look at the load across all adaptors, by default we will probably choose the adaptor with the highest usage
                    DateTime startTime = DateTime.Now;

                    IPv4InterfaceStatistics[] stats = new IPv4InterfaceStatistics[interfacesToUse.Length];
                    startSent = new long[interfacesToUse.Length];
                    startReceived = new long[interfacesToUse.Length];

                    for (int i = 0; i < interfacesToUse.Length; ++i)
                    {
                        stats[i] = interfacesToUse[i].GetIPv4Statistics();
                        startSent[i] = stats[i].BytesSent;
                        startReceived[i] = stats[i].BytesReceived;
                    }
                    
                    if (commsShutdown) return;

                    //Thread.Sleep(NetworkLoadUpdateWindowMS);
                    NetworkLoadThreadWait.WaitOne(NetworkLoadUpdateWindowMS);

                    if (commsShutdown) return;

                    stats = new IPv4InterfaceStatistics[interfacesToUse.Length];
                    endSent = new long[interfacesToUse.Length];
                    endReceived = new long[interfacesToUse.Length];

                    for (int i = 0; i < interfacesToUse.Length; ++i)
                    {
                        stats[i] = interfacesToUse[i].GetIPv4Statistics();
                        endSent[i] = stats[i].BytesSent;
                        endReceived[i] = stats[i].BytesReceived;
                    }
                    
                    DateTime endTime = DateTime.Now;

                    List<double> outUsage = new List<double>();
                    List<double> inUsage = new List<double>();
                    for(int i=0; i<startSent.Length; i++)
                    {
                        outUsage.Add((double)(endSent[i] - startSent[i]) / ((double)(InterfaceLinkSpeed * (endTime - startTime).TotalMilliseconds) / 8000));
                        inUsage.Add((double)(endReceived[i] - startReceived[i]) / ((double)(InterfaceLinkSpeed * (endTime - startTime).TotalMilliseconds) / 8000));
                    }

                    //double loadValue = Math.Max(outUsage.Max(), inUsage.Max());
                    double inMax = double.MinValue, outMax = double.MinValue;
                    for (int i = 0; i < startSent.Length; ++i)
                    {
                        if (inUsage[i] > inMax) inMax = inUsage[i];
                        if (outUsage[i] > outMax) outMax = outUsage[i];
                    }
                                        
                    //If either of the usage levels have gone above 2 it suggests we are most likely on a faster connection that we think
                    //As such we will bump the interfacelinkspeed upto 1Gbps so that future load calcualtions more acurately reflect the 
                    //actual load.
                    if (inMax > 2 || outMax > 2) InterfaceLinkSpeed = 950000000;

                    //Limit to one
                    CurrentNetworkLoadIncoming = (inMax > 1 ? 1 : inMax);
                    CurrentNetworkLoadOutgoing = (outMax > 1 ? 1 : outMax);

                    currentNetworkLoadValuesIncoming.AddValue(CurrentNetworkLoadIncoming);
                    currentNetworkLoadValuesOutgoing.AddValue(CurrentNetworkLoadOutgoing);

                    //We can only have upto 255 seconds worth of data in the average list
                    int maxListSize = (int)(255000.0 / NetworkLoadUpdateWindowMS);
                    currentNetworkLoadValuesIncoming.TrimList(maxListSize);
                    currentNetworkLoadValuesOutgoing.TrimList(maxListSize);
                }
                catch (Exception ex)
                {
                    LogError(ex, "NetworkLoadWorker");
                    
                    //It may be the interfaces available to the OS have changed so we will reset them here
                    interfacesToUse = NetworkInterface.GetAllNetworkInterfaces();
                    //If an error has happened we dont want to thrash the problem, we wait for 5 seconds and hope whatever was wrong goes away
                    Thread.Sleep(5000);
                }
            }
        }
#endif
        #endregion

        #region Established Connections
        /// <summary>
        /// Locker for connection dictionaries
        /// </summary>
        internal static object globalDictAndDelegateLocker = new object();

        /// <summary>
        /// Primary connection dictionary stored by network indentifier
        /// </summary>
        internal static Dictionary<ShortGuid, Dictionary<ConnectionType, List<Connection>>> allConnectionsByIdentifier = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<Connection>>>();

        /// <summary>
        /// Secondary connection dictionary stored by ip end point. First key is remote IPEndPoint, second key is local IPEndPoint, third key is connection type
        /// </summary>
        internal static Dictionary<IPEndPoint, Dictionary<IPEndPoint, Dictionary<ConnectionType, Connection>>> allConnectionsByIPEndPoint = new Dictionary<IPEndPoint, Dictionary<IPEndPoint, Dictionary<ConnectionType, Connection>>>();

        /// <summary>
        /// Old connection cache so that requests for connectionInfo can be returned even after a connection has been closed.
        /// </summary>
        internal static Dictionary<ShortGuid, Dictionary<ConnectionType, List<ConnectionInfo>>> oldNetworkIdentifierToConnectionInfo = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<ConnectionInfo>>>();
        #endregion

        #region Incoming Data and Connection Config
        /// <summary>
        /// Used for switching between async and sync connectionListen modes. Default is false. No noticable performance difference between the two modes.
        /// </summary>
        public static bool ConnectionListenModeUseSync { get; set; }

        /// <summary>
        /// Used for switching between listening on a single interface or multiple interfaces. Default is true. See <see cref="AllowedIPPrefixes"/> and <see cref="AllowedAdaptorNames"/>
        /// </summary>
        public static bool ListenOnAllAllowedInterfaces { get; set; }

        /// <summary>
        /// Receive data buffer size. Default is 80KB. CAUTION: Changing the default value can lead to performance degredation.
        /// </summary>
        public static int ReceiveBufferSizeBytes { get; set; }

        /// <summary>
        /// Send data buffer size. Default is 80KB. CAUTION: Changing the default value can lead to performance degredation.
        /// </summary>
        public static int SendBufferSizeBytes { get; set; }

        /// <summary>
        /// The threadpool used by networkComms.Net to execute incoming packet handlers.
        /// </summary>
        public static CommsThreadPool CommsThreadPool { get; set; }

        /// <summary>
        /// Once we have received all incoming data we handle it further. This is performed at the global level to help support different priorities.
        /// </summary>
        /// <param name="itemAsObj">Possible PriorityQueueItem. If null is provided an item will be removed from the global item queue</param>
        internal static void CompleteIncomingItemTask(object itemAsObj)
        {
            if (itemAsObj == null)
                throw new ArgumentNullException("itemAsObj", "Provided parameter itemAsObj cannot be null.");

            PriorityQueueItem item = null;
            try
            {
                //If the packetBytes are null we need to ask the incoming packet queue for what we should be running
                item = itemAsObj as PriorityQueueItem;

                if (item == null)
                    throw new InvalidCastException("Cast from object to PriorityQueueItem resulted in null reference, unable to continue.");

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Handling a " + item.PacketHeader.PacketType + " packet from " + item.Connection.ConnectionInfo + " with a priority of " + item.Priority.ToString() + ".");

#if !WINDOWS_PHONE
                if (Thread.CurrentThread.Priority != (ThreadPriority)item.Priority) Thread.CurrentThread.Priority = (ThreadPriority)item.Priority;
#endif

                //Check for a shutdown connection
                if (item.Connection.ConnectionInfo.ConnectionState == ConnectionState.Shutdown) return;

                //We only look at the check sum if we want to and if it has been set by the remote end
                if (NetworkComms.EnablePacketCheckSumValidation && item.PacketHeader.ContainsOption(PacketHeaderStringItems.CheckSumHash))
                {
                    var packetHeaderHash = item.PacketHeader.GetOption(PacketHeaderStringItems.CheckSumHash);

                    //Validate the checkSumhash of the data
                    string packetDataSectionMD5 = NetworkComms.MD5Bytes(item.DataStream);
                    if (packetHeaderHash != packetDataSectionMD5)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn(" ... corrupted packet detected, expected " + packetHeaderHash + " but received " + packetDataSectionMD5 + ".");

                        //We have corruption on a resend request, something is very wrong so we throw an exception.
                        if (item.PacketHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend)) throw new CheckSumException("Corrupted md5CheckFailResend packet received.");

                        if (item.PacketHeader.PayloadPacketSize < NetworkComms.CheckSumMismatchSentPacketCacheMaxByteLimit)
                        {
                            //Instead of throwing an exception we can request the packet to be resent
                            Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend), packetHeaderHash, NetworkComms.InternalFixedSendReceiveOptions);
                            item.Connection.SendPacket(returnPacket);
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
                        item.Connection.SendPacket(returnPacket);
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
                    item.Connection.SendPacket(returnPacket);
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
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("A communcation exception occured in CompleteIncomingPacketWorker(), connection with " + item.Connection.ConnectionInfo + " be closed.");
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
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("An unhandled exception occured in CompleteIncomingPacketWorker(), connection with " + item.Connection.ConnectionInfo + " be closed. See log file for more information.");
                    item.Connection.CloseConnection(true, 3);
                }
            }
            finally
            {
                //We need to dispose the data stream correctly
                if (item!=null) item.DataStream.Close();

#if !WINDOWS_PHONE
                //Ensure the thread returns to the pool with a normal priority
                if (Thread.CurrentThread.Priority != ThreadPriority.Normal) Thread.CurrentThread.Priority = ThreadPriority.Normal;
#endif
            }
        }
        #endregion

#if !WINDOWS_PHONE
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
        /// When enabled uses an MD5 checksum to validate all received packets. Default is false, relying on any possible connection checksum alone. 
        /// Also when enabled any packets sent less than CheckSumMismatchSentPacketCacheMaxByteLimit will be cached for a duration to ensure successful delivery.
        /// Default false.
        /// </summary>
        public static bool EnablePacketCheckSumValidation { get; set; }

        /// <summary>
        /// When checksum validation is enabled sets the limit below which sent packets are cached to ensure successful delivery. Default 75KB.
        /// </summary>
        public static int CheckSumMismatchSentPacketCacheMaxByteLimit { get; set; }

        /// <summary>
        /// When a sent packet has been cached for a possible resend this is the minimum length of time it will be retained. Default is 1.0 minutes.
        /// </summary>
        public static double MinimumSentPacketCacheTimeMinutes { get; set; }

        /// <summary>
        /// Records the last sent packet cache cleanup time. Prevents the sent packet cache from being checked too frequently.
        /// </summary>
        internal static DateTime LastSentPacketCacheCleanup { get; set; }
        #endregion

        #region PacketType Config and Global Handlers
        /// <summary>
        /// An internal reference copy of all reservedPacketTypeNames.
        /// </summary>
        internal static string[] reservedPacketTypeNames = Enum.GetNames(typeof(ReservedPacketType));

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
        /// <typeparam name="T">The type of object which is expected for this handler</typeparam>
        /// <param name="packetHeader">The <see cref="PacketHeader"/> of the incoming packet</param>
        /// <param name="connection">The connection with which this packet was received</param>
        /// <param name="incomingObject">The incoming object of specified type T</param>
        public delegate void PacketHandlerCallBackDelegate<T>(PacketHeader packetHeader, Connection connection, T incomingObject);

        /// <summary>
        /// If true any unknown incoming packet types are ignored. Default is false and will result in an error file being created if an unknown packet type is received.
        /// </summary>
        public static bool IgnoreUnknownPacketTypes { get; set; }

        /// <summary>
        /// Add an incoming packet handler using default SendReceiveOptions. Multiple handlers for the same packet type will be executed in the order they are added.
        /// </summary>
        /// <typeparam name="T">The type of incoming object</typeparam>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when a packet of packetTypeStr is received</param>
        public static void AppendGlobalIncomingPacketHandler<T>(string packetTypeStr, PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer)
        {
            //Checks for unmanaged packet types
            if (packetTypeStr == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
            {
                if (DefaultSendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new InvalidOperationException("Attempted to add packet handler for an unmanaged packet type when the global send receive options serializer was not NullSerializer.");

                if (DefaultSendReceiveOptions.DataProcessors.Count > 0)
                    throw new InvalidOperationException("Attempted to add packet handler for an unmanaged packet type when the global send receive options contains data processors. Data processors may not be used inline with unmanaged packet types.");
            }

            AppendGlobalIncomingPacketHandler<T>(packetTypeStr, packetHandlerDelgatePointer, DefaultSendReceiveOptions);
        }

        /// <summary>
        /// Add an incoming packet handler using the provided SendReceiveOptions. Multiple handlers for the same packet type will be executed in the order they are added.
        /// </summary>
        /// <typeparam name="T">The type of incoming object</typeparam>
        /// <param name="packetTypeStr">The packet type for which this handler will be executed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be executed when a packet of packetTypeStr is received</param>
        /// <param name="sendReceiveOptions">The SendReceiveOptions to be used for the provided packet type</param>
        public static void AppendGlobalIncomingPacketHandler<T>(string packetTypeStr, PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer, SendReceiveOptions sendReceiveOptions)
        {
            if (packetTypeStr == null) throw new ArgumentNullException("packetTypeStr", "Provided packetType string cannot be null.");
            if (packetHandlerDelgatePointer == null) throw new ArgumentNullException("packetHandlerDelgatePointer", "Provided PacketHandlerCallBackDelegate<T> cannot be null.");
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
                        throw new PacketHandlerException("The proivded SendReceiveOptions are not compatible with existing SendReceiveOptions already specified for this packetTypeStr.");
                }
                else
                    globalIncomingPacketUnwrappers.Add(packetTypeStr, new PacketTypeUnwrapper(packetTypeStr, sendReceiveOptions));

                //Ad the handler to the list
                if (globalIncomingPacketHandlers.ContainsKey(packetTypeStr))
                {
                    //Make sure we avoid duplicates
                    PacketTypeHandlerDelegateWrapper<T> toCompareDelegate = new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer);

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

                    globalIncomingPacketHandlers[packetTypeStr].Add(new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer));
                }
                else
                    globalIncomingPacketHandlers.Add(packetTypeStr, new List<IPacketTypeHandlerDelegateWrapper>() { new PacketTypeHandlerDelegateWrapper<T>(packetHandlerDelgatePointer) });

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
        public static void RemoveGlobalIncomingUnmanagedPacketHandler(Delegate packetHandlerDelgatePointer)
        {
            RemoveGlobalIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged), packetHandlerDelgatePointer);
        }

        /// <summary>
        /// Removes the provided delegate for the specified packet type. If the provided delegate does not exist for this packet type just returns.
        /// </summary>
        /// <param name="packetTypeStr">The packet type for which the delegate will be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to be removed</param>
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

                //We take a copy of the handlers list incase it is modified outside of the lock
                List<IPacketTypeHandlerDelegateWrapper> handlersCopy = null;
                lock (globalDictAndDelegateLocker)
                    if (globalIncomingPacketHandlers.ContainsKey(packetHeader.PacketType))
                        handlersCopy = new List<IPacketTypeHandlerDelegateWrapper>(globalIncomingPacketHandlers[packetHeader.PacketType]);

                if (handlersCopy == null && !IgnoreUnknownPacketTypes && !ignoreUnknownPacketTypeOverride)
                {
                    //We may get here if we have not added any custom delegates for reserved packet types
                    bool isReservedType = false;

                    for (int i = 0; i < reservedPacketTypeNames.Length; i++)
                    {
                        if (reservedPacketTypeNames[i] == packetHeader.PacketType)
                        {
                            isReservedType = true;
                            break;
                        }
                    }

                    if (!isReservedType)
                    {
                        //Change this to just a log because generally a packet of the wrong type is nothing to really worry about
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn("The received packet type '" + packetHeader.PacketType + "' has no configured handler and network comms is not set to ignore unknown packet types. Set NetworkComms.IgnoreUnknownPacketTypes=true to prevent this error.");
                        LogError(new UnexpectedPacketTypeException("The received packet type '" + packetHeader.PacketType + "' has no configured handler and network comms is not set to ignore unknown packet types. Set NetworkComms.IgnoreUnknownPacketTypes=true to prevent this error."), "PacketHandlerErrorGlobal_" + packetHeader.PacketType);
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

                    //Pass the object to all necessary delgates
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
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal("An exception occured in TriggerPacketHandler() for a packet type '" + packetHeader.PacketType + "'. See error log file for more information.");
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
        public static bool GlobalIncomingPacketHandlerExists(string packetTypeStr, Delegate packetHandlerDelgatePointer)
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
        public static bool GlobalIncomingUnmanagedPacketHandlerExists(string packetTypeStr, Delegate packetHandlerDelgatePointer)
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
        /// Add a new connection establish delegate which will be called for every connection once it has been succesfully established.
        /// </summary>
        /// <param name="connectionEstablishDelegate">The delegate to call after all connection establishments.</param>
        /// <param name="runSynchronously">If true this ConnectionEstablishShutdownDelegate will be called synchronously during the connection establish. The connection will not be considered established until the ConnectionEstablishShutdownDelegate has completed.</param>
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
        /// Shutdown all connections, comms threads and execute OnCommsShutdown event. Any packet handlers are left unchanged. If any comms activity has taken place this should be called on application close.
        /// </summary>
        /// <param name="threadShutdownTimeoutMS">The time to wait for worker threads to close before attempting a thread abort.</param>
        public static void Shutdown(int threadShutdownTimeoutMS = 1000)
        {
            if (LoggingEnabled) logger.Trace("NetworkCommsDotNet shutdown initiated.");
            commsShutdown = true;

            CommsThreadPool.BeginShutdown();
            Connection.ShutdownBase(threadShutdownTimeoutMS);
            TCPConnection.Shutdown(threadShutdownTimeoutMS);
            UDPConnection.Shutdown();

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

#if !WINDOWS_PHONE && !ANDROID
            try
            {
                if (NetworkLoadThread != null)
                {
                    NetworkLoadThreadWait.Set();
                    if (!NetworkLoadThread.Join(threadShutdownTimeoutMS))
                    {
                        NetworkLoadThread.Abort();
                        throw new CommsSetupShutdownException("Timeout waiting for NetworkLoadThread thread to shutdown after " + threadShutdownTimeoutMS.ToString() + " ms. ");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }
#endif

            try
            {
                if (OnCommsShutdown != null) OnCommsShutdown(null, new EventArgs());
            }
            catch (Exception ex)
            {
                LogError(ex, "CommsShutdownError");
            }

            CommsThreadPool.EndShutdown(threadShutdownTimeoutMS);

            commsShutdown = false;
            if (LoggingEnabled) logger.Info("NetworkCommsDotNet has shutdown");

#if !WINDOWS_PHONE && !NO_LOGGING
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
        /// Returns true if comms logging has been enabled.
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
        /// Logs the provided exception to a file to assist troubleshooting.
        /// </summary>
        /// <param name="ex">The exception to be logged</param>
        /// <param name="fileName">The filename to use. A timestamp and extension .txt will be appended automatically</param>
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
#else
                using (Process currentProcess = System.Diagnostics.Process.GetCurrentProcess())
                    entireFileName = fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + currentProcess.Id.ToString() + "-" + Thread.CurrentContext.ContextID.ToString() + "]");
#endif

                if (LoggingEnabled) logger.Fatal(entireFileName, ex);

                try
                {
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
        /// The following are used for internal comms objects, packet headers, connection establishment etc. 
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
        /// Send the provided object to the specified destination using TCP. Uses default sendReceiveOptions. For more control over options see connection specific methods.
        /// </summary>
        /// <param name="packetTypeStr">Packet type to use for send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="destinationPort">The destination listen port</param>
        /// <param name="sendObject">The obect to send</param>
        public static void SendObject(string packetTypeStr, string destinationIPAddress, int destinationPort, object sendObject)
        {
            TCPConnection conn = TCPConnection.GetConnection(new ConnectionInfo(destinationIPAddress, destinationPort));
            conn.SendObject(packetTypeStr, sendObject);
        }

        /// <summary>
        /// Send the provided object to the specified destination and wait for a return object using TCP. Uses default sendReceiveOptions. For more control over options see connection specific methods.
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="destinationIPAddress">The destination ip address</param>
        /// <param name="destinationPort">The destination listen port</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <returns>The expected return object</returns>
        public static returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string destinationIPAddress, int destinationPort, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject)
        {
            TCPConnection conn = TCPConnection.GetConnection(new ConnectionInfo(destinationIPAddress, destinationPort));
            return conn.SendReceiveObject<returnObjectType>(sendingPacketTypeStr, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject);
        }

        /// <summary>
        /// Return the MD5 hash of the provided memory stream as a string. Stream position will be equal to the length of stream on return, this ensures the MD5 is consistent.
        /// </summary>
        /// <param name="streamToMD5">The bytes which will be checksummed</param>
        /// <returns>The MD5 checksum as a string</returns>
        public static string MD5Bytes(Stream streamToMD5)
        {
            if (streamToMD5 == null) throw new ArgumentNullException("streamToMD5", "Provided Stream cannot be null.");

            string resultStr;

            using (System.Security.Cryptography.HashAlgorithm md5 =
#if WINDOWS_PHONE
            new DPSBase.MD5Managed())
#else
            System.Security.Cryptography.MD5.Create())
#endif
            {
                //If we don't ensure the position is consistent the MD5 changes
                streamToMD5.Seek(0, SeekOrigin.Begin);
                resultStr = BitConverter.ToString(md5.ComputeHash(streamToMD5)).Replace("-", "");
            }

            return resultStr;
        }

        /// <summary>
        /// Return the MD5 hash of the provided memory stream as a string. Stream position will be equal to the length of stream on return, this ensures the MD5 is consistent.
        /// </summary>
        /// <param name="streamToMD5">The bytes which will be checksummed</param>
        /// <param name="start">The start position in the stream</param>
        /// <param name="length">The length in the stream to MD5</param>
        /// <returns>The MD5 checksum as a string</returns>
        public static string MD5Bytes(Stream streamToMD5, long start, int length)
        {
            if (streamToMD5 == null) throw new ArgumentNullException("streamToMD5", "Provided Stream cannot be null.");

            using (MemoryStream stream = new MemoryStream(length))
            {
                StreamWriteWithTimeout.Write(streamToMD5, start, length, stream, 8000, 100, 2000);
                return MD5Bytes(stream);
            }
        }

        /// <summary>
        /// Return the MD5 hash of the provided byte array as a string
        /// </summary>
        /// <param name="bytesToMd5">The bytes which will be checksummed</param>
        /// <returns>The MD5 checksum as a string</returns>
        public static string MD5Bytes(byte[] bytesToMd5)
        {
            if (bytesToMd5 == null) throw new ArgumentNullException("bytesToMd5", "Provided byte[] cannot be null.");

            using(MemoryStream stream = new MemoryStream(bytesToMd5, 0, bytesToMd5.Length, false, true))
                return MD5Bytes(stream);
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
        /// Returns a ConnectionInfo array containing information for all connections which have the provided networkIdentifier. It is also possible to include information for closed connections.
        /// </summary>
        /// <param name="networkIdentifier">The networkIdentifier corresponding to the desired connectionInfo information</param>
        /// <param name="includeClosedConnections">If true will include information for connections which are closed. Otherwise only active connections will be included.</param>
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
        /// Returns the total number of connections where the <see cref="ConnectionInfo.RemoteEndPoint"/> matches the provided <see cref="IPAddress"/>
        /// </summary>
        /// <param name="matchRemoteEndPointIP">The <see cref="IPAddress"/> to match</param>
        /// <returns>Total number of connections where the <see cref="ConnectionInfo.RemoteEndPoint "/> matches the provided <see cref="IPAddress"/></returns>
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
            CloseAllConnections(ConnectionType.Undefined, new IPEndPoint[0]);
        }

        /// <summary>
        /// Close all connections of the provided <see cref="ConnectionType"/>
        /// </summary>
        /// <param name="connectionType">The type of connections to be closed</param>
        public static void CloseAllConnections(ConnectionType connectionType)
        {
            CloseAllConnections(connectionType, new IPEndPoint[0]);
        }

        /// <summary>
        /// Close all connections of the provided <see cref="ConnectionType"/> except to provided <see cref="IPEndPoint"/> array.
        /// </summary>
        /// <param name="connectionTypeToClose">The type of connections to be closed. ConnectionType.<see cref="ConnectionType.Undefined"/> matches all types.</param>
        /// <param name="closeAllExceptTheseRemoteIPEndPoints">Close all except those with remote IPEndPoint that is provided in <see cref="IPEndPoint"/> array</param>
        public static void CloseAllConnections(ConnectionType connectionTypeToClose, IPEndPoint[] closeAllExceptTheseRemoteIPEndPoints)
        {
            List<Connection> connectionsToClose = new List<Connection>();

            lock (globalDictAndDelegateLocker)
            {
                List<Connection> allConnectionsOfType = GetExistingConnection(connectionTypeToClose);
                foreach (Connection conn in allConnectionsOfType)
                {
                    bool dontClose = false;
                    foreach (IPEndPoint endPointToNotClose in closeAllExceptTheseRemoteIPEndPoints)
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
        /// <param name="connectionType">The type of connections to return. ConnectionType.<see cref="ConnectionType.Undefined"/> matches all types.</param>
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
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/> matches all types.</param>
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
        /// <param name="remoteEndPoint">Remote IPEndPoint corresponding with the desired connection. Use IPAddress.Any to match all IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="localEndPoint">Local IPEndPoint corresponding with the desired connection. Use IPAddress.Any to match all IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/> matches all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>A list of connections to the desired peer. If no matching connections exists returns empty list.</returns>
        public static List<Connection> GetExistingConnection(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint, ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Undefined)
        {
            if (remoteEndPoint == null) throw new ArgumentNullException("remoteEndPoint", "remoteEndPoint may not be null.");
            if (localEndPoint == null) throw new ArgumentNullException("localEndPoint", "localEndPoint may not be null.");

            List<Connection> result = new List<Connection>();

            //We create a list of matching IPEndPoints. [0] is remoteEndPoint, [1] will be localEndPoint
            Dictionary<IPEndPoint, List<IPEndPoint>> matchedIPEndPoints = new Dictionary<IPEndPoint,List<IPEndPoint>>();
            lock (globalDictAndDelegateLocker)
            {
                #region Match Remote IPEndPoint
                //If the remoteEndPoint only has a port specified
                if ((remoteEndPoint.Address == IPAddress.Any || remoteEndPoint.Address == IPAddress.IPv6Any) &&
                    remoteEndPoint.Port > 0)
                {
                    //If the provided IP is match any then we look for matching ports
                    foreach (IPEndPoint endPoint in allConnectionsByIPEndPoint.Keys)
                    {
                        if (endPoint.Port == remoteEndPoint.Port)
                            matchedIPEndPoints.Add(endPoint, new List<IPEndPoint>());
                    }
                }
                else if ((remoteEndPoint.Address == IPAddress.Any || remoteEndPoint.Address == IPAddress.IPv6Any) &&
                    remoteEndPoint.Port == 0)
                {
                    foreach (IPEndPoint endPoint in allConnectionsByIPEndPoint.Keys)
                        matchedIPEndPoints.Add(endPoint, new List<IPEndPoint>());
                }
                else if ((remoteEndPoint.Address != IPAddress.Any && remoteEndPoint.Address != IPAddress.IPv6Any) &&
                    remoteEndPoint.Port == 0)
                {
                    //If the provided IP is set but the port is 0 we aim to match the IPAddress
                    foreach (IPEndPoint endPoint in allConnectionsByIPEndPoint.Keys)
                    {
                        if (endPoint.Address == remoteEndPoint.Address)
                            matchedIPEndPoints.Add(endPoint, new List<IPEndPoint>());
                    }
                }
                else
                {
                    if (allConnectionsByIPEndPoint.ContainsKey(remoteEndPoint))
                        matchedIPEndPoints.Add(remoteEndPoint, new List<IPEndPoint>());
                }
                #endregion

                #region Match Local IPEndPoint
                foreach (KeyValuePair<IPEndPoint, List<IPEndPoint>> keyPair in matchedIPEndPoints)
                {
                    //If the localEndPoint only has a port specified
                    if ((localEndPoint.Address == IPAddress.Any || localEndPoint.Address == IPAddress.IPv6Any) &&
                        localEndPoint.Port > 0)
                    {
                        //If the provided IP is match any then we look for matching ports
                        foreach (IPEndPoint endPoint in allConnectionsByIPEndPoint[keyPair.Key].Keys)
                        {
                            if (endPoint.Port == localEndPoint.Port)
                                keyPair.Value.Add(endPoint);
                        }
                    }
                    else if ((localEndPoint.Address == IPAddress.Any || localEndPoint.Address == IPAddress.IPv6Any) &&
                        localEndPoint.Port == 0)
                    {
                        foreach (IPEndPoint endPoint in allConnectionsByIPEndPoint[keyPair.Key].Keys)
                            keyPair.Value.Add(endPoint);
                    }
                    else if ((localEndPoint.Address != IPAddress.Any && localEndPoint.Address != IPAddress.IPv6Any) &&
                        localEndPoint.Port == 0)
                    {
                        //If the provided IP is set but the port is 0 we aim to match the IPAddress
                        foreach (IPEndPoint endPoint in allConnectionsByIPEndPoint[keyPair.Key].Keys)
                        {
                            if (endPoint.Address == localEndPoint.Address)
                                keyPair.Value.Add(endPoint);
                        }
                    }
                    else
                    {
                        if (allConnectionsByIPEndPoint[keyPair.Key].ContainsKey(localEndPoint))
                            keyPair.Value.Add(localEndPoint);
                    }
                }
                #endregion

                //Now pick out all of the matched IPEndPoints and see if there are matched connections
                foreach (IPEndPoint currentRemoteEndPoint in matchedIPEndPoints.Keys)
                {
                    foreach (IPEndPoint currentLocalEndPoint in matchedIPEndPoints[currentRemoteEndPoint])
                    {
                        if (connectionType == ConnectionType.Undefined)
                        {
                            foreach (ConnectionType connType in allConnectionsByIPEndPoint[currentRemoteEndPoint][currentLocalEndPoint].Keys)
                            {
                                if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined || allConnectionsByIPEndPoint[currentRemoteEndPoint][currentLocalEndPoint][connType].ConnectionInfo.ApplicationLayerProtocol == applicationLayerProtocol)
                                    result.Add(allConnectionsByIPEndPoint[currentRemoteEndPoint][currentLocalEndPoint][connType]);
                            }
                        }
                        else
                        {
                            if (allConnectionsByIPEndPoint[currentRemoteEndPoint][currentLocalEndPoint].ContainsKey(connectionType) &&
                                (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined || allConnectionsByIPEndPoint[currentRemoteEndPoint][currentLocalEndPoint][connectionType].ConnectionInfo.ApplicationLayerProtocol == applicationLayerProtocol))
                                result.Add(allConnectionsByIPEndPoint[currentRemoteEndPoint][currentLocalEndPoint][connectionType]);
                        }
                    }
                }
            }

            if (LoggingEnabled)
            { 
                if (result.Count == 0)
                    logger.Trace("RetrieveConnection by remoteEndPoint='" + remoteEndPoint.Address + ":" + remoteEndPoint.Port.ToString() + "', localEndPoint='"+localEndPoint.Address+":"+localEndPoint.Port+"', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol + "'. No matching connections found.");
                else
                    logger.Trace("RetrieveConnection by remoteEndPoint='" + remoteEndPoint.Address + ":" + remoteEndPoint.Port.ToString() + "', localEndPoint='" + localEndPoint.Address + ":" + localEndPoint.Port + "', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol + "'. " + result.Count+ " matching connections found.");
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
            if (connectionInfo == null) throw new ArgumentNullException("connectionInfo", "Provided ConnectionInfo cannot be null.");
            if (connectionInfo.ConnectionType == ConnectionType.Undefined) throw new ArgumentException("connectionInfo", "Provided ConnectionInfo does not specify a connection type.");
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
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/> matches all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>True if a matching connection exists, otherwise false</returns>
        public static bool ConnectionExists(ShortGuid networkIdentifier, ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled)
        {
            if (LoggingEnabled)
                logger.Trace("Checking for existing connection by identifier='" + networkIdentifier + "', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol + "'.");

            return GetExistingConnection(networkIdentifier, connectionType, applicationLayerProtocol).Count > 0;
        }

        /// <summary>
        /// Check if a connection exists with the provided parameters.
        /// </summary>
        /// <param name="remoteEndPoint">IPEndPoint corresponding with the desired connection. Use IPAddress.Any to match all IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="localEndPoint">Local IPEndPoint corresponding with the desired connection. Use IPAddress.Any to match all IPAddresses. Use port number 0 to match all port numbers.</param>
        /// <param name="connectionType">The <see cref="ConnectionType"/> desired. ConnectionType.<see cref="ConnectionType.Undefined"/> matches all types.</param>
        /// <param name="applicationLayerProtocol">Connections with matching ApplicationLayerProtocolStatus.
        /// Use ApplicationLayerProtocolStatus.<see cref="ApplicationLayerProtocolStatus.Undefined"/> to match all status types.</param>
        /// <returns>True if a matching connection exists, otherwise false</returns>
        public static bool ConnectionExists(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint, ConnectionType connectionType, ApplicationLayerProtocolStatus applicationLayerProtocol = ApplicationLayerProtocolStatus.Enabled)
        {
            if (LoggingEnabled)
                logger.Trace("Checking for existing connection by endPoint='" + remoteEndPoint.Address + ":" + remoteEndPoint.Port.ToString() + "', connectionType='" + connectionType.ToString() + "' and ApplicationLayerProtocolStatus='" + applicationLayerProtocol + "'.");

            return GetExistingConnection(remoteEndPoint, localEndPoint, connectionType, applicationLayerProtocol).Count > 0;
        }

        /// <summary>
        /// Removes the reference to the provided connection from within networkComms. DOES NOT CLOSE THE CONNECTION. Returns true if the provided connection reference existed and was removed, false otherwise.
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
                    (allConnectionsByIPEndPoint.ContainsKey(connection.ConnectionInfo.RemoteEndPoint) &&
                    allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint].ContainsKey(connection.ConnectionInfo.LocalEndPoint) &&
                    allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint].ContainsKey(connection.ConnectionInfo.ConnectionType) &&
                    allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint][connection.ConnectionInfo.ConnectionType] == connection))
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
                if (allConnectionsByIPEndPoint.ContainsKey(connection.ConnectionInfo.RemoteEndPoint))
                {
                    if (allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint].ContainsKey(connection.ConnectionInfo.LocalEndPoint) &&
                        allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint].ContainsKey(connection.ConnectionInfo.ConnectionType) &&
                        allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint][connection.ConnectionInfo.ConnectionType] == connection)
                    {
                        allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint].Remove(connection.ConnectionInfo.ConnectionType);

                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Removed connection reference by endPoint for " + connection.ConnectionInfo);
                    }

                    if (allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint].ContainsKey(connection.ConnectionInfo.LocalEndPoint) &&
                        allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint][connection.ConnectionInfo.LocalEndPoint].Count == 0)
                        allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint].Remove(connection.ConnectionInfo.LocalEndPoint);

                    //If this was the last connection type for this endpoint we can remove the endpoint reference as well
                    if (allConnectionsByIPEndPoint[connection.ConnectionInfo.RemoteEndPoint].Count == 0)
                        allConnectionsByIPEndPoint.Remove(connection.ConnectionInfo.RemoteEndPoint);
                }
                #endregion
            }

            return returnValue;
        }

        /// <summary>
        /// Adds a reference by IPEndPoint to the provided connection within networkComms.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="remoteIPEndPointToUse">An optional override which forces a specific remote IPEndPoint</param>
        /// <param name="localIPEndPointToUse">An optional override which forces a specific local IPEndPoint</param>
        internal static void AddConnectionReferenceByRemoteEndPoint(Connection connection, IPEndPoint remoteIPEndPointToUse = null, IPEndPoint localIPEndPointToUse = null)
        {
            if (NetworkComms.LoggingEnabled)
                NetworkComms.Logger.Trace("Adding connection reference by endPoint. Connection='"+connection.ConnectionInfo+"'." +
                    (remoteIPEndPointToUse != null ? " Provided override endPoint of " + remoteIPEndPointToUse.Address + ":" + remoteIPEndPointToUse.Port.ToString() : ""));

            //If the remoteEndPoint is IPAddress.Any we don't record it by endPoint
            if (connection.ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any) ||
                connection.ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.IPv6Any)|| 
                (remoteIPEndPointToUse != null && remoteIPEndPointToUse.Address.Equals(IPAddress.Any)) ||
                (remoteIPEndPointToUse != null && remoteIPEndPointToUse.Address.Equals(IPAddress.IPv6Any)))
                return;

            //A localEndPoint address of IPAddress.Any just means we have not established the connection yet
            //if (connection.ConnectionInfo.LocalEndPoint.Address.Equals(IPAddress.Any) ||  
            //    connection.ConnectionInfo.LocalEndPoint.Address.Equals(IPAddress.IPv6Any))
            //    throw new ConnectionSetupException("Connection local endpoint cannot be IPAddress.Any or IPAddress.IPv6Any at this point.");

            if (connection.ConnectionInfo.ConnectionState == ConnectionState.Established || connection.ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                throw new ConnectionSetupException("Connection reference by endPoint should only be added before a connection is established. This is to prevent duplicate connections.");

            if (remoteIPEndPointToUse == null) remoteIPEndPointToUse = connection.ConnectionInfo.RemoteEndPoint;
            if (localIPEndPointToUse == null) localIPEndPointToUse = connection.ConnectionInfo.LocalEndPoint;

            //We can double check for an existing connection here first so that it occurs outside the lock
            //We look for a connection with either ApplicationProtocolStatus as the endPoint should not be in use
            List<Connection> existingConnection = GetExistingConnection(remoteIPEndPointToUse, connection.ConnectionInfo.LocalEndPoint, connection.ConnectionInfo.ConnectionType, ApplicationLayerProtocolStatus.Undefined);
            if (existingConnection.Count > 0 && connection != existingConnection[0] &&
                ((existingConnection[0].ConnectionInfo.ConnectionType == ConnectionType.UDP && existingConnection[0].ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled) || 
                existingConnection[0].ConnectionInfo.ConnectionState == ConnectionState.Established)) 
                existingConnection[0].ConnectionAlive();

            //For UDP connections which do not enable the application protocol we can't check the remote
            //peer. We choose here to assume the new connection is the better choice, so we close the existing conneciton
            if (existingConnection.Count > 0 && connection != existingConnection[0] &&
                existingConnection[0].ConnectionInfo.ConnectionType == ConnectionType.UDP &&
                existingConnection[0].ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
                existingConnection[0].CloseConnection(false, -12);

            //How do we prevent multiple threads from trying to create a duplicate connection??
            lock (globalDictAndDelegateLocker)
            {
                existingConnection = GetExistingConnection(remoteIPEndPointToUse, connection.ConnectionInfo.LocalEndPoint, connection.ConnectionInfo.ConnectionType, ApplicationLayerProtocolStatus.Undefined);
                //We now check for an existing connection again from within the lock
                if (existingConnection.Count > 0)
                {
                    //If a connection still exist we don't assume it is the same as above
                    if (existingConnection[0] != connection)
                    {
                        throw new DuplicateConnectionException("A different connection already exists with the desired endPoint (" + remoteIPEndPointToUse.Address + ":" + remoteIPEndPointToUse.Port.ToString() + "). This can occur if the connections have different ApplicationProtocolLayer statuses or two peers try to connect to each other simultaneously. New connection is " + (existingConnection[0].ConnectionInfo.ServerSide ? "server side" : "client side") + " - " + connection.ConnectionInfo +
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
                    if (allConnectionsByIPEndPoint.ContainsKey(remoteIPEndPointToUse))
                    {
                        if (allConnectionsByIPEndPoint[remoteIPEndPointToUse].ContainsKey(localIPEndPointToUse))
                        {
                            if (allConnectionsByIPEndPoint[remoteIPEndPointToUse][localIPEndPointToUse].ContainsKey(connection.ConnectionInfo.ConnectionType))
                                throw new Exception("Idiot check fail. The method ConnectionExists should have prevented execution getting here!!");
                            else
                                allConnectionsByIPEndPoint[remoteIPEndPointToUse][localIPEndPointToUse].Add(connection.ConnectionInfo.ConnectionType, connection);
                        }
                        else
                            allConnectionsByIPEndPoint[remoteIPEndPointToUse].Add(localIPEndPointToUse, new Dictionary<ConnectionType, Connection>() { { connection.ConnectionInfo.ConnectionType, connection } });
                    }
                    else
                        allConnectionsByIPEndPoint.Add(remoteIPEndPointToUse, new Dictionary<IPEndPoint, Dictionary<ConnectionType, Connection>>() { { localIPEndPointToUse, new Dictionary<ConnectionType, Connection>() { { connection.ConnectionInfo.ConnectionType, connection } } } });

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
        internal static void UpdateConnectionReferenceByEndPoint(Connection connection, IPEndPoint newRemoteEndPoint, IPEndPoint newLocalEndPoint)
        {
            if (NetworkComms.LoggingEnabled)
                NetworkComms.Logger.Trace("Updating connection reference by endPoint. Connection='" + connection.ConnectionInfo + "'." + (newRemoteEndPoint != null ? " Provided new endPoint of " + newRemoteEndPoint.Address + ":" + newRemoteEndPoint.Port.ToString() : ""));

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
        /// Add a reference by networkIdentifier to the provided connection within NetworkComms. Requires a reference by IPEndPoint to already exist.
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

#if NO_LOGGING
    /// <summary>
    /// On some platforms NLog has issues so this class provides the most basic logging featyres.
    /// </summary>
    public class Logger
    {
        internal object locker = new object();
        internal string LogFileLocation { get; set; }

        public void Trace(string message) { log("Trace", message); }
        public void Debug(string message) { log("Debug", message); }
        public void Fatal(string message, Exception e = null) { log("Fatal", message); }
        public void Info(string message) { log("Info", message); }
        public void Warn(string message) { log("Warn", message); }
        public void Error(string message) { log("Error", message); }

        private void log(string level, string message)
        {
            if (LogFileLocation != null)
            {
                //Try to get the threadId which is very usefull when debugging
                string threadId = null;
                try
                {
                    threadId = Thread.CurrentThread.ManagedThreadId.ToString();
                }
                catch (Exception) { }

                try
                {
                    lock (locker)
                    {
                        using (var sw = new StreamWriter(LogFileLocation, true))
                        {
                            if (threadId != null)
                                sw.WriteLine(DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + threadId + " - " + level + "] - " + message);
                            else
                                sw.WriteLine(DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + level + "] - " + message);
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        public Logger() { }

        public void Shutdown() { }
    }
#endif
}