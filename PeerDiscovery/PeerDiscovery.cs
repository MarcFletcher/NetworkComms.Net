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
        /// <summary>
        /// The maximum time allowed in MS before a request to discover peers returns. Default 2000
        /// </summary>
        public static int DefaultMaximumDiscoverTimeMS { get; set; }

        /// <summary>
        /// The minimum port number that will be used when making this peer discoverable. Default 10000
        /// </summary>
        public static int MinTargetLocalPort { get; set; }

        /// <summary>
        /// The maximum port number that will be used when making this peer discoverable. Default 10099
        /// </summary>
        public static int MaxTargetLocalPort { get; set; }

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

        static PeerDiscovery()
        {
            DefaultMaximumDiscoverTimeMS = 2000;

            MinTargetLocalPort = 10000;
            MaxTargetLocalPort = 10099;
        }

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
            }

            //Add the packet handlers
            if (!NetworkComms.GlobalIncomingPacketHandlerExists<byte[]>(discoveryPacketType, PeerDiscoveryHandler))
                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>(discoveryPacketType, PeerDiscoveryHandler);
        }

        /// <summary>
        /// Make this peer discoverable using the provided connection type and local end point.
        /// </summary>
        /// <param name="connectionType"></param>
        /// <param name="localDiscoveryEndPoint"></param>
        public static void EnableDiscoverable(ConnectionType connectionType, EndPoint localDiscoveryEndPoint)
        {

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
        /// Discover local peers using the provided connection type. Returns connectionInfos of discovered peers.
        /// </summary>
        /// <param name="connectionType">The connection type to use for discovering peers.</param>
        /// <returns></returns>
        public static List<EndPoint> DiscoverPeers(ConnectionType connectionType)
        {
            return DiscoverPeers(connectionType, DefaultMaximumDiscoverTimeMS);
        }

        /// <summary>
        /// Discover local peers using the provided connection type. Returns connectionInfos of discovered peers.
        /// </summary>
        /// <param name="connectionType">The connection type to use for discovering peers.</param>
        /// <param name="maximumDiscoverTime">The maximum time allowed in MS before a request to discover peers returns.</param>
        /// <returns></returns>
        public static List<EndPoint> DiscoverPeers(ConnectionType connectionType, int maximumDiscoverTime)
        {
            //Discover peers asynchronously, and return after maximumDiscoverTime 

            throw new NotImplementedException("");
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
        public static Dictionary<ConnectionType,List<EndPoint>> LocalDiscoveryEndPoints()
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

        /// <summary>
        /// Handle the incoming peer discovery packet
        /// </summary>
        /// <typeparam name="?"></typeparam>
        /// <param name="header"></param>
        /// <param name="connection"></param>
        /// <param name="data"></param>
        private static void PeerDiscoveryHandler(PacketHeader header, Connection connection, byte[] data)
        {

        }
    }
}
