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
using System.Net;
using System.Diagnostics;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A connection object which utilises <see href="http://en.wikipedia.org/wiki/User_Datagram_Protocol">UDP</see> to communicate between peers.
    /// </summary>
    public partial class UDPConnection : Connection
    {
        /// <summary>
        /// The local udp connection listeners
        /// </summary>
        static Dictionary<IPEndPoint, UDPConnection> udpConnectionListeners = new Dictionary<IPEndPoint, UDPConnection>();
        static object udpClientListenerLocker = new object();
  
        /// <summary>
        /// The rogue udp connection is used for sending ONLY if no available locally bound client is available
        /// </summary>
        static UDPConnection udpRogueSender;
        static object udpRogueSenderCreationLocker = new object();

        /// <summary>
        /// The maximum datagram size limit for udp
        /// </summary>
        const int maximumSingleDatagramSizeBytes = 65506;

        /// <summary>
        /// Static construtor which creates the rogue sender
        /// </summary>
        static UDPConnection()
        {
            lock (udpRogueSenderCreationLocker)
            {
                if (udpRogueSender == null && !NetworkComms.commsShutdown)
                    udpRogueSender = new UDPConnection(new ConnectionInfo(true, ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(IPAddress.Any, 0)), NetworkComms.DefaultSendReceiveOptions, UDPOptions.None, false);
            }
        }

        /// <summary>
        /// Create a UDP connection with the provided connectionInfo. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.GetExistingConnection()"/> and overrides.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="level">The UDP level to use for this connection</param>
        /// <param name="listenForReturnPackets">If set to true will ensure that reply packets are handled</param>
        /// <returns>Returns a <see cref="UDPConnection"/></returns>
        public static UDPConnection GetConnection(ConnectionInfo connectionInfo, UDPOptions level, bool listenForReturnPackets = true)
        {
            return GetConnection(connectionInfo, NetworkComms.DefaultSendReceiveOptions, level, listenForReturnPackets, null);
        }

        /// <summary>
        /// Create a UDP connection with the provided connectionInfo and and sets the connection default SendReceiveOptions. If there is an existing connection that is returned instead.
        /// If a new connection is created it will be registered with NetworkComms and can be retreived using <see cref="NetworkComms.GetExistingConnection()"/>.
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo to be used to create connection</param>
        /// <param name="defaultSendRecieveOptions">The SendReceiveOptions to use as defaults for this connection</param>
        /// <param name="level">The UDP options to use for this connection</param>
        /// <param name="listenForReturnPackets">If set to true will ensure that reply packets can be received</param>
        /// <returns>Returns a <see cref="UDPConnection"/></returns>
        public static UDPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendRecieveOptions, UDPOptions level, bool listenForReturnPackets = true)
        {
            return GetConnection(connectionInfo, defaultSendRecieveOptions, level, listenForReturnPackets, null);
        }

        /// <summary>
        /// Internal UDP creation method that performs the necessary tasks
        /// </summary>
        /// <param name="connectionInfo"></param>
        /// <param name="defaultSendRecieveOptions"></param>
        /// <param name="level"></param>
        /// <param name="listenForReturnPackets"></param>
        /// <param name="existingConnection"></param>
        /// <returns></returns>
        internal static UDPConnection GetConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendRecieveOptions, UDPOptions level, bool listenForReturnPackets, UDPConnection existingConnection)
        {
            connectionInfo.ConnectionType = ConnectionType.UDP;

            UDPConnection connection = null;
            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                if (NetworkComms.ConnectionExists(connectionInfo.RemoteEndPoint, ConnectionType.UDP))
                    connection = (UDPConnection)NetworkComms.GetExistingConnection(connectionInfo.RemoteEndPoint, ConnectionType.UDP);
                else
                {
                    //If we are listening on what will be the outgoing adaptor we send with that client to ensure if our connection info is handed off we are connectable by others
                    if (existingConnection == null)
                    {
                        try
                        {
                            Socket testSocket = new Socket(connectionInfo.RemoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                            testSocket.Connect(connectionInfo.RemoteEndPoint);

                            lock (udpClientListenerLocker)
                            {
                                IPEndPoint existingLocalEndPoint = ExistingLocalListenEndPoints(((IPEndPoint)testSocket.LocalEndPoint).Address);
                                if (existingLocalEndPoint != null)
                                {
                                    existingConnection = udpConnectionListeners[existingLocalEndPoint];

                                    //If we are using an existing listener there is no need to listen for packets
                                    listenForReturnPackets = false;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Failed to determine preferred existing udpClientListener to " + connectionInfo.RemoteEndPoint.Address + ":" + connectionInfo.RemoteEndPoint.Port + ". Will create an isolated udp connection instead.");
                        }
                    }

                    connection = new UDPConnection(connectionInfo, defaultSendRecieveOptions, level, listenForReturnPackets, existingConnection);
                }
            }

            TriggerConnectionKeepAliveThread();

            return connection;
        }

        /// <summary>
        /// Listen for incoming UDP packets on all allowed local IP's on default port.
        /// </summary>
        public static void StartListening()
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
                            StartListening(new IPEndPoint(ip, NetworkComms.DefaultListenPort), false);
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
                StartListening(new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), true);
        }

        /// <summary>
        /// Listen for incoming UDP packets on specified <see cref="IPEndPoint"/>. 
        /// </summary>
        /// <param name="newLocalEndPoint">The localEndPoint to listen for packets on</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true)
        {
            lock (udpClientListenerLocker)
            {
                //If a listener has already been added there is no need to continue
                if (udpConnectionListeners.ContainsKey(newLocalEndPoint)) return;

                UDPConnection newListeningConnection;

                try
                {
                    newListeningConnection = new UDPConnection(new ConnectionInfo(true, ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), newLocalEndPoint), NetworkComms.DefaultSendReceiveOptions, UDPOptions.None, true);
                }
                catch (SocketException)
                {
                    if (useRandomPortFailOver)
                        newListeningConnection = new UDPConnection(new ConnectionInfo(true, ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(newLocalEndPoint.Address, 0)), NetworkComms.DefaultSendReceiveOptions, UDPOptions.None, true);
                    else
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Error("It was not possible to open port #" + newLocalEndPoint.Port + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                        throw new CommsSetupShutdownException("It was not possible to open port #" + newLocalEndPoint.Port + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }

                IPEndPoint ipEndPointUsed = (IPEndPoint)newListeningConnection.udpClientThreadSafe.LocalEndPoint;

                if (udpConnectionListeners.ContainsKey(ipEndPointUsed))
                    throw new CommsSetupShutdownException("Unable to add new UDP listenerInstance to udpConnectionListeners as there is an existing entry.");
                else
                {
                    udpConnectionListeners.Add(ipEndPointUsed, newListeningConnection);
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Added new UDP listener localEndPoint - " + ipEndPointUsed.Address + ":" + ipEndPointUsed.Port);
                }
            }

            TriggerConnectionKeepAliveThread();
        }

        /// <summary>
        /// Listen for incoming UDP packets on provided list of <see cref="IPEndPoint"/>. 
        /// </summary>
        /// <param name="localEndPoints">The localEndPoints to listen for packets on.</param>
        /// <param name="useRandomPortFailOver">If true and the requested local port is not available will select one at random. If false and a port is unavailable will throw <see cref="CommsSetupShutdownException"/></param>
        public static void StartListening(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
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
        /// Returns a list of <see cref="IPEndPoint"/> corresponding with all UDP local listeners
        /// </summary>
        /// <returns>List of <see cref="IPEndPoint"/> corresponding with all UDP local listeners</returns>
        public static List<IPEndPoint> ExistingLocalListenEndPoints()
        {
            lock (udpClientListenerLocker)
                return udpConnectionListeners.Keys.ToList();
        }

        /// <summary>
        /// Returns an <see cref="IPEndPoint"/> corresponding to a possible local listener on the provided <see cref="IPAddress"/>. If not listening on provided <see cref="IPAddress"/> returns null.
        /// </summary>
        /// <param name="ipAddress">The <see cref="IPAddress"/> to match to a possible local listener</param>
        /// <returns>If listener exists returns <see cref="IPAddress"/> otherwise null</returns>
        public static IPEndPoint ExistingLocalListenEndPoints(IPAddress ipAddress)
        {
            lock (udpClientListenerLocker)
                return (from current in udpConnectionListeners.Keys where current.Address.Equals(ipAddress) select current).FirstOrDefault();
        }

        /// <summary>
        /// Returns true if there is atleast one local UDP listeners
        /// </summary>
        /// <returns></returns>
        public static bool Listening()
        {
            lock (udpClientListenerLocker)
                return udpConnectionListeners.Count > 0;
        }

        /// <summary>
        /// Shutdown everything UDP related
        /// </summary>
        internal static void Shutdown()
        {
            //Close any established udp listeners
            try
            {
                CloseAndRemoveAllLocalConnectionListeners();
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "UDPCommsShutdownError");
            }
        }

        /// <summary>
        /// Close down all local UDP listeners
        /// </summary>
        private static void CloseAndRemoveAllLocalConnectionListeners()
        {
            lock (udpClientListenerLocker)
            {
                try
                {
                    foreach (var connection in udpConnectionListeners.Values)
                    {
                        try
                        {
                            connection.CloseConnection(false, -7);
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
                finally
                {
                    //Once we have stopped all listeners we set the list to null incase we want to resart listening
                    udpConnectionListeners = new Dictionary<IPEndPoint, UDPConnection>();
                }
            }
        }

        /// <summary>
        /// Sends a single object to the provided IPAddress and Port. NOTE: Any possible reply will be ignored unless listening for incoming udp packets. 
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        /// <param name="objectToSend">The object to send.</param>
        /// <param name="ipAddress">The destination IP address. Supports multicast addresses such as 192.168.0.255 etc</param>
        /// <param name="port">The destination port.</param>
        public static void SendObject(string sendingPacketType, object objectToSend, string ipAddress, int port)
        {
            SendObject(sendingPacketType, objectToSend, new IPEndPoint(IPAddress.Parse(ipAddress), port));
        }

        /// <summary>
        /// Sends a single object to the provided endPoint. NOTE: Any possible reply will be ignored unless listening for incoming udp packets. 
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        /// <param name="objectToSend">The object to send</param>
        /// <param name="ipEndPoint">The destination IPEndPoint. Supports multicast endpoints.</param>
        public static void SendObject(string sendingPacketType, object objectToSend, IPEndPoint ipEndPoint)
        {
            UDPConnection connectionToUse = udpRogueSender;

            //If we are listening on what will be the outgoing adaptor we send with that client to ensure reply packets are collected
            //Determining this is annoyingly non-trivial

            //For now we will use the following method and look to improve upon it in future
            //Some very quick testing gave an average runtime of this method to be 0.12ms (1000 iterations) (perhaps not so bad after all)
            try
            {
                Socket testSocket = new Socket(ipEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                testSocket.Connect(ipEndPoint);

                lock (udpClientListenerLocker)
                {
                    IPEndPoint existingLocalEndPoint = ExistingLocalListenEndPoints(((IPEndPoint)testSocket.LocalEndPoint).Address);
                    if (existingLocalEndPoint != null)
                        connectionToUse = udpConnectionListeners[existingLocalEndPoint];
                }
            }
            catch (Exception)
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Failed to determine preferred existing udpClientListener to " + ipEndPoint.Address + ":" + ipEndPoint.Port + ". Will just use the rogue udp sender instead.");
            }

            Packet sendPacket = new Packet(sendingPacketType, objectToSend, connectionToUse.ConnectionDefaultSendReceiveOptions);
            connectionToUse.SendPacketSpecific(sendPacket, ipEndPoint);
        }
    }
}
