// 
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
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;
using System.Net.Sockets;

namespace NetworkCommsDotNet.Connections
{
    #if !NET2
    /// <summary>
    /// Global connection base class for NetworkComms.Net. Most user interactions happen using a connection object. 
    /// Extended by <see cref="TCPConnection"/>, <see cref="UDPConnection"/> and <see cref="NetworkCommsDotNet.Connections.Bluetooth.BluetoothConnection"/>.
    /// </summary>
    #else
    /// <summary>
    /// Global connection base class for NetworkComms.Net. Most user interactions happen using a connection object. 
    /// Extended by <see cref="TCPConnection"/> and <see cref="UDPConnection"/>.
    /// </summary>
    #endif
    public abstract partial class Connection
    {
        static ManualResetEvent workedThreadSignal = new ManualResetEvent(false);
        static volatile bool shutdownWorkerThreads = false;
        static object staticConnectionLocker = new object();
        static Thread connectionKeepAliveWorker;

        /// <summary>
        /// Private static constructor which sets the connection defaults
        /// </summary>
        static Connection()
        {
            ConnectionKeepAlivePollIntervalSecs = 30;
            MaxNumSendTimes = 100;
            MinNumSendsBeforeConnectionSpecificSendTimeout = 4;
            MinSendTimeoutMS = 2000;
            MinimumMSPerKBSendTimeout = 20;
            DefaultMSPerKBSendTimeout = 1000;
            NumberOfStDeviationsForWriteTimeout = 3;
        }

        /// <summary>
        /// The minimum number of milliseconds to allow per KB before a write timeout may occur. Default is 20.0.
        /// </summary>
        public static double MinimumMSPerKBSendTimeout { get; set; }

        /// <summary>
        /// The maximum number of writes intervals to maintain. Default is 100.
        /// </summary>
        public static int MaxNumSendTimes { get; set; }

        /// <summary>
        /// The minimum number of writes before the connection specific write timeouts will be used. Default is 4.
        /// </summary>
        public static int MinNumSendsBeforeConnectionSpecificSendTimeout { get; set; }

        /// <summary>
        /// The default milliseconds per KB write timeout before connection specific values become available. Default is 1000. See <see cref="MinNumSendsBeforeConnectionSpecificSendTimeout"/>.
        /// </summary>
        public static int DefaultMSPerKBSendTimeout { get; set; }

        /// <summary>
        /// The minimum timeout for any sized send in milliseconds. Prevents timeouts when sending less than 1KB. Default is 2000.
        /// </summary>
        public static int MinSendTimeoutMS { get; set; }

        /// <summary>
        /// The interval between keep alive polls of all connections. Set to int.MaxValue to disable keep alive poll
        /// </summary>
        public static int ConnectionKeepAlivePollIntervalSecs { get; set; }

        /// <summary>
        /// The number of standard deviations from the mean to use for write timeouts. Default is 3.0.
        /// </summary>
        public static double NumberOfStDeviationsForWriteTimeout { get; set; }

        /// <summary>
        /// Starts the connectionKeepAliveWorker thread if it is not already started
        /// </summary>
        protected static void TriggerConnectionKeepAliveThread()
        {
            lock (staticConnectionLocker)
            {
                if (!shutdownWorkerThreads && (connectionKeepAliveWorker == null || connectionKeepAliveWorker.ThreadState == ThreadState.Stopped))
                {
                    connectionKeepAliveWorker = new Thread(ConnectionKeepAliveWorker);
                    connectionKeepAliveWorker.Name = "ConnectionKeepAliveWorker";
                    connectionKeepAliveWorker.IsBackground = true;
                    connectionKeepAliveWorker.Start();
                }
            }
        }

