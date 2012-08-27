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
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Threading.Tasks;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A TCPConnection represents each established tcp connection between two peers.
    /// </summary>
    public partial class TCPConnection : Connection
    {
        static object staticTCPConnectionLocker = new object();
        static Dictionary<IPEndPoint, TcpListener> tcpListenerDict = new Dictionary<IPEndPoint, TcpListener>();

        static volatile bool shutdownTCPWorkerThreads = false;
        static Thread newIncomingConnectionWorker;
        static Thread connectionKeepAliveWorker;

        /// <summary>
        /// Private static TCP constructor which sets any TCP connection defaults
        /// </summary>
        static TCPConnection()
        {
            ConnectionKeepAlivePollIntervalSecs = 30;
        }

        /// <summary>
        /// The interval between keep alive polls of all connections. Set to int.MaxValue to disable keep alive poll
        /// </summary>
        public static int ConnectionKeepAlivePollIntervalSecs { get; set; }

        /// <summary>
        /// By default networkComms.net disables all usage of the nagle algorithm. If you wish it to be used for established connections set this property to true.
        /// </summary>
        public static bool EnableNagleAlgorithmForNewConnections { get; set; }

        /// <summary>
        /// Accept new TCP connections on default IP's and Port's
        /// </summary>
        public static void AddNewLocalListener()
        {
            List<IPAddress> localIPs = NetworkComms.AllAvailableLocalIPs();

            if (NetworkComms.ListenOnAllAllowedInterfaces)
            {
                try
                {
                    foreach (IPAddress ip in localIPs)
                    {
                        try
                        {
                            AddNewLocalListener(new IPEndPoint(ip, NetworkComms.DefaultListenPort), false);
                        }
                        catch (CommsSetupShutdownException)
                        {

                        }
                    }
                }
                catch (Exception)
                {
                    //If there is an exception here we remove any added listeners and then rethrow
                    Shutdown();
                    throw;
                }
            }
            else
                AddNewLocalListener(new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), true);
        }

        /// <summary>
        /// Accept new TCP connections on specified IP and port
        /// </summary>
        /// <param name="newLocalEndPoint"></param>
        public static void AddNewLocalListener(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true)
        {
            lock (staticTCPConnectionLocker)
            {
                if (tcpListenerDict.ContainsKey(newLocalEndPoint))
                    throw new CommsSetupShutdownException("Provided newLocalEndPoint already exists in tcpListenerDict.");

                TcpListener newListenerInstance;

                try
                {
                    newListenerInstance = new TcpListener(newLocalEndPoint.Address, newLocalEndPoint.Port);
                    newListenerInstance.Start();
                }
                catch (SocketException)
                {
                    //If the port we wanted is not available
                    if (useRandomPortFailOver)
                    {
                        newListenerInstance = new TcpListener(newLocalEndPoint.Address, 0);
                        newListenerInstance.Start();
                    }
                    else
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Error("It was not possible to open port #" + newLocalEndPoint.Port + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                        throw new CommsSetupShutdownException("It was not possible to open port #" + newLocalEndPoint.Port + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }

                IPEndPoint ipEndPointUsed = (IPEndPoint)newListenerInstance.LocalEndpoint;

                if (tcpListenerDict.ContainsKey(ipEndPointUsed))
                    throw new CommsSetupShutdownException("Unable to add new TCP listenerInstance to tcpListenerDict as there is an existing entry.");
                else
                {
                    //If we were succesfull we can add the new localEndPoint to our dict
                    tcpListenerDict.Add(ipEndPointUsed, newListenerInstance);
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Added new TCP localEndPoint - " + ipEndPointUsed.Address + ":" + ipEndPointUsed.Port);
                }
            }

            TriggerIncomingConnectionWorkerThread();
        }

        /// <summary>
        /// Accept new TCP connections on specified IP's and port's
        /// </summary>
        /// <param name="localEndPoint"></param>
        public static void AddNewLocalListener(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
            try
            {
                foreach (var endPoint in localEndPoints)
                    AddNewLocalListener(endPoint, useRandomPortFailOver);
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Returns an endPoint corresponding to a possible local listener on the provided ipAddress. If not listening on provided IP returns null.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns></returns>
        public static IPEndPoint ExistingLocalListener(IPAddress ipAddress)
        {
            lock (staticTCPConnectionLocker)
                return (from current in tcpListenerDict.Keys where current.Address.Equals(ipAddress) select current).FirstOrDefault();
        }

        /// <summary>
        /// Returns a list of all current tcp local end point listeners
        /// </summary>
        /// <returns></returns>
        public static List<IPEndPoint> CurrentLocalEndPoints()
        {
            lock (staticTCPConnectionLocker)
                return tcpListenerDict.Keys.ToList();
        }

        public static bool ListeningForConnections()
        {
            lock (staticTCPConnectionLocker)
                return tcpListenerDict.Count > 0;
        }

        private static void TriggerConnectionKeepAliveThread()
        {
            lock (staticTCPConnectionLocker)
            {
                if (connectionKeepAliveWorker == null || connectionKeepAliveWorker.ThreadState == ThreadState.Stopped)
                {
                    connectionKeepAliveWorker = new Thread(ConnectionKeepAliveWorker);
                    connectionKeepAliveWorker.Name = "TCPConnectionKeepAliveWorker";
                    connectionKeepAliveWorker.Start();
                }
            }
        }

        private static void ConnectionKeepAliveWorker()
        {
            shutdownTCPWorkerThreads = false;

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("TCP Connection keep alive polling thread has started.");
            DateTime lastPollCheck = DateTime.Now;

            do
            {
                try
                {
                    //We have a short sleep here so that we can exit the thread fairly quickly if we need too
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
                    NetworkComms.LogError(ex, "TCPKeepAlivePollError");
                }
            } while (!shutdownTCPWorkerThreads);
        }

        /// <summary>
        /// Polls all existing connections based on ConnectionKeepAlivePollIntervalSecs value. Serverside connections are polled slightly earlier than client side to help reduce potential congestion.
        /// </summary>
        /// <param name="returnImmediately"></param>
        private static void AllConnectionsSendNullPacketKeepAlive(bool returnImmediately = false)
        {
            //Loop through all connections and test the alive state
            List<Connection> allTCPConnections = NetworkComms.RetrieveConnection(ConnectionType.TCP);

            List<Task> connectionCheckTasks = new List<Task>();

            for (int i = 0; i < allTCPConnections.Count; i++)
            {
                int innerIndex = i;

                connectionCheckTasks.Add(Task.Factory.StartNew(new Action(() =>
                {
                    try
                    {
                        //If the connection is server side we poll preferentially
                        if (allTCPConnections[innerIndex] != null)
                        {
                            if (allTCPConnections[innerIndex].ConnectionInfo.ServerSide)
                            {
                                //We check the last incoming traffic time
                                //In scenarios where the client is sending us lots of data there is no need to poll
                                if ((DateTime.Now - allTCPConnections[innerIndex].ConnectionInfo.LastTrafficTime).TotalSeconds > ConnectionKeepAlivePollIntervalSecs)
                                    ((TCPConnection)allTCPConnections[innerIndex]).SendNullPacket();
                            }
                            else
                            {
                                //If we are client side we wait upto an additional 3 seconds to do the poll
                                //This means the server will probably beat us
                                if ((DateTime.Now - allTCPConnections[innerIndex].ConnectionInfo.LastTrafficTime).TotalSeconds > ConnectionKeepAlivePollIntervalSecs + 1.0 + (NetworkComms.randomGen.NextDouble() * 2.0))
                                    ((TCPConnection)allTCPConnections[innerIndex]).SendNullPacket();
                            }
                        }
                    }
                    catch (Exception) { }
                })));
            }

            if (!returnImmediately) Task.WaitAll(connectionCheckTasks.ToArray());
        }

        /// <summary>
        /// Start the IncomingConnectionWorker if required
        /// </summary>
        private static void TriggerIncomingConnectionWorkerThread()
        {
            lock (staticTCPConnectionLocker)
            {
                if (newIncomingConnectionWorker == null || newIncomingConnectionWorker.ThreadState == ThreadState.Stopped)
                {
                    newIncomingConnectionWorker = new Thread(IncomingConnectionWorker);
                    newIncomingConnectionWorker.Name = "TCPNewConnectionWorker";
                    newIncomingConnectionWorker.Start();
                }
            }
        }

        /// <summary>
        /// Picks up any new incoming connections
        /// </summary>
        private static void IncomingConnectionWorker()
        {
            shutdownTCPWorkerThreads = false;

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("TCP IncomingConnectionWorker thread started.");

            try
            {
                do
                {
                    try
                    {
                        bool pickedUpNewConnection = false;

                        List<TcpListener> currentTCPListeners;
                        lock (staticTCPConnectionLocker)
                            currentTCPListeners = tcpListenerDict.Values.ToList();

                        foreach (var listener in currentTCPListeners)
                        {
                            if (listener.Pending() && !shutdownTCPWorkerThreads)
                            {
                                pickedUpNewConnection = true;

                                //Pick up the new connection
                                TcpClient newClient = listener.AcceptTcpClient();
                                CreateConnection(new ConnectionInfo(true, ConnectionType.TCP, (IPEndPoint)newClient.Client.RemoteEndPoint), NetworkComms.DefaultSendReceiveOptions, newClient, true);
                            }
                        }

                        //We will only pause if we didnt get any new connections
                        if (!pickedUpNewConnection && !shutdownTCPWorkerThreads)
                            Thread.Sleep(100);
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
                                NetworkComms.LogError(ex, "CommsSetupError_SE");
                            else
                                NetworkComms.LogError(ex, "CommsSetupError");
                        }
                    }
                } while (!shutdownTCPWorkerThreads);
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "CriticalCommsError");
            }
            finally
            {
                //We try to close all of the tcpListeners
                CloseAndRemoveAllLocalConnectionListeners();
            }

            //newIncomingListenThread = null;
            if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("TCP IncomingConnectionWorker thread ended.");
        }

        /// <summary>
        /// Shutdown everything TCP related
        /// </summary>
        internal static void Shutdown(int threadShutdownTimeoutMS = 1000)
        {
            try
            {
                shutdownTCPWorkerThreads = true;

                CloseAndRemoveAllLocalConnectionListeners();

                //If the worker thread does not shutdown in the required time we kill it
                if (newIncomingConnectionWorker != null && !newIncomingConnectionWorker.Join(threadShutdownTimeoutMS))
                    newIncomingConnectionWorker.Abort();

                if (connectionKeepAliveWorker != null && !connectionKeepAliveWorker.Join(threadShutdownTimeoutMS))
                    newIncomingConnectionWorker.Abort();
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "TCPCommsShutdownError");
            }
        }

        private static void CloseAndRemoveAllLocalConnectionListeners()
        {
            lock (staticTCPConnectionLocker)
            {
                try
                {
                    foreach (var listener in tcpListenerDict.Values)
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
                    tcpListenerDict = new Dictionary<IPEndPoint, TcpListener>();
                }
            }
        }
    }
}
