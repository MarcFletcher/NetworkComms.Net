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

        static volatile bool shutdownNewIncomingConnectionWorker = false;
        static Thread newIncomingConnectionWorker;

        static Thread connectionKeepAliveWorker;

        public static bool ConnectionKeepAliveWorkerEnabled { get; private set; }

        /// <summary>
        /// Accept new TCP connections on default IP's and Port's
        /// </summary>
        public static void AddNewLocalConnectionListener()
        {
            List<IPAddress> localIPs = NetworkComms.AllAllowedLocalIPs();

            if (NetworkComms.ListenOnAllAllowedInterfaces)
            {
                try
                {
                    foreach (IPAddress ip in localIPs)
                    {
                        try
                        {
                            AddNewLocalConnectionListener(new IPEndPoint(ip, NetworkComms.DefaultListenPort), false);
                        }
                        catch (CommsSetupException)
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
                AddNewLocalConnectionListener(new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), true);
        }

        /// <summary>
        /// Accept new TCP connections on specified IP and port
        /// </summary>
        /// <param name="newLocalEndPoint"></param>
        public static void AddNewLocalConnectionListener(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true)
        {
            lock (staticTCPConnectionLocker)
            {
                if (tcpListenerDict.ContainsKey(newLocalEndPoint))
                    throw new CommsSetupException("Provided newLocalEndPoint already exists in tcpListenerDict.");

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
                        throw new CommsSetupException("It was not possible to open port #" + newLocalEndPoint.Port + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }

                if (tcpListenerDict.ContainsKey((IPEndPoint)newListenerInstance.LocalEndpoint))
                    throw new CommsSetupException("Unable to add new TCP listenerInstance to tcpListenerDict as there is an existing entry.");
                else
                {
                    //If we were succesfull we can add the new localEndPoint to our dict
                    tcpListenerDict.Add((IPEndPoint)newListenerInstance.LocalEndpoint, newListenerInstance);
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Added new localEndPoint - " + newLocalEndPoint.Address + ":" + newLocalEndPoint.Port);
                }
            }

            TriggerIncomingConnectionWorkerThread();
        }

        /// <summary>
        /// Accept new TCP connections on specified IP's and port's
        /// </summary>
        /// <param name="localEndPoint"></param>
        public static void AddNewLocalConnectionListener(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
            try
            {
                foreach (var endPoint in localEndPoints)
                    AddNewLocalConnectionListener(endPoint, useRandomPortFailOver);
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Returns an endPoint corresponding to a possible listener on the provided ipAddress. If not listening returns null.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns></returns>
        public static IPEndPoint ExistingConnectionListener(IPAddress ipAddress)
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

        /// <summary>
        /// Start the IncomingConnectionWorker if required
        /// </summary>
        private static void TriggerIncomingConnectionWorkerThread()
        {
            lock (tcpListenerDict)
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
                            if (listener.Pending() && !shutdownNewIncomingConnectionWorker)
                            {
                                pickedUpNewConnection = true;

                                //Pick up the new connection
                                TcpClient newClient = listener.AcceptTcpClient();

                                CreateConnection(new ConnectionInfo(true, ConnectionType.TCP, (IPEndPoint)newClient.Client.RemoteEndPoint), newClient, true);
                            }
                        }

                        //We will only pause if we didnt get any new connections
                        if (!pickedUpNewConnection && !shutdownNewIncomingConnectionWorker)
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
                                NetworkComms.LogError(ex, "CommsSetupError_SE");
                            else
                                NetworkComms.LogError(ex, "CommsSetupError");
                        }
                    }
                } while (!shutdownNewIncomingConnectionWorker);
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "CriticalCommsError");
            }
            finally
            {
                //We try to close all of the tcpListeners
                CloseAndRemoveAllLocalEndPoints();

                //If we get this far we have definately stopped accepting new connections
                shutdownNewIncomingConnectionWorker = false;
            }

            //newIncomingListenThread = null;
            if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("TCP IncomingConnectionWorker thread ended.");
        }

        /// <summary>
        /// Shutdown everything TCP related
        /// </summary>
        public static void Shutdown(int threadShutdownTimeoutMS = 1000)
        {
            try
            {
                shutdownNewIncomingConnectionWorker = true;

                CloseAndRemoveAllLocalEndPoints();
                CloseAllConnections();

                //If the worker thread does not shutdown in the required time we kill it
                if (newIncomingConnectionWorker != null && !newIncomingConnectionWorker.Join(threadShutdownTimeoutMS))
                    newIncomingConnectionWorker.Abort();
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "CommsShutdownError");
            }
        }

        public static void CloseAllConnections()
        {
            throw new NotImplementedException();
        }

        private static void CloseAndRemoveAllLocalEndPoints()
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
