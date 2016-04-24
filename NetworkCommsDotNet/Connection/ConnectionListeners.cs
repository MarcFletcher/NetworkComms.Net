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

#if NET35 || NET4
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using NetworkCommsDotNet.Connections.Bluetooth;
#endif

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#endif

namespace NetworkCommsDotNet.Connections
{
    public abstract partial class Connection
    {
        /// <summary>
        /// All connection listeners are recorded in the static connection base.
        /// </summary>
        static Dictionary<ConnectionType, Dictionary<EndPoint, ConnectionListenerBase>> listenersDict = new Dictionary<ConnectionType, Dictionary<EndPoint, ConnectionListenerBase>>();

        /// <summary>
        /// Start listening for incoming connections of the provided <see cref="ConnectionType"/>. 
        /// If the desired localEndPoint is not available will throw a CommsSetupShutdownException.
        /// </summary>
        /// <param name="connectionType">The <see cref="ConnectionType"/> to start listening for.</param>
        /// <param name="desiredLocalEndPoint">The desired localEndPoint. For IPEndPoints use IPAddress.Any 
        /// to listen on all <see cref="HostInfo.IP.FilteredLocalAddresses()"/> and port 0 to randomly select an available port.</param>
        /// <param name="allowDiscoverable">Determines if the listeners created will be discoverable if <see cref="Tools.PeerDiscovery"/> is enabled.</param>
        /// <returns>A list of all listeners used.</returns>
        public static List<ConnectionListenerBase> StartListening<T>(ConnectionType connectionType, T desiredLocalEndPoint, bool allowDiscoverable = false) where T : EndPoint
        {
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined is not a valid parameter value.", "connectionType");
            if (desiredLocalEndPoint == null) throw new ArgumentNullException("desiredLocalEndPoint", "desiredLocalEndPoint cannot be null.");

            List<ConnectionListenerBase> listeners = new List<ConnectionListenerBase>();

            if (connectionType == ConnectionType.TCP || connectionType == ConnectionType.UDP)
            {
                IPEndPoint desiredLocalIPEndPoint = desiredLocalEndPoint as IPEndPoint;
                if (desiredLocalIPEndPoint == null)
                    throw new ArgumentException("The provided desiredLocalEndPoint must be an IPEndPoint for TCP and UDP connection types.", "desiredLocalEndPoint");

                //Collect a list of IPEndPoints we want to listen on
                List<IPEndPoint> localListenIPEndPoints = new List<IPEndPoint>();

                if (desiredLocalIPEndPoint.Address == IPAddress.Any)
                {
                    foreach(IPAddress address in HostInfo.IP.FilteredLocalAddresses())
                        localListenIPEndPoints.Add(new IPEndPoint(address, desiredLocalIPEndPoint.Port));

                    //We could also listen on the IPAddress.Any adaptor here
                    //but it is not supported by all platforms so use the specific start listening override instead
                }
                else
                    localListenIPEndPoints.Add(desiredLocalIPEndPoint);

                //Initialise the listener list
                for (int i = 0; i < localListenIPEndPoints.Count; i++)
                {
                    if (connectionType == ConnectionType.TCP)
                        listeners.Add(new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, allowDiscoverable));
                    else if (connectionType == ConnectionType.UDP)
                        listeners.Add(new UDPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPConnection.DefaultUDPOptions, allowDiscoverable));
                }

