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
using System.IO;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

#if WINDOWS_PHONE || NETFX_CORE
using Windows.Networking.Sockets;
using Windows.Networking;
using System.Runtime.InteropServices.WindowsRuntime;
#endif

namespace NetworkCommsDotNet.Connections.UDP
{
    /// <summary>
    /// A connection object which utilises <see href="http://en.wikipedia.org/wiki/User_Datagram_Protocol">UDP</see> to communicate between peers.
    /// </summary>
    public sealed partial class UDPConnection : IPConnection
    {
#if WINDOWS_PHONE || NETFX_CORE
        internal DatagramSocket socket;
#else
        internal UdpClientWrapper udpClient;
#endif

        /// <summary>
        /// Options associated with this UDPConnection
        /// </summary>
        public UDPOptions ConnectionUDPOptions { get; private set; }

        /// <summary>
        /// An isolated UDP connection will only accept incoming packets coming from a specific RemoteEndPoint.
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
        internal UDPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, UDPOptions level, bool listenForIncomingPackets, UDPConnection existingConnection = null)
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            if (connectionInfo.ConnectionType != ConnectionType.UDP)
                throw new ArgumentException("Provided connectionType must be UDP.", "connectionInfo");

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Creating new UDPConnection with " + connectionInfo);

            if (connectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled && level != UDPOptions.None)
                throw new ArgumentException("If the application layer protocol has been disabled the provided UDPOptions can only be UDPOptions.None.");

            ConnectionUDPOptions = level;

            if (listenForIncomingPackets && existingConnection != null)
                throw new Exception("Unable to listen for incoming packets if an existing client has been provided. This is to prevent possible multiple accidently listens on the same client.");

            if (existingConnection == null)
            {
                if (connectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.Any) || connectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.IPv6Any))
                {
#if WINDOWS_PHONE || NETFX_CORE
                    //We are creating an unbound endPoint, this is currently the rogue UDP sender and listeners only
                    socket = new DatagramSocket();

                    if (listenForIncomingPackets)
                        socket.MessageReceived += socket_MessageReceived;

                    socket.BindEndpointAsync(new HostName(ConnectionInfo.LocalIPEndPoint.Address.ToString()), ConnectionInfo.LocalIPEndPoint.Port.ToString()).AsTask().Wait();
#else
                    //We are creating an unbound endPoint, this is currently the rogue UDP sender and listeners only
                    udpClient = new UdpClientWrapper(new UdpClient(ConnectionInfo.LocalIPEndPoint));
#endif
                }
                else
                {
                    //If this is a specific connection we link to a default end point here
                    isIsolatedUDPConnection = true;

#if WINDOWS_PHONE || NETFX_CORE
                    if (ConnectionInfo.LocalEndPoint == null || 
                        (ConnectionInfo.LocalIPEndPoint.Address == IPAddress.Any && connectionInfo.LocalIPEndPoint.Port == 0) ||
                        (ConnectionInfo.LocalIPEndPoint.Address == IPAddress.IPv6Any && connectionInfo.LocalIPEndPoint.Port == 0))
                    {
                        socket = new DatagramSocket();

                        if (listenForIncomingPackets)
                            socket.MessageReceived += socket_MessageReceived;

                        socket.ConnectAsync(new HostName(ConnectionInfo.RemoteIPEndPoint.Address.ToString()), ConnectionInfo.RemoteIPEndPoint.Port.ToString()).AsTask().Wait();
                    }
                    else
                    {
                        socket = new DatagramSocket();

                        if (listenForIncomingPackets)
                            socket.MessageReceived += socket_MessageReceived;

                        EndpointPair pair = new EndpointPair(new HostName(ConnectionInfo.LocalIPEndPoint.Address.ToString()), ConnectionInfo.LocalIPEndPoint.Port.ToString(),
                            new HostName(ConnectionInfo.RemoteIPEndPoint.Address.ToString()), ConnectionInfo.RemoteIPEndPoint.Port.ToString());

                        socket.ConnectAsync(pair).AsTask().Wait();
                    }
#else
                    if (ConnectionInfo.LocalEndPoint == null)
                        udpClient = new UdpClientWrapper(new UdpClient(ConnectionInfo.RemoteEndPoint.AddressFamily));
                    else
                        udpClient = new UdpClientWrapper(new UdpClient(ConnectionInfo.LocalIPEndPoint));

                    //By calling connect we discard packets from anything other then the provided remoteEndPoint on our localEndPoint
                    udpClient.Connect(ConnectionInfo.RemoteIPEndPoint);
#endif
                }

