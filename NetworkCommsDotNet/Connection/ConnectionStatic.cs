//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
using System.Threading.Tasks;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Connections
{
    #if !NET2 && !WINDOWS_PHONE
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
#if NETFX_CORE
        static Task connectionKeepAliveWorker;
#else
        static Thread connectionKeepAliveWorker;
#endif

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
#if NETFX_CORE
                if (!shutdownWorkerThreads && (connectionKeepAliveWorker == null || connectionKeepAliveWorker.IsCompleted))
                {
                    connectionKeepAliveWorker = new Task(ConnectionKeepAliveWorker, TaskCreationOptions.LongRunning);
                    connectionKeepAliveWorker.Start();
                }
#else
                if (!shutdownWorkerThreads && (connectionKeepAliveWorker == null || connectionKeepAliveWorker.ThreadState == ThreadState.Stopped))
                {
                    connectionKeepAliveWorker = new Thread(ConnectionKeepAliveWorker);
                    connectionKeepAliveWorker.Name = "ConnectionKeepAliveWorker";
                    connectionKeepAliveWorker.IsBackground = true;
                    connectionKeepAliveWorker.Start();
                }
#endif
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
                    //We have a short sleep here so that we can exit the thread fairly quickly if we need too
                    if (ConnectionKeepAlivePollIntervalSecs == int.MaxValue)
                        workedThreadSignal.WaitOne(5000);
                    else
                        workedThreadSignal.WaitOne(100);

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
                if (!allConnectionsComplete.WaitOne(allConnections.Count * 2500))
                    //This timeout should not really happen so we are going to log an error if it does
                    LogTools.LogException(new TimeoutException("Timeout after " + allConnections.Count.ToString() + " seconds waiting for null packet sends to finish. " + remainingConnectionCount.ToString() + " connection waits remain. This error indicates very high send load or a possible send deadlock."), "NullPacketKeepAliveTimeoutError");
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
#if NETFX_CORE
                if (connectionKeepAliveWorker != null && !connectionKeepAliveWorker.Wait(threadShutdownTimeoutMS))
                    throw new CommsSetupShutdownException("Connection keep alive worker failed to shutdown");
#else
                if (connectionKeepAliveWorker != null && !connectionKeepAliveWorker.Join(threadShutdownTimeoutMS))
                    connectionKeepAliveWorker.Abort();
#endif
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
