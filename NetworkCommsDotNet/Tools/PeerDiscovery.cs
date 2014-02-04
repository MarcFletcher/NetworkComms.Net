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
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

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
#elif NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#endif

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Provides the ability to discover discoverable peers in the 
    /// </summary>
    public static class PeerDiscovery
    {
        public enum DiscoveryMethod
        {
            UDPBroadcast,
            TCPPortScan,
            BluetoothSDP,
        }

        class PeerListennerEndPoint
        {
            public ConnectionType ConnectionType { get; private set; }
            public EndPoint EndPoint { get; private set; }

            public IPEndPoint IPEndPoint { get { return EndPoint as IPEndPoint; } }

#if NET35 || NET4
            public BluetoothEndPoint BTEndPoint { get { return EndPoint as BluetoothEndPoint; } }
#endif

            public PeerListennerEndPoint(ConnectionType connectionType, EndPoint endPoint)
            {
                if (connectionType == Connections.ConnectionType.Undefined)
                    throw new ArgumentException("Cannot create a PeerEndPoint if connectioType is Undefined","connectionType");

                if ((connectionType == Connections.ConnectionType.TCP || connectionType == Connections.ConnectionType.UDP) && !(endPoint is IPEndPoint))
                    throw new ArgumentException("If conneciton type is of an IP type then the provided endPoint must be an IPEndPoint", "endPoint");

#if NET35 || NET4
                if(connectionType == Connections.ConnectionType.Bluetooth && !(endPoint is BluetoothEndPoint))
                    throw new ArgumentException("If conneciton type is Bluetooth type then the provided endPoint must be a BluetoothEndPoint", "endPoint");
#endif

                this.ConnectionType = connectionType;
                this.EndPoint = endPoint;
            }

            private PeerListennerEndPoint(){}

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

            public static PeerListennerEndPoint Deserialize(byte[] data)
            {
                PeerListennerEndPoint result = new PeerListennerEndPoint();

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
                    byte[] address = new byte[6];
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
        /// The wait time in MS before all peers discovered are returned. Default 2000
        /// </summary>
        public static int DefaultDiscoverTimeMS { get; set; }

        /// <summary>
        /// The minimum port number that will be used when making this peer discoverable. Default 10000
        /// </summary>
        public static int MinTargetLocalIPPort { get; set; }

        /// <summary>
        /// The maximum port number that will be used when making this peer discoverable. Default 10099
        /// </summary>
        public static int MaxTargetLocalIPPort { get; set; }

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
        /// The service on which discovery will run for bluetooth
        /// </summary>
        public static Guid BluetoothDiscoveryService { get; set; }

        /// <summary>
        /// The event delegate which can optionally be used when a peer is successfully discovered.
        /// </summary>
        /// <param name="peerEndPoint"></param>
        /// <param name="connectionType"></param>
        public delegate void PeerDiscoveredHandler(ConnectionType connectionType, EndPoint peerEndPoint);

        /// <summary>
        /// Triggered when a peer is discovered
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
        /// The packet type used for peer discovery
        /// </summary>
        private static string discoveryPacketType = "PeerDiscovery";

        /// <summary>
        /// Listeners associated with this peers discover status
        /// </summary>
        private static Dictionary<DiscoveryMethod, List<ConnectionListenerBase>> _discoveryListeners = new Dictionary<DiscoveryMethod, List<ConnectionListenerBase>>();

        /// <summary>
        /// A dictionary which records discovered peers
        /// </summary>
        private static Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>> _discoveredPeers = new Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>>();
        #endregion

        static PeerDiscovery()
        {
            DefaultDiscoverTimeMS = 2000;

            MinTargetLocalIPPort = 10000;
            MaxTargetLocalIPPort = 10020;

            BluetoothDiscoveryService = new Guid("3a768eea-cbda-4926-a82d-831cb89092ac");
        }

        #region Local Configuration
        /// <summary>
        /// Make this peer discoverable using the provided connection type. 
        /// IMPORTANT NOTE: For IP networks we strongly recommend using UDP as the connection type.
        /// </summary>
        /// <param name="discoveryMethod"></param>
        public static void EnableDiscoverable(DiscoveryMethod discoveryMethod)
        {
            lock (_syncRoot)
            {
                if (_discoveryListeners.ContainsKey(discoveryMethod))
                    return;

                //Based on the connection type select all local endPoints and then enable discoverable
                if (discoveryMethod == DiscoveryMethod.TCPPortScan || discoveryMethod == DiscoveryMethod.UDPBroadcast)
                {
                    List<ConnectionListenerBase> listeners = new List<ConnectionListenerBase>();

                    //We should select one of the target points across all adaptors, no need for all adaptors to have
                    //selected a single uniform port.
                    List<IPAddress> localAddresses = HostInfo.IP.FilteredLocalAddresses();

                    foreach (IPAddress address in localAddresses)
                    {
                        //Keep trying to listen on an ever increasing port number
                        for (int tryPort = MinTargetLocalIPPort; tryPort <= MaxTargetLocalIPPort; tryPort++)
                        {
                            try
                            {
                                List<ConnectionListenerBase> newlisteners = Connection.StartListening(discoveryMethod == DiscoveryMethod.UDPBroadcast ? ConnectionType.UDP : ConnectionType.TCP, new IPEndPoint(address, tryPort));

                                //Once we are successfully listening we can break
                                listeners.AddRange(newlisteners);
                                break;
                            }
                            catch (Exception) { }

                            if (tryPort == MaxTargetLocalIPPort)
                                throw new CommsSetupShutdownException("Failed to find local available listen port on address " + address.ToString() + " while trying to make this peer discoverable.");
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
                        listeners.AddRange(Connection.StartListening(ConnectionType.Bluetooth, new BluetoothEndPoint(radio.LocalAddress, BluetoothDiscoveryService)));
                    }
                    
                    _discoveryListeners.Add(discoveryMethod, listeners);
                }
#endif
                else
                    throw new NotImplementedException("This feature has not been implemented for the provided connection type.");

                //Add the packet handlers if required
                if (!NetworkComms.GlobalIncomingPacketHandlerExists<byte[]>(discoveryPacketType, PeerDiscoveryHandler))
                    NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
            }
        }

        /// <summary>
        /// Make this peer discoverable using the provided connection type and local end point.
        /// IMPORTANT NOTE: For IP networks we strongly recommend using UDP as the connection type.
        /// </summary>
        /// <param name="discoveryMethod"></param>
        /// <param name="localDiscoveryEndPoint"></param>
        public static void EnableDiscoverable(DiscoveryMethod discoveryMethod, EndPoint localDiscoveryEndPoint)
        {
            if (discoveryMethod == DiscoveryMethod.TCPPortScan || discoveryMethod == DiscoveryMethod.UDPBroadcast)
            {
                lock (_syncRoot)
                {
                    if (_discoveryListeners.ContainsKey(discoveryMethod))
                        throw new ArgumentException("Peer is already discoverable for the provided connectionType", "connectionType");

                    //Based on the connection type select all local endPoints and then enable discoverable
                    _discoveryListeners.Add(discoveryMethod, Connection.StartListening(discoveryMethod == DiscoveryMethod.UDPBroadcast ? ConnectionType.UDP : ConnectionType.TCP, localDiscoveryEndPoint));

                    //Add the packet handlers if required
                    if (!NetworkComms.GlobalIncomingPacketHandlerExists<byte[]>(discoveryPacketType, PeerDiscoveryHandler))
                        NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
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

                    _discoveryListeners.Add(discoveryMethod, Connection.StartListening(ConnectionType.Bluetooth, localDiscoveryEndPoint));

                    //Add the packet handlers if required
                    if (!NetworkComms.GlobalIncomingPacketHandlerExists<byte[]>(discoveryPacketType, PeerDiscoveryHandler))
                        NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
                }
            }
#endif
        }

        /// <summary>
        /// Disable this peers discoverable status for the provided connection type. 
        /// </summary>
        /// <param name="discoveryMethod">The connection type to disable discovery for.</param>
        public static void DisableDiscoverable(DiscoveryMethod discoveryMethod)
        {
            if (discoveryMethod == DiscoveryMethod.TCPPortScan || discoveryMethod == DiscoveryMethod.UDPBroadcast)
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
        /// Returns true if local discovery endPoints exist for the provided connectionType.
        /// </summary>
        /// <param name="discoveryMethod"></param>
        /// <returns></returns>
        public static bool IsDiscoverable(DiscoveryMethod discoveryMethod)
        {
            return LocalDiscoveryEndPoints(discoveryMethod).Count > 0;
        }

        /// <summary>
        /// Returns the local endpoints that are currently used to make this peer discoverable.
        /// </summary>
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

        /// <summary>
        /// Discover local peers using the provided connection type and default discover time. Returns EndPoints of discovered peers.
        /// </summary>
        /// <param name="discoveryMethod">The connection type to use for discovering peers.</param>
        /// <returns></returns>
        public static List<EndPoint> DiscoverPeers(DiscoveryMethod discoveryMethod)
        {
            return DiscoverPeers(discoveryMethod, DefaultDiscoverTimeMS);
        }

        /// <summary>
        /// Discover local peers using the provided connection type. Returns connectionInfos of discovered peers.
        /// IMPORTANT NOTE: For IP networks we strongly recommend using UDP as the connection type.
        /// </summary>
        /// <param name="discoveryMethod">The connection type to use for discovering peers.</param>
        /// <param name="discoverTimeMS">The wait time, after all requests have been made, in MS before all peers discovered are returned .</param>
        /// <returns></returns>
        public static List<EndPoint> DiscoverPeers(DiscoveryMethod discoveryMethod, int discoverTimeMS)
        {
            if (!IsDiscoverable(discoveryMethod))
                throw new InvalidOperationException("Please ensure this peer is discoverable before attempting to discover other peers.");

            List<EndPoint> result;
            lock (_discoverSyncRoot)
            {
                //Clear the discovered peers cache
                lock(_syncRoot)
                    _discoveredPeers = new Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>>();

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
        /// Discover local peers using the provided connection type asynchronously. Makes a single async request for peers to announce.
        /// Append to OnPeerDiscovered event to handle discovered peers. 
        /// IMPORTANT NOTE: For IP networks we strongly recommend using UDP as the connection type.
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

        private static List<EndPoint> DiscoverPeersUDP(int discoverTimeMS)
        {
            using (Packet sendPacket = new Packet(discoveryPacketType, new byte[4] { 0, 0, 0, 0 }, NetworkComms.DefaultSendReceiveOptions))
            {
                for (int port = MinTargetLocalIPPort; port <= MaxTargetLocalIPPort; port++)
                    UDPConnection.SendObject<byte[]>(sendPacket, new IPEndPoint(IPAddress.Broadcast, port), NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled);
            }

            Thread.Sleep(discoverTimeMS);

            List<EndPoint> result = new List<EndPoint>();
            lock (_syncRoot)
            {
                foreach (var pair in _discoveredPeers)
                    foreach (IPEndPoint endPoint in pair.Value.Keys)
                        result.Add(endPoint);
            }

            return result;
        }

        private static List<EndPoint> DiscoverPeersTCP(int discoverTimeMS)
        {
            //We have to try and manually connect to all peers and see if they respond

            //Get a list of all IPEndPoint that we should try and connect to
            //This requires the network and peer portion of current IP addresses

            //We can only do TCP discovery for IPv4 ranges. Doing it for IPv6 would be a BAD
            //idea due to the shear volume of addresses

            throw new NotImplementedException("Peer discovery has not yet been implemented for TCP.");
        }

#if NET35 || NET4

        private static List<EndPoint> DiscoverPeersBT(int discoverTimeout)
        {
            object locker = new object();

            AutoResetEvent completeEv = new AutoResetEvent(false);
            EventHandler<DiscoverDevicesEventArgs> callBack = (sender, e) =>
                {
                    using (Packet sendPacket = new Packet(discoveryPacketType, new byte[4] { 0, 0, 0, 0 }, NetworkComms.DefaultSendReceiveOptions))
                    {
                        foreach (var dev in e.Devices)
                        {
                            foreach (var serviceRecord in dev.GetServiceRecords(BluetoothService.RFCommProtocol))
                            {
                                if (serviceRecord.AttributeIds.Contains(BluetoothConnectionListener.NetworkCommsBTAttributeId.NetworkCommsEndPoint))
                                {
                                    var remoteEndPoint = new BluetoothEndPoint(dev.DeviceAddress, serviceRecord.GetAttributeById(UniversalAttributeId.ServiceClassIdList).Value.GetValueAsElementList()[0].GetValueAsUuid());
                                    BluetoothConnection.GetConnection(new ConnectionInfo(remoteEndPoint)).SendPacket<byte[]>(sendPacket);
                                }
                            }
                        }

                    }

                    completeEv.Set();
                };

            BluetoothComponent com = new InTheHand.Net.Bluetooth.BluetoothComponent();
            com.DiscoverDevicesComplete += callBack;            
            com.DiscoverDevicesAsync(255, false, false, false, true, com);

            completeEv.WaitOne(discoverTimeout);
            
            List<EndPoint> result = new List<EndPoint>();
            lock (_syncRoot)
            {
                foreach (var pair in _discoveredPeers)
                    foreach (IPEndPoint endPoint in pair.Value.Keys)
                        result.Add(endPoint);
            }

            return result;
        }
               
#endif

        #region Incoming Comms Handlers
        /// <summary>
        /// Handle the incoming peer discovery packet
        /// </summary>
        /// <param name="header"></param>
        /// <param name="connection"></param>
        /// <param name="data"></param>
        private static void PeerDiscoveryHandler(PacketHeader header, Connection connection, byte[] data)
        {
            if (data.Length < 4) throw new Exception("Idiot check");
            
            DiscoveryMethod discoveryMethod = DiscoveryMethod.UDPBroadcast;
            if (connection.ConnectionInfo.ConnectionType == ConnectionType.TCP)
                discoveryMethod = DiscoveryMethod.TCPPortScan;

            //Ignore discovery packets that came from this peer
            if (!Connection.ExistingLocalListenEndPoints(connection.ConnectionInfo.ConnectionType).Contains(connection.ConnectionInfo.RemoteEndPoint))
            {
                //If the only thing that was sent was an empty list (first int/4bytes is zero) then this is a peer discovery request. 
                //Send back our listenner info if we have any discoverable listeners
                if (data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 0 && IsDiscoverable(discoveryMethod))
                {                    
                    byte[] responseData = SerializeLocalListennerList();

                    if (responseData.Length != 4)
                        connection.SendObject(discoveryPacketType, responseData);
                }
                else
                {
                    //If this is the case then we have found listenners on a peer and we need to add them to our known peers
                    List<PeerListennerEndPoint> remoteListenners = DeserializeRemoteListennerList(data);

                    foreach (PeerListennerEndPoint peer in remoteListenners)
                    {
                        //Trigger the discovery event
                        if (OnPeerDiscovered != null)
                            OnPeerDiscovered(peer.ConnectionType, peer.EndPoint);

                        //This is a peer discovery reply, we need to add this to the tracking dictionary
                        lock (_syncRoot)
                        {
                            if (_discoveredPeers.ContainsKey(peer.ConnectionType))
                                _discoveredPeers[peer.ConnectionType][peer.EndPoint] = DateTime.Now;
                            else
                                _discoveredPeers.Add(peer.ConnectionType, new Dictionary<EndPoint, DateTime>() { { peer.EndPoint, DateTime.Now } });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Serializes the local 
        /// </summary>
        /// <returns></returns>
        private static byte[] SerializeLocalListennerList()
        {
            List<ConnectionListenerBase> allListeners = NetworkCommsDotNet.Connections.Connection.AllExistingLocalListeners();

            using (MemoryStream ms = new MemoryStream())
            {
                int discoverableCount = 0;
                ms.Seek(sizeof(int), SeekOrigin.Begin);

                foreach (ConnectionListenerBase listener in allListeners)
                {
                    if (listener.IsDiscoverable)
                    {
                        PeerListennerEndPoint ep = new PeerListennerEndPoint(listener.ConnectionType, listener.LocalListenEndPoint);
                        byte[] bytes = ep.Serialize();

                        ms.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
                        ms.Write(bytes, 0, bytes.Length);
                        discoverableCount++;
                    }
                }

                ms.Seek(0, 0);
                ms.Write(BitConverter.GetBytes(discoverableCount), 0, sizeof(int));

                ms.Flush();

                return ms.ToArray();
           }            
        }

        private static List<PeerListennerEndPoint> DeserializeRemoteListennerList(byte[] data)
        {
            List<PeerListennerEndPoint> result = new List<PeerListennerEndPoint>();

            int offset = 0;
            int numElements = BitConverter.ToInt32(data, offset); offset += sizeof(int);

            for (int i = 0; i < numElements; i++)
            {
                int size = BitConverter.ToInt32(data, offset); offset += sizeof(int);

                if (size > data.Length)
                    throw new Exception();

                byte[] peerData = new byte[size];
                Buffer.BlockCopy(data, offset, peerData, 0, peerData.Length); offset += peerData.Length;
                result.Add(PeerListennerEndPoint.Deserialize(peerData));
            }

            return result;
        }

        #endregion
    }
}
