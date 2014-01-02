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
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Threading;
using DPSBase;

#if WINDOWS_PHONE
using Windows.Networking.Sockets;
using Windows.Networking;
using System.Runtime.InteropServices.WindowsRuntime;
#endif

namespace NetworkCommsDotNet
{
    public sealed partial class UDPConnection : Connection
    {
#if WINDOWS_PHONE
        DatagramSocket socket;
#else
        UdpClientThreadSafe udpClientThreadSafe;
#endif

        /// <summary>
        /// Options associated with this UDPConnection
        /// </summary>
        public UDPOptions ConnectionUDPOptions { get; private set; }

        /// <summary>
        /// An isolated udp connection will only accept incoming packets coming from a specific RemoteEndPoint.
        /// </summary>
        bool isIsolatedUDPConnection = false;

        /// <summary>
        /// Internal constructor for UDP connections
        /// </summary>
        /// <param name="connectionInfo"></param>
        /// <param name="defaultSendReceiveOptions"></param>
        /// <param name="level"></param>
        /// <param name="listenForIncomingPackets"></param>
        /// <param name="existingConnection"></param>
        private UDPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, UDPOptions level, bool listenForIncomingPackets, UDPConnection existingConnection = null)
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Creating new UDPConnection with " + connectionInfo);

            if (connectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled && level != UDPOptions.None)
                throw new ArgumentException("If the application layer protocol has been disabled the provided UDPOptions can only be UDPOptions.None.");

            ConnectionUDPOptions = level;

            if (listenForIncomingPackets && existingConnection != null)
                throw new Exception("Unable to listen for incoming packets if an existing client has been provided. This is to prevent possible multiple accidently listens on the same client.");

            if (existingConnection == null)
            {
                if (connectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any) || connectionInfo.RemoteEndPoint.Address.Equals(IPAddress.IPv6Any))
                {
#if WINDOWS_PHONE
                    //We are creating an unbound endPoint, this is currently the rogue UDP sender and listeners only
                    socket = new DatagramSocket();

                    if (listenForIncomingPackets)
                        socket.MessageReceived += socket_MessageReceived;

                    socket.BindEndpointAsync(new HostName(ConnectionInfo.LocalEndPoint.Address.ToString()), ConnectionInfo.LocalEndPoint.Port.ToString()).AsTask().Wait();
#else
                    //We are creating an unbound endPoint, this is currently the rogue UDP sender and listeners only
                    udpClientThreadSafe = new UdpClientThreadSafe(new UdpClient(ConnectionInfo.LocalEndPoint));
#endif
                }
                else
                {
                    //If this is a specific connection we link to a default end point here
                    isIsolatedUDPConnection = true;

#if WINDOWS_PHONE
                    if (ConnectionInfo.LocalEndPoint == null)
                    {
                        socket = new DatagramSocket();

                        if (listenForIncomingPackets)
                            socket.MessageReceived += socket_MessageReceived;

                        socket.ConnectAsync(new HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString()).AsTask().Wait();
                    }
                    else
                    {
                        socket = new DatagramSocket();

                        if (listenForIncomingPackets)
                            socket.MessageReceived += socket_MessageReceived;

                        EndpointPair pair = new EndpointPair(new HostName(ConnectionInfo.LocalEndPoint.Address.ToString()), ConnectionInfo.LocalEndPoint.Port.ToString(),
                            new HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString());

                        socket.ConnectAsync(pair).AsTask().Wait();
                    }
#else
                    if (ConnectionInfo.LocalEndPoint == null)
                        udpClientThreadSafe = new UdpClientThreadSafe(new UdpClient(ConnectionInfo.RemoteEndPoint.AddressFamily));
                    else
                        udpClientThreadSafe = new UdpClientThreadSafe(new UdpClient(ConnectionInfo.LocalEndPoint));

                    //By calling connect we discard packets from anything other then the provided remoteEndPoint on our localEndPoint
                    udpClientThreadSafe.Connect(ConnectionInfo.RemoteEndPoint);
#endif
                }

#if !WINDOWS_PHONE
                //NAT traversal does not work in .net 2.0
                //Mono does not seem to have implemented AllowNatTraversal method and attempting the below method call will throw an exception
                //if (Type.GetType("Mono.Runtime") == null)
                    //Allow NAT traversal by default for all udp clients
                //    udpClientThreadSafe.AllowNatTraversal(true);

