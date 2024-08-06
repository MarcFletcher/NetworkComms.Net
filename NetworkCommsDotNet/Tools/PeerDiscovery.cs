﻿// 
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
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.IO;

using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;

#if NET35 || NET4
using InTheHand.Net.Sockets;
using InTheHand.Net.Bluetooth;
using InTheHand.Net;
using InTheHand.Net.Bluetooth.AttributeIds;
using NetworkCommsDotNet.Connections.Bluetooth;
#endif

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Provides the ability to discover 'discoverable' peers on the local network.
    /// </summary>
    public static class PeerDiscovery
    {
        /// <summary>
        /// Encapsulates the different local listen modes. If the default mode does
        /// not work please try the others.
        /// </summary>
        public enum LocalListenMode
        {
            /// <summary>
            /// Listens independently on each available adaptor for broadcast packets. This
            /// is the default mode and works on all native .net platforms.
            /// </summary>
            EachAdaptorIndependently,

            /// <summary>
            /// Listens only on 0.0.0.0. This is required by some *nix/mobile systems.
            /// </summary>
            OnlyZeroAdaptor,

            /// <summary>
            /// Listens on all adaptors independently, as well as 0.0.0.0. Included only for ]
            /// debugging purposes.
            /// </summary>
            Both,
        }

        /// <summary>
        /// Encapsulates the different peer discovery methods available.
        /// </summary>
        public enum DiscoveryMethod
        {
            /// <summary>
            /// Peer discovery using a UDP broadcast. Strongly recommended for IP networks.
            /// </summary>
            UDPBroadcast,

            /// <summary>
            /// Peer discovery using the bluetooth SDP protocol.
            /// </summary>
            BluetoothSDP,

            /// <summary>
            /// Peer discovery using a TCP port scan. Very slow and adversely affects performance on the local network. 
            /// Should only be used with network configurations where UDP broadcast is unsuccessful. Only IPv4 networks
            /// are included in a TCP Port scan.
            /// </summary>
            TCPPortScan,
        }

        /// <summary>
        /// Used to communicate discoverable listeners between peers
        /// </summary>
        private class PeerListenerEndPoint
        {
            public ConnectionType ConnectionType { get; private set; }
            public EndPoint EndPoint { get; private set; }

            public IPEndPoint IPEndPoint { get { return EndPoint as IPEndPoint; } }

#if NET35 || NET4
            public BluetoothEndPoint BTEndPoint { get { return EndPoint as BluetoothEndPoint; } }
#endif

            public PeerListenerEndPoint(ConnectionType connectionType, EndPoint endPoint)
            {
                if (connectionType == Connections.ConnectionType.Undefined)
                    throw new ArgumentException("Cannot create a PeerEndPoint if connectioType is Undefined","connectionType");

                if ((connectionType == Connections.ConnectionType.TCP || connectionType == Connections.ConnectionType.UDP) && !(endPoint is IPEndPoint))
                    throw new ArgumentException("If connection type is of an IP type then the provided endPoint must be an IPEndPoint", "endPoint");

#if NET35 || NET4
                if(connectionType == Connections.ConnectionType.Bluetooth && !(endPoint is BluetoothEndPoint))
                    throw new ArgumentException("If connection type is Bluetooth type then the provided endPoint must be a BluetoothEndPoint", "endPoint");
#endif

                this.ConnectionType = connectionType;
                this.EndPoint = endPoint;
            }

            private PeerListenerEndPoint(){}

            public byte[] Serialize()
            {
                byte[] result = null;

                if (ConnectionType == Connections.ConnectionType.UDP || ConnectionType == Connections.ConnectionType.TCP)
                {
                    var type = BitConverter.GetBytes((int)ConnectionType);
                    var address = IPEndPoint.Address.GetAddressBytes();
                    var addressLength = BitConverter.GetBytes(address.Length);
                    var port = BitConverter.GetBytes(IPEndPoint.Port);

                    int offset = 0;
                    result = new byte[type.Length + addressLength.Length + address.Length + port.Length];
                    Buffer.BlockCopy(type, 0, result, offset, type.Length); offset += type.Length;
                    Buffer.BlockCopy(addressLength, 0, result, offset, addressLength.Length); offset += addressLength.Length;
                    Buffer.BlockCopy(address, 0, result, offset, address.Length); offset += address.Length;
                    Buffer.BlockCopy(port, 0, result, offset, port.Length); offset += port.Length;
                }
#if NET35 || NET4
                else if (ConnectionType == Connections.ConnectionType.Bluetooth)
                {
                    var type = BitConverter.GetBytes((int)ConnectionType);
                    var address = BTEndPoint.Address.ToByteArray();
                    var port = BTEndPoint.Service.ToByteArray();

                    int offset = 0;
                    result = new byte[type.Length + address.Length + port.Length];
                    Buffer.BlockCopy(type, 0, result, offset, type.Length); offset += type.Length;
                    Buffer.BlockCopy(address, 0, result, offset, address.Length); offset += address.Length;
                    Buffer.BlockCopy(port, 0, result, offset, port.Length); offset += port.Length;
                }
#endif
                else
                    throw new Exception();

                return result;
            }

            public static PeerListenerEndPoint Deserialize(byte[] data)
            {
                PeerListenerEndPoint result = new PeerListenerEndPoint();

                int offset = 0;
                result.ConnectionType = (Connections.ConnectionType)BitConverter.ToInt32(data, offset); offset += sizeof(int);

                if (result.ConnectionType == ConnectionType.TCP || result.ConnectionType == ConnectionType.UDP)
                {
                    byte[] address = new byte[BitConverter.ToInt32(data, offset)]; offset += sizeof(int);
                    Buffer.BlockCopy(data, offset, address, 0, address.Length); offset += address.Length;
                    int port = BitConverter.ToInt32(data, offset); offset += sizeof(int);

                    result.EndPoint = new IPEndPoint(new IPAddress(address), port);
                }
#if NET35 || NET4
                else if (result.ConnectionType == ConnectionType.Bluetooth)
                {
                    byte[] address = new byte[8];
                    Buffer.BlockCopy(data, offset, address, 0, address.Length); offset += address.Length;
                    byte[] service = new byte[16];
                    Buffer.BlockCopy(data, offset, service, 0, service.Length); offset += service.Length;

                    result.EndPoint = new BluetoothEndPoint(new BluetoothAddress(address), new Guid(service));
                }
#endif
                else
                    throw new Exception();

                return result;
            }
        }

        #region Public Properties
        /// <summary>
        /// The wait time in milliseconds before all peers discovered are returned for synchronous discovery methods. Default 2000ms.
        /// </summary>
        public static int DefaultDiscoverTimeMS { get; set; }

        /// <summary>
        /// The minimum port number that will be used when making this peer discoverable. Default 10000.
        /// </summary>
        public static int MinTargetLocalIPPort { get; set; }

        /// <summary>
        /// The maximum port number that will be used when making this peer discoverable. Default 10020.
        /// </summary>
        public static int MaxTargetLocalIPPort { get; set; }

        /// <summary>
        /// The mode to use when listening for incoming peer discovery packets. Some systems require
        /// one mode whilst on others this will throw exceptions. If you have problems please
        /// use a different mode. Default is LocalListenMode.EachAdaptorIndependently
        /// </summary>
        public static LocalListenMode ListenMode { get; set; }

        /// <summary>
        /// Backing field for DefaultIPDiscoveryMethod
        /// </summary>
        private static DiscoveryMethod _defaultIPDiscoveryMethod = DiscoveryMethod.UDPBroadcast;
        /// <summary>
        /// The default discovery method to use for IP type connections (UDP and TCP). By default this is DiscoveryMethod.UDPBroadcast.
        /// </summary>
        public static DiscoveryMethod DefaultIPDiscoveryMethod
        {
            get
            {
                return _defaultIPDiscoveryMethod;
            }
            set
            {
                if (value == DiscoveryMethod.UDPBroadcast || value == DiscoveryMethod.TCPPortScan)
                    _defaultIPDiscoveryMethod = value;
                else
                    throw new ArgumentException("DefaultIPDiscoveryMethod must be either DiscoveryMethod.UDPBroadcast or DiscoveryMethod.TCPPortScan", "DefaultIPDiscoveryMethod");
            }
        }

        /// <summary>
        /// The service on which discovery will run for bluetooth. Default is '3a768eea-cbda-4926-a82d-831cb89092ac'.
        /// </summary>
        public static Guid BluetoothDiscoveryService { get; set; }

        /// <summary>
        /// The event delegate which can optionally be used when a peer is successfully discovered.
        /// </summary>
        /// <param name="peerIdentifier">The ShortGuid corresponding with the discovered peers network identifier</param>
        /// <param name="discoveredListenerEndPoints">The discovered listener endPoints for the discovered peer</param>
        public delegate void PeerDiscoveredHandler(ShortGuid peerIdentifier, Dictionary<ConnectionType, List<EndPoint>> discoveredListenerEndPoints);

        /// <summary>
        /// Event triggered when a peer is discovered.
        /// </summary>
        public static event PeerDiscoveredHandler OnPeerDiscovered;
        #endregion

        #region Private Properties
        /// <summary>
        /// A private object to ensure thread safety
        /// </summary>
        private static object _syncRoot = new object();

        /// <summary>
        /// A private object used to prevent parallel discovery requests being made
        /// </summary>
        private static object _discoverSyncRoot = new object();

        /// <summary>
        /// The packet type string used for peer discovery
        /// </summary>
        private static string discoveryPacketType = "PeerDiscovery";

        /// <summary>
        /// Local listeners specifically associated with this peers discover feature
        /// </summary>
        private static Dictionary<DiscoveryMethod, List<ConnectionListenerBase>> _discoveryListeners = new Dictionary<DiscoveryMethod, List<ConnectionListenerBase>>();

        /// <summary>
        /// Dictionary which records discovered peers
        /// </summary>
        private static Dictionary<ShortGuid, Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>>> _discoveredPeers = new Dictionary<ShortGuid, Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>>>();

        /// <summary>
        /// A custom thread pool for performing a TCPPortScan
        /// </summary>
        private static CommsThreadPool _tcpPortScanThreadPool = new CommsThreadPool(0, Environment.ProcessorCount * 60, Environment.ProcessorCount * 60, new TimeSpan(0, 0, 5));
        #endregion

        static PeerDiscovery()
        {
            DefaultDiscoverTimeMS = 2000;

            MinTargetLocalIPPort = 10000;
            MaxTargetLocalIPPort = 10020;

            BluetoothDiscoveryService = new Guid("3a768eea-cbda-4926-a82d-831cb89092ac");

            ListenMode = LocalListenMode.EachAdaptorIndependently;
        }

        #region Local Configuration
        /// <summary>
        /// Make this peer discoverable using the provided <see cref="DiscoveryMethod"/>. 
        /// Uses all suitable and allowed adaptors, e.g. for IP networks uses <see cref="HostInfo.IP.FilteredLocalAddresses()"/>.
        /// IMPORTANT NOTE: For IP networks we strongly recommend using the UDP broadcast discovery method.
        /// </summary>
        /// <param name="discoveryMethod"></param>
        public static void EnableDiscoverable(DiscoveryMethod discoveryMethod)
        {
            lock (_syncRoot)
            {
                if (_discoveryListeners.ContainsKey(discoveryMethod))
                    return;

                //Based on the connection type select all local endPoints and then enable discoverable
                if (discoveryMethod == DiscoveryMethod.UDPBroadcast || discoveryMethod == DiscoveryMethod.TCPPortScan)
                {
                    //We should select one of the target points across all adaptors, no need for all adaptors to have
                    //selected a single uniform port which is what happens if we just pass IPAddress.Any to the StartListening method
                    List<IPAddress> localAddresses = new List<IPAddress>();

                    if (ListenMode == LocalListenMode.EachAdaptorIndependently || ListenMode == LocalListenMode.Both)
                        localAddresses.AddRange(HostInfo.IP.FilteredLocalAddresses());

                    if (ListenMode == LocalListenMode.OnlyZeroAdaptor || ListenMode == LocalListenMode.Both)
                        localAddresses.Add(IPAddress.Any);

                    List<ConnectionListenerBase> listeners = new List<ConnectionListenerBase>();
                    foreach (IPAddress address in localAddresses)
                    {
                        //Keep trying to listen on an ever increasing port number
                        for (int tryPort = MinTargetLocalIPPort; tryPort <= MaxTargetLocalIPPort; tryPort++)
                        {
                            try
                            {
                                ConnectionListenerBase listener;
                                if (discoveryMethod == DiscoveryMethod.UDPBroadcast)
                                    listener = new UDPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPOptions.None, true);
                                else
                                    listener = new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, true);

                                Connection.StartListening(listener, new IPEndPoint(address, tryPort));

                                //Once we are successfully listening we can break
                                listeners.Add(listener);
                                break;
                            }
                            catch (Exception) { }

                            if (tryPort == MaxTargetLocalIPPort)
                                throw new CommsSetupShutdownException("Failed to find local available listen port on address " + address.ToString() + " while trying to make this peer discoverable. Consider increasing the available port range via MinTargetLocalIPPort and MaxTargetLocalIPPort.");
                        }
                    }

                    _discoveryListeners.Add(discoveryMethod, listeners);
                }
#if NET35 || NET4
                else if (discoveryMethod == DiscoveryMethod.BluetoothSDP)
                {
                    List<ConnectionListenerBase> listeners = new List<ConnectionListenerBase>();

                    foreach (BluetoothRadio radio in BluetoothRadio.AllRadios)
                    {
                        radio.Mode = RadioMode.Discoverable;
                        listeners.AddRange(Connection.StartListening(ConnectionType.Bluetooth, new BluetoothEndPoint(radio.LocalAddress, BluetoothDiscoveryService), true));
                    }
                    
                    _discoveryListeners.Add(discoveryMethod, listeners);
                }
#endif
                else
                    throw new NotImplementedException("The requested discovery method has not been implemented on the current platform.");
                
                //Add the packet handlers if required
                foreach (var byMethodPair in _discoveryListeners)
                {
                    foreach(ConnectionListenerBase listener in byMethodPair.Value)
                    {
                        if (!listener.IncomingPacketHandlerExists(discoveryPacketType, new NetworkComms.PacketHandlerCallBackDelegate<byte[]>(PeerDiscoveryHandler)))
                            listener.AppendIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
                    }
                }
                    
            }
        }

        /// <summary>
        /// Make this peer discoverable using the provided <see cref="DiscoveryMethod"/>. 
        /// IMPORTANT NOTE: For IP networks we strongly recommend using the UDP broadcast discovery method.
        /// </summary>
        /// <param name="discoveryMethod">The discovery method for which this peer should be discoverable</param>
        /// <param name="localDiscoveryEndPoint">The local endpoint with which to make this peer discoverable</param>
        public static void EnableDiscoverable(DiscoveryMethod discoveryMethod, EndPoint localDiscoveryEndPoint)
        {

            if (discoveryMethod == DiscoveryMethod.UDPBroadcast || discoveryMethod == DiscoveryMethod.TCPPortScan)
            {
                lock (_syncRoot)
                {
                    if (_discoveryListeners.ContainsKey(discoveryMethod))
                        return;

                    if (!HostInfo.IP.FilteredLocalAddresses().Contains((localDiscoveryEndPoint as IPEndPoint).Address))
                        throw new ArgumentException("Provided endpoint must use a valid local address", "localDiscoveryEndPoint");

                    if ((localDiscoveryEndPoint as IPEndPoint).Port == 0)
                    {
                        IPAddress address = (localDiscoveryEndPoint as IPEndPoint).Address;

                        //Keep trying to listen on an ever increasing port number
                        for (int tryPort = MinTargetLocalIPPort; tryPort <= MaxTargetLocalIPPort; tryPort++)
                        {
                            try
                            {
                                ConnectionListenerBase listener;
                                if (discoveryMethod == DiscoveryMethod.UDPBroadcast)
                                    listener = new UDPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPOptions.None, true);
                                else
                                    listener = new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, true);

                                Connection.StartListening(listener, new IPEndPoint(address, tryPort));

                                //Once we are successfully listening we can break
                                _discoveryListeners.Add(discoveryMethod, new List<ConnectionListenerBase>() { listener });
                                break;
                            }
                            catch (Exception) { }

                            if (tryPort == MaxTargetLocalIPPort)
                                throw new CommsSetupShutdownException("Failed to find local available listen port on address " + address.ToString() + " while trying to make this peer discoverable. Consider increasing the available port range via MinTargetLocalIPPort and MaxTargetLocalIPPort.");
                        }
                    }
                    else
                    {
                        //Based on the connection type select all local endPoints and then enable discoverable
                        ConnectionListenerBase listener;
                        if (discoveryMethod == DiscoveryMethod.UDPBroadcast)
                            listener = new UDPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPOptions.None, true);
                        else
                            listener = new TCPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, true);

                        Connection.StartListening(listener, localDiscoveryEndPoint);

                        //Once we are successfully listening we can break
                        _discoveryListeners.Add(discoveryMethod, new List<ConnectionListenerBase>() { listener });
                    }

                    //Add the packet handlers if required
                    foreach (var byMethodPair in _discoveryListeners)
                    {
                        foreach (ConnectionListenerBase listener in byMethodPair.Value)
                        {
                            if (!listener.IncomingPacketHandlerExists(discoveryPacketType, new NetworkComms.PacketHandlerCallBackDelegate<byte[]>(PeerDiscoveryHandler)))
                                listener.AppendIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
                        }
                    }
                }
            }