#if !WINDOWS_PHONE && !NETFX_CORE
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
                if (!existingConnection.ConnectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.Any))
                    throw new Exception("If an existing udpClient is provided it must be unbound to a specific remoteEndPoint");

#if WINDOWS_PHONE || NETFX_CORE
                //Using an exiting client allows us to send from the same port as for the provided existing connection
                this.socket = existingConnection.socket;
#else
                //Using an exiting client allows us to send from the same port as for the provided existing connection
                this.udpClient = existingConnection.udpClient;
#endif
            }

            IPEndPoint localEndPoint;
#if WINDOWS_PHONE || NETFX_CORE
            localEndPoint = new IPEndPoint(IPAddress.Parse(socket.Information.LocalAddress.DisplayName.ToString()), int.Parse(socket.Information.LocalPort));
#else
            localEndPoint = udpClient.LocalIPEndPoint;
#endif

            //We can update the localEndPoint so that it is correct
            if (!ConnectionInfo.LocalEndPoint.Equals(localEndPoint))
            {
                //We should now be able to set the connectionInfo localEndPoint
                NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteIPEndPoint, localEndPoint);
                ConnectionInfo.UpdateLocalEndPointInfo(localEndPoint);
            }
        }

        /// <inheritdoc />
        protected override void EstablishConnectionSpecific()
        {
            //If the application layer protocol is enabled and the UDP option is set
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

        /// <inheritdoc />
        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
#if WINDOWS_PHONE || NETFX_CORE
            //We only call close on the udpClient if this is a specific UDP connection or we are calling close from the parent UDP connection
            if (socket != null && (isIsolatedUDPConnection || (ConnectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.Any))))
                socket.Dispose();
#else
            //We only call close on the udpClient if this is a specific UDP connection or we are calling close from the parent UDP connection
            if (udpClient != null && (isIsolatedUDPConnection || (ConnectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.Any))))
                udpClient.CloseClient();