                if (listenForIncomingPackets)
                    StartIncomingDataListen();
#endif
            }
            else
            {
                if (!existingConnection.ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))
                    throw new Exception("If an existing udpClient is provided it must be unbound to a specific remoteEndPoint");

#if WINDOWS_PHONE
                //Using an exiting client allows us to send from the same port as for the provided existing connection
                this.socket = existingConnection.socket;
#else
                //Using an exiting client allows us to send from the same port as for the provided existing connection
                this.udpClientThreadSafe = existingConnection.udpClientThreadSafe;
#endif
            }

            IPEndPoint localEndPoint;
#if WINDOWS_PHONE
            localEndPoint = new IPEndPoint(IPAddress.Parse(socket.Information.LocalAddress.DisplayName.ToString()), int.Parse(socket.Information.LocalPort));
#else
            localEndPoint = udpClientThreadSafe.LocalEndPoint;
#endif

            //We can update the localEndPoint so that it is correct
            if (ConnectionInfo.LocalEndPoint == null || ConnectionInfo.LocalEndPoint.Port == 0)
                ConnectionInfo.UpdateLocalEndPointInfo(localEndPoint);
        }

        /// <summary>
        /// Establish this UDP connection. This will become more relevant as additional udp levels are supported.
        /// </summary>
        protected override void EstablishConnectionSpecific()
        {
            //If the application layer protocol is enabled and the udp option is set
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled &&
                (ConnectionUDPOptions & UDPOptions.Handshake) == UDPOptions.Handshake)
                ConnectionHandshake();
            else
            {
                //If there is no handshake we can now consider the connection established
                TriggerConnectionEstablishDelegates();

                //Trigger any connection setup waits
                connectionSetupWait.Set();
            }
        }

        /// <summary>
        /// Executes UDP specific shutdown tasks
        /// </summary>
        /// <param name="closeDueToError">True if closing connection due to error</param>
        /// <param name="logLocation">An optional debug parameter.</param>
        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
#if WINDOWS_PHONE
            //We only call close on the udpClient if this is a specific udp connection or we are calling close from the parent udp connection
            if (socket != null && (isIsolatedUDPConnection || (ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))))
                socket.Dispose();
#else
            //We only call close on the udpClient if this is a specific udp connection or we are calling close from the parent udp connection
            if (udpClientThreadSafe != null && (isIsolatedUDPConnection || (ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))))
                udpClientThreadSafe.CloseClient();