#if NET35 || NET4
            else if (discoveryMethod == DiscoveryMethod.BluetoothSDP)
            {
                lock (_syncRoot)
                {
                    foreach (BluetoothRadio radio in BluetoothRadio.AllRadios)
                        if (radio.LocalAddress == (localDiscoveryEndPoint as BluetoothEndPoint).Address)
                            radio.Mode = RadioMode.Discoverable;

                    _discoveryListeners.Add(discoveryMethod, Connection.StartListening(ConnectionType.Bluetooth, localDiscoveryEndPoint, true));

                    //Add the packet handlers if required
                    foreach (var byMethodPair in _discoveryListeners)
                    {
                        foreach (ConnectionListenerBase listener in byMethodPair.Value)
                        {
                            if (!listener.IncomingPacketHandlerExists(discoveryPacketType, new NetworkComms.PacketHandlerCallBackDelegate<byte[]>(PeerDiscoveryHandler)))
                                listener.AppendIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
                        }
                    }
                }
            }
#endif
            else
                throw new NotImplementedException("The requested discovery method has not been implemented on the current platform.");
        }

        /// <summary>
        /// Disable this peers discoverable status for the provided <see cref="DiscoveryMethod"/>. 
        /// </summary>
        /// <param name="discoveryMethod">The <see cref="DiscoveryMethod"/> to disable discovery for.</param>
        public static void DisableDiscoverable(DiscoveryMethod discoveryMethod)
        {

            if (discoveryMethod == DiscoveryMethod.UDPBroadcast || discoveryMethod == DiscoveryMethod.TCPPortScan)
            {
                lock (_syncRoot)
                {
                    if (_discoveryListeners.ContainsKey(discoveryMethod))
                    {
                        Connection.StopListening(_discoveryListeners[discoveryMethod]);
                        _discoveryListeners.Remove(discoveryMethod);
                    }
                }
            }
#if NET35 || NET4
            else if (discoveryMethod == DiscoveryMethod.BluetoothSDP)
            {
                lock (_syncRoot)
                {
                    foreach (BluetoothRadio radio in BluetoothRadio.AllRadios)
                        radio.Mode = RadioMode.Connectable;

                    if (_discoveryListeners.ContainsKey(discoveryMethod))
                    {
                        Connection.StopListening(_discoveryListeners[discoveryMethod]);
                        _discoveryListeners.Remove(discoveryMethod);
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Disable this peers discoverable status for all discovery methods.
        /// </summary>
        public static void DisableDiscoverable()
        {
            lock (_syncRoot)
            {
#if NET35 || NET4
                foreach (BluetoothRadio radio in BluetoothRadio.AllRadios)
                    radio.Mode = RadioMode.Connectable;
#endif

                foreach (DiscoveryMethod currentType in _discoveryListeners.Keys)
                    Connection.StopListening(_discoveryListeners[currentType]);

                _discoveryListeners = new Dictionary<DiscoveryMethod, List<ConnectionListenerBase>>();
            }
        }

        /// <summary>
        /// Returns true if local discovery endPoints exist for the provided <see cref="DiscoveryMethod"/>.
        /// </summary>
        /// <param name="discoveryMethod"></param>
        /// <returns></returns>
        public static bool IsDiscoverable(DiscoveryMethod discoveryMethod)
        {
            return LocalDiscoveryEndPoints(discoveryMethod).Count > 0;
        }

        /// <summary>
        /// Returns the local endpoints that are currently used to make this peer discoverable using
        /// the provided <see cref="DiscoveryMethod"/>.
        /// </summary>
        /// <param name="discoveryMethod"></param>
        /// <returns></returns>
        public static List<EndPoint> LocalDiscoveryEndPoints(DiscoveryMethod discoveryMethod)
        {
            Dictionary<DiscoveryMethod, List<EndPoint>> result = LocalDiscoveryEndPoints();
            if (result.ContainsKey(discoveryMethod))
                return result[discoveryMethod];
            else
                return new List<EndPoint>();
        }

        /// <summary>
        /// Returns the local endpoints that are currently used to make this peer discoverable.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<DiscoveryMethod, List<EndPoint>> LocalDiscoveryEndPoints()
        {
            Dictionary<DiscoveryMethod, List<EndPoint>> result = new Dictionary<DiscoveryMethod, List<EndPoint>>();

            lock (_syncRoot)
            {
                foreach (DiscoveryMethod currentType in _discoveryListeners.Keys)
                {
                    result.Add(currentType, new List<EndPoint>());

                    foreach (ConnectionListenerBase listener in _discoveryListeners[currentType])
                        result[currentType].Add(listener.LocalListenEndPoint);
                }
            }

            return result;
        }
        #endregion

        #region Discover Peer Methods
        /// <summary>
        /// Discover local peers using the provided <see cref="DiscoveryMethod"/> and default discover time. Returns
        /// dictionary keyed on peer network identifier. IMPORTANT NOTE: For IP networks we strongly recommend using the UDP 
        /// broadcast discovery method.
        /// </summary>
        /// <param name="discoveryMethod">The <see cref="DiscoveryMethod"/> to use for discovering peers.</param>
        /// <returns></returns>
        public static Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> DiscoverPeers(DiscoveryMethod discoveryMethod)
        {
            return DiscoverPeers(discoveryMethod, DefaultDiscoverTimeMS);
        }

        /// <summary>
        /// Discover local peers using the provided <see cref="DiscoveryMethod"/>. Returns
        /// dictionary keyed on peer network identifier. IMPORTANT NOTE: For IP networks we strongly recommend using the UDP 
        /// broadcast discovery method.
        /// </summary>
        /// <param name="discoveryMethod">The <see cref="DiscoveryMethod"/> to use for discovering peers.</param>
        /// <param name="discoverTimeMS">The wait time, after all requests have been made, in milliseconds before all discovered peers are returned.</param>
        /// <returns></returns>
        public static Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> DiscoverPeers(DiscoveryMethod discoveryMethod, int discoverTimeMS)
        {
            if (!IsDiscoverable(discoveryMethod))
                throw new InvalidOperationException("Please ensure this peer is discoverable before attempting to discover other peers.");

            Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> result;
            lock (_discoverSyncRoot)
            {
                //Clear the discovered peers cache
                lock (_syncRoot)
                    _discoveredPeers = new Dictionary<ShortGuid, Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>>>();

                if (discoveryMethod == DiscoveryMethod.UDPBroadcast)
                    result = DiscoverPeersUDP(discoverTimeMS);
                else if (discoveryMethod == DiscoveryMethod.TCPPortScan)
                    result = DiscoverPeersTCP(discoverTimeMS);
#if NET35 || NET4
                else if (discoveryMethod == DiscoveryMethod.BluetoothSDP)
                    result = DiscoverPeersBT(discoverTimeMS);
#endif
                else
                    throw new NotImplementedException("Peer discovery has not been implemented for the provided connection type.");
            }

            return result;
        }

        /// <summary>
        /// Discover local peers using the provided <see cref="DiscoveryMethod"/> asynchronously. Makes a single async request 
        /// for peers to announce. Ensure that you append to the OnPeerDiscovered event to handle discovered peers. 
        /// IMPORTANT NOTE: For IP networks we strongly recommend using the UDP broadcast discovery method.
        /// </summary>
        /// <param name="discoveryMethod"></param>
        public static void DiscoverPeersAsync(DiscoveryMethod discoveryMethod)
        {
            if (!IsDiscoverable(discoveryMethod))
                throw new InvalidOperationException("Please ensure this peer is discoverable before attempting to discover other peers.");

            NetworkComms.CommsThreadPool.EnqueueItem(QueueItemPriority.Normal, (state) =>
                {
                    try
                    {
                        DiscoverPeers(discoveryMethod, 0);
                    }
                    catch (Exception) { }
                }, null);
        }

        /// <summary>
        /// Discover peers using UDP broadcast
        /// </summary>
        /// <param name="discoverTimeMS"></param>
        /// <returns></returns>
        private static Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> DiscoverPeersUDP(int discoverTimeMS)
        {
            SendReceiveOptions nullOptions = new SendReceiveOptions<NullSerializer>();
            StreamTools.StreamSendWrapper sendStream =
                new StreamTools.StreamSendWrapper(new StreamTools.ThreadSafeStream(new MemoryStream(new byte[0])));

            using (Packet sendPacket = new Packet(discoveryPacketType, sendStream, nullOptions))
            {
                for (int port = MinTargetLocalIPPort; port <= MaxTargetLocalIPPort; port++)
                    UDPConnection.SendObject<byte[]>(sendPacket, new IPEndPoint(IPAddress.Broadcast, port), nullOptions, ApplicationLayerProtocolStatus.Enabled);
            }

            sendStream.ThreadSafeStream.Dispose(true);

            AutoResetEvent sleep = new AutoResetEvent(false);
#if NET2
            sleep.WaitOne(discoverTimeMS, false);
#else
            sleep.WaitOne(discoverTimeMS);
#endif

            Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> result = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>>();
            lock (_syncRoot)
            {
                foreach (var idPair in _discoveredPeers)
                {
                    if(!result.ContainsKey(idPair.Key))
                        result.Add(idPair.Key, new Dictionary<ConnectionType,List<EndPoint>>());

                    foreach(var typePair in idPair.Value)
                    {
                        if(!result[idPair.Key].ContainsKey(typePair.Key))
                            result[idPair.Key].Add(typePair.Key, new List<EndPoint>());

                        foreach(var endPoint in typePair.Value)
                            if(!result[idPair.Key][typePair.Key].Contains(endPoint.Key))
                                result[idPair.Key][typePair.Key].Add(endPoint.Key);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Discover peers using TCP port scan
        /// </summary>
        /// <param name="discoverTimeMS"></param>
        /// <returns></returns>
        private static Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> DiscoverPeersTCP(int discoverTimeMS)
        {
            #region Determine All Possible Peers/Port Combinations
            //Get a list of all IPEndPoint that we should try and connect to
            //This requires the network and peer portion of current IP addresses
            List<IPEndPoint> allIPEndPointsToConnect = new List<IPEndPoint>();

            //Look at all possible addresses
            foreach (var iFace in NetworkInterface.GetAllNetworkInterfaces())
            {
                bool interfaceValid = false;
                var unicastAddresses = iFace.GetIPProperties().UnicastAddresses;

                //Check if this adaptor is allowed
                if (HostInfo.RestrictLocalAdaptorNames != null)
                {
                    foreach (var currentName in HostInfo.RestrictLocalAdaptorNames)
                    {
                        if (iFace.Name == currentName)
                        {
                            interfaceValid = true;
                            break;
                        }
                    }
                }
                else
                    interfaceValid = true;

                //If the interface is not allowed move to the next adaptor
                if (!interfaceValid)
                    continue;

                //If the adaptor is allowed we can now investigate the individual addresses
                foreach (var address in unicastAddresses)
                {
                    //We are only interested in IPV4 ranges. A TCPPortScan on an IPV6 range may take a while
                    if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !IPRange.IsAutoAssignedAddress(address.Address))
                    {
                        //Check if we have restricted the addresses
                        bool addressAllowed = true;
                        if (HostInfo.IP.RestrictLocalAddressRanges != null)
                            addressAllowed = IPRange.Contains(HostInfo.IP.RestrictLocalAddressRanges, address.Address);

                        if (addressAllowed)
                        {
                            //Generate all possible IPEndPoints for the current address and subnetmask
                            //We have a special catch for the loopback address which has a very large range
                            List<IPAddress> addressesInRange;
                            if (address.Address.Equals(IPAddress.Loopback))
                                addressesInRange = new List<IPAddress>() { IPAddress.Loopback };
                            else
                            {
                                IPRange range = new IPRange(address.Address, address.IPv4Mask);
                                addressesInRange = range.AllAddressesInRange();
                            }

                            foreach (IPAddress currentAddressInRange in addressesInRange)
                            {
                                for (int port = MinTargetLocalIPPort; port <= MaxTargetLocalIPPort; port++)
                                    allIPEndPointsToConnect.Add(new IPEndPoint(currentAddressInRange, port));
                            }
                        }
                    }
                }
            }
            #endregion

            #region Send Discovery Packet & Wait
            //For each address send the discovery packet
            SendReceiveOptions nullOptions = new SendReceiveOptions<NullSerializer>();
            StreamTools.StreamSendWrapper sendStream =
                new StreamTools.StreamSendWrapper(new StreamTools.ThreadSafeStream(new MemoryStream(new byte[0])));

            int previousConnectionTimeout = NetworkComms.ConnectionEstablishTimeoutMS;
            NetworkComms.ConnectionEstablishTimeoutMS = 1000;

            AutoResetEvent allSendsCompleteEvent = new AutoResetEvent(false);
            long interlockedCompletedCount = 0;
            object _syncRoot = new object();
            List<Connection> allConnections = new List<Connection>();

            //Get unconnected TCP connections
            foreach (IPEndPoint remoteEndPoint in allIPEndPointsToConnect)
            {
                Connection conn = TCPConnection.GetConnection(new ConnectionInfo(remoteEndPoint), false);
                conn.AppendIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
                allConnections.Add(conn);
            }

            using (Packet sendPacket = new Packet(discoveryPacketType, sendStream, nullOptions))
            {
                foreach (Connection conn in allConnections)
                {
                    Connection innerConnection = conn;

                    //The longest wait for the port scan is the TCP connect timeout
                    //The thread pool will start a large number of threads (each of which does very little)
                    // to greatly speed this up
                    _tcpPortScanThreadPool.EnqueueItem(QueueItemPriority.Normal, (state) =>
                    {
                        try
                        {
                            try
                            {
								innerConnection.EstablishConnection();
								innerConnection.SendPacket<byte[]>(sendPacket);
                            }
                            catch (CommsException)
                            {
                            }

                            lock (_syncRoot)
                            {
                                interlockedCompletedCount++;
                                if (interlockedCompletedCount == allIPEndPointsToConnect.Count)
                                    allSendsCompleteEvent.Set();
                            }
                        }
                        catch (Exception) { }
                    }, null);
                }

                allSendsCompleteEvent.WaitOne();
            }

            NetworkComms.ConnectionEstablishTimeoutMS = previousConnectionTimeout;
            
            sendStream.ThreadSafeStream.Dispose(true);

            AutoResetEvent sleep = new AutoResetEvent(false);
            //We wait at least 1 second so that connected peers can respond
            //If we do not wait and close all connections immediately we may miss some replies
#if NET2
            sleep.WaitOne(Math.Max(discoverTimeMS, 500), false);
#else
            sleep.WaitOne(Math.Max(discoverTimeMS, 500));
#endif

            //Close any connections we may have established
            foreach (Connection conn in allConnections)
            {
                try
                {
                    conn.CloseConnection(false);
                }
                catch (CommsException) { }
            }
            #endregion

            #region Return Discovered Peers
            Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> result = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>>();
            lock (_syncRoot)
            {
                foreach (var idPair in _discoveredPeers)
                {
                    if (!result.ContainsKey(idPair.Key))
                        result.Add(idPair.Key, new Dictionary<ConnectionType, List<EndPoint>>());

                    foreach (var typePair in idPair.Value)
                    {
                        if (!result[idPair.Key].ContainsKey(typePair.Key))
                            result[idPair.Key].Add(typePair.Key, new List<EndPoint>());

                        foreach (var endPoint in typePair.Value)
                            if (!result[idPair.Key][typePair.Key].Contains(endPoint.Key))
                                result[idPair.Key][typePair.Key].Add(endPoint.Key);
                    }
                }
            }

            return result;
            #endregion
        }

#if NET35 || NET4
        /// <summary>
        /// Discover peers using BT SDP
        /// </summary>
        /// <param name="discoverTimeout"></param>
        /// <returns></returns>
        private static Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> DiscoverPeersBT(int discoverTimeout)
        {
            object locker = new object();
            AutoResetEvent btDiscoverFinished = new AutoResetEvent(false);

            EventHandler<DiscoverDevicesEventArgs> callBack = (sender, e) =>
                {
                    List<EndPoint> endPointsToSendTo = new List<EndPoint>();

                    foreach (var dev in e.Devices)
                        foreach (var serviceRecord in dev.GetServiceRecords(BluetoothService.RFCommProtocol))
                            if (serviceRecord.AttributeIds.Contains(BluetoothConnectionListener.NetworkCommsBTAttributeId.NetworkCommsEndPoint))
                                endPointsToSendTo.Add(new BluetoothEndPoint(dev.DeviceAddress, serviceRecord.GetAttributeById(UniversalAttributeId.ServiceClassIdList).Value.GetValueAsElementList()[0].GetValueAsUuid()));
                    
                    using (Packet sendPacket = new Packet(discoveryPacketType, new byte[0], NetworkComms.DefaultSendReceiveOptions))
                    {
                        foreach (var remoteEndPoint in endPointsToSendTo)
                        {
                            var connection = BluetoothConnection.GetConnection(new ConnectionInfo(remoteEndPoint));
                            connection.SendPacket<byte[]>(sendPacket);
                        }
                    }

                    btDiscoverFinished.Set();
                };

            BluetoothComponent com = new InTheHand.Net.Bluetooth.BluetoothComponent();
            com.DiscoverDevicesComplete += callBack;            
            com.DiscoverDevicesAsync(255, false, false, false, true, com);

            btDiscoverFinished.WaitOne();
            Thread.Sleep(discoverTimeout);

            Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> result = new Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>>();
            lock (_syncRoot)
            {
                foreach (var idPair in _discoveredPeers)
                {
                    if(!result.ContainsKey(idPair.Key))
                        result.Add(idPair.Key, new Dictionary<ConnectionType,List<EndPoint>>());

                    foreach(var typePair in idPair.Value)
                    {
                        if(!result[idPair.Key].ContainsKey(typePair.Key))
                            result[idPair.Key].Add(typePair.Key, new List<EndPoint>());

                        foreach(var endPoint in typePair.Value)
                            if(!result[idPair.Key][typePair.Key].Contains(endPoint.Key))
                                result[idPair.Key][typePair.Key].Add(endPoint.Key);
                    }
                }
            }

            return result;
        }     
#endif
        #endregion

        #region Incoming Comms Handlers
        /// <summary>
        /// Handle the incoming peer discovery packet
        /// </summary>
        /// <param name="header"></param>
        /// <param name="connection"></param>
        /// <param name="data"></param>
        private static void PeerDiscoveryHandler(PacketHeader header, Connection connection, byte[] data)
        {            
            DiscoveryMethod discoveryMethod = DiscoveryMethod.UDPBroadcast;

            if (connection.ConnectionInfo.ConnectionType == ConnectionType.TCP)
                discoveryMethod = DiscoveryMethod.TCPPortScan;
#if NET35 || NET4
            else if (connection.ConnectionInfo.ConnectionType == ConnectionType.Bluetooth)
                discoveryMethod = DiscoveryMethod.BluetoothSDP;
#endif

            //Ignore discovery packets that came from this peer
            if (!Connection.ExistingLocalListenEndPoints(connection.ConnectionInfo.ConnectionType).Contains(connection.ConnectionInfo.RemoteEndPoint))
            {
                //If the only thing that was sent was an empty byte array this is a peer discovery request. 
                if (data.Length == 0 && IsDiscoverable(discoveryMethod))
                {
                    //Send back our listener info 
                    byte[] responseData = SerializeLocalListenerList();
                    connection.SendObject(discoveryPacketType, responseData);
                }
                else if (data.Length > 0)
                {
                    //This is a peer discovery reply, we may need to add this to the tracking dictionary
                    ShortGuid peerIdentifier;

                    //If this is the case then we have found listeners on a peer and we need to add them to our known peers
                    List<PeerListenerEndPoint> remoteListeners = DeserializeRemoteListenerList(data, out peerIdentifier);

                    Dictionary<ConnectionType, List<EndPoint>> discoveredPeerListeners = new Dictionary<ConnectionType, List<EndPoint>>();
                    foreach (PeerListenerEndPoint peer in remoteListeners)
                    {
                        if (discoveredPeerListeners.ContainsKey(peer.ConnectionType))
                            discoveredPeerListeners[peer.ConnectionType].Add(peer.EndPoint);
                        else
                            discoveredPeerListeners.Add(peer.ConnectionType, new List<EndPoint>() { peer.EndPoint });
                    }

                    bool newlyDiscoveredPeerEndPoint = false;
                    lock (_syncRoot)
                    {
                        if (!_discoveredPeers.ContainsKey(peerIdentifier))
                            _discoveredPeers.Add(peerIdentifier, new Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>>());

                        //If no remote listeners were returned we can only used the remoteEndPoint used to send this reply
                        if (remoteListeners.Count == 0)
                        {
                            if (!_discoveredPeers[peerIdentifier].ContainsKey(connection.ConnectionInfo.ConnectionType))
                                _discoveredPeers[peerIdentifier].Add(connection.ConnectionInfo.ConnectionType, new Dictionary<EndPoint, DateTime>());

                            if (!_discoveredPeers[peerIdentifier][connection.ConnectionInfo.ConnectionType].ContainsKey(connection.ConnectionInfo.RemoteEndPoint))
                            {
                                newlyDiscoveredPeerEndPoint = true;
                                _discoveredPeers[peerIdentifier][connection.ConnectionInfo.ConnectionType].Add(connection.ConnectionInfo.RemoteEndPoint, DateTime.Now);
                            }
                        }
                        else
                        {
                            foreach (PeerListenerEndPoint peerEndPoint in remoteListeners)
                            {
                                if (!_discoveredPeers[peerIdentifier].ContainsKey(peerEndPoint.ConnectionType))
                                {
                                    newlyDiscoveredPeerEndPoint = true;
                                    _discoveredPeers[peerIdentifier].Add(peerEndPoint.ConnectionType, new Dictionary<EndPoint, DateTime>() { { peerEndPoint.EndPoint, DateTime.Now } });
                                }
                                else if (!_discoveredPeers[peerIdentifier][peerEndPoint.ConnectionType].ContainsKey(peerEndPoint.EndPoint))
                                {
                                    newlyDiscoveredPeerEndPoint = true;
                                    _discoveredPeers[peerIdentifier][peerEndPoint.ConnectionType].Add(peerEndPoint.EndPoint, DateTime.Now);
                                }
                            }
                        }
                    }

                    //Trigger the discovery event if it has been set AND this is the first time a new peer end point was discovered
                    //If a peer is discoverable on multiple adaptors which we have access too we may receive numerous (duplicate)
                    //discovery reports. We are only interested in triggering on the first one, not subsequent identical copies.
                    //This ensures the synchronous and asynchronous methods execute in a similar fashion
                    if (OnPeerDiscovered != null && newlyDiscoveredPeerEndPoint)
                        OnPeerDiscovered(peerIdentifier, discoveredPeerListeners);
                }
            }
        }

        /// <summary>
        /// Serializes the local listeners that are discoverable
        /// </summary>
        /// <returns></returns>
        private static byte[] SerializeLocalListenerList()
        {
            List<ConnectionListenerBase> allListeners = Connection.AllExistingLocalListeners();
            
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(NetworkComms.NetworkIdentifier.Guid.ToByteArray(), 0, 16);

                int discoverableCount = 0;
                ms.Seek(sizeof(int) + 16, SeekOrigin.Begin);

                foreach (ConnectionListenerBase listener in allListeners)
                {
                    if (listener.IsDiscoverable)
                    {
                        PeerListenerEndPoint ep = new PeerListenerEndPoint(listener.ConnectionType, listener.LocalListenEndPoint);
                        byte[] bytes = ep.Serialize();

                        ms.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
                        ms.Write(bytes, 0, bytes.Length);
                        discoverableCount++;
                    }
                }

                ms.Seek(16, 0);
                ms.Write(BitConverter.GetBytes(discoverableCount), 0, sizeof(int));

                ms.Flush();

                return ms.ToArray();
           }            
        }

        /// <summary>
        /// Deserializes remote listeners that are discoverable
        /// </summary>
        /// <param name="data"></param>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        private static List<PeerListenerEndPoint> DeserializeRemoteListenerList(byte[] data, out ShortGuid networkIdentifier)
        {
            List<PeerListenerEndPoint> result = new List<PeerListenerEndPoint>();
            
            int offset = 0;

            byte[] idData = new byte[16];
            Buffer.BlockCopy(data, 0, idData, 0, 16); offset += 16;
            networkIdentifier = new ShortGuid(new Guid(idData));
            int numElements = BitConverter.ToInt32(data, offset); offset += sizeof(int);

            for (int i = 0; i < numElements; i++)
            {
                int size = BitConverter.ToInt32(data, offset); offset += sizeof(int);

                if (size > data.Length)
                    throw new Exception();

                byte[] peerData = new byte[size];
                Buffer.BlockCopy(data, offset, peerData, 0, peerData.Length); offset += peerData.Length;
                result.Add(PeerListenerEndPoint.Deserialize(peerData));
            }

            return result;
        }
        #endregion
    }
}