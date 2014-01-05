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
using DPSBase;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Global connection base class for NetworkCommsDotNet. Most user interactions happen using a connection object. Extended by <see cref="TCPConnection"/> and <see cref="UDPConnection"/>.
    /// </summary>
    public abstract partial class Connection
    {
        /// <summary>
        /// All connection listeners are recorded in the static connection base.
        /// </summary>
        static Dictionary<ConnectionType, Dictionary<IPEndPoint, ConnectionListenerBase>> listenersDict = new Dictionary<ConnectionType, Dictionary<IPEndPoint, ConnectionListenerBase>>();

        /// <summary>
        /// Start listening on all allowed local IPs, <see cref="NetworkComms.AllAllowedIPs()"/>, 
        /// or <see cref="NetworkComms.AllAllowedIPs()"/>[0] if <see cref="NetworkComms.ListenOnAllAllowedInterfaces"/> is false,
        /// on the default port for the provided <see cref="ConnectionType"/>. If the default port is
        /// unavailable will fail over to a random port.
        /// </summary>
        /// <param name="connectionType">The <see cref="ConnectionType"/> to start listening for.</param>
        public static void StartListening(ConnectionType connectionType)
        {
            StartListening(connectionType, true);
        }

        /// <summary>
        /// Start listening on all allowed local IPs, <see cref="NetworkComms.AllAllowedIPs()"/>, 
        /// or <see cref="NetworkComms.AllAllowedIPs()"/>[0] if <see cref="NetworkComms.ListenOnAllAllowedInterfaces"/> is false,
        /// on the default port for the provided <see cref="ConnectionType"/>.
        /// </summary>
        /// <param name="connectionType">The <see cref="ConnectionType"/> to start listening for.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and provided port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(ConnectionType connectionType, bool useRandomPortFailOver)
        {
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined is not a valid parameter value.", "connectionType");

            List<IPAddress> localIPs = NetworkComms.AllAllowedIPs();

            if (NetworkComms.ListenOnAllAllowedInterfaces)
            {
                //Construct the listener list
                List<ConnectionListenerBase> listeners = new List<ConnectionListenerBase>();
                for (int i = 0; i < localIPs.Count; i++)
                {
                    if (connectionType == ConnectionType.TCP)
                        listeners.Add(new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled));
                    else if (connectionType == ConnectionType.UDP)
                        listeners.Add(new UDPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPConnection.DefaultUDPOptions));
                    else
                        throw new NotImplementedException("This method has not been implemented for the provided connection type.");
                }

                try
                {
                    for (int i = 0; i < localIPs.Count; i++)
                    {
                        try
                        {
                            StartListening(listeners[i], new IPEndPoint(localIPs[i], NetworkComms.DefaultListenPort), useRandomPortFailOver);
                        }
                        catch (CommsSetupShutdownException) { }
                    }
                }
                catch (Exception)
                {
                    //If there is an exception here we remove any added listeners and then rethrow
                    for (int i = 0; i < listeners.Count; i++)
                    {
                        if (listeners[i].IsListening)
                            StopListening(listeners[i].ConnectionType, listeners[i].LocalListenIPEndPoint);
                    }

                    throw;
                }
            }
            else
            {
                ConnectionListenerBase listener;
                if (connectionType == ConnectionType.TCP)
                    listener = new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled);
                else if (connectionType == ConnectionType.UDP)
                    listener = new UDPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPConnection.DefaultUDPOptions);
                else
                    throw new NotImplementedException("This method has not been implemented for the provided connection type.");

                StartListening(listener, new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), useRandomPortFailOver);
            }
        }

        /// <summary>
        /// Start listening for new incoming connections on specified <see cref="IPEndPoint"/>s. Listener is matched
        /// with desired localIPEndPoint based on List index. Inspect listener.LocalListenIPEndPoint 
        /// when method returns to determine the <see cref="IPEndPoint"/>s used.
        /// </summary>
        /// <param name="listeners">The listeners to use.</param>
        /// <param name="desiredLocalIPEndPoints">The desired local <see cref="IPEndPoint"/>s to use for listening. Use a port number 
        /// of 0 to dynamically select a port.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and provided port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(List<ConnectionListenerBase> listeners, List<IPEndPoint> desiredLocalIPEndPoints, bool useRandomPortFailOver)
        {
            if (listeners.Count != desiredLocalIPEndPoints.Count) throw new ArgumentException("The number of elements in listeners and desiredLocalIPEndPoints must be equal.");
            if (desiredLocalIPEndPoints == null) throw new ArgumentNullException("desiredLocalIPEndPoints", "Provided List<IPEndPoint> cannot be null.");

            lock (staticConnectionLocker)
            {
                try
                {
                    for (int i = 0; i < listeners.Count; i++)
                        StartListening(listeners[i], desiredLocalIPEndPoints[i], useRandomPortFailOver);
                }
                catch (Exception)
                {
                    //If there is an exception we ensure all listeners are stopped
                    for (int i = 0; i < listeners.Count; i++)
                    {
                        if (listeners[i].IsListening)
                            StopListening(listeners[i].ConnectionType, listeners[i].LocalListenIPEndPoint);
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// Start listening for new incoming connections on specified <see cref="IPEndPoint"/>. Inspect listener.LocalListenIPEndPoint 
        /// when method returns to determine the <see cref="IPEndPoint"/> used.
        /// </summary>
        /// <param name="listener">The listener to use.</param>
        /// <param name="desiredLocalIPEndPoint">The desired local <see cref="IPEndPoint"/> to use for listening. Use a port number 
        /// of 0 to dynamically select a port.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and provided port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(ConnectionListenerBase listener, IPEndPoint desiredLocalIPEndPoint, bool useRandomPortFailOver)
        {
            #region Input Validation
            if (listener == null) throw new ArgumentNullException("listener", "Provided listener cannot be null.");
            if (desiredLocalIPEndPoint == null) throw new ArgumentNullException("desiredLocalEndPoint", "Provided desiredLocalEndPoint cannot be null.");
            if (desiredLocalIPEndPoint.Address == IPAddress.Any || desiredLocalIPEndPoint.Address == IPAddress.IPv6Any) throw new ArgumentException("desiredLocalEndPoint must specify a valid local IPAddress.", "desiredLocalEndPoint");
            #endregion

            lock (staticConnectionLocker)
            {
                if (desiredLocalIPEndPoint.Port > 0 && !useRandomPortFailOver &&
                    listenersDict.ContainsKey(listener.ConnectionType) &&
                    listenersDict[listener.ConnectionType].ContainsKey(desiredLocalIPEndPoint))
                    throw new CommsSetupShutdownException("A listener already exists for " + listener.ConnectionType.ToString() + 
                        " connections with a local IPEndPoint " + desiredLocalIPEndPoint.ToString() + ". Please try a different local IPEndPoint" +
                        " or all random port fail over.");

                //Start listening
                listener.StartListening(desiredLocalIPEndPoint, useRandomPortFailOver);

                //Idiot check
                if (!listener.IsListening) throw new CommsSetupShutdownException("Listener is not listening after calling StartListening.");

                //Idiot check
                if (listenersDict.ContainsKey(listener.ConnectionType) &&
                    listenersDict[listener.ConnectionType].ContainsKey(listener.LocalListenIPEndPoint))
                {
                    listener.StopListening();
                    throw new InvalidOperationException("According to the listener dictionary there is already a listener for " + listener.ConnectionType.ToString() +
                        " connections with a local IPEndPoint " + listener.LocalListenIPEndPoint.ToString() + ". This should not be possible.");
                }

                //Add the listener to the global dict                
                if (listenersDict.ContainsKey(listener.ConnectionType))
                {
                    if (listenersDict[listener.ConnectionType].ContainsKey(listener.LocalListenIPEndPoint))
                        throw new CommsSetupShutdownException("A listener already exists with an IPEndPoint that matches the new listener. This should not be possible.");
                    else
                        listenersDict[listener.ConnectionType].Add(listener.LocalListenIPEndPoint, listener);
                }
                else
                    listenersDict.Add(listener.ConnectionType, new Dictionary<IPEndPoint, ConnectionListenerBase>() { { listener.LocalListenIPEndPoint, listener } });
            }
        }

        /// <summary>
        /// Stops all local listeners
        /// </summary>
        public static void StopListening()
        {
            lock (staticConnectionLocker)
            {
                List<ConnectionType> connectionTypes = new List<ConnectionType>(listenersDict.Keys);
                foreach (ConnectionType connectionType in connectionTypes)
                {
                    List<IPEndPoint> ipEndPoints = new List<IPEndPoint>(listenersDict[connectionType].Keys);
                    foreach (IPEndPoint ipEndPoint in ipEndPoints)
                        StopListening(connectionType, ipEndPoint);
                }
            }
        }

        /// <summary>
        /// Stops all local listeners of the provided <see cref="ConnectionType"/>.
        /// </summary>
        /// <param name="connectionType">The <see cref="ConnectionType"/> to match.</param>
        public static void StopListening(ConnectionType connectionType)
        {
            lock (staticConnectionLocker)
            {
                if (listenersDict.ContainsKey(connectionType))
                {
                    Dictionary<IPEndPoint, ConnectionListenerBase> listenerCopy =
                        new Dictionary<IPEndPoint, ConnectionListenerBase>(listenersDict[connectionType]);

                    foreach (IPEndPoint ipEndPoint in listenerCopy.Keys)
                        StopListening(connectionType, ipEndPoint);
                }
            }
        }

        /// <summary>
        /// Stop listening for new incoming connections on specified <see cref="IPEndPoint"/> and remove it from the local listeners dictionary.
        /// </summary>
        /// <param name="connectionType">The connection type to listen for.</param>
        /// <param name="localIPEndPointToClose">The local <see cref="IPEndPoint"/> to stop listening for connections on.</param>
        public static void StopListening(ConnectionType connectionType, IPEndPoint localIPEndPointToClose)
        {
            #region Input Validation
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined is not valid when calling this method.", "connectionType");
            if (localIPEndPointToClose == null) throw new ArgumentNullException("localIPEndPointToClose", "Provided localIPEndPointToClose cannot be null.");
            if (localIPEndPointToClose.Address == IPAddress.Any || localIPEndPointToClose.Address == IPAddress.IPv6Any) throw new ArgumentException("localIPEndPointToClose must specify a valid local IPAddress.", "localIPEndPointToClose");
            if (localIPEndPointToClose.Port == 0) throw new ArgumentException("The provided localIPEndPointToClose.Port may not be 0", "localIPEndPointToClose");
            #endregion

            lock (staticConnectionLocker)
            {
                if (listenersDict.ContainsKey(connectionType) &&
                    listenersDict[connectionType].ContainsKey(localIPEndPointToClose))
                {
                    listenersDict[connectionType][localIPEndPointToClose].StopListening();
                    listenersDict[connectionType].Remove(localIPEndPointToClose);

                    if (listenersDict[connectionType].Count == 0)
                        listenersDict.Remove(connectionType);
                }
            }
        }

        /// <summary>
        /// Returns true if atleast one local listener exists.
        /// </summary>
        /// <returns></returns>
        public static bool Listening()
        {
            return ExistingLocalListenEndPoints().Count > 0;
        }

        /// <summary>
        /// Returns true if atleast one local listener of the provided <see cref="ConnectionType"/> exists.
        /// </summary>
        /// <param name="connectionType">The <see cref="ConnectionType"/> to check.</param>
        /// <returns></returns>
        public static bool Listening(ConnectionType connectionType)
        {
            return ExistingLocalListenEndPoints(connectionType).Count > 0;
        }

        /// <summary>
        /// Returns a dictionary corresponding to all current local listeners. Key is connection type, value is local IPEndPoint of listener.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<ConnectionType, List<IPEndPoint>> ExistingLocalListenEndPoints()
        {
            Dictionary<ConnectionType, List<IPEndPoint>> result = new Dictionary<ConnectionType, List<IPEndPoint>>();
            lock (staticConnectionLocker)
            {
                foreach (ConnectionType connType in listenersDict.Keys)
                {
                    if (listenersDict[connType].Count > 0)
                        result.Add(connType, new List<IPEndPoint>());

                    foreach (IPEndPoint endPoint in listenersDict[connType].Keys)
                    {
                        if (listenersDict[connType][endPoint].IsListening)
                            result[connType].Add(endPoint);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding to possible local listeners of the provided 
        /// <see cref="ConnectionType"/>. If no listeners exist returns empty list.
        /// </summary>
        /// <param name="connectionType">The connection type to match. Use ConnectionType.Undefined to match all.</param>
        /// <returns></returns>
        public static List<IPEndPoint> ExistingLocalListenEndPoints(ConnectionType connectionType)
        {
            return ExistingLocalListenEndPoints(connectionType, new IPEndPoint(IPAddress.Any, 0));
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding to possible local listeners of the provided 
        /// <see cref="ConnectionType"/> with a local IPEndPoint with matching <see cref="IPAddress"/>. 
        /// If no matching listeners exist returns empty list.
        /// </summary>
        /// <param name="connectionType">Connection type to match. Use ConnectionType.Undefined to match all.</param>
        /// <param name="ipEndPoint">The <see cref="IPEndPoint"/> to match to local listeners. Use IPAddress.Any to match all addresses. 
        /// Use port 0 to match all ports.</param>
        /// <returns></returns>
        public static List<IPEndPoint> ExistingLocalListenEndPoints(ConnectionType connectionType, IPEndPoint ipEndPoint)
        {
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined may not be used with this override. Please see others.", "connectionType");

            List<IPEndPoint> result = new List<IPEndPoint>();
            lock (staticConnectionLocker)
            {
                if (listenersDict.ContainsKey(connectionType))
                {
                    foreach (IPEndPoint endPoint in listenersDict[connectionType].Keys)
                    {
                        if ((ipEndPoint.Address == IPAddress.Any || ipEndPoint.Address == IPAddress.IPv6Any) && ipEndPoint.Port == 0)
                        {
                            //Add if listening
                            if (listenersDict[connectionType][endPoint].IsListening)
                                result.Add(endPoint);
                        }
                        else if (!(ipEndPoint.Address == IPAddress.Any || ipEndPoint.Address == IPAddress.IPv6Any) && ipEndPoint.Port == 0)
                        {
                            //Match the IP Address
                            if (endPoint.Address.Equals(ipEndPoint.Address) &&
                                listenersDict[connectionType][endPoint].IsListening)
                                result.Add(endPoint);
                        }
                        else if ((ipEndPoint.Address == IPAddress.Any || ipEndPoint.Address == IPAddress.IPv6Any) && ipEndPoint.Port > 0)
                        {
                            //Match the port
                            if (endPoint.Port == ipEndPoint.Port &&
                                listenersDict[connectionType][endPoint].IsListening)
                                result.Add(endPoint);
                        }
                        else if (endPoint.Equals(ipEndPoint) &&
                                listenersDict[connectionType][endPoint].IsListening)
                        {
                            result.Add(endPoint);
                            break;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns a list of requested existing local listeners.
        /// </summary>
        /// <typeparam name="T">Type of listener to return.</typeparam>
        /// <param name="ipEndPointToMatch">The <see cref="IPEndPoint"/> to match to local listeners. Use IPAddress.Any to match all 
        /// addresses. Use port 0 to match all ports.</param>
        /// <returns></returns>
        public static List<T> ExistingLocalListeners<T>(IPEndPoint ipEndPointToMatch) where T : ConnectionListenerBase
        {
            List<T> result = new List<T>();
            lock (staticConnectionLocker)
            {
                //Get a list of valid endPoints
                if (typeof(T) == typeof(UDPConnectionListener))
                {
                    List<IPEndPoint> IPEndPointsToUse = ExistingLocalListenEndPoints(ConnectionType.UDP, ipEndPointToMatch);
                    foreach (IPEndPoint ipEndPoint in IPEndPointsToUse)
                        result.Add((T)listenersDict[ConnectionType.UDP][ipEndPoint]);
                }
                else if (typeof(T) == typeof(TCPConnectionListener))
                {
                    List<IPEndPoint> IPEndPointsToUse = ExistingLocalListenEndPoints(ConnectionType.TCP, ipEndPointToMatch);
                    foreach (IPEndPoint ipEndPoint in IPEndPointsToUse)
                        result.Add((T)listenersDict[ConnectionType.TCP][ipEndPoint]);
                }
                else
                    throw new NotImplementedException("This method has not been implemented for provided type of " + typeof(T));
            }

            return result;
        }
    }
}