#endif
        }

        /// <summary>
        /// Send a packet to the specified ipEndPoint. This feature is unique to UDP because of its connectionless structure.
        /// </summary>
        /// <param name="packet">Packet to send</param>
        /// <param name="ipEndPoint">The target ipEndPoint</param>
        private void SendPacketSpecific<packetObjectType>(IPacket packet, IPEndPoint ipEndPoint)
        {
#if FREETRIAL
            if (ipEndPoint.Address == IPAddress.Broadcast)
                throw new NotSupportedException("Unable to send UDP broadcast datagram using this version of NetworkComms.Net. Please purchase a commercial license from www.networkcomms.net which supports UDP broadcast datagrams.");
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
                throw new CommunicationException("Attempted to send a UDP packet whose serialised size was " + (headerBytes.Length + packet.PacketData.Length).ToString() + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes.ToString() + ". Consider using a TCP connection to send this object.");

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Sending a UDP packet of type '" + packet.PacketHeader.PacketType + "' from " + ConnectionInfo.LocalIPEndPoint.Address + ":" + ConnectionInfo.LocalIPEndPoint.Port.ToString() + " to " + ipEndPoint.Address + ":" + ipEndPoint.Port.ToString() + " containing " + headerBytes.Length.ToString() + " header bytes and " + packet.PacketData.Length.ToString() + " payload bytes.");

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

#if WINDOWS_PHONE || NETFX_CORE
            var getStreamTask = socket.GetOutputStreamAsync(new HostName(ipEndPoint.Address.ToString()), ipEndPoint.Port.ToString()).AsTask();
            getStreamTask.Wait();

            var outputStream = getStreamTask.Result;

            outputStream.WriteAsync(WindowsRuntimeBufferExtensions.AsBuffer(udpDatagram)).AsTask().Wait();
            outputStream.FlushAsync().AsTask().Wait();
#else
            udpClient.Send(udpDatagram, udpDatagram.Length, ipEndPoint);
#endif

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Completed send of a UDP packet of type '" + packet.PacketHeader.PacketType + "' from " + ConnectionInfo.LocalIPEndPoint.Address + ":" + ConnectionInfo.LocalIPEndPoint.Port.ToString() + " to " + ipEndPoint.Address + ":" + ipEndPoint.Port.ToString() + ".");
        }

        /// <inheritdoc />
        protected override double[] SendStreams(StreamTools.StreamSendWrapper[] streamsToSend, double maxSendTimePerKB, long totalBytesToSend)
        {
#if FREETRIAL
            if (this.ConnectionInfo.RemoteEndPoint.Address == IPAddress.Broadcast)
                throw new NotSupportedException("Unable to send UDP broadcast datagram using this version of NetworkComms.Net. Please purchase a commercial license from www.networkcomms.net which supports UDP broadcast datagrams.");
#endif

            if (ConnectionInfo.RemoteIPEndPoint.Address.Equals(IPAddress.Any))
                throw new CommunicationException("Unable to send packet using this method as remoteEndPoint equals IPAddress.Any");

            if (totalBytesToSend > maximumSingleDatagramSizeBytes)
                throw new CommunicationException("Attempted to send a UDP packet whose length is " + totalBytesToSend.ToString() + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes.ToString() + ". Consider using a TCP connection to send this object.");

            byte[] udpDatagram = new byte[totalBytesToSend];
            MemoryStream udpDatagramStream = new MemoryStream(udpDatagram, 0, udpDatagram.Length, true);
            
            for (int i = 0; i < streamsToSend.Length; i++)
            {
                if (streamsToSend[i].Length > 0)
                {
                    //Write each stream
                    streamsToSend[i].ThreadSafeStream.CopyTo(udpDatagramStream, streamsToSend[i].Start, streamsToSend[i].Length, NetworkComms.SendBufferSizeBytes, maxSendTimePerKB, MinSendTimeoutMS);

                    streamsToSend[i].ThreadSafeStream.Dispose();
                }
            }

            DateTime startTime = DateTime.Now;
#if WINDOWS_PHONE || NETFX_CORE
            var getStreamTask = socket.GetOutputStreamAsync(new HostName(ConnectionInfo.RemoteIPEndPoint.Address.ToString()), ConnectionInfo.RemoteIPEndPoint.Port.ToString()).AsTask();
            getStreamTask.Wait();

            var outputStream = getStreamTask.Result;

            outputStream.WriteAsync(WindowsRuntimeBufferExtensions.AsBuffer(udpDatagram)).AsTask().Wait();
            outputStream.FlushAsync().AsTask().Wait();
#else
            udpClient.Send(udpDatagram, udpDatagram.Length, ConnectionInfo.RemoteIPEndPoint);
#endif

            udpDatagramStream.Dispose();

            //Calculate timings based on fractional byte length
            double[] timings = new double[streamsToSend.Length];
            double elapsedMS = (DateTime.Now - startTime).TotalMilliseconds;
            for (int i = 0; i < streamsToSend.Length; i++)
                timings[i] = elapsedMS * (streamsToSend[i].Length / (double)totalBytesToSend);

            return timings;
        }

        /// <inheritdoc />
        protected override void StartIncomingDataListen()
        {
#if WINDOWS_PHONE || NETFX_CORE
            throw new NotImplementedException("Not needed for UDP connections on Windows Phone 8");
#else

            if (NetworkComms.ConnectionListenModeUseSync)
            {
                if (incomingDataListenThread == null)
                {
                    incomingDataListenThread = new Thread(IncomingUDPPacketWorker);
                    incomingDataListenThread.Priority = NetworkComms.timeCriticalThreadPriority;
                    incomingDataListenThread.Name = "UDP_IncomingDataListener";
                    incomingDataListenThread.IsBackground = true;
                    incomingDataListenThread.Start();
                }
            }
            else
            {
                if (asyncListenStarted) throw new ConnectionSetupException("Async listen already started. Why has this been called twice?.");

                udpClient.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), udpClient);

                asyncListenStarted = true;
            }
#endif
        }

