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
using System.Threading.Tasks;
using SerializerBase;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// NetworkComms maintains a top level Connection object for shared methods
    /// </summary>
    public abstract partial class Connection
    {
        static volatile bool shutdownWorkerThreads = false;
        static object staticConnectionLocker = new object();
        static Thread connectionKeepAliveWorker;

        /// <summary>
        /// Private static TCP constructor which sets any connection defaults
        /// </summary>
        static Connection()
        {
            ConnectionKeepAlivePollIntervalSecs = 30;
        }

        /// <summary>
        /// The interval between keep alive polls of all connections. Set to int.MaxValue to disable keep alive poll
        /// </summary>
        public static int ConnectionKeepAlivePollIntervalSecs { get; set; }

        /// <summary>
        /// Starts the connectionKeepAliveWorker thread if it is not already started
        /// </summary>
        protected static void TriggerConnectionKeepAliveThread()
        {
            lock (staticConnectionLocker)
            {
                if (connectionKeepAliveWorker == null || connectionKeepAliveWorker.ThreadState == ThreadState.Stopped)
                {
                    connectionKeepAliveWorker = new Thread(ConnectionKeepAliveWorker);
                    connectionKeepAliveWorker.Name = "ConnectionKeepAliveWorker";
                    connectionKeepAliveWorker.Start();
                }
            }
        }

        /// <summary>
        /// A single static worker thread which keeps connections alive
        /// </summary>
        private static void ConnectionKeepAliveWorker()
        {
            shutdownWorkerThreads = false;

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Connection keep alive polling thread has started.");
            DateTime lastPollCheck = DateTime.Now;

            do
            {
                try
                {
                    //We have a short sleep here so that we can exit the thread fairly quickly if we need too
                    if (ConnectionKeepAlivePollIntervalSecs == int.MaxValue)
                        Thread.Sleep(5000);
                    else
                        Thread.Sleep(100);

                    //Any connections which we have not seen in the last poll interval get tested using a null packet
                    if (ConnectionKeepAlivePollIntervalSecs < int.MaxValue && (DateTime.Now - lastPollCheck).TotalSeconds > (double)ConnectionKeepAlivePollIntervalSecs)
                    {
                        AllConnectionsSendNullPacketKeepAlive();
                        lastPollCheck = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    NetworkComms.LogError(ex, "ConnectionKeepAlivePollError");
                }
            } while (!shutdownWorkerThreads);
        }

        /// <summary>
        /// Polls all existing connections based on ConnectionKeepAlivePollIntervalSecs value. Serverside connections are polled slightly earlier than client side to help reduce potential congestion.
        /// </summary>
        /// <param name="returnImmediately"></param>
        private static void AllConnectionsSendNullPacketKeepAlive(bool returnImmediately = false)
        {
            //Loop through all connections and test the alive state
            List<Connection> allConnections = NetworkComms.RetrieveConnection();

            List<Task> connectionCheckTasks = new List<Task>();

            for (int i = 0; i < allConnections.Count; i++)
            {
                int innerIndex = i;

                connectionCheckTasks.Add(Task.Factory.StartNew(new Action(() =>
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
                                //If we are client side we wait upto an additional 3 seconds to do the poll
                                //This means the server will probably beat us
                                if ((DateTime.Now - allConnections[innerIndex].ConnectionInfo.LastTrafficTime).TotalSeconds > ConnectionKeepAlivePollIntervalSecs + 1.0 + (NetworkComms.randomGen.NextDouble() * 2.0))
                                    allConnections[innerIndex].SendNullPacket();
                            }
                        }
                    }
                    catch (Exception) { }
                })));
            }

            if (!returnImmediately) Task.WaitAll(connectionCheckTasks.ToArray());
        }

        /// <summary>
        /// Shutdown any static connection components
        /// </summary>
        /// <param name="threadShutdownTimeoutMS"></param>
        internal static void ShutdownBase(int threadShutdownTimeoutMS = 1000)
        {
            try
            {
                shutdownWorkerThreads = true;

                if (connectionKeepAliveWorker != null && !connectionKeepAliveWorker.Join(threadShutdownTimeoutMS))
                    connectionKeepAliveWorker.Abort();
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "CommsShutdownError");
            }
        }
    }
}
