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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Diagnostics;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Connections.UDP
{
    public sealed partial class UDPConnection : IPConnection
    {
        /// <summary>
        /// By default a UDP datagram sent to an unreachable destination will result in an ICMP Destination Unreachable 
        /// packet. This can result in a SocketException on the local end. To avoid this behaviour these ICMP packets 
        /// are ignored by default, i.e. this value is set to true. Setting this value to false could cause new UDP 
        /// connections to close, possibly unexpectedly.
        /// </summary>
        public static bool IgnoreICMPDestinationUnreachable { get; set; }

        /// <summary>
        /// The default UDPOptions to use where none are otherwise specified.
        /// </summary>
        public static UDPOptions DefaultUDPOptions { get; set; }

        /// <summary>
        /// The rogue UDP connection is used for sending ONLY if no available locally bound client is available.
        /// First key is address family of rogue sender, second key is value of ApplicationLayerProtocolEnabled.
        /// Third key is local IPEndPoint of the rouge sender
        /// </summary>
        static Dictionary<ApplicationLayerProtocolStatus, Dictionary<IPEndPoint, UDPConnection>> udpRogueSenders = new Dictionary<ApplicationLayerProtocolStatus, Dictionary<IPEndPoint, UDPConnection>>();
        static object udpRogueSenderCreationLocker = new object();

        /// <summary>
        /// The maximum datagram size limit for UDP
        /// </summary>
        const int maximumSingleDatagramSizeBytes = 65506;

        /// <summary>
        /// Static constructor which creates the rogue sender
        /// </summary>
        static UDPConnection()
        {
            IgnoreICMPDestinationUnreachable = true;
        }

        #region GetConnection
        /// <summary>
        /// Create a UDP connection with the provided connectionInfo. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retrieved using 
        /// <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="level">The UDP level to use for this connection</param>
        /// <param name="listenForReturnPackets">If set to true will ensure that reply packets are handled</param>
        /// <param name="establishIfRequired">Will establish the connection, triggering connection establish delegates if a 
        /// new connection is returned</param>
        /// <returns>Returns a <see cref="UDPConnection"/></returns>
        public static UDPConnection GetConnection(ConnectionInfo connectionInfo, UDPOptions level, bool listenForReturnPackets = true, bool establishIfRequired = true)
        {
            return GetConnection(connectionInfo, level, NetworkComms.DefaultSendReceiveOptions, listenForReturnPackets, null, null, establishIfRequired);
        }

        /// <summary>
        /// Create a UDP connection with the provided connectionInfo and and sets the connection default SendReceiveOptions. 
        /// If there is an existing connection that is returned instead. If a new connection is created it will be registered 
        /// with NetworkComms and can be retrieved using <see cref="NetworkComms.GetExistingConnection(ConnectionInfo)"/>.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions to use as defaults for this connection</param>
        /// <param name="level">The UDP options to use for this connection</param>
        /// <param name="listenForReturnPackets">If set to true will ensure that reply packets can be received</param>
        /// <param name="establishIfRequired">Will establish the connection, triggering connection establish delegates if a 
        /// new connection is returned</param>
        /// <returns>Returns a <see cref="UDPConnection"/></returns>
        public static UDPConnection GetConnection(ConnectionInfo connectionInfo, UDPOptions level, SendReceiveOptions defaultSendReceiveOptions, bool listenForReturnPackets = true, bool establishIfRequired = true)
        {
            return GetConnection(connectionInfo, level, defaultSendReceiveOptions, listenForReturnPackets, null, null, establishIfRequired);
        }

        /// <summary>
        /// Internal UDP creation method that performs the necessary tasks
        /// </summary>
        /// <param name="connectionInfo"></param>
        /// <param name="defaultSendReceiveOptions"></param>
        /// <param name="level"></param>
        /// <param name="listenForReturnPackets"></param>
        /// <param name="existingListenerConnection"></param>
        /// <param name="possibleHandshakeUDPDatagram"></param>
        /// <param name="establishIfRequired">Will establish the connection, triggering connection establish delegates if a new 
        /// connection is returned</param>
        /// <returns></returns>
        internal static UDPConnection GetConnection(ConnectionInfo connectionInfo, UDPOptions level, SendReceiveOptions defaultSendReceiveOptions, bool listenForReturnPackets, UDPConnection existingListenerConnection, HandshakeUDPDatagram possibleHandshakeUDPDatagram, bool establishIfRequired = true)
        {
            connectionInfo.ConnectionType = ConnectionType.UDP;

            bool newConnection = false;
            UDPConnection connection = null;
            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                List<Connection> existingConnections = NetworkComms.GetExistingConnection(connectionInfo.RemoteIPEndPoint, connectionInfo.LocalIPEndPoint, ConnectionType.UDP, connectionInfo.ApplicationLayerProtocol);
                if (existingConnections.Count > 0)
                    connection = (UDPConnection)existingConnections[0];
                else
                {
                    //If we are listening on what will be the outgoing adaptor we send with that client to ensure if our connection info is handed off we are connectable by others
                    if (existingListenerConnection == null)
                    {
                        try
                        {
                            IPEndPoint localEndPoint = IPTools.BestLocalEndPoint(connectionInfo.RemoteIPEndPoint);
                            //Set the port to 0 so that we match any listener
                            localEndPoint.Port = 0;
                            List<UDPConnectionListener> existingListeners = Connection.ExistingLocalListeners<UDPConnectionListener>(localEndPoint);

                            for (int i = 0; i < existingListeners.Count; i++)
                            {
                                if (existingListeners[i].UDPConnection.ConnectionInfo.ApplicationLayerProtocol == connectionInfo.ApplicationLayerProtocol)
                                {
                                    existingListenerConnection = existingListeners[i].UDPConnection;

                                    //If we are using an existing listener there is no need to listen for packets
                                    listenForReturnPackets = false;

                                    //Once we have a matching connection we can break
                                    break;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Failed to determine preferred existing udpClientListener to " + connectionInfo.RemoteIPEndPoint.Address + ":" + connectionInfo.RemoteIPEndPoint.Port.ToString() + ". Will create an isolated udp connection instead.");
                        }
                    }

                    //If an existing connection does not exist but the info we are using suggests it should we need to reset the info
                    //so that it can be reused correctly. This case generally happens when using Comms in the format 
                    //UDPConnection.GetConnection(info).SendObject(packetType, objToSend);
                    if (connectionInfo.ConnectionState == ConnectionState.Established || connectionInfo.ConnectionState == ConnectionState.Shutdown)
                        connectionInfo.ResetConnectionInfo();

                    connection = new UDPConnection(connectionInfo, defaultSendReceiveOptions, level, listenForReturnPackets, existingListenerConnection);
                    newConnection = true;
                }
            }

            //If we expect a UDP handshake we need to handle incoming datagrams here, if we have it available,
            //  before trying to establish the connection.
            //This is different for TCP connections because things happen in the reverse order
            //UDP - Already listening, receive connectionsetup, configure connection
            //TCP - Receive TCPClient, configure connection, start listening for connectionsetup, wait for connectionsetup
            //
            //possibleHandshakeUDPDatagram will only be set when GetConnection() is called from a listener
            //If multiple threads try to create an outgoing UDP connection to the same endPoint all but the originating 
            //thread will be held on connection.WaitForConnectionEstablish();
            if (possibleHandshakeUDPDatagram != null &&
                (connection.ConnectionUDPOptions & UDPOptions.Handshake) == UDPOptions.Handshake)
            {
                lock (connection.packetBuilder.Locker)
                {
                    connection.packetBuilder.AddPartialPacket(possibleHandshakeUDPDatagram.DatagramBytes.Length, possibleHandshakeUDPDatagram.DatagramBytes);
                    if (connection.packetBuilder.TotalBytesCached > 0) connection.IncomingPacketHandleHandOff(connection.packetBuilder);
                }

                if (connection.packetBuilder.TotalPartialPacketCount > 0)
                    LogTools.LogException(new Exception("Packet builder had remaining packets after a call to IncomingPacketHandleHandOff. Until sequenced packets are implemented this indicates a possible error."), "UDPConnectionError");

                possibleHandshakeUDPDatagram.DatagramHandled = true;
            }

            //We must perform the establish outside the lock as for TCP connections
            if (newConnection && establishIfRequired)
            {
                //Call establish on the connection if it is not a rogue sender or listener
                if (!connectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.Any) && !connectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.IPv6Any))
                    connection.EstablishConnection();
            }
            else if (!newConnection)
                connection.WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS);

            //UDP does not need keep alives
            //if (!NetworkComms.commsShutdown)
            //    TriggerConnectionKeepAliveThread();

            return connection;
        }
        #endregion

        #region Static SendObject
        /// <summary>
        /// Sends a single object to the provided IPAddress and Port. NOTE: Any possible reply will be ignored unless listening 
        /// for incoming UDP packets. 
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        /// <param name="objectToSend">The object to send.</param>
        /// <param name="ipAddress">The destination IP address. Supports multicast addresses such as 192.168.0.255 etc.</param>
        /// <param name="port">The destination port.</param>
        public static void SendObject(string sendingPacketType, object objectToSend, string ipAddress, int port)
        {
            IPAddress ipAddressParse;
            if(!IPAddress.TryParse(ipAddress, out ipAddressParse))
                throw new ArgumentException("Provided ipAddress string was not successfully parsed.", "ipAddress");

            SendObject(sendingPacketType, objectToSend, new IPEndPoint(ipAddressParse, port));
        }

        /// <summary>
        /// Sends a single object to the provided endPoint. NOTE: Any possible reply will be ignored unless listening for incoming UDP packets. 
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        /// <param name="objectToSend">The object to send</param>
        /// <param name="ipEndPoint">The destination IPEndPoint. Supports multicast endpoints.</param>
        public static void SendObject(string sendingPacketType, object objectToSend, IPEndPoint ipEndPoint)
        {
            SendObject(sendingPacketType, objectToSend, ipEndPoint, NetworkComms.DefaultSendReceiveOptions);
        }

        /// <summary>
        /// Sends a single object to the provided endPoint. NOTE: Any possible reply will be ignored unless listening for incoming UDP packets. 
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        /// <param name="objectToSend">The object to send</param>
        /// <param name="ipEndPoint">The destination IPEndPoint. Supports multicast endpoints.</param>
        /// <param name="sendReceiveOptions">The sendReceiveOptions to use for this send</param>
        public static void SendObject(string sendingPacketType, object objectToSend, IPEndPoint ipEndPoint, SendReceiveOptions sendReceiveOptions)
        {
            SendObject(sendingPacketType, objectToSend, ipEndPoint, sendReceiveOptions, ApplicationLayerProtocolStatus.Enabled);
        }

        /// <summary>
        /// Sends a single object to the provided endPoint. NOTE: Any possible reply will be ignored unless listening for incoming UDP packets. 
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        /// <param name="objectToSend">The object to send</param>
        /// <param name="ipEndPoint">The destination IPEndPoint. Supports multicast endpoints.</param>
        /// <param name="sendReceiveOptions">The sendReceiveOptions to use for this send</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you use the NetworkComms.Net application layer protocol.</param>
        public static void SendObject<sendObjectType>(string sendingPacketType, sendObjectType objectToSend, IPEndPoint ipEndPoint, SendReceiveOptions sendReceiveOptions, ApplicationLayerProtocolStatus applicationLayerProtocol)
        {
            Packet sendPacket = objectToSend as Packet;
            if (sendPacket == null)
                sendPacket = new Packet(sendingPacketType, objectToSend, sendReceiveOptions);
            else
            {
                if (sendPacket.PacketHeader.PacketType != sendingPacketType)
                    throw new ArgumentException("Unable to send object of type Packet if the PacketHeader.PacketType and sendingPacketType do not match.");
            }

            SendObject<sendObjectType>(sendPacket, ipEndPoint, sendReceiveOptions, applicationLayerProtocol);
        }

        /// <summary>
        /// Sends a <see cref="Packet"/> to the provided endPoint. Offers more performance if an identical packet is being sent to multiple peers. 
        /// NOTE: Any possible reply will be ignored unless listening for incoming UDP packets. 
        /// </summary>
        /// <typeparam name="packetPayloadObjectType">The type of object encapsulated by the provided packet</typeparam>
        /// <param name="packetToSend">The packet to send</param>
        /// <param name="ipEndPoint">The destination IPEndPoint. Supports multicast endpoints.</param>
        /// <param name="sendReceiveOptions">The sendReceiveOptions to use for this send</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you use the NetworkComms.Net application layer protocol.</param>
        public static void SendObject<packetPayloadObjectType>(IPacket packetToSend, IPEndPoint ipEndPoint, SendReceiveOptions sendReceiveOptions, ApplicationLayerProtocolStatus applicationLayerProtocol)
        {
            if (ipEndPoint == null) throw new ArgumentNullException("ipEndPoint");
            if (sendReceiveOptions == null) throw new ArgumentNullException("sendReceiveOptions");

            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Undefined)
                throw new ArgumentException("A value of ApplicationLayerProtocolStatus.Undefined is invalid when using this method.", "applicationLayerProtocol");

            if (sendReceiveOptions.Options.ContainsKey("ReceiveConfirmationRequired"))
                throw new ArgumentException("Attempted to use a rouge UDP sender when the provided send receive" +
                    " options specified the ReceiveConfirmationRequired option, which is unsupported. Please create a specific connection" +
                    "instance to use this feature.", "sendReceiveOptions");

            //Check the send receive options
            if (applicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
            {
                if (sendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new ArgumentException("Attempted to use a rouge UDP sender when the provided send receive" +
                        " options serialiser was not NullSerializer. Please provide compatible send receive options in order to successfully" +
                        " instantiate this unmanaged connection.", "sendReceiveOptions");

                if (sendReceiveOptions.DataProcessors.Count > 0)
                    throw new ArgumentException("Attempted to use a rouge UDP sender when the provided send receive" +
                        " options contains data processors. Data processors may not be used with unmanaged connections." +
                        " Please provide compatible send receive options in order to successfully instantiate this unmanaged connection.", "sendReceiveOptions");
            }

            List<UDPConnection> connectionsToUse = null;

            //Initialise best local end point as match all
            IPEndPoint bestLocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                bestLocalEndPoint = IPTools.BestLocalEndPoint(ipEndPoint);
                //Set the port to 0 to match all.
                bestLocalEndPoint.Port = 0;
            }
            catch (Exception ex)
            {
                LogTools.LogException(ex, "BestLocalEndPointError");
            }

            //If we are already listening on what will be the outgoing adaptor we can send with that client to ensure reply packets are collected
            //The exception here is the broadcasting which goes out all adaptors
            if (ipEndPoint.Address != IPAddress.Broadcast)
            {
                #region Check For Existing Local Listener
                List<UDPConnectionListener> existingListeners = Connection.ExistingLocalListeners<UDPConnectionListener>(bestLocalEndPoint);

                for (int i = 0; i < existingListeners.Count; i++)
                {
                    if (existingListeners[i].UDPConnection.ConnectionInfo.ApplicationLayerProtocol == applicationLayerProtocol)
                    {
                        connectionsToUse = new List<UDPConnection> { existingListeners[i].UDPConnection };

                        //Once we have a matching connection we can break
                        break;
                    }
                }
                #endregion

                //If we have not picked up an existing listener we need to use/create a rougeSender
                if (connectionsToUse == null)
                {
                    #region Check For Suitable Rouge Sender
                    lock (udpRogueSenderCreationLocker)
                    {
                        if (NetworkComms.commsShutdown)
                            throw new CommunicationException("Attempting to send UDP packet but NetworkCommsDotNet is in the process of shutting down.");
                        else
                        {
                            if (!udpRogueSenders.ContainsKey(applicationLayerProtocol) ||
                                !udpRogueSenders[applicationLayerProtocol].ContainsKey(bestLocalEndPoint) ||
                                udpRogueSenders[applicationLayerProtocol][bestLocalEndPoint].ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                            {
                                //Create a new rogue sender
                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Creating UDPRougeSender.");

                                if (!udpRogueSenders.ContainsKey(applicationLayerProtocol))
                                    udpRogueSenders.Add(applicationLayerProtocol, new Dictionary<IPEndPoint, UDPConnection>());

                                IPAddress anyRemoteIP = AnyRemoteIPAddress(ipEndPoint.AddressFamily);
                                udpRogueSenders[applicationLayerProtocol][bestLocalEndPoint] = new UDPConnection(new ConnectionInfo(ConnectionType.UDP, new IPEndPoint(anyRemoteIP, 0), bestLocalEndPoint, applicationLayerProtocol), sendReceiveOptions, UDPConnection.DefaultUDPOptions, false);
                            }

                            connectionsToUse = new List<UDPConnection> { udpRogueSenders[applicationLayerProtocol][bestLocalEndPoint] };
                        }
                    }
                    #endregion
                }
            }
            else
            {
                #region Get A Sender On All Interfaces For Broadcast
                lock (udpRogueSenderCreationLocker)
                {
                    //We do something special for broadcasts by selected EVERY adaptor
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Getting senders for UDP broadcasting.");

                    if (!udpRogueSenders.ContainsKey(applicationLayerProtocol))
                        udpRogueSenders.Add(applicationLayerProtocol, new Dictionary<IPEndPoint, UDPConnection>());

                    connectionsToUse = new List<UDPConnection>();

                    //This is a broadcast and we need to send the broadcast over every local adaptor
                    List<IPAddress> validLocalIPAddresses = HostInfo.IP.FilteredLocalAddresses();
                    foreach (IPAddress address in validLocalIPAddresses)
                    {
                        IPEndPoint currentLocalIPEndPoint = new IPEndPoint(address, 0);
                        List<UDPConnectionListener> existingListeners = Connection.ExistingLocalListeners<UDPConnectionListener>(currentLocalIPEndPoint);

                        //If there is an existing listener we use that
                        if (existingListeners.Count > 0)
                        {
                            for (int i = 0; i < existingListeners.Count; i++)
                            {
                                if (existingListeners[i].UDPConnection.ConnectionInfo.ApplicationLayerProtocol == applicationLayerProtocol)
                                {
                                    connectionsToUse.Add(existingListeners[i].UDPConnection);

                                    //Once we have a matching connection we can break
                                    break;
                                }
                            }
                        }
                        else
                        {
                            //If not we check the rouge senders
                            if (!udpRogueSenders[applicationLayerProtocol].ContainsKey(currentLocalIPEndPoint) ||
                                udpRogueSenders[applicationLayerProtocol][currentLocalIPEndPoint].ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                            {
                                IPAddress anyRemoteIP = AnyRemoteIPAddress(currentLocalIPEndPoint.AddressFamily);

                                udpRogueSenders[applicationLayerProtocol][currentLocalIPEndPoint] = new UDPConnection(new ConnectionInfo(ConnectionType.UDP, new IPEndPoint(anyRemoteIP, 0), currentLocalIPEndPoint, applicationLayerProtocol), sendReceiveOptions, UDPConnection.DefaultUDPOptions, false);
                            }

                            connectionsToUse.Add(udpRogueSenders[applicationLayerProtocol][currentLocalIPEndPoint]);
                        }
                    }
                }
                #endregion
            }

            foreach (UDPConnection connection in connectionsToUse)
            {
                try
                {
                    connection.SendPacketSpecific<packetPayloadObjectType>(packetToSend, ipEndPoint);
                }
                catch (SocketException) { /* Ignore any socket exceptions */ }
            }

            //Dispose of the packet
            packetToSend.Dispose();
        }

        /// <summary>
        /// Provides an IP address that matches all IPAddresses of the provided targetAddressFamily
        /// </summary>
        /// <param name="targetAddressFamily"></param>
        /// <returns></returns>
        private static IPAddress AnyRemoteIPAddress(AddressFamily targetAddressFamily)
        {
            if (targetAddressFamily == AddressFamily.InterNetwork)
                return IPAddress.Any;
            else if (targetAddressFamily == AddressFamily.InterNetworkV6)
                return IPAddress.IPv6Any;
            else
                throw new CommunicationException("Attempting to send UDP packet over unsupported network address family: " + targetAddressFamily.ToString());

        }
        #endregion

        #region Depreciated
        /// <summary>
        /// Listen for incoming UDP packets on all allowed local IP's on default port.
        /// </summary>
        /// <param name="useRandomPortFailOver">If true and the default local port is not available will select one at random. If false 
        /// and a port is unavailable listening will not be enabled on that adaptor unless NetworkComms.ListenOnAllAllowedInterfaces is 
        /// false in which case a <see cref="CommsSetupShutdownException"/> will be thrown instead.</param>
        [Obsolete("Depreciated, please use Connection.StartListening.")]
        public static void StartListening(bool useRandomPortFailOver = false)
        {
            List<IPAddress> localIPs = HostInfo.IP.FilteredLocalAddresses();

            try
            {
                foreach (IPAddress ip in localIPs)
                {
                    try
                    {
                        StartListening(new IPEndPoint(ip, 0), useRandomPortFailOver);
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

        /// <summary>
        /// Listen for incoming UDP packets on provided list of <see cref="IPEndPoint"/>. 
        /// </summary>
        /// <param name="localEndPoints">The localEndPoints to listen for packets on.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. If false 
        /// and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        [Obsolete("Depreciated, please use Connection.StartListening.")]
        public static void StartListening(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
            if (localEndPoints == null) throw new ArgumentNullException("localEndPoints", "Provided List<IPEndPoint> cannot be null.");

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
        /// Listen for incoming UDP packets on specified <see cref="IPEndPoint"/>. 
        /// </summary>
        /// <param name="newLocalEndPoint">The localEndPoint to listen for packets on</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. 
        /// If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        /// <param name="allowDiscoverable">Determines if the newly created <see cref="ConnectionListenerBase"/> will be discoverable if <see cref="Tools.PeerDiscovery"/> is enabled.</param>
        [Obsolete("Depreciated, please use Connection.StartListening.")]
        public static void StartListening(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true, bool allowDiscoverable = false)
        {
            UDPConnectionListener listener = new UDPConnectionListener(NetworkComms.DefaultSendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPConnection.DefaultUDPOptions, allowDiscoverable);
            Connection.StartListening(listener, newLocalEndPoint, useRandomPortFailOver);
        }

        /// <summary>
        /// Returns a list of <see cref="IPEndPoint"/> corresponding with all UDP local listeners
        /// </summary>
        /// <returns>List of <see cref="IPEndPoint"/> corresponding with all UDP local listeners</returns>
        [Obsolete("Depreciated, please use Connection.ExistingLocalListenEndPoints.")]
        public static List<IPEndPoint> ExistingLocalListenEndPoints()
        {
            List<IPEndPoint> result = new List<IPEndPoint>();
            foreach (IPEndPoint endPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.UDP))
                result.Add(endPoint);

            return result;
        }

        /// <summary>
        /// Returns an <see cref="IPEndPoint"/> corresponding to a possible local listener on the provided <see cref="IPAddress"/>. 
        /// If not listening on provided <see cref="IPAddress"/> returns null.
        /// </summary>
        /// <param name="ipAddress">The <see cref="IPAddress"/> to match to a possible local listener</param>
        /// <returns>If listener exists returns <see cref="IPAddress"/> otherwise null</returns>
        [Obsolete("Depreciated, please use Connection.ExistingLocalListenEndPoints.")]
        public static IPEndPoint ExistingLocalListenEndPoints(IPAddress ipAddress)
        {
            List<IPEndPoint> result = new List<IPEndPoint>();
            foreach (IPEndPoint endPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.UDP, new IPEndPoint(ipAddress, 0)))
                result.Add(endPoint);

            return result[0];
        }

        /// <summary>
        /// Returns true if listening for new UDP connections.
        /// </summary>
        /// <returns>True if listening for new UDP connections.</returns>
        [Obsolete("Depreciated, please use Connection.Listening.")]
        public static bool Listening()
        {
            return Connection.Listening(ConnectionType.UDP);
        }
        #endregion
    }
}