        /// <summary>
        /// A single static worker thread which keeps connections alive
        /// </summary>
        private static void ConnectionKeepAliveWorker()
        {
            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Connection keep alive polling thread has started.");
            DateTime lastPollCheck = DateTime.Now;

            while (!shutdownWorkerThreads)
            {
                try
                {
#if NET2
                    //We have a short sleep here so that we can exit the thread fairly quickly if we need too
                    if (ConnectionKeepAlivePollIntervalSecs == int.MaxValue)
                        workedThreadSignal.WaitOne(5000, false);
                    else
                        workedThreadSignal.WaitOne(100, false);
#else
                    //We have a short sleep here so that we can exit the thread fairly quickly if we need too
                    if (ConnectionKeepAlivePollIntervalSecs == int.MaxValue)
                        workedThreadSignal.WaitOne(5000);
                    else
                        workedThreadSignal.WaitOne(100);
#endif

                    //Check for shutdown here
                    if (shutdownWorkerThreads) break;

                    //Any connections which we have not seen in the last poll interval get tested using a null packet
                    if (ConnectionKeepAlivePollIntervalSecs < int.MaxValue && (DateTime.Now - lastPollCheck).TotalSeconds > (double)ConnectionKeepAlivePollIntervalSecs)
                    {
                        AllConnectionsSendNullPacketKeepAlive();
                        lastPollCheck = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    LogTools.LogException(ex, "ConnectionKeepAlivePollError");
                }
            }
        }

        /// <summary>
        /// Polls all existing connections based on ConnectionKeepAlivePollIntervalSecs value. Server side connections are polled 
        /// slightly earlier than client side to help reduce potential congestion.
        /// </summary>
        /// <param name="returnImmediately">If true runs as task and returns immediately.</param>
        private static void AllConnectionsSendNullPacketKeepAlive(bool returnImmediately = false)
        {
            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Starting AllConnectionsSendNullPacketKeepAlive");

            //Loop through all connections and test the alive state
            List<Connection> allConnections = NetworkComms.GetExistingConnection(ApplicationLayerProtocolStatus.Enabled);
            int remainingConnectionCount = allConnections.Count;

            QueueItemPriority nullSendPriority = QueueItemPriority.AboveNormal;
            
            ManualResetEvent allConnectionsComplete = new ManualResetEvent(false);
            for (int i = 0; i < allConnections.Count; i++)
            {
                //We don't send null packets to unconnected UDP connections
                UDPConnection asUDP = allConnections[i] as UDPConnection;
                if (asUDP != null && asUDP.ConnectionUDPOptions == UDPOptions.None)
                {
                    if (Interlocked.Decrement(ref remainingConnectionCount) == 0)
                        allConnectionsComplete.Set();

                    continue;
                }
                else
                {
                    int innerIndex = i;
                    NetworkComms.CommsThreadPool.EnqueueItem(nullSendPriority, new WaitCallback((obj) =>
                    {
                        try
                        {
                            //If the connection is server side we poll preferentially
                            if (allConnections[innerIndex] != null)
                            {
                                if (allConnections[innerIndex].ConnectionInfo.ServerSide)
                                {
                                    //We check the last incoming traffic time
                                    //In scenarios where the client is sending us lots of data there is no need to poll
                                    if ((DateTime.Now - allConnections[innerIndex].ConnectionInfo.LastTrafficTime).TotalSeconds > ConnectionKeepAlivePollIntervalSecs)
                                        allConnections[innerIndex].SendNullPacket();
                                }
                                else
                                {
                                    //If we are client side we wait up to an additional 3 seconds to do the poll
                                    //This means the server will probably beat us
                                    if ((DateTime.Now - allConnections[innerIndex].ConnectionInfo.LastTrafficTime).TotalSeconds > ConnectionKeepAlivePollIntervalSecs + 1.0 + (NetworkComms.randomGen.NextDouble() * 2.0))
                                        allConnections[innerIndex].SendNullPacket();
                                }
                            }
                        }
                        catch (Exception) { }
                        finally
                        {
                            if (Interlocked.Decrement(ref remainingConnectionCount) == 0)
                                allConnectionsComplete.Set();
                        }
                    }), null);
                }
            }

            //Max wait is 1 seconds per connection
            if (!returnImmediately && allConnections.Count > 0)
            {
#if NET2
                if (!allConnectionsComplete.WaitOne(allConnections.Count * 2500, false))
#else
                if (!allConnectionsComplete.WaitOne(allConnections.Count * 2500))
#endif
                    //This timeout should not really happen so we are going to log an error if it does
                    //LogTools.LogException(new TimeoutException("Timeout after " + allConnections.Count.ToString() + " seconds waiting for null packet sends to finish. " + remainingConnectionCount.ToString() + " connection waits remain. This error indicates very high send load or a possible send deadlock."), "NullPacketKeepAliveTimeoutError");
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Warn("Timeout after " + allConnections.Count.ToString() + " seconds waiting for null packet sends to finish. " + remainingConnectionCount.ToString() + " connection waits remain. This error indicates very high send load or a possible send deadlock.");
            }
        }

        /// <summary>
        /// Shutdown any static connection components
        /// </summary>
        /// <param name="threadShutdownTimeoutMS"></param>
        internal static void Shutdown(int threadShutdownTimeoutMS = 1000)
        {
            try
            {
                StopListening();
            }
            catch (Exception ex)
            {
                LogTools.LogException(ex, "CommsShutdownError");
            }

            try
            {
                shutdownWorkerThreads = true;

                if (connectionKeepAliveWorker != null && !connectionKeepAliveWorker.Join(threadShutdownTimeoutMS))
                    connectionKeepAliveWorker.Abort();

            }
            catch (Exception ex)
            {
                LogTools.LogException(ex, "CommsShutdownError");
            }
            finally
            {
                shutdownWorkerThreads = false;
                workedThreadSignal.Reset();
            }
        }
    }
}