#endif
        }

        /// <summary>
        /// Send a packet to the RemoteEndPoint specified in the ConnectionInfo
        /// </summary>
        /// <param name="packet">Packet to send</param>
        protected override void SendPacketSpecific(Packet packet)
        {
#if FREETRIAL
            if (this.ConnectionInfo.RemoteEndPoint.Address == IPAddress.Broadcast)
                throw new NotSupportedException("Unable to send UDP broadcast datagram using this version of NetworkComms.Net. Please purchase a commerical license from www.networkcomms.net which supports UDP broadcast datagrams.");
#endif

            if (ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))
                throw new CommunicationException("Unable to send packet using this method as remoteEndPoint equals IPAddress.Any");

            byte[] headerBytes = new byte[0];
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
                headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);
            else
            {
                if (packet.PacketHeader.PacketType != Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
                    throw new UnexpectedPacketTypeException("Only 'Unmanaged' packet types can be used if the NetworkComms.Net application layer protocol is disabled.");

                if (packet.PacketData.Length == 0)
                    throw new NotSupportedException("Sending a zero length array if the NetworkComms.Net application layer protocol is disabled is not supported.");
            }

            //We are limited in size for the isolated send
            if (headerBytes.Length + packet.PacketData.Length > maximumSingleDatagramSizeBytes)
                throw new CommunicationException("Attempted to send a udp packet whose serialised size was " + (headerBytes.Length + packet.PacketData.Length).ToString() + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes.ToString() + ". Consider using a TCP connection to send this object.");

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Sending a UDP packet of type '" + packet.PacketHeader.PacketType + "' to " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port.ToString() + " containing " + headerBytes.Length.ToString() + " header bytes and " + packet.PacketData.Length.ToString() + " payload bytes.");

            //Prepare the single byte array to send
            byte[] udpDatagram;
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
            {
                udpDatagram = packet.PacketData.ThreadSafeStream.ToArray(headerBytes.Length);

                //Copy the header bytes into the datagram
                Buffer.BlockCopy(headerBytes, 0, udpDatagram, 0, headerBytes.Length);
            }
            else
                udpDatagram = packet.PacketData.ThreadSafeStream.ToArray();

#if WINDOWS_PHONE
            var getStreamTask = socket.GetOutputStreamAsync(new HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString()).AsTask();
            getStreamTask.Wait();

            var outputStream = getStreamTask.Result;

            outputStream.WriteAsync(WindowsRuntimeBufferExtensions.AsBuffer(udpDatagram)).AsTask().Wait();
            outputStream.FlushAsync().AsTask().Wait();
#else
            udpClientThreadSafe.Send(udpDatagram, udpDatagram.Length, ConnectionInfo.RemoteEndPoint);
#endif

            if (packet.PacketData.ThreadSafeStream.CloseStreamAfterSend)
                packet.PacketData.ThreadSafeStream.Close();

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Completed send of a UDP packet of type '" + packet.PacketHeader.PacketType + "' to " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port.ToString() + " containing " + headerBytes.Length.ToString() + " header bytes and " + packet.PacketData.Length.ToString() + " payload bytes.");
        }

        /// <summary>
        /// Send a packet to the specified ipEndPoint. This feature is unique to UDP because of its connectionless structure.
        /// </summary>
        /// <param name="packet">Packet to send</param>
        /// <param name="ipEndPoint">The target ipEndPoint</param>
        private void SendPacketSpecific(Packet packet, IPEndPoint ipEndPoint)
        {
#if FREETRIAL
            if (ipEndPoint.Address == IPAddress.Broadcast)
                throw new NotSupportedException("Unable to send UDP broadcast datagram using this version of NetworkComms.Net. Please purchase a commerical license from www.networkcomms.net which supports UDP broadcast datagrams.");
#endif

            byte[] headerBytes = new byte[0];
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
            {
                long packetSequenceNumber;
                lock (sendLocker)
                {
                    //Set packet sequence number inside sendLocker
                    //Increment the global counter as well to ensure future connections with the same host can not create duplicates
                    Interlocked.Increment(ref NetworkComms.totalPacketSendCount);
                    packetSequenceNumber = packetSequenceCounter++;
                    packet.PacketHeader.SetOption(PacketHeaderLongItems.PacketSequenceNumber, packetSequenceNumber);
                }

                headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);
            }
            else
            {
                if (packet.PacketHeader.PacketType != Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
                    throw new UnexpectedPacketTypeException("Only 'Unmanaged' packet types can be used if the NetworkComms.Net application layer protocol is disabled.");

                if (packet.PacketData.Length == 0)
                    throw new NotSupportedException("Sending a zero length array if the NetworkComms.Net application layer protocol is disabled is not supported.");
            }

            //We are limited in size for the isolated send
            if (headerBytes.Length + packet.PacketData.Length > maximumSingleDatagramSizeBytes)
                throw new CommunicationException("Attempted to send a udp packet whose serialised size was " + (headerBytes.Length + packet.PacketData.Length).ToString() + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes.ToString() + ". Consider using a TCP connection to send this object.");

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Sending a UDP packet of type '" + packet.PacketHeader.PacketType + "' from " + ConnectionInfo.LocalEndPoint.Address + ":" + ConnectionInfo.LocalEndPoint.Port.ToString() + " to " + ipEndPoint.Address + ":" + ipEndPoint.Port.ToString() + " containing " + headerBytes.Length.ToString() + " header bytes and " + packet.PacketData.Length.ToString() + " payload bytes.");

            //Prepare the single byte array to send
            byte[] udpDatagram;
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
            {
                udpDatagram = packet.PacketData.ThreadSafeStream.ToArray(headerBytes.Length);

                //Copy the header bytes into the datagram
                Buffer.BlockCopy(headerBytes, 0, udpDatagram, 0, headerBytes.Length);
            }
            else
                udpDatagram = packet.PacketData.ThreadSafeStream.ToArray();

#if WINDOWS_PHONE
            var getStreamTask = socket.GetOutputStreamAsync(new HostName(ipEndPoint.Address.ToString()), ipEndPoint.Port.ToString()).AsTask();
            getStreamTask.Wait();

            var outputStream = getStreamTask.Result;

            outputStream.WriteAsync(WindowsRuntimeBufferExtensions.AsBuffer(udpDatagram)).AsTask().Wait();
            outputStream.FlushAsync().AsTask().Wait();
#else
            udpClientThreadSafe.Send(udpDatagram, udpDatagram.Length, ipEndPoint);
#endif

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Completed send of a UDP packet of type '" + packet.PacketHeader.PacketType + "' from " + ConnectionInfo.LocalEndPoint.Address + ":" + ConnectionInfo.LocalEndPoint.Port.ToString() + " to " + ipEndPoint.Address + ":" + ipEndPoint.Port.ToString() + ".");
        }

        /// <summary>
        /// Sends a null packet using UDP
        /// </summary>
        protected override void SendNullPacket()
        {
            //We cant send a null packet to the IPAddress.Any address
            if (!ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))
            {
                if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Ignoring null packet send to " + ConnectionInfo + " as the application layer protocol is disabled.");
                    return;
                }

                try
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Sending null packet to " + ConnectionInfo);

