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
    /// A connection object which utilises <see href="http://en.wikipedia.org/wiki/Transmission_Control_Protocol">TCP</see> to communicate between peers.
    /// </summary>
    public partial class TCPConnection : Connection
    {
        static object staticTCPConnectionLocker = new object();
        static Dictionary<IPEndPoint, TcpListener> tcpListenerDict = new Dictionary<IPEndPoint, TcpListener>();

        static volatile bool shutdownIncomingConnectionWorkerThread = false;
        static Thread newIncomingConnectionWorker;

        /// <summary>
        /// By default usage of <see href="http://en.wikipedia.org/wiki/Nagle's_algorithm">Nagle's algorithm</see> during TCP exchanges is disabled for performance reasons. If you wish it to be used for newly established connections set this property to true.
        /// </summary>
        public static bool EnableNagleAlgorithmForNewConnections { get; set; }

        /// <summary>
        /// Accept new incoming TCP connections on all allowed IP's and Port's
        /// </summary>
        /// <param name="useRandomPortFailOver">If true and the default local port is not available will select one at random. If false and a port is unavailable listening will not be enabled on that adaptor</param>
        public static void StartListening(bool useRandomPortFailOver = false)
        {
            List<IPAddress> localIPs = NetworkComms.AllAllowedIPs();

            if (NetworkComms.ListenOnAllAllowedInterfaces)
            {
                try
                {
                    foreach (IPAddress ip in localIPs)
                    {
                        try
                        {
                            StartListening(new IPEndPoint(ip, NetworkComms.DefaultListenPort), useRandomPortFailOver);
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
                StartListening(new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), useRandomPortFailOver);
        }

        /// <summary>
        /// Accept new incoming TCP connections on specified <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="newLocalEndPoint">The localEndPoint to listen for connections on.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true)
        {
            lock (staticTCPConnectionLocker)
            {
                //If as listener is already added there is not need to continue
                if (tcpListenerDict.ContainsKey(newLocalEndPoint)) return;

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
        /// Accept new TCP connections on specified list of <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="localEndPoints">The localEndPoints to listen for connections on</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available on a given IPEndPoint will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
            try
            {
                foreach (var endPoint in localEndPoints)
                    StartListening(endPoint, useRandomPortFailOver);
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding to all current TCP local listeners
        /// </summary>
        /// <returns>List of <see cref="IPEndPoint"/> corresponding to all current TCP local listeners</returns>
        public static List<IPEndPoint> ExistingLocalListenEndPoints()
        {
            lock (staticTCPConnectionLocker)
                return tcpListenerDict.Keys.ToList();
        }

        /// <summary>
        /// Returns an <see cref="IPEndPoint"/> corresponding to a possible local listener on the provided <see cref="IPAddress"/>. If not listening on provided <see cref="IPAddress"/> returns null.
        /// </summary>
        /// <param name="ipAddress">The <see cref="IPAddress"/> to match to a possible local listener</param>
        /// <returns>If listener exists returns <see cref="IPAddress"/> otherwise null</returns>
        public static IPEndPoint ExistingLocalListenEndPoints(IPAddress ipAddress)
        {
            lock (staticTCPConnectionLocker)
                return (from current in tcpListenerDict.Keys where current.Address.Equals(ipAddress) select current).FirstOrDefault();
        }

        /// <summary>
        /// Returns true if there is atleast one local TCP listeners
        /// </summary>
        /// <returns>True if there is atleast one local TCP listeners</returns>
        public static bool Listening()
        {
            lock (staticTCPConnectionLocker)
                return tcpListenerDict.Count > 0;
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
            shutdownIncomingConnectionWorkerThread = false;

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
                            if (listener.Pending() && !shutdownIncomingConnectionWorkerThread)
                            {
                                pickedUpNewConnection = true;

                                //Pick up the new connection
                                TcpClient newClient = listener.AcceptTcpClient();
                                GetConnection(new ConnectionInfo(true, ConnectionType.TCP, (IPEndPoint)newClient.Client.RemoteEndPoint), NetworkComms.DefaultSendReceiveOptions, newClient, true);
                            }
                        }

                        //We will only pause if we didnt get any new connections
                        if (!pickedUpNewConnection && !shutdownIncomingConnectionWorkerThread)
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
                } while (!shutdownIncomingConnectionWorkerThread);
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
                shutdownIncomingConnectionWorkerThread = true;

                CloseAndRemoveAllLocalConnectionListeners();

                //If the worker thread does not shutdown in the required time we kill it
                if (newIncomingConnectionWorker != null && !newIncomingConnectionWorker.Join(threadShutdownTimeoutMS))
                    newIncomingConnectionWorker.Abort();
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "TCPCommsShutdownError");
            }
        }

        /// <summary>
        /// Close down all local TCP listeners
        /// </summary>
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
