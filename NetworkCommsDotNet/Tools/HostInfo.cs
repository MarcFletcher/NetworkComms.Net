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

using NetworkCommsDotNet.DPSBase;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.NetworkInformation;
using System.Net.Sockets;

#if NET35 || NET4
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
#endif

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// A static class which provides information about the local host.
    /// </summary>
    public static class HostInfo
    {       

        static HostInfo()
        {
            IP.NetworkLoadUpdateWindowMS = 2000;
            IP.InterfaceLinkSpeed = 95000000;
        }

        /// <summary>
        /// Returns the current machine hostname
        /// </summary>
        public static string HostName
        {
            get
            {
                return Dns.GetHostName();
            }
        }

        static string[] _restrictLocalAdaptorNames = null;
        /// <summary>
        /// Restricts the addresses that may be used when listening.
        /// If set <see cref="IP.FilteredLocalAddresses()"/> will only return addresses corresponding with specified adaptors names. 
        /// Please see <see cref="AllLocalAdaptorNames()"/> for a list of local adaptor names.
        /// Correct format is string[] { "Local Area Connection", "eth0", "en0", "wlan0" }.
        /// </summary>
        public static string[] RestrictLocalAdaptorNames 
        {
            get { return _restrictLocalAdaptorNames; }
            set
            {
                _restrictLocalAdaptorNames = value;
            }
        }

        /// <summary>
        /// Returns all local adaptor names. Can be used to determine which adaptor names to use with <see cref="RestrictLocalAdaptorNames"/>.
        /// </summary>
        /// <returns></returns>
        public static List<string> AllLocalAdaptorNames()
        {
            List<string> result = new List<string>();

            foreach (var iFace in NetworkInterface.GetAllNetworkInterfaces())
                result.Add(iFace.Name);

            return result;
        }

        /// <summary>
        /// Host IP information
        /// </summary>
        public static class IP
        {
            //Local IPAddress cache. Provides significant performance improvement if
            //the IPAddresses are enumerated many times in a short period of time
            static List<IPAddress> filteredLocalAddressesCache = null;
			static DateTime filteredLocalAddressesCacheUpdate = DateTime.UtcNow;

            /// <summary>
            /// Restricts the IPAdddresses that are returned by <see cref="FilteredLocalAddresses()"/>.
            /// If using StartListening overrides that do not take IPEndPoints NetworkComms.Net 
            /// will only listen on IP Addresses within provided ranges. Also see <see cref="RestrictLocalAdaptorNames"/>.
            /// The order of provided ranges determines the order of IPAddresses returned by <see cref="FilteredLocalAddresses()"/>.
            /// </summary>
            public static IPRange[] RestrictLocalAddressRanges { get; set; }

            /// <summary>
            /// Returns all allowed local IP addresses. Caches results for up to 5 second since the previous refresh.
            /// If <see cref="RestrictLocalAdaptorNames"/> has been set only returns IP addresses corresponding with specified adaptors.
            /// If <see cref="RestrictLocalAddressRanges"/> has been set only returns matching addresses ordered in descending 
            /// preference. i.e. Most preferred at [0].
            /// </summary>
            /// <returns></returns>
            public static List<IPAddress> FilteredLocalAddresses()
            {
                return FilteredLocalAddresses(false);
            }

            /// <summary>
            /// Returns all allowed local IP addresses. Caches results for up to 5 second since the previous refresh unless forceCacheUpdate is true.
            /// If <see cref="RestrictLocalAdaptorNames"/> has been set only returns IP addresses corresponding with specified adaptors.
            /// If <see cref="RestrictLocalAddressRanges"/> has been set only returns matching addresses ordered in descending 
            /// preference. i.e. Most preferred at [0].
            /// </summary>
            /// <param name="forceCacheUpdate">If true will refresh the cache and return latest result</param>
            /// <returns></returns>
            public static List<IPAddress> FilteredLocalAddresses(bool forceCacheUpdate)
            {
                if (filteredLocalAddressesCache != null &&
					(DateTime.UtcNow - filteredLocalAddressesCacheUpdate).TotalSeconds < 5)
                    return filteredLocalAddressesCache;
                else
                {
                    
                    List<IPAddress> validIPAddresses = new List<IPAddress>();

                    foreach (var iFace in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        bool interfaceValid = false;
                        var unicastAddresses = iFace.GetIPProperties().UnicastAddresses;

                        //Check if this adaptor is allowed
                        if (RestrictLocalAdaptorNames != null)
                        {
                            foreach (var currentName in RestrictLocalAdaptorNames)
                            {
                                if (iFace.Name == currentName)
                                {
                                    interfaceValid = true;
                                    break;
                                }
                            }
                        }
                        else
                            interfaceValid = true;

                        //If the interface is not allowed move to the next adaptor
                        if (!interfaceValid)
                            continue;

                        //If the adaptor is allowed we can now investigate the individual addresses
                        foreach (var address in unicastAddresses)
                        {
                            if (address.Address.AddressFamily == AddressFamily.InterNetwork || address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                if (!IPRange.IsAutoAssignedAddress(address.Address))
                                {
                                    bool allowed = false;

                                    if (RestrictLocalAddressRanges != null)
                                    {
                                        if (IPRange.Contains(RestrictLocalAddressRanges, address.Address))
                                            allowed = true;
                                    }
                                    else
                                        allowed = true;

                                    if (!allowed)
                                        continue;

                                    if (address.Address != IPAddress.None)
                                        validIPAddresses.Add(address.Address);
                                }
                            }
                        }
                    }

                    //Sort the results to be returned
                    if (RestrictLocalAddressRanges != null)
                    {
                        validIPAddresses.Sort((a, b) =>
                        {
                            for (int i = 0; i < RestrictLocalAddressRanges.Length; i++)
                            {
                                if (RestrictLocalAddressRanges[i].Contains(a))
                                {
                                    if (RestrictLocalAddressRanges[i].Contains(b))
                                        return 0;
                                    else
                                        return -1;
                                }
                                else if (RestrictLocalAddressRanges[i].Contains(b))
                                    return 1;
                            }

                            return 0;
                        });
                    }

                    filteredLocalAddressesCache = validIPAddresses;
					filteredLocalAddressesCacheUpdate = DateTime.UtcNow;

                    return validIPAddresses;
                }
            }

            /// <summary>
            /// The number of milliseconds over which to take an instance load (CurrentNetworkLoad) to be used in averaged 
            /// values (AverageNetworkLoad). Default is 2000ms. Shorter values can be used but less than 200ms may cause significant 
            /// errors in the value of returned value, especially in mono environments.
            /// </summary>
            public static int NetworkLoadUpdateWindowMS { get; set; }

            private static double currentNetworkLoadIncoming;
            private static double currentNetworkLoadOutgoing;

            private static Thread NetworkLoadThread = null;
            private static CommsMath currentNetworkLoadValuesIncoming;
            private static CommsMath currentNetworkLoadValuesOutgoing;
            private static ManualResetEvent NetworkLoadThreadWait;

            /// <summary>
            /// The interface link speed in bits/sec used for network load calculations. Default is 100Mb/sec
            /// </summary>
            public static long InterfaceLinkSpeed { get; set; }

            /// <summary>
            /// Returns the current instance network usage, as a value between 0 and 1. Returns the largest value for any available 
            /// network adaptor. Triggers load analysis upon first call.
            /// </summary>
            public static double CurrentNetworkLoadIncoming
            {
                get
                {
                    //We start the load thread when we first access the network load
                    //this helps cut down on unnecessary threads if unrequired
                    if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                    {
                        lock (NetworkComms.globalDictAndDelegateLocker)
                        {
                            if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                            {
                                currentNetworkLoadValuesIncoming = new CommsMath();
                                currentNetworkLoadValuesOutgoing = new CommsMath();

                                NetworkLoadThread = new Thread(NetworkLoadWorker);
                                NetworkLoadThread.Name = "NetworkLoadThread";
                                NetworkLoadThread.IsBackground = true;
                                NetworkLoadThread.Start();
                            }
                        }
                    }

                    return currentNetworkLoadIncoming;

                }
                private set { currentNetworkLoadIncoming = value; }
            }

            /// <summary>
            /// Returns the current instance network usage, as a value between 0 and 1. Returns the largest value for any available network 
            /// adaptor. Triggers load analysis upon first call.
            /// </summary>
            public static double CurrentNetworkLoadOutgoing
            {
                get
                {

                    //We start the load thread when we first access the network load
                    //this helps cut down on unnecessary threads if unrequired
                    if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                    {
                        lock (NetworkComms.globalDictAndDelegateLocker)
                        {
                            if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                            {
                                currentNetworkLoadValuesIncoming = new CommsMath();
                                currentNetworkLoadValuesOutgoing = new CommsMath();

                                NetworkLoadThread = new Thread(NetworkLoadWorker);
                                NetworkLoadThread.Name = "NetworkLoadThread";
                                NetworkLoadThread.IsBackground = true;
                                NetworkLoadThread.Start();
                            }
                        }
                    }

                    return currentNetworkLoadOutgoing;
                }
                private set { currentNetworkLoadOutgoing = value; }
            }

            /// <summary>
            /// Returns the averaged value of CurrentNetworkLoadIncoming, as a value between 0 and 1, for a time window of up to 254 seconds. 
            /// Triggers load analysis upon first call.
            /// </summary>
            /// <param name="secondsToAverage">Number of seconds over which historical data should be used to arrive at an average</param>
            /// <returns>Average network load as a double between 0 and 1</returns>
            public static double AverageNetworkLoadIncoming(byte secondsToAverage)
            {

                if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                {
                    lock (NetworkComms.globalDictAndDelegateLocker)
                    {
                        if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                        {
                            currentNetworkLoadValuesIncoming = new CommsMath();
                            currentNetworkLoadValuesOutgoing = new CommsMath();

                            NetworkLoadThread = new Thread(NetworkLoadWorker);
                            NetworkLoadThread.Name = "NetworkLoadThread";
                            NetworkLoadThread.IsBackground = true;
                            NetworkLoadThread.Start();
                        }
                    }
                }

                return currentNetworkLoadValuesIncoming.CalculateMean((int)((secondsToAverage * 1000.0) / NetworkLoadUpdateWindowMS));
            }

            /// <summary>
            /// Returns the averaged value of CurrentNetworkLoadIncoming, as a value between 0 and 1, for a time window of up to 254 seconds.
            /// Triggers load analysis upon first call.
            /// </summary>
            /// <param name="secondsToAverage">Number of seconds over which historical data should be used to arrive at an average</param>
            /// <returns>Average network load as a double between 0 and 1</returns>
            public static double AverageNetworkLoadOutgoing(byte secondsToAverage)
            {

                if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                {
                    lock (NetworkComms.globalDictAndDelegateLocker)
                    {
                        if (!NetworkComms.commsShutdown && NetworkLoadThread == null)
                        {
                            currentNetworkLoadValuesIncoming = new CommsMath();
                            currentNetworkLoadValuesOutgoing = new CommsMath();

                            NetworkLoadThread = new Thread(NetworkLoadWorker);
                            NetworkLoadThread.Name = "NetworkLoadThread";
                            NetworkLoadThread.IsBackground = true;
                            NetworkLoadThread.Start();
                        }
                    }
                }

                return currentNetworkLoadValuesOutgoing.CalculateMean((int)((secondsToAverage * 1000.0) / NetworkLoadUpdateWindowMS));
            }

            /// <summary>
            /// Shutdown any background threads in the host tools
            /// </summary>
            /// <param name="threadShutdownTimeoutMS"></param>
            internal static void ShutdownThreads(int threadShutdownTimeoutMS = 1000)
            {
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
                    LogTools.LogException(ex, "CommsShutdownError");
                }
            }

            /// <summary>
            /// Takes a network load snapshot (CurrentNetworkLoad) every NetworkLoadUpdateWindowMS
            /// </summary>
            private static void NetworkLoadWorker()
            {
                NetworkLoadThreadWait = new ManualResetEvent(false);

                //Get all interfaces
                NetworkInterface[] interfacesToUse = NetworkInterface.GetAllNetworkInterfaces();

                long[] startSent, startReceived, endSent, endReceived;

                while (!NetworkComms.commsShutdown)
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

                        if (NetworkComms.commsShutdown) return;

                        //Thread.Sleep(NetworkLoadUpdateWindowMS);
#if NET2
                        NetworkLoadThreadWait.WaitOne(NetworkLoadUpdateWindowMS, false);
#else
                        NetworkLoadThreadWait.WaitOne(NetworkLoadUpdateWindowMS);
#endif

                        if (NetworkComms.commsShutdown) return;

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
                        for (int i = 0; i < startSent.Length; i++)
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
                        //As such we will bump the interface link speed up to 1Gbps so that future load calculations more accurately reflect the 
                        //actual load.
                        if (inMax > 2 || outMax > 2) InterfaceLinkSpeed = 950000000;

                        //Limit to one
                        CurrentNetworkLoadIncoming = (inMax > 1 ? 1 : inMax);
                        CurrentNetworkLoadOutgoing = (outMax > 1 ? 1 : outMax);

                        currentNetworkLoadValuesIncoming.AddValue(CurrentNetworkLoadIncoming);
                        currentNetworkLoadValuesOutgoing.AddValue(CurrentNetworkLoadOutgoing);

                        //We can only have up to 255 seconds worth of data in the average list
                        int maxListSize = (int)(255000.0 / NetworkLoadUpdateWindowMS);
                        currentNetworkLoadValuesIncoming.TrimList(maxListSize);
                        currentNetworkLoadValuesOutgoing.TrimList(maxListSize);
                    }
                    catch (Exception ex)
                    {
                        //There is no need to log if the exception is due to a change in interfaces
                        if (ex.GetType() != typeof(NetworkInformationException))
                            LogTools.LogException(ex, "NetworkLoadWorker");

                        //It may be the interfaces available to the OS have changed so we will reset them here
                        interfacesToUse = NetworkInterface.GetAllNetworkInterfaces();
                        //If an error has happened we don't want to thrash the problem, we wait for 5 seconds and hope whatever was wrong goes away
                        Thread.Sleep(5000);
                    }
                }
            }
        }

#if NET35 || NET4

        /// <summary>
        /// Host bluetooth information
        /// </summary>
        public static class BT
        {
            /// <summary>
            /// Returns all allowed local Bluetooth addresses. 
            /// If <see cref="RestrictLocalAdaptorNames"/> has been set only returns bBluetooth addresses corresponding with specified adaptors.
            /// </summary>
            /// <returns></returns>
            public static List<BluetoothAddress> FilteredLocalAddresses()
            {
                List<BluetoothAddress> allowedAddresses = new List<BluetoothAddress>();

                if (RestrictLocalAdaptorNames == null)
                {
                    foreach (var radio in BluetoothRadio.AllRadios)
                        allowedAddresses.Add(radio.LocalAddress);
                }
                else
                {
                    foreach (var radio in BluetoothRadio.AllRadios)
                    {
                        foreach (var name in RestrictLocalAdaptorNames)
                        {
                            if (name == radio.Name)
                            {
                                allowedAddresses.Add(radio.LocalAddress);
                                break;
                            }
                        }
                    }
                }

                return allowedAddresses;
            }
        }

#endif
    }
}