#if WINDOWS_PHONE
                    //Send a single 0 byte
                    var getStreamTask = socket.GetOutputStreamAsync(new HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString()).AsTask();
                    getStreamTask.Wait();

                    var outputStream = getStreamTask.Result;

                    outputStream.WriteAsync(WindowsRuntimeBufferExtensions.AsBuffer(new byte[] { 0 })).AsTask().Wait();
                    outputStream.FlushAsync().AsTask().Wait();
#else
                    //Send a single 0 byte
                    udpClientThreadSafe.Send(new byte[] { 0 }, 1, ConnectionInfo.RemoteEndPoint);
#endif

                    //Update the traffic time after we have written to netStream
                    ConnectionInfo.UpdateLastTrafficTime();

                    //If the connection is shutdown we should call close
                    if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown) CloseConnection(false, -9);
                }
                catch (Exception)
                {
                    CloseConnection(true, 40);
                }
            }
        }

        /// <summary>
        /// Start listening for incoming udp data
        /// </summary>
        protected override void StartIncomingDataListen()
        {
#if WINDOWS_PHONE
            throw new NotImplementedException("Not needed for UDP connections on Windows Phone 8");
#else

            if (NetworkComms.ConnectionListenModeUseSync)
            {
                if (incomingDataListenThread == null)
                {
                    incomingDataListenThread = new Thread(IncomingUDPPacketWorker);
                    incomingDataListenThread.Priority = NetworkComms.timeCriticalThreadPriority;
                    incomingDataListenThread.Name = "IncomingDataListener";
                    incomingDataListenThread.Start();
                }
            }
            else
                udpClientThreadSafe.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), udpClientThreadSafe);
#endif
        }

