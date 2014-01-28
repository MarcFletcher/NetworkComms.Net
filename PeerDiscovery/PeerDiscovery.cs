using DPSBase;
using NetworkCommsDotNet;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace NetworkCommsDotNet.PeerDiscovery
{
    /// <summary>
    /// Provides the ability to discover discoverable peers in the 
    /// </summary>
    public static class PeerDiscovery
    {
        #region Public Properties
        /// <summary>
        /// The wait time in MS before all peers discovered are returned. Default 2000
        /// </summary>
        public static int DefaultDiscoverTimeMS { get; set; }

        /// <summary>
        /// The minimum port number that will be used when making this peer discoverable. Default 10000
        /// </summary>
        public static int MinTargetLocalPort { get; set; }

        /// <summary>
        /// The maximum port number that will be used when making this peer discoverable. Default 10099
        /// </summary>
        public static int MaxTargetLocalPort { get; set; }
        #endregion

        #region Private Properties
        /// <summary>
        /// A private object to ensure thread safety
        /// </summary>
        private static object _syncRoot = new object();

        /// <summary>
        /// The packet type used for peer discovery
        /// </summary>
        private static string discoveryPacketType = "PeerDiscovery";

        /// <summary>
        /// Listeners associated with this peers discover status
        /// </summary>
        private static Dictionary<ConnectionType, List<ConnectionListenerBase>> _discoveryListeners = new Dictionary<ConnectionType, List<ConnectionListenerBase>>();

        /// <summary>
        /// A dictionary which records discovered peers
        /// </summary>
        private static Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>> _discoveredPeers = new Dictionary<ConnectionType, Dictionary<EndPoint, DateTime>>();
        #endregion

        static PeerDiscovery()
        {
            DefaultDiscoverTimeMS = 2000;

            MinTargetLocalPort = 10000;
            MaxTargetLocalPort = 10099;
        }

        #region Local Configuration
        /// <summary>
        /// Make this peer discoverable using the provided connection type. 
        /// </summary>
        /// <param name="connectionType"></param>
        public static void EnableDiscoverable(ConnectionType connectionType)
        {
            lock (_syncRoot)
            {
                if (_discoveryListeners.ContainsKey(connectionType))
                    throw new ArgumentException("Peer is already discoverable for the provided connectionType", "connectionType");

                //Based on the connection type select all local endPoints and then enable discoverable
                if (connectionType == ConnectionType.TCP || connectionType == ConnectionType.UDP)
                {
                    List<ConnectionListenerBase> listeners = null;

                    try
                    {
                        listeners = Connection.StartListening(connectionType, new IPEndPoint(IPAddress.Any, MinTargetLocalPort));
                    }
                    catch (Exception)
                    {
                        //Keep trying to listen on an ever increasing port number
                        for (int tryPort = MinTargetLocalPort; tryPort <= MaxTargetLocalPort; tryPort++)
                        {
                            try
                            {
                                listeners = Connection.StartListening(connectionType, new IPEndPoint(IPAddress.Any, tryPort));

                                //Once we are successfully listening we can break
                                break;
                            }
                            catch (Exception) { }

                            if (tryPort == MaxTargetLocalPort)
                                throw new CommsSetupShutdownException("Failed to find local available listen port while trying to make this peer discoverable.");
                        }
                    }

                    _discoveryListeners.Add(connectionType, listeners);
                }
                else
                    throw new NotImplementedException("This feature has not been implemented for the provided connection type.");

                //Add the packet handlers if required
                if (!NetworkComms.GlobalIncomingPacketHandlerExists<byte[]>(discoveryPacketType, PeerDiscoveryHandler))
                    NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
            }
        }

        /// <summary>
        /// Make this peer discoverable using the provided connection type and local end point.
        /// </summary>
        /// <param name="connectionType"></param>
        /// <param name="localDiscoveryEndPoint"></param>
        public static void EnableDiscoverable(ConnectionType connectionType, EndPoint localDiscoveryEndPoint)
        {
            lock (_syncRoot)
            {
                if (_discoveryListeners.ContainsKey(connectionType))
                    throw new ArgumentException("Peer is already discoverable for the provided connectionType", "connectionType");

                //Based on the connection type select all local endPoints and then enable discoverable
                _discoveryListeners.Add(connectionType, Connection.StartListening(connectionType, localDiscoveryEndPoint));

                //Add the packet handlers if required
                if (!NetworkComms.GlobalIncomingPacketHandlerExists<byte[]>(discoveryPacketType, PeerDiscoveryHandler))
                    NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
            }
        }

        /// <summary>
        /// Disable this peers discoverable status for the provided connection type. 
        /// </summary>
        /// <param name="connectionType">The connection type to disable discovery for. Use ConnectionType.Undefined to match all.</param>
        public static void DisableDiscoverable(ConnectionType connectionType)
        {
            lock (_syncRoot)
            {
                if (connectionType == ConnectionType.Undefined)
                {
                    foreach (ConnectionType currentType in _discoveryListeners.Keys)
                        Connection.StopListening(_discoveryListeners[currentType]);

                    _discoveryListeners = new Dictionary<ConnectionType, List<ConnectionListenerBase>>();

                }
                else if (_discoveryListeners.ContainsKey(connectionType))
                {
                    Connection.StopListening(_discoveryListeners[connectionType]);
                    _discoveryListeners.Remove(connectionType);
                }
            }
        }

        /// <summary>
        /// Returns true if local discovery endPoints exist for the provided connectionType.
        /// </summary>
        /// <param name="connectionType"></param>
        /// <returns></returns>
        public static bool IsDiscoverable(ConnectionType connectionType)
        {
            return LocalDiscoveryEndPoints(connectionType).Count > 0;
        }

        /// <summary>
        /// Returns the local endpoints that are currently used to make this peer discoverable.
        /// </summary>
        /// <returns></returns>
        public static List<EndPoint> LocalDiscoveryEndPoints(ConnectionType connectionType)
        {
            Dictionary<ConnectionType, List<EndPoint>> result = LocalDiscoveryEndPoints();

            if (result.ContainsKey(connectionType))
                return result[connectionType];
            else
                return new List<EndPoint>();
        }

        /// <summary>
        /// Returns the local endpoints that are currently used to make this peer discoverable.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<ConnectionType, List<EndPoint>> LocalDiscoveryEndPoints()
        {
            Dictionary<ConnectionType, List<EndPoint>> result = new Dictionary<ConnectionType, List<EndPoint>>();

            lock (_syncRoot)
            {
                foreach (ConnectionType currentType in _discoveryListeners.Keys)
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
        /// <param name="connectionType">The connection type to use for discovering peers.</param>
        /// <returns></returns>
        public static List<EndPoint> DiscoverPeers(ConnectionType connectionType)
        {
            return DiscoverPeers(connectionType, DefaultDiscoverTimeMS);
        }

        /// <summary>
        /// Discover local peers using the provided connection type. Returns connectionInfos of discovered peers.
        /// </summary>
        /// <param name="connectionType">The connection type to use for discovering peers.</param>
        /// <param name="discoverTimeMS">The wait time in MS before all peers discovered are returned.</param>
        /// <returns></returns>
        public static List<EndPoint> DiscoverPeers(ConnectionType connectionType, int discoverTimeMS)
        {
            List<EndPoint> result;

            if (connectionType == ConnectionType.UDP)
                result = DiscoverPeersUDP(discoverTimeMS);
            else if (connectionType == ConnectionType.TCP)
                result = DiscoverPeersTCP(discoverTimeMS);
            else
                throw new NotImplementedException("Peer discovery has not been implemented for the provided connection type.");

            return result;
        }

        private static List<EndPoint> DiscoverPeersUDP(int discoverTimeMS)
        {
            //We use UDP broadcasting to contact peers

            //Get a list of all multicast addressees for all adaptors
            List<IPEndPoint> addressesToContact = new List<IPEndPoint>();

            throw new NotImplementedException();
        }

        private static List<EndPoint> DiscoverPeersTCP(int discoverTimeMS)
        {
            //We have to try and manually connect to all peers and see if they respond

            //Get a list of all IPEndPoint that we should try and connect to
            //This requires the network and peer portion of current IP addresses
            List<IPEndPoint> addressesToContact = new List<IPEndPoint>();

            throw new NotImplementedException();
        }

        #region Incoming Comms Handlers
        /// <summary>
        /// Handle the incoming peer discovery packet
        /// </summary>
        /// <param name="header"></param>
        /// <param name="connection"></param>
        /// <param name="data"></param>
        private static void PeerDiscoveryHandler(PacketHeader header, Connection connection, byte[] data)
        {
            if (data.Length != 1) throw new ArgumentException("Idiot check exception");

            if (data[0] == 0)
            {
                //This is a peer discovery request, we just need to let the other peer know we are alive
                connection.SendObject(discoveryPacketType, new byte[] { 1 });
            }
            else
            {
                //This is a peer discovery reply, we need to add this to the tracking dictionary
                lock (_syncRoot)
                {
                    if (_discoveredPeers.ContainsKey(connection.ConnectionInfo.ConnectionType))
                        _discoveredPeers[connection.ConnectionInfo.ConnectionType][connection.ConnectionInfo.RemoteEndPoint] = DateTime.Now;
                    else
                        _discoveredPeers.Add(connection.ConnectionInfo.ConnectionType, new Dictionary<EndPoint,DateTime>() 
                            { {connection.ConnectionInfo.RemoteEndPoint, DateTime.Now} });
                }
            }
        }
        #endregion
    }
}
