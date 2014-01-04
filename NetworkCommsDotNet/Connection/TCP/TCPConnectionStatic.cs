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
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using DPSBase;

#if WINDOWS_PHONE
using Windows.Networking.Sockets;
#endif

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A connection object which utilises <see href="http://en.wikipedia.org/wiki/Transmission_Control_Protocol">TCP</see> to communicate between peers.
    /// </summary>
    public sealed partial class TCPConnection : Connection
    {
        static object staticTCPConnectionLocker = new object();

#if WINDOWS_PHONE
        static Dictionary<IPEndPoint, StreamSocketListener> tcpListenerDict = new Dictionary<IPEndPoint, StreamSocketListener>();
#else
        static volatile bool shutdownIncomingConnectionWorkerThread = false;
        static Thread newIncomingConnectionWorker;
        static Dictionary<IPEndPoint, TcpListener> tcpListenerDict = new Dictionary<IPEndPoint, TcpListener>();
#endif
        static Dictionary<IPEndPoint, ApplicationLayerProtocolStatus> tcpListenerAppLayProtocolEnabledDict = new Dictionary<IPEndPoint, ApplicationLayerProtocolStatus>();

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
                            StartListening(new IPEndPoint(ip, NetworkComms.DefaultListenPort), ApplicationLayerProtocolStatus.Enabled, useRandomPortFailOver);
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
                StartListening(new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), ApplicationLayerProtocolStatus.Enabled, useRandomPortFailOver);
        }

        /// <summary>
        /// Accept new incoming TCP connections on all allowed IP's and Port's
        /// </summary>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide usefull features such as inline serialisation, 
        /// transparent packet tranmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="useRandomPortFailOver">If true and the default local port is not available will select one at random. If false and a port is unavailable listening will not be enabled on that adaptor</param>
        public static void StartListening(ApplicationLayerProtocolStatus applicationLayerProtocol, bool useRandomPortFailOver = false)
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
                            StartListening(new IPEndPoint(ip, NetworkComms.DefaultListenPort), applicationLayerProtocol, useRandomPortFailOver);
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
                StartListening(new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), applicationLayerProtocol, useRandomPortFailOver);
        }

        /// <summary>
        /// Accept new TCP connections on specified list of <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="localEndPoints">The localEndPoints to listen for connections on</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available on a given IPEndPoint will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
            if (localEndPoints == null) throw new ArgumentNullException("localEndPoints", "Provided List<IPEndPoint> cannot be null.");

            try
            {
                foreach (var endPoint in localEndPoints)
                    StartListening(endPoint, ApplicationLayerProtocolStatus.Enabled, useRandomPortFailOver);
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Accept new TCP connections on specified list of <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="localEndPoints">The localEndPoints to listen for connections on</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide usefull features such as inline serialisation, 
        /// transparent packet tranmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available on a given IPEndPoint will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(List<IPEndPoint> localEndPoints, ApplicationLayerProtocolStatus applicationLayerProtocol, bool useRandomPortFailOver = true)
        {
            if (localEndPoints == null) throw new ArgumentNullException("localEndPoints", "Provided List<IPEndPoint> cannot be null.");

            try
            {
                foreach (var endPoint in localEndPoints)
                    StartListening(endPoint, applicationLayerProtocol, useRandomPortFailOver);
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Accept new incoming TCP connections on specified <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="newLocalEndPoint">The localEndPoint to listen for connections on.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true)
        {
            StartListening(newLocalEndPoint, ApplicationLayerProtocolStatus.Enabled, useRandomPortFailOver);
        }

        /// <summary>
        /// Accept new incoming TCP connections on specified <see cref="IPEndPoint"/>
        /// </summary>
        /// <param name="newLocalEndPoint">The localEndPoint to listen for connections on.</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide usefull features such as inline serialisation, 
        /// transparent packet tranmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(IPEndPoint newLocalEndPoint, ApplicationLayerProtocolStatus applicationLayerProtocol, bool useRandomPortFailOver = true)
        {
            lock (staticTCPConnectionLocker)
            {
                //If as listener is already added there is not need to continue
                if (tcpListenerDict.ContainsKey(newLocalEndPoint)) return;

#if WINDOWS_PHONE
                StreamSocketListener newListenerInstance = new StreamSocketListener();
                newListenerInstance.ConnectionReceived += newListenerInstance_ConnectionReceived;
#else
                TcpListener newListenerInstance;
#endif

                try
                {
#if WINDOWS_PHONE
                    newListenerInstance.BindEndpointAsync(new Windows.Networking.HostName(newLocalEndPoint.Address.ToString()), newLocalEndPoint.Port.ToString()).AsTask().Wait();
#else
                    newListenerInstance = new TcpListener(newLocalEndPoint.Address, newLocalEndPoint.Port);
                    newListenerInstance.Start();
#endif
                }
                catch (SocketException)
                {
                    //If the port we wanted is not available
                    if (useRandomPortFailOver)
                    {
                        try
                        {
#if WINDOWS_PHONE
                        newListenerInstance.BindEndpointAsync(new Windows.Networking.HostName(newLocalEndPoint.Address.ToString()), "").AsTask().Wait(); 
#else
                            newListenerInstance = new TcpListener(newLocalEndPoint.Address, 0);
                            newListenerInstance.Start();
#endif
                        }
                        catch (SocketException)
                        {
                            //If we get another socket exception this appears to be a bad IP. We will just ignore this IP
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to open a random port on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                            throw new CommsSetupShutdownException("It was not possible to open a random port on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                        }
                    }
                    else
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to open port #" + newLocalEndPoint.Port.ToString() + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                        throw new CommsSetupShutdownException("It was not possible to open port #" + newLocalEndPoint.Port.ToString() + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }

#if WINDOWS_PHONE
                IPEndPoint ipEndPointUsed = new IPEndPoint(newLocalEndPoint.Address, int.Parse(newListenerInstance.Information.LocalPort));  
#else
                IPEndPoint ipEndPointUsed = (IPEndPoint)newListenerInstance.LocalEndpoint;
#endif

                if (tcpListenerDict.ContainsKey(ipEndPointUsed))
                    throw new CommsSetupShutdownException("Unable to add new TCP listenerInstance to tcpListenerDict as there is an existing entry.");
                else
                {
                    //If we were succesfull we can add the new localEndPoint to our dict
                    tcpListenerDict.Add(ipEndPointUsed, newListenerInstance);
                    tcpListenerAppLayProtocolEnabledDict.Add(ipEndPointUsed, applicationLayerProtocol);
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Added new TCP localEndPoint with " + (applicationLayerProtocol==ApplicationLayerProtocolStatus.Enabled? "application layer protocol enabled" : "no application layer protocol") + " on " + ipEndPointUsed.Address + ":" + ipEndPointUsed.Port.ToString());
                }
            }

#if !WINDOWS_PHONE
            TriggerIncomingConnectionWorkerThread();
#endif
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding to all current TCP local listeners
        /// </summary>
        /// <returns>List of <see cref="IPEndPoint"/> corresponding to all current TCP local listeners</returns>
        public static List<IPEndPoint> ExistingLocalListenEndPoints()
        {
            lock (staticTCPConnectionLocker)
            {
                List<IPEndPoint> res = new List<IPEndPoint>();
                foreach (var pair in tcpListenerDict)
                    res.Add(pair.Key);
                
                return res;
            }
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding to a possible local listeners on the provided <see cref="IPAddress"/>. If not listening on provided <see cref="IPAddress"/> returns empty list.
        /// </summary>
        /// <param name="ipAddress">The <see cref="IPAddress"/> to match to a possible local listener</param>
        /// <returns>If listener exists returns <see cref="IPAddress"/> otherwise null</returns>
        public static List<IPEndPoint> ExistingLocalListenEndPoints(IPAddress ipAddress)
        {
            List<IPEndPoint> returnList = new List<IPEndPoint>();
            lock (staticTCPConnectionLocker)
            {                
                foreach (var pair in tcpListenerDict)
                    if (pair.Key.Address.Equals(ipAddress))
                        returnList.Add(pair.Key);         
            }

            return returnList;
        }

        /// <summary>
        /// If the provided <see cref="IPEndPoint"/> matches an existing local listener returns the requested status.
        /// If the <see cref="IPEndPoint"/> does not match an existing local listener returns ApplicationLayerProtocolStatus.Undefined.
        /// </summary>
        /// <param name="ipEndPoint">The <see cref="IPEndPoint"/> of an existing local listener.</param>
        /// <returns>The status of the listeners application layer protocol usage.</returns>
        public static ApplicationLayerProtocolStatus ExistingLocalListenEndPointApplicationLayerProtocolStatus(IPEndPoint ipEndPoint)
        {
            lock (staticTCPConnectionLocker)
            {
                if (tcpListenerAppLayProtocolEnabledDict.ContainsKey(ipEndPoint))
                    return tcpListenerAppLayProtocolEnabledDict[ipEndPoint];
                else
                    return ApplicationLayerProtocolStatus.Undefined;
            }
        }

        /// <summary>
        /// Returns true if listening for new TCP connections.
        /// </summary>
        /// <returns>True if listening for new TCP connections.</returns>
        public static bool Listening()
        {
            lock (staticTCPConnectionLocker)
                return tcpListenerDict.Count > 0;
        }

#if WINDOWS_PHONE
        private static void newListenerInstance_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(args.Socket.Information.LocalAddress.DisplayName.ToString()), int.Parse(args.Socket.Information.LocalPort));
                ApplicationLayerProtocolStatus applicationLayerProtocolStatus = ExistingLocalListenEndPointApplicationLayerProtocolStatus(localEndPoint);

                ConnectionInfo newConnectionInfo;
                if (applicationLayerProtocolStatus != ApplicationLayerProtocolStatus.Undefined)
                    newConnectionInfo = new ConnectionInfo(true, ConnectionType.TCP, new IPEndPoint(IPAddress.Parse(args.Socket.Information.RemoteAddress.DisplayName.ToString()), int.Parse(args.Socket.Information.RemotePort)), applicationLayerProtocolStatus);
                else
                    newConnectionInfo = new ConnectionInfo(true, ConnectionType.TCP, new IPEndPoint(IPAddress.Parse(args.Socket.Information.RemoteAddress.DisplayName.ToString()), int.Parse(args.Socket.Information.RemotePort)));

                TCPConnection.GetConnection(newConnectionInfo, NetworkComms.DefaultSendReceiveOptions, args.Socket, true);
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
                        NetworkComms.LogError(ex, "ConnectionSetupError_SE");
                    else
                        NetworkComms.LogError(ex, "ConnectionSetupError");
                }
            }
        }
#else
        /// <summary>
        /// Start the IncomingConnectionWorker if required
        /// </summary>
        private static void TriggerIncomingConnectionWorkerThread()
        {
            lock (staticTCPConnectionLocker)
            {
                if (!NetworkComms.commsShutdown && (newIncomingConnectionWorker == null || newIncomingConnectionWorker.ThreadState == ThreadState.Stopped))
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
            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("TCP IncomingConnectionWorker thread started.");

            try
            {
                while (!shutdownIncomingConnectionWorkerThread)
                {
                    try
                    {
                        bool pickedUpNewConnection = false;

                        //Create copies of the relevant dictionaries
                        List<TcpListener> currentTCPListeners = new List<TcpListener>();
                        List<ApplicationLayerProtocolStatus> currentTCPListenerAppLayProtocol = new List<ApplicationLayerProtocolStatus>();
                        lock (staticTCPConnectionLocker)
                        {
                            foreach (var pair in tcpListenerDict)
                            {
                                currentTCPListeners.Add(pair.Value);
                                currentTCPListenerAppLayProtocol.Add(tcpListenerAppLayProtocolEnabledDict[pair.Key]);
                            }
                        }

                        for(int i=0; i<currentTCPListeners.Count; i++)
                        {
                            TcpListener listener = currentTCPListeners[i];
                            ApplicationLayerProtocolStatus applicationLayerProtocol = currentTCPListenerAppLayProtocol[i];

                            if (!shutdownIncomingConnectionWorkerThread && listener.Pending())
                            {
                                pickedUpNewConnection = true;

                                //Pick up the new connection
                                TcpClient newClient = listener.AcceptTcpClient();

                                //Perform the establish in a task so that we can continue picking up new connections here
                                ThreadPool.QueueUserWorkItem(new WaitCallback((obj) =>
                                {
                                    #region Pickup The New Connection
                                    try
                                    {
                                        GetConnection(new ConnectionInfo(true, ConnectionType.TCP, (IPEndPoint)newClient.Client.RemoteEndPoint, applicationLayerProtocol), NetworkComms.DefaultSendReceiveOptions, newClient, true);
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
                                                NetworkComms.LogError(ex, "ConnectionSetupError_SE");
                                            else
                                                NetworkComms.LogError(ex, "ConnectionSetupError");
                                        }
                                    }
                                    #endregion
                                }));
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
                    catch (ObjectDisposedException)
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
                }
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
            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("TCP IncomingConnectionWorker thread ended.");
        }
#endif

        /// <summary>
        /// Shutdown everything TCP related
        /// </summary>
        internal static void Shutdown(int threadShutdownTimeoutMS = 1000)
        {
#if WINDOWS_PHONE
            try
            {
                CloseAndRemoveAllLocalConnectionListeners();
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "TCPCommsShutdownError");
            }  
#else
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
            finally
            {
                shutdownIncomingConnectionWorkerThread = false;
            }
#endif
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
                            #if WINDOWS_PHONE
                            if (listener != null) listener.Dispose();
#else
                            if (listener != null) listener.Stop();
#endif
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
                finally
                {
                    //Once we have stopped all listeners we set the list to null incase we want to resart listening
#if WINDOWS_PHONE
                    tcpListenerDict = new Dictionary<IPEndPoint,StreamSocketListener>();
#else
                    tcpListenerDict = new Dictionary<IPEndPoint, TcpListener>();
                    tcpListenerAppLayProtocolEnabledDict = new Dictionary<IPEndPoint, ApplicationLayerProtocolStatus>();
#endif
                }
            }
        }
    }
}