#if WINDOWS_PHONE
        void socket_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            try
            {                
                var stream = args.GetDataStream().AsStreamForRead();
                var dataLength = args.GetDataReader().UnconsumedBufferLength;
                
                byte[] receivedBytes = new byte[dataLength];
                using (MemoryStream mem = new MemoryStream(receivedBytes))
                    stream.CopyTo(mem);

                //Received data after comms shutdown initiated. We should just close the connection
                if (NetworkComms.commsShutdown) CloseConnection(false, -15);

                stream = null;
               
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Received " + receivedBytes.Length + " bytes via UDP from " + args.RemoteAddress + ":" + args.RemotePort + ".");

                UDPConnection connection;
                HandshakeUDPDatagram possibleHandshakeUDPDatagram = new HandshakeUDPDatagram(receivedBytes);
                if (isIsolatedUDPConnection)
                    //This connection was created for a specific remoteEndPoint so we can handle the data internally
                    connection = this;
                else
                {
                    //Look for an existing connection, if one does not exist we will create it
                    //This ensures that all further processing knows about the correct endPoint
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(args.RemoteAddress.DisplayName.ToString()), int.Parse(args.RemotePort));
                    IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(sender.Information.LocalAddress.DisplayName.ToString()), int.Parse(sender.Information.LocalPort));

                    connection = GetConnection(new ConnectionInfo(true, ConnectionType.UDP, remoteEndPoint, localEndPoint, ConnectionInfo.ApplicationLayerProtocol), ConnectionDefaultSendReceiveOptions, ConnectionUDPOptions, false, this, possibleHandshakeUDPDatagram);
                }

                if (!possibleHandshakeUDPDatagram.DatagramHandled)
                {
                    //We pass the data off to the specific connection
                    //Lock on the packetbuilder locker as we may recieve udp packets in parallel from this host
                    lock (connection.packetBuilder.Locker)
                    {
                        connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                        if (connection.packetBuilder.TotalBytesCached > 0) connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                        if (connection.packetBuilder.TotalPartialPacketCount > 0)
                            NetworkComms.LogError(new Exception("Packet builder had remaining packets after a call to IncomingPacketHandleHandOff. Until sequenced packets are implemented this indicates a possible error."), "UDPConnectionError");
                    }   
                }
            }
            //On any error here we close the connection
            catch (NullReferenceException)
            {
                CloseConnection(true, 25);
            }
            catch (ArgumentNullException)
            {
                CloseConnection(true, 38);
            }
            catch (IOException)
            {
                CloseConnection(true, 26);
            }
            catch (ObjectDisposedException)
            {
                CloseConnection(true, 27);
            }
            catch (SocketException)
            {
                //Recieve may throw a SocketException ErrorCode=10054  after attempting to send a datagram to an unreachable target. 
                //We will try to get around this by ignoring the ICMP packet causing these problems on client creation
                CloseConnection(true, 28);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 29);
            }
            catch (ConnectionSetupException)
            {
                //Can occur if data is received as comms is being shutdown. 
                //Method will attempt to create new connection which will throw ConnectionSetupException.
                CloseConnection(true, 50);
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_UDPConnectionIncomingPacketHandler");
                CloseConnection(true, 30);
            }
        }