#if WINDOWS_PHONE || NETFX_CORE
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
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(args.RemoteAddress.DisplayName.ToString()), int.Parse(args.RemotePort));
                    IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(sender.Information.LocalAddress.DisplayName.ToString()), int.Parse(sender.Information.LocalPort));

                    ConnectionInfo desiredConnection = new ConnectionInfo(ConnectionType.UDP, remoteEndPoint, localEndPoint, ConnectionInfo.ApplicationLayerProtocol, ConnectionInfo.ConnectionListener);
                    try
                    {
                        //Look for an existing connection, if one does not exist we will create it
                        //This ensures that all further processing knows about the correct endPoint
                        connection = GetConnection(desiredConnection, ConnectionUDPOptions, ConnectionDefaultSendReceiveOptions, false, this, possibleHandshakeUDPDatagram);
                    }
                    catch (ConnectionShutdownException)
                    {
                        if ((ConnectionUDPOptions & UDPOptions.Handshake) == UDPOptions.Handshake)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Attempted to get connection " + desiredConnection + " but this caused a ConnectionShutdownException. Exception caught and ignored as should only happen if the connection was closed shortly after being created.");
                            connection = null;
                        }
                        else
                            throw;
                    }
                }

                if (connection != null && !possibleHandshakeUDPDatagram.DatagramHandled)
                {
                    //We pass the data off to the specific connection
                    //Lock on the packetbuilder locker as we may receive UDP packets in parallel from this host
                    lock (connection.packetBuilder.Locker)
                    {
                        connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                        if (connection.packetBuilder.TotalBytesCached > 0) connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                        if (connection.packetBuilder.TotalPartialPacketCount > 0)
                            LogTools.LogException(new Exception("Packet builder had " + connection.packetBuilder.TotalBytesCached + " bytes remaining after a call to IncomingPacketHandleHandOff with connection " + connection.ConnectionInfo + ". Until sequenced packets are implemented this indicates a possible error."), "UDPConnectionError");
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
                //Receive may throw a SocketException ErrorCode=10054  after attempting to send a datagram to an unreachable target. 
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
                LogTools.LogException(ex, "Error_UDPConnectionIncomingPacketHandler");
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
                UdpClientWrapper client = (UdpClientWrapper)ar.AsyncState;
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.None, 0);
                byte[] receivedBytes = client.EndReceive(ar, ref remoteEndPoint);

                //Received data after comms shutdown initiated. We should just close the connection
                if (NetworkComms.commsShutdown) CloseConnection(false, -13);

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Received " + receivedBytes.Length.ToString() + " bytes via UDP from " + remoteEndPoint.Address + ":" + remoteEndPoint.Port.ToString() + ".");

                UDPConnection connection;
                HandshakeUDPDatagram possibleHandshakeUDPDatagram = new HandshakeUDPDatagram(receivedBytes);
                if (isIsolatedUDPConnection)
                    //This connection was created for a specific remoteEndPoint so we can handle the data internally
                    connection = this;
                else
                {
                    ConnectionInfo desiredConnection = new ConnectionInfo(ConnectionType.UDP, remoteEndPoint, udpClient.LocalIPEndPoint, ConnectionInfo.ApplicationLayerProtocol, ConnectionInfo.ConnectionListener);
                    try
                    {
                        //Look for an existing connection, if one does not exist we will create it
                        //This ensures that all further processing knows about the correct endPoint
                        connection = GetConnection(desiredConnection, ConnectionUDPOptions, ConnectionDefaultSendReceiveOptions, false, this, possibleHandshakeUDPDatagram);
                    }
                    catch (ConnectionShutdownException)
                    {
                        if ((ConnectionUDPOptions & UDPOptions.Handshake) == UDPOptions.Handshake)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Attempted to get connection " + desiredConnection + " but this caused a ConnectionShutdownException. Exception caught and ignored as should only happen if the connection was closed shortly after being created.");
                            connection = null;
                        }
                        else
                            throw;
                    }
                }

                if (connection != null && !possibleHandshakeUDPDatagram.DatagramHandled)
                {
                    //We pass the data off to the specific connection
                    //Lock on the packetbuilder locker as we may receive UDP packets in parallel from this host
                    lock (connection.packetBuilder.Locker)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + receivedBytes.Length.ToString() + " bytes added to packetBuilder for " + connection.ConnectionInfo + ". Cached " + connection.packetBuilder.TotalBytesCached.ToString() + " bytes, expecting " + connection.packetBuilder.TotalBytesExpected.ToString() + " bytes.");

                        connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                        if (connection.packetBuilder.TotalBytesCached > 0) connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                        if (connection.packetBuilder.TotalPartialPacketCount > 0)
                            LogTools.LogException(new Exception("Packet builder had " + connection.packetBuilder.TotalBytesCached + " bytes remaining after a call to IncomingPacketHandleHandOff with connection " + connection.ConnectionInfo + ". Until sequenced packets are implemented this indicates a possible error."), "UDPConnectionError");
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
                //Receive may throw a SocketException ErrorCode=10054  after attempting to send a datagram to an unreachable target. 
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
                LogTools.LogException(ex, "Error_UDPConnectionIncomingPacketHandler");
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

                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.None, 0);
                    byte[] receivedBytes = udpClient.Receive(ref remoteEndPoint);

                    //Received data after comms shutdown initiated. We should just close the connection
                    if (NetworkComms.commsShutdown) CloseConnection(false, -14);

                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Received " + receivedBytes.Length.ToString() + " bytes via UDP from " + remoteEndPoint.Address + ":" + remoteEndPoint.Port.ToString() + ".");

                    UDPConnection connection;
                    HandshakeUDPDatagram possibleHandshakeUDPDatagram = new HandshakeUDPDatagram(receivedBytes);
                    if (isIsolatedUDPConnection)
                        //This connection was created for a specific remoteEndPoint so we can handle the data internally
                        connection = this;
                    else
                    {
                        ConnectionInfo desiredConnection = new ConnectionInfo(ConnectionType.UDP, remoteEndPoint, udpClient.LocalIPEndPoint, ConnectionInfo.ApplicationLayerProtocol, ConnectionInfo.ConnectionListener);
                        try
                        {
                            //Look for an existing connection, if one does not exist we will create it
                            //This ensures that all further processing knows about the correct endPoint
                            connection = GetConnection(desiredConnection, ConnectionUDPOptions, ConnectionDefaultSendReceiveOptions, false, this, possibleHandshakeUDPDatagram);
                        }
                        catch (ConnectionShutdownException)
                        {
                            if ((ConnectionUDPOptions & UDPOptions.Handshake) == UDPOptions.Handshake)
                            {
                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Attempted to get connection " + desiredConnection + " but this caused a ConnectionShutdownException. Exception caught and ignored as should only happen if the connection was closed shortly after being created.");
                                connection = null;
                            }
                            else
                                throw;
                        }
                    }

                    if (connection != null && !possibleHandshakeUDPDatagram.DatagramHandled)
                    {
                        //We pass the data off to the specific connection
                        //Lock on the packetbuilder locker as we may receive UDP packets in parallel from this host
                        lock (connection.packetBuilder.Locker)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + receivedBytes.Length.ToString() + " bytes added to packetBuilder for " + connection.ConnectionInfo + ". Cached " + connection.packetBuilder.TotalBytesCached.ToString() + " bytes, expecting " + connection.packetBuilder.TotalBytesExpected.ToString() + " bytes.");

                            connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                            if (connection.packetBuilder.TotalBytesCached > 0) connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                            if (connection.packetBuilder.TotalPartialPacketCount > 0)
                                LogTools.LogException(new Exception("Packet builder had " + connection.packetBuilder.TotalBytesCached + " bytes remaining after a call to IncomingPacketHandleHandOff with connection " + connection.ConnectionInfo + ". Until sequenced packets are implemented this indicates a possible error."), "UDPConnectionError");
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
                //Receive may throw a SocketException ErrorCode=10054  after attempting to send a datagram to an unreachable target. 
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
                LogTools.LogException(ex, "Error_UDPConnectionIncomingPacketHandler");
                CloseConnection(true, 41);
            }

            //Clear the listen thread object because the thread is about to end
            incomingDataListenThread = null;

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Incoming data listen thread ending for " + ConnectionInfo);
        }
#endif
    }
}