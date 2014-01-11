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

#if NETFX_CORE
using NetworkCommsDotNet.XPlatformHelper;
#endif

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
        static Dictionary<ConnectionType, Dictionary<EndPoint, ConnectionListenerBase>> listenersDict = new Dictionary<ConnectionType, Dictionary<EndPoint, ConnectionListenerBase>>();

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
#if !NET2 && !WINDOWS_PHONE && !NETFX_CORE
                    else if (connectionType == ConnectionType.Bluetooth)
                        listeners.Add(new BluetoothConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled));
#endif
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
                            StopListening(listeners[i].ConnectionType, listeners[i].LocalListenEndPoint);
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
#if !NET2 && !WINDOWS_PHONE && !NETFX_CORE
                else if (connectionType == ConnectionType.Bluetooth)
                    listener = new BluetoothConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled);
#endif
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
        /// <param name="desiredLocalEndPoints">The desired local <see cref="IPEndPoint"/>s to use for listening. Use a port number 
        /// of 0 to dynamically select a port.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and provided port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(List<ConnectionListenerBase> listeners, List<EndPoint> desiredLocalEndPoints, bool useRandomPortFailOver)
        {
            if (listeners == null) throw new ArgumentNullException("listeners", "Provided listeners cannot be null.");
            if (desiredLocalEndPoints == null) throw new ArgumentNullException("desiredLocalIPEndPoints", "Provided List<IPEndPoint> cannot be null.");
            if (listeners.Count != desiredLocalEndPoints.Count) throw new ArgumentException("The number of elements in listeners and desiredLocalIPEndPoints must be equal.");
            
            lock (staticConnectionLocker)
            {
                try
                {
                    for (int i = 0; i < listeners.Count; i++)
                        StartListening(listeners[i], desiredLocalEndPoints[i], useRandomPortFailOver);
                }
                catch (Exception)
                {
                    //If there is an exception we ensure all listeners are stopped
                    for (int i = 0; i < listeners.Count; i++)
                    {
                        if (listeners[i].IsListening)
                            StopListening(listeners[i].ConnectionType, listeners[i].LocalListenEndPoint);
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
        /// <param name="desiredLocalEndPoint">The desired local <see cref="IPEndPoint"/> to use for listening. Use a port number 
        /// of 0 to dynamically select a port.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and provided port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(ConnectionListenerBase listener, EndPoint desiredLocalEndPoint, bool useRandomPortFailOver)
        {
            #region Input Validation
            if (listener == null) throw new ArgumentNullException("listener", "Provided listener cannot be null.");
            if (desiredLocalEndPoint == null) throw new ArgumentNullException("desiredLocalEndPoint", "Provided desiredLocalEndPoint cannot be null.");
            if (desiredLocalEndPoint.GetType() == typeof(IPEndPoint) && (((IPEndPoint)desiredLocalEndPoint).Address == IPAddress.Any || ((IPEndPoint)desiredLocalEndPoint).Address == IPAddress.IPv6Any)) throw new ArgumentException("desiredLocalEndPoint must specify a valid local IPAddress.", "desiredLocalEndPoint");
            #endregion

            lock (staticConnectionLocker)
            {
                if (!useRandomPortFailOver &&
                    listenersDict.ContainsKey(listener.ConnectionType) &&
                    listenersDict[listener.ConnectionType].ContainsKey(desiredLocalEndPoint))
                    throw new CommsSetupShutdownException("A listener already exists for " + listener.ConnectionType.ToString() + 
                        " connections with a local IPEndPoint " + desiredLocalEndPoint.ToString() + ". Please try a different local IPEndPoint" +
                        " or all random port fail over.");

                //Start listening
                listener.StartListening(desiredLocalEndPoint, useRandomPortFailOver);

                //Idiot check
                if (!listener.IsListening) throw new CommsSetupShutdownException("Listener is not listening after calling StartListening.");

                //Idiot check
                if (listenersDict.ContainsKey(listener.ConnectionType) &&
                    listenersDict[listener.ConnectionType].ContainsKey(listener.LocalListenEndPoint))
                {
                    listener.StopListening();
                    throw new InvalidOperationException("According to the listener dictionary there is already a listener for " + listener.ConnectionType.ToString() +
                        " connections with a local IPEndPoint " + listener.LocalListenEndPoint.ToString() + ". This should not be possible.");
                }

                //Add the listener to the global dict                
                if (listenersDict.ContainsKey(listener.ConnectionType))
                {
                    if (listenersDict[listener.ConnectionType].ContainsKey(listener.LocalListenEndPoint))
                        throw new CommsSetupShutdownException("A listener already exists with an IPEndPoint that matches the new listener. This should not be possible.");
                    else
                        listenersDict[listener.ConnectionType].Add(listener.LocalListenEndPoint, listener);
                }
                else
                    listenersDict.Add(listener.ConnectionType, new Dictionary<EndPoint, ConnectionListenerBase>() { { listener.LocalListenEndPoint, listener } });
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
                    List<EndPoint> endPoints = new List<EndPoint>(listenersDict[connectionType].Keys);
                    foreach (EndPoint currentEndPoint in endPoints)
                        StopListening(connectionType, currentEndPoint);
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
                    Dictionary<EndPoint, ConnectionListenerBase> listenerCopy =
                        new Dictionary<EndPoint, ConnectionListenerBase>(listenersDict[connectionType]);

                    foreach (EndPoint endPoint in listenerCopy.Keys)
                        StopListening(connectionType, endPoint);
                }
            }
        }

        /// <summary>
        /// Stop listening for new incoming connections on specified <see cref="IPEndPoint"/> and remove it from the local listeners dictionary.
        /// </summary>
        /// <param name="connectionType">The connection type to listen for.</param>
        /// <param name="localEndPointToClose">The local <see cref="IPEndPoint"/> to stop listening for connections on.</param>
        public static void StopListening(ConnectionType connectionType, EndPoint localEndPointToClose)
        {
            #region Input Validation
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined is not valid when calling this method.", "connectionType");
            if (localEndPointToClose == null) throw new ArgumentNullException("localEndPointToClose", "Provided localEndPointToClose cannot be null.");
            if (localEndPointToClose.GetType() == typeof(IPEndPoint))
            {
                IPEndPoint localIPEndPointToClose = (IPEndPoint)localEndPointToClose;
                if (localIPEndPointToClose.Address == IPAddress.Any || localIPEndPointToClose.Address == IPAddress.IPv6Any) throw new ArgumentException("localEndPointToClose must specify a valid local IPAddress.", "localEndPointToClose");
                if (localIPEndPointToClose.Port == 0) throw new ArgumentException("The provided localIPEndPointToClose.Port may not be 0", "localEndPointToClose");
            }
            #endregion

            lock (staticConnectionLocker)
            {
                if (listenersDict.ContainsKey(connectionType) &&
                    listenersDict[connectionType].ContainsKey(localEndPointToClose))
                {
                    listenersDict[connectionType][localEndPointToClose].StopListening();
                    listenersDict[connectionType].Remove(localEndPointToClose);

                    if (listenersDict[connectionType].Count == 0)
                        listenersDict.Remove(connectionType);
                }
            }
        }

        /// <summary>
        /// Returns true if at least one local listener of the provided <see cref="ConnectionType"/> exists.
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
        public static Dictionary<ConnectionType, List<EndPoint>> AllExistingLocalListenEndPoints()
        {
            Dictionary<ConnectionType, List<EndPoint>> result = new Dictionary<ConnectionType, List<EndPoint>>();
            lock (staticConnectionLocker)
            {
                foreach (ConnectionType connType in listenersDict.Keys)
                {
                    if (listenersDict[connType].Count > 0)
                        result.Add(connType, new List<EndPoint>());

                    foreach (EndPoint endPoint in listenersDict[connType].Keys)
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
        public static List<EndPoint> ExistingLocalListenEndPoints(ConnectionType connectionType)
        {
            return ExistingLocalListenEndPoints(connectionType, new IPEndPoint(IPAddress.Any, 0));
        }

        /// <summary>
        /// Returns a list of <see cref="EndPoint"/> corresponding to possible local listeners of the provided 
        /// <see cref="ConnectionType"/> with a local EndPoint with matching <see cref="IPAddress"/>. 
        /// If no matching listeners exist returns empty list.
        /// </summary>
        /// <param name="connectionType">Connection type to match. Use ConnectionType.Undefined to match all.</param>
        /// <param name="localEndPointToMatch">The <see cref="IPEndPoint"/> to match to local listeners. Use IPAddress.Any to match all addresses. 
        /// Use port 0 to match all ports.</param>
        /// <returns></returns>
        public static List<EndPoint> ExistingLocalListenEndPoints(ConnectionType connectionType, EndPoint localEndPointToMatch)
        {
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined may not be used with this override. Please see others.", "connectionType");

            IPEndPoint ipEndPointToMatch = localEndPointToMatch as IPEndPoint;

            List<EndPoint> result = new List<EndPoint>();
            lock (staticConnectionLocker)
            {
                if (listenersDict.ContainsKey(connectionType))
                {
                    foreach (EndPoint endPoint in listenersDict[connectionType].Keys)
                    {
                        IPEndPoint ipEndPoint = endPoint as IPEndPoint;
                        if (ipEndPointToMatch != null && (ipEndPointToMatch.Address == IPAddress.Any || ipEndPointToMatch.Address == IPAddress.IPv6Any) && ipEndPointToMatch.Port == 0)
                        {
                            //Add if listening
                            if (listenersDict[connectionType][ipEndPoint].IsListening)
                                result.Add(ipEndPoint);
                        }
                        else if (ipEndPointToMatch != null && !(ipEndPointToMatch.Address == IPAddress.Any || ipEndPointToMatch.Address == IPAddress.IPv6Any) && ipEndPointToMatch.Port == 0)
                        {
                            //Match the IP Address
                            if (ipEndPoint.Address.Equals(ipEndPointToMatch.Address) &&
                                listenersDict[connectionType][ipEndPoint].IsListening)
                                result.Add(ipEndPoint);
                        }
                        else if (ipEndPointToMatch != null && (ipEndPointToMatch.Address == IPAddress.Any || ipEndPointToMatch.Address == IPAddress.IPv6Any) && ipEndPointToMatch.Port > 0)
                        {
                            //Match the port
                            if (ipEndPoint.Port == ipEndPointToMatch.Port &&
                                listenersDict[connectionType][ipEndPoint].IsListening)
                                result.Add(ipEndPoint);
                        }
                        else if (endPoint.Equals(localEndPointToMatch) &&
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
        /// <param name="endPointToMatch">The <see cref="IPEndPoint"/> to match to local listeners. Use IPAddress.Any to match all 
        /// addresses. Use port 0 to match all ports.</param>
        /// <returns></returns>
        public static List<T> ExistingLocalListeners<T>(EndPoint endPointToMatch) where T : ConnectionListenerBase
        {
            List<T> result = new List<T>();
            lock (staticConnectionLocker)
            {
                //Get a list of valid endPoints
                if (typeof(T) == typeof(UDPConnectionListener))
                {
                    List<EndPoint> endPointsToUse = ExistingLocalListenEndPoints(ConnectionType.UDP, endPointToMatch);
                    foreach (EndPoint endPoint in endPointsToUse)
                        result.Add((T)listenersDict[ConnectionType.UDP][endPoint]);
                }
                else if (typeof(T) == typeof(TCPConnectionListener))
                {
                    List<EndPoint> endPointsToUse = ExistingLocalListenEndPoints(ConnectionType.TCP, endPointToMatch);
                    foreach (EndPoint endPoint in endPointsToUse)
                        result.Add((T)listenersDict[ConnectionType.TCP][endPoint]);
                }
                else
                    throw new NotImplementedException("This method has not been implemented for provided type of " + typeof(T));
            }

            return result;
        }
    }
}