#else
        /// <summary>
        /// Incoming data listen async method
        /// </summary>
        /// <param name="ar">Call back state data</param>
        private void IncomingUDPPacketHandler(IAsyncResult ar)
        {
            try
            {
                UdpClientThreadSafe client = (UdpClientThreadSafe)ar.AsyncState;
                IPEndPoint endPoint = new IPEndPoint(IPAddress.None, 0);
                byte[] receivedBytes = client.EndReceive(ar, ref endPoint);

                //Received data after comms shutdown initiated. We should just close the connection
                if (NetworkComms.commsShutdown) CloseConnection(false, -13);

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Received " + receivedBytes.Length.ToString() + " bytes via UDP from " + endPoint.Address + ":" + endPoint.Port.ToString() + ".");

                UDPConnection connection;
                HandshakeUDPDatagram possibleHandshakeUDPDatagram = new HandshakeUDPDatagram(receivedBytes);
                if (isIsolatedUDPConnection)
                    //This connection was created for a specific remoteEndPoint so we can handle the data internally
                    connection = this;
                else
                    //Look for an existing connection, if one does not exist we will create it
                    //This ensures that all further processing knows about the correct endPoint
                    connection = GetConnection(new ConnectionInfo(true, ConnectionType.UDP, endPoint, udpClientThreadSafe.LocalEndPoint, ConnectionInfo.ApplicationLayerProtocol), ConnectionDefaultSendReceiveOptions, ConnectionUDPOptions, false, this, possibleHandshakeUDPDatagram);

                if (!possibleHandshakeUDPDatagram.DatagramHandled)
                {
                    //We pass the data off to the specific connection
                    //Lock on the packetbuilder locker as we may recieve udp packets in parallel from this host
                    lock (connection.packetBuilder.Locker)
                    {
                        connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                        if (connection.packetBuilder.TotalBytesCached > 0) connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                        if (connection.packetBuilder.TotalPartialPacketCount > 0)
                            NetworkComms.LogError(new Exception("Packet builder had remaining packets after a call to IncomingPacketHandleHandOff. Until sequenced packets are implemented this indicates a possible error."), "UDPConnectionError");
                    }
                }

                client.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), client);
            }
            //On any error here we close the connection
            catch (NullReferenceException)
            {
                CloseConnection(true, 25);
            }
            catch (ArgumentNullException)
            {
                CloseConnection(true, 36);
            }
            catch (IOException)
            {
                CloseConnection(true, 26);
            }
            catch (ObjectDisposedException)
            {
                CloseConnection(true, 27);
            }
            catch (SocketException)
            {
                //Recieve may throw a SocketException ErrorCode=10054  after attempting to send a datagram to an unreachable target. 
                //We will try to get around this by ignoring the ICMP packet causing these problems on client creation
                CloseConnection(true, 28);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 29);
            }
            catch (ConnectionSetupException)
            {
                //Can occur if data is received as comms is being shutdown. 
                //Method will attempt to create new connection which will throw ConnectionSetupException.
                CloseConnection(true, 50);
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_UDPConnectionIncomingPacketHandler");
                CloseConnection(true, 30);
            }
        }

        /// <summary>
        /// Incoming data listen sync method
        /// </summary>
        private void IncomingUDPPacketWorker()
        {
            try
            {
                while (true)
                {
                    if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                        break;

                    IPEndPoint endPoint = new IPEndPoint(IPAddress.None, 0);
                    byte[] receivedBytes = udpClientThreadSafe.Receive(ref endPoint);

                    //Received data after comms shutdown initiated. We should just close the connection
                    if (NetworkComms.commsShutdown) CloseConnection(false, -14);

                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Received " + receivedBytes.Length.ToString() + " bytes via UDP from " + endPoint.Address + ":" + endPoint.Port.ToString() + ".");

                    UDPConnection connection;
                    HandshakeUDPDatagram possibleHandshakeUDPDatagram = new HandshakeUDPDatagram(receivedBytes);
                    if (isIsolatedUDPConnection)
                        //This connection was created for a specific remoteEndPoint so we can handle the data internally
                        connection = this;
                    else
                        //Look for an existing connection, if one does not exist we will create it
                        //This ensures that all further processing knows about the correct endPoint
                        connection = GetConnection(new ConnectionInfo(true, ConnectionType.UDP, endPoint, udpClientThreadSafe.LocalEndPoint, ConnectionInfo.ApplicationLayerProtocol), ConnectionDefaultSendReceiveOptions, ConnectionUDPOptions, false, this, possibleHandshakeUDPDatagram);

                    if (!possibleHandshakeUDPDatagram.DatagramHandled)
                    {
                        //We pass the data off to the specific connection
                        //Lock on the packetbuilder locker as we may recieve udp packets in parallel from this host
                        lock (connection.packetBuilder.Locker)
                        {
                            connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                            if (connection.packetBuilder.TotalBytesCached > 0) connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                            if (connection.packetBuilder.TotalPartialPacketCount > 0)
                                NetworkComms.LogError(new Exception("Packet builder had remaining packets after a call to IncomingPacketHandleHandOff. Until sequenced packets are implemented this indicates a possible error."), "UDPConnectionError");
                        }
                    }
                }
            }
            //On any error here we close the connection
            catch (NullReferenceException)
            {
                CloseConnection(true, 20);
            }
            catch (ArgumentNullException)
            {
                CloseConnection(true, 37);
            }
            catch (IOException)
            {
                CloseConnection(true, 21);
            }
            catch (ObjectDisposedException)
            {
                CloseConnection(true, 22);
            }
            catch (SocketException)
            {
                //Recieve may throw a SocketException ErrorCode=10054  after attempting to send a datagram to an unreachable target. 
                //We will try to get around this by ignoring the ICMP packet causing these problems on client creation
                CloseConnection(true, 23);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 24);
            }
            catch (ConnectionSetupException)
            {
                //Can occur if data is received as comms is being shutdown. 
                //Method will attempt to create new connection which will throw ConnectionSetupException.
                CloseConnection(true, 50);
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_UDPConnectionIncomingPacketHandler");
                CloseConnection(true, 41);
            }

            //Clear the listen thread object because the thread is about to end
            incomingDataListenThread = null;

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Incoming data listen thread ending for " + ConnectionInfo);
        }
#endif
    }
}