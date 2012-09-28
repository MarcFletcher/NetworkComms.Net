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
using System.IO;
using System.Threading;

namespace NetworkCommsDotNet
{
    public partial class UDPConnection : Connection
    {
        UdpClientThreadSafe udpClientThreadSafe;

        /// <summary>
        /// The level at which this connection operates
        /// </summary>
        UDPOptions udpLevel;

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
            udpLevel = level;

            if (listenForIncomingPackets && existingConnection != null)
                throw new Exception("Unable to listen for incoming packets if an existing client has been provided. This is to prevent possible multiple accidently listens on the same client.");

            if (existingConnection == null)
            {
                if (connectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))
                    //We are creating an unbound endPoint, this is currently the rogue UDP sender and listeners only
                    udpClientThreadSafe = new UdpClientThreadSafe(new UdpClient(ConnectionInfo.LocalEndPoint));
                else
                {
                    //If this is a specific connection we link to a default end point here
                    isIsolatedUDPConnection = true;

                    if (ConnectionInfo.LocalEndPoint == null)
                        udpClientThreadSafe = new UdpClientThreadSafe(new UdpClient(new IPEndPoint(IPAddress.Any, 0)));
                    else
                        udpClientThreadSafe = new UdpClientThreadSafe(new UdpClient(ConnectionInfo.LocalEndPoint));

                    //By calling connect we discard packets from anything other then the provided remoteEndPoint on our localEndPoint
                    udpClientThreadSafe.Connect(ConnectionInfo.RemoteEndPoint);
                }

                //Allow NAT traversal by default for all udp clients
                udpClientThreadSafe.AllowNatTraversal(true);

                if (listenForIncomingPackets)
                    StartIncomingDataListen();
            }
            else
            {
                if (!existingConnection.ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))
                    throw new Exception("If an existing udpClient is provided it must be unbound to a specific remoteEndPoint");