                //Start listening on all selected listeners
                //We do this is a separate step in case there is an exception
                try
                {
                    for (int i = 0; i < localListenIPEndPoints.Count; i++)
                            StartListening(listeners[i], localListenIPEndPoints[i], false);
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
#if NET35 || NET4
            else if (connectionType == ConnectionType.Bluetooth)
            {
                BluetoothEndPoint desiredLocalBTEndPoint = desiredLocalEndPoint as BluetoothEndPoint;
                if (desiredLocalBTEndPoint == null)
                    throw new ArgumentException("The provided desiredLocalEndPoint must be a BluetoothEndPoint for Bluetooth connection types.", "desiredLocalEndPoint");

                //Collect a list of BluetoothEndPoints we want to listen on
                List<BluetoothEndPoint> localListenBTEndPoints = new List<BluetoothEndPoint>();
                if (desiredLocalBTEndPoint.Address == BluetoothAddress.None)
                {
                    foreach (var address in HostInfo.BT.FilteredLocalAddresses())
                        localListenBTEndPoints.Add(new BluetoothEndPoint(address, desiredLocalBTEndPoint.Service));
                }
                else
                    localListenBTEndPoints.Add(desiredLocalBTEndPoint);

                //Initialise the listener list
                for (int i = 0; i < localListenBTEndPoints.Count; i++)
                    listeners.Add(new BluetoothConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, allowDiscoverable));

                //Start listening on all selected listeners
                //We do this is a separate step in case there is an exception
                try
                {
                    for (int i = 0; i < localListenBTEndPoints.Count; i++)
                        StartListening(listeners[i], localListenBTEndPoints[i], false);
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
#endif
            else
                throw new NotImplementedException("This method has not been implemented for the provided connections of type " + connectionType);

            return listeners;
        }

        /// <summary>
        /// Start listening for new incoming connections on specified <see cref="IPEndPoint"/>. Inspect listener.LocalListenIPEndPoint 
        /// when method returns to determine the <see cref="IPEndPoint"/> used.
        /// </summary>
        /// <param name="listener">The listener to use.</param>
        /// <param name="desiredLocalEndPoint">The desired local <see cref="IPEndPoint"/> to use for listening. IPAddress.Any corresponds with listening on
        /// 0.0.0.0. Use a port number of 0 to dynamically select a port.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and provided port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening<T>(ConnectionListenerBase listener, T desiredLocalEndPoint, bool useRandomPortFailOver = false) where T : EndPoint
        {
            #region Input Validation
            if (listener == null) throw new ArgumentNullException("listener", "Provided listener cannot be null.");
            if (desiredLocalEndPoint == null) throw new ArgumentNullException("desiredLocalEndPoint", "Provided desiredLocalEndPoint cannot be null.");

            //Commented out as listening on IPAddress.Any does have specific usage cases, such as receiving UDP broadcast on iOS
            //if (desiredLocalEndPoint.GetType() == typeof(IPEndPoint) && ((desiredLocalEndPoint as IPEndPoint).Address == IPAddress.Any || (desiredLocalEndPoint as IPEndPoint).Address == IPAddress.IPv6Any)) throw new ArgumentException("desiredLocalEndPoint must specify a valid local IPAddress.", "desiredLocalEndPoint");

#if NET35 || NET4
            if (desiredLocalEndPoint is BluetoothEndPoint && (desiredLocalEndPoint as BluetoothEndPoint).Address == BluetoothAddress.None) throw new ArgumentException("desiredLocalEndPoint must specify a valid local Bluetooth Address.", "desiredLocalEndPoint");
#endif
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

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("Listener started (" + listener.ConnectionType.ToString() + "-" + (listener.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? "E" : "D") + " - " + listener.LocalListenEndPoint.ToString() + ").");

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
        /// Start listening for new incoming connections on specified <see cref="IPEndPoint"/>s. Listener is matched
        /// with desired localIPEndPoint based on List index. Inspect listener.LocalListenIPEndPoint 
        /// when method returns to determine the <see cref="IPEndPoint"/>s used.
        /// </summary>
        /// <param name="listeners">The listeners to use.</param>
        /// <param name="desiredLocalEndPoints">The desired local <see cref="IPEndPoint"/>s to use for listening. IPAddress.Any corresponds with listening on
        /// 0.0.0.0. Use a port number of 0 to dynamically select a port.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and provided port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening<T>(List<ConnectionListenerBase> listeners, List<T> desiredLocalEndPoints, bool useRandomPortFailOver) where T : EndPoint
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
        /// <param name="connectionType">The <see cref="ConnectionType"/> to close.</param>
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
        /// Stop listening for new incoming connections on specified <see cref="ConnectionListenerBase"/> and remove it from the local listeners dictionary.
        /// </summary>
        /// <param name="listener">The listener which should stop listening.</param>
        public static void StopListening(ConnectionListenerBase listener)
        {
            if (listener == null) throw new ArgumentNullException("listener", "Provided listener cannot be null.");

            StopListening(listener.ConnectionType, listener.LocalListenEndPoint);
        }

        /// <summary>
        /// Stop listening for new incoming connections on specified list of <see cref="ConnectionListenerBase"/> and remove them from the local listeners dictionary.
        /// </summary>
        /// <param name="listeners">The listeners which should stop listening</param>
        public static void StopListening(List<ConnectionListenerBase> listeners)
        {
            if (listeners == null) throw new ArgumentNullException("listeners", "Provided listener cannot be null.");

            foreach(ConnectionListenerBase listener in listeners)
                StopListening(listener);
        }

        /// <summary>
        /// Stop listening for new incoming connections on specified <see cref="EndPoint"/> and remove it from the local listeners dictionary.
        /// </summary>
        /// <param name="connectionType">The connection type to stop listening on.</param>
        /// <param name="localEndPointToClose">The local <see cref="EndPoint"/> to stop listening for connections on.</param>
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
                    if (NetworkComms.LoggingEnabled)
                    {
                        if (listenersDict[connectionType][localEndPointToClose].IsListening)
                            NetworkComms.Logger.Info("Listener stopping (" + listenersDict[connectionType][localEndPointToClose].ConnectionType.ToString() + "-" + (listenersDict[connectionType][localEndPointToClose].ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? "E" : "D") + " - " + listenersDict[connectionType][localEndPointToClose].LocalListenEndPoint.ToString() + ").");
                        else
                            NetworkComms.Logger.Info("Listener stopping (" + listenersDict[connectionType][localEndPointToClose].ConnectionType.ToString() + "-" + (listenersDict[connectionType][localEndPointToClose].ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled ? "E" : "D") + " - Not listening when stopped).");
                    }

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
        /// Returns a dictionary corresponding to all current local listeners. Key is connection type, value is local EndPoint of listener.
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
        /// Returns a list of <see cref="EndPoint"/>s corresponding to possible local listeners of the provided 
        /// <see cref="ConnectionType"/>. If no listeners exist returns empty list.
        /// </summary>
        /// <param name="connectionType">The connection type to match. Use ConnectionType.Undefined to match all.</param>
        /// <returns></returns>
        public static List<EndPoint> ExistingLocalListenEndPoints(ConnectionType connectionType)
        {
            if (connectionType == ConnectionType.TCP || connectionType == ConnectionType.UDP)
                return ExistingLocalListenEndPoints(connectionType, new IPEndPoint(IPAddress.Any, 0));
#if NET35 || NET4
            else if (connectionType == ConnectionType.Bluetooth)
                return ExistingLocalListenEndPoints(connectionType, new BluetoothEndPoint(BluetoothAddress.None, BluetoothService.Empty));
#endif
            else
                throw new ArgumentException("Method ExistingLocalListenEndPoints is not defined for the connection type: " + connectionType.ToString(), "connectionType");
        }

        /// <summary>
        /// Returns a list of <see cref="EndPoint"/> corresponding to possible local listeners of the provided 
        /// <see cref="ConnectionType"/> with a local EndPoint with matching <see cref="IPAddress"/>. 
        /// If no matching listeners exist returns empty list.
        /// </summary>
        /// <param name="connectionType">Connection type to match. Use ConnectionType.Undefined to match all.</param>
        /// <param name="localEndPointToMatch">The <see cref="IPEndPoint"/> to match to local listeners. Use IPAddress.Any 
        /// to match all addresses. Use port 0 to match all ports.</param>
        /// <returns></returns>
        public static List<EndPoint> ExistingLocalListenEndPoints(ConnectionType connectionType, EndPoint localEndPointToMatch)
        {
            if (connectionType == ConnectionType.Undefined) throw new ArgumentException("ConnectionType.Undefined may not be used with this override. Please see others.", "connectionType");
            if (localEndPointToMatch == null) throw new ArgumentNullException("localEndPointToMatch");

#if NET4 || NET35
            if (connectionType == ConnectionType.Bluetooth)
            {
                InTheHand.Net.BluetoothEndPoint btEndPointToMatch = localEndPointToMatch as InTheHand.Net.BluetoothEndPoint;

                if (btEndPointToMatch == null)
                    throw new ArgumentException("Local endpoint must be a BluetoothEndPoint for Bluetooth connection type", "localEndPointToMatch");

                List<EndPoint> btResult = new List<EndPoint>();
                lock (staticConnectionLocker)
                {
                    if (listenersDict.ContainsKey(connectionType))
                    {
                        foreach (EndPoint endPoint in listenersDict[connectionType].Keys)
                        {
                            var btEndPoint = endPoint as InTheHand.Net.BluetoothEndPoint;
                            if (btEndPointToMatch.Address == BluetoothAddress.None && btEndPointToMatch.Service == BluetoothService.Empty)
                            {
                                btResult.Add(btEndPoint);
                            }
                            else if (btEndPointToMatch.Address != BluetoothAddress.None && btEndPointToMatch.Service == BluetoothService.Empty)
                            {
                                //Match the address
                                if (btEndPoint.Address.Equals(btEndPointToMatch.Address) &&
                                    listenersDict[connectionType][btEndPoint].IsListening)
                                    btResult.Add(btEndPoint);
                            }
                            else if (btEndPointToMatch.Address == BluetoothAddress.None && btEndPointToMatch.Service != BluetoothService.Empty)
                            {
                                //Match the service
                                if (btEndPoint.Service.Equals(btEndPointToMatch.Service) &&
                                    listenersDict[connectionType][btEndPoint].IsListening)
                                    btResult.Add(btEndPoint);
                            }
                            else if (endPoint.Equals(localEndPointToMatch) && listenersDict[connectionType][endPoint].IsListening)
                            {
                                btResult.Add(endPoint);
                                break;
                            }
                        }
                    }
                }

                return btResult;
            }
#endif

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
                        else if (endPoint.Equals(localEndPointToMatch) && listenersDict[connectionType][endPoint].IsListening)
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
        /// <typeparam name="listenerType">Type of listener to return.</typeparam>
        /// <param name="endPointToMatch">The <see cref="EndPoint"/> to match to local listeners. Use IPAddress.Any to match all 
        /// addresses. Use port 0 to match all ports.</param>
        /// <returns></returns>
        public static List<listenerType> ExistingLocalListeners<listenerType>(EndPoint endPointToMatch) where listenerType : ConnectionListenerBase
        {
            List<listenerType> result = new List<listenerType>();
            lock (staticConnectionLocker)
            {
                //Get a list of valid endPoints
                if (typeof(listenerType) == typeof(UDPConnectionListener))
                {
                    List<EndPoint> endPointsToUse = ExistingLocalListenEndPoints(ConnectionType.UDP, endPointToMatch);
                    foreach (EndPoint endPoint in endPointsToUse)
                        result.Add((listenerType)listenersDict[ConnectionType.UDP][endPoint]);
                }
                else if (typeof(listenerType) == typeof(TCPConnectionListener))
                {
                    List<EndPoint> endPointsToUse = ExistingLocalListenEndPoints(ConnectionType.TCP, endPointToMatch);
                    foreach (EndPoint endPoint in endPointsToUse)
                        result.Add((listenerType)listenersDict[ConnectionType.TCP][endPoint]);
                }
#if NET35 || NET4
                else if (typeof(listenerType) == typeof(BluetoothConnectionListener))
                {
                    List<EndPoint> endPointsToUse = ExistingLocalListenEndPoints(ConnectionType.Bluetooth, endPointToMatch);
                    foreach (EndPoint endPoint in endPointsToUse)
                        result.Add((listenerType)listenersDict[ConnectionType.Bluetooth][endPoint]);
                }
#endif
                else
                    throw new NotImplementedException("This method has not been implemented for provided type of " + typeof(listenerType));
            }

            return result;
        }

        /// <summary>
        /// Returns a list of all local listeners
        /// </summary>
        /// <returns>A list of all local listeners</returns>
        public static List<ConnectionListenerBase> AllExistingLocalListeners()
        {
            List<ConnectionListenerBase> result = new List<ConnectionListenerBase>();
            lock (staticConnectionLocker)
            {
				//Dictionary<ConnectionType, List<EndPoint>> endPoints = AllExistingLocalListenEndPoints();
                
                foreach (var byConnectionType in listenersDict)
                    foreach (var byEndPoint in byConnectionType.Value)
                        result.Add(byEndPoint.Value);
            }

            return result;
        }
    }
}
