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

namespace NetworkCommsDotNet
{
    //Maximum packet size is theoretically 65,507 bytes
    public partial class UDPConnection : Connection
    {
        UdpClientThreadSafe udpClientThreadSafe;
        UDPLevel udpLevel;

        //This is a dedicated connection object bound to a specific endPoint
        bool isSpecificUDPConnection;

        protected UDPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, UDPLevel level, bool listenForIncomingPackets, UDPConnection existingConnection = null)
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            udpLevel = level;
            isSpecificUDPConnection = false;

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
                    isSpecificUDPConnection = true;
                    udpClientThreadSafe = new UdpClientThreadSafe(new UdpClient(ConnectionInfo.LocalEndPoint));
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

                //Using an exiting client allows us to keep sending from the same local port for multiple connections
                this.udpClientThreadSafe = existingConnection.udpClientThreadSafe;
            }

            //We can update the localEndPoint so that it is correct
            if (ConnectionInfo.LocalEndPoint.Port == 0)
                ConnectionInfo.UpdateLocalEndPointInfo(udpClientThreadSafe.LocalEndPoint);
        }

        protected override void EstablishConnectionSpecific()
        {
            //There is generally no establish for a UDP connection
            if (udpLevel > 0)
                throw new NotImplementedException("Future version of networkComms will support udp levels correctly");
        }

        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
            //We only call close on the udpClient if this is a specific udp connection or we are calling close from the parent udp connection
            if (isSpecificUDPConnection || (ConnectionInfo.RemoteEndPoint.Address.Equals(IPAddress.Any)))
                udpClientThreadSafe.CloseClient();
        }

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

        protected override void StartIncomingDataListen()
        {
            if (NetworkComms.ConnectionListenModeUseSync)
                throw new NotImplementedException("Not yet implemented!");
            else
                udpClientThreadSafe.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), udpClientThreadSafe);
        }

        protected void IncomingUDPPacketHandler(IAsyncResult ar)
        {
            UdpClientThreadSafe client = (UdpClientThreadSafe)ar.AsyncState;
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] receivedBytes = client.EndReceive(ar, ref endPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Recieved " + receivedBytes.Length + " bytes via UDP from " + endPoint.Address + ":" + endPoint.Port + ".");

            if (isSpecificUDPConnection)
            {
                //This connection was created a specific connection so we can handle the data internally
                packetBuilder.AddPacket(receivedBytes.Length, receivedBytes);
                IncomingPacketHandleHandOff(packetBuilder);
            }
            else
            {
                //Look for an existing connection, if one does not exist we will create it
                //This ensures that all further processing knows about the correct endPoint
                UDPConnection connection;
                lock (NetworkComms.globalDictAndDelegateLocker)
                {
                    if (NetworkComms.ConnectionExists(endPoint, ConnectionType.UDP))
                        connection = (UDPConnection)NetworkComms.RetrieveConnection(endPoint, ConnectionType.UDP);
                    else
                        connection = new UDPConnection(new ConnectionInfo(true, ConnectionType.UDP, endPoint, udpClientThreadSafe.LocalEndPoint), ConnectionDefaultSendReceiveOptions, udpLevel, false, this);
                }

                //We pass the data off to the specific connection
                connection.packetBuilder.AddPacket(receivedBytes.Length, receivedBytes);
                connection.IncomingPacketHandleHandOff(connection.packetBuilder);

                if (connection.packetBuilder.CurrentPacketCount() > 0)
                    throw new Exception("Packet builder had remaining packets after a call to IncomingPacketHandleHandOff. Until sequenced packets are implemented this indicates a possible error.");
            }

            //Listen for more udp packets!!
            client.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), client);
        }

        public static void CloseAllConnections(EndPoint[] closeAllExceptTheseEndPoints)
        {
            throw new NotImplementedException();
        }

        internal static void Shutdown()
        {
            //Close any established udp listeners

            throw new NotImplementedException();
        }
    }
}