                //Using an exiting client allows us to send from the same port as for the provided existing connection
                this.udpClientThreadSafe = existingConnection.udpClientThreadSafe;
            }

            //We can update the localEndPoint so that it is correct
            if (ConnectionInfo.LocalEndPoint == null || ConnectionInfo.LocalEndPoint.Port == 0)
                ConnectionInfo.UpdateLocalEndPointInfo(udpClientThreadSafe.LocalEndPoint);
        }

        /// <summary>
        /// Establish this UDP connection. This will become more relevant as additional udp levels are supported.
        /// </summary>
        protected override void EstablishConnectionSpecific()
        {
            //There is generally no establish for a UDP connection
            if (udpLevel > 0)
                throw new NotImplementedException("A future version of networkComms will support additional udp levels.");
        }

        /// <summary>
        /// Executes UDP specific shutdown tasks
        /// </summary>
        /// <param name="closeDueToError">True if closing connection due to error</param>
        /// <param name="logLocation">An optional debug parameter.</param>
        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
            //We only call close on the udpClient if this is a specific udp connection or we are calling close from the parent udp connection
            if (isIsolatedUDPConnection || (ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any)))
                udpClientThreadSafe.CloseClient();
        }

        /// <summary>
        /// Send a packet to the RemoteEndPoint specified in the ConnectionInfo
        /// </summary>
        /// <param name="packet">Packet to send</param>
        protected override void SendPacketSpecific(Packet packet)
        {
            if (ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))
                throw new CommunicationException("Unable to send packet using this method as remoteEndPoint equals IPAddress.Any");

            byte[] headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);

            //We are limited in size for the isolated send
            if (headerBytes.Length + packet.PacketData.Length > maximumSingleDatagramSizeBytes)
                throw new CommunicationException("Attempted to send a udp packet whose serialised size was " + (headerBytes.Length + packet.PacketData.Length) + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes + ". Consider using a TCP connection to send this object.");

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending a UDP packet of type '" + packet.PacketHeader.PacketType + "' to " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");

            //Prepare the single byte array to send
            byte[] udpDatagram = new byte[headerBytes.Length + packet.PacketData.Length];

            Buffer.BlockCopy(headerBytes, 0, udpDatagram, 0, headerBytes.Length);
            Buffer.BlockCopy(packet.PacketData, 0, udpDatagram, headerBytes.Length, packet.PacketData.Length);

            udpClientThreadSafe.Send(udpDatagram, udpDatagram.Length, ConnectionInfo.RemoteEndPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Completed send of a UDP packet of type '" + packet.PacketHeader.PacketType + "' to " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");
        }

        /// <summary>
        /// Send a packet to the specified ipEndPoint. This feature is unique to UDP because of its connectionless structure.
        /// </summary>
        /// <param name="packet">Packet to send</param>
        /// <param name="ipEndPoint">The target ipEndPoint</param>
        private void SendPacketSpecific(Packet packet, IPEndPoint ipEndPoint)
        {
            byte[] headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);

            //We are limited in size for the isolated send
            if (headerBytes.Length + packet.PacketData.Length > maximumSingleDatagramSizeBytes)
                throw new CommunicationException("Attempted to send a udp packet whose serialised size was " + (headerBytes.Length + packet.PacketData.Length) + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes + ". Consider using a TCP connection to send this object.");

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending a UDP packet of type '" + packet.PacketHeader.PacketType + "' to " + ipEndPoint.Address + ":" + ipEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");

            //Prepare the single byte array to send
            byte[] udpDatagram = new byte[headerBytes.Length + packet.PacketData.Length];

            Buffer.BlockCopy(headerBytes, 0, udpDatagram, 0, headerBytes.Length);
            Buffer.BlockCopy(packet.PacketData, 0, udpDatagram, headerBytes.Length, packet.PacketData.Length);

            udpClientThreadSafe.Send(udpDatagram, udpDatagram.Length, ipEndPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Completed send of a UDP packet of type '" + packet.PacketHeader.PacketType + "' to " + ipEndPoint.Address + ":" + ipEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + packet.PacketData.Length + " payload bytes.");
        }

        /// <summary>
        /// Sends a null packet using UDP
        /// </summary>
        protected override void SendNullPacket()
        {
            //We cant send a null packet to the IPAddress.Any address
            if (!ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any))
            {
                try
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Sending null packet to " + ConnectionInfo);

                    //Send a single 0 byte
                    udpClientThreadSafe.Send(new byte[] { 0 }, 1, ConnectionInfo.RemoteEndPoint);

                    //Update the traffic time after we have written to netStream
                    ConnectionInfo.UpdateLastTrafficTime();
                }
                catch (Exception)
                {
                    CloseConnection(true, 19);
                }
            }
        }

        /// <summary>
        /// Start listening for incoming udp data
        /// </summary>
        protected override void StartIncomingDataListen()
        {
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
        }

        /// <summary>
        /// Incoming data listen async method
        /// </summary>
        /// <param name="ar">Call back state data</param>
        protected void IncomingUDPPacketHandler(IAsyncResult ar)
        {
            UdpClientThreadSafe client = (UdpClientThreadSafe)ar.AsyncState;
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] receivedBytes = client.EndReceive(ar, ref endPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Recieved " + receivedBytes.Length + " bytes via UDP from " + endPoint.Address + ":" + endPoint.Port + ".");

            if (isIsolatedUDPConnection)
            {
                //This connection was created for a specific remoteEndPoint so we can handle the data internally
                packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                IncomingPacketHandleHandOff(packetBuilder);
            }
            else
            {
                //Look for an existing connection, if one does not exist we will create it
                //This ensures that all further processing knows about the correct endPoint
                UDPConnection connection = CreateConnection(new ConnectionInfo(true, ConnectionType.UDP, endPoint, udpClientThreadSafe.LocalEndPoint), ConnectionDefaultSendReceiveOptions, udpLevel, false, this); 

                //We pass the data off to the specific connection
                connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                if (connection.packetBuilder.TotalPartialPacketCount > 0)
                    throw new Exception("Packet builder had remaining packets after a call to IncomingPacketHandleHandOff. Until sequenced packets are implemented this indicates a possible error.");
            }

            //Listen for more udp packets!!
            client.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), client);
        }

        /// <summary>
        /// Incoming data listen sync method
        /// </summary>
        protected void IncomingUDPPacketWorker()
        {
            try
            {
                while (true)
                {
                    if (ConnectionInfo.ConnectionShutdown)
                        break;

                    IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = udpClientThreadSafe.Receive(ref endPoint);

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Recieved " + receivedBytes.Length + " bytes via UDP from " + endPoint.Address + ":" + endPoint.Port + ".");

                    if (isIsolatedUDPConnection)
                    {
                        //This connection was created for a specific remoteEndPoint so we can handle the data internally
                        packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                        IncomingPacketHandleHandOff(packetBuilder);
                    }
                    else
                    {
                        //Look for an existing connection, if one does not exist we will create it
                        //This ensures that all further processing knows about the correct endPoint
                        UDPConnection connection = CreateConnection(new ConnectionInfo(true, ConnectionType.UDP, endPoint, udpClientThreadSafe.LocalEndPoint), ConnectionDefaultSendReceiveOptions, udpLevel, false, this);

                        //We pass the data off to the specific connection
                        connection.packetBuilder.AddPartialPacket(receivedBytes.Length, receivedBytes);
                        connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                        if (connection.packetBuilder.TotalPartialPacketCount > 0)
                            throw new Exception("Packet builder had remaining packets after a call to IncomingPacketHandleHandOff. Until sequenced packets are implemented this indicates a possible error.");
                    }
                }
            }
            //On any error here we close the connection
            catch (NullReferenceException)
            {
                CloseConnection(true, 20);
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
                CloseConnection(true, 23);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 24);
            }

            //Clear the listen thread object because the thread is about to end
            incomingDataListenThread = null;

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Incoming data listen thread ending for " + ConnectionInfo);
        }
    }
}
