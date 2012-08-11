using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;

namespace NetworkCommsDotNet
{
    public partial class UDPConnection : Connection
    {
        static object udpSingletonLocker = new object();
        static UdpClient udpClientSingleton;
        const int maximumSingleDatagramSizeBytes = 65506;

        static UDPConnection()
        {
            lock (udpSingletonLocker)
            {
                if (udpClientSingleton == null)
                    udpClientSingleton = new UdpClient();
            }
        }

        /// <summary>
        /// Listen for incoming udp connections on the default port across all adaptors
        /// </summary>
        public static void AddNewLocalConnectionListener()
        {
            UdpClient newClient = new UdpClient(new IPEndPoint(IPAddress.Any, NetworkComms.DefaultListenPort));
            //we listen for udp connections here

            //each adaptor listens on a single thread/callback

            //if the incoming packet type is a connection setup then we can launch the connection setup procedure

            //if the incoming packet type is anything else we just trigger our delegates
            newClient.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), newClient);
        }

        private static void IncomingUDPPacketHandler(IAsyncResult ar)
        {
            UdpClient client = (UdpClient)ar.AsyncState;
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] receivedBytes = client.EndReceive(ar, ref endPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Recieved " + receivedBytes.Length + " bytes via UDP.");

            //Listen for more udp packets!!
            client.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), client);
        }

        public static void SendObject(string sendingPacketType, object objectToSend, IPEndPoint ipEndPoint)
        {
            Packet sendPacket = new Packet(sendingPacketType, objectToSend, NetworkComms.DefaultSendReceiveOptions);
            
            //To keep memory copies to a minimum we send the header and payload in two calls to networkStream.Write
            byte[] headerBytes = sendPacket.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);

            //We are limited in size for the isolated send
            if (headerBytes.Length + sendPacket.PacketData.Length > maximumSingleDatagramSizeBytes)
                throw new CommunicationException("Attempted to send a udp packet whose serialised size was " + headerBytes.Length + sendPacket.PacketData.Length + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes + ". Consider using a TCP connection to send this object.");

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending a UDP packet of type '" + sendingPacketType + "' to " + ipEndPoint.Address + ":" + ipEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + sendPacket.PacketData.Length + " payload bytes.");

            //Prepare the single byte array to send
            byte[] udpDatagram = new byte[headerBytes.Length + sendPacket.PacketData.Length];

            Buffer.BlockCopy(headerBytes, 0, udpDatagram, 0, headerBytes.Length);
            Buffer.BlockCopy(sendPacket.PacketData, 0, udpDatagram, headerBytes.Length, sendPacket.PacketData.Length);

            lock (udpSingletonLocker)
                udpClientSingleton.Send(udpDatagram, udpDatagram.Length, ipEndPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Completed send of a UDP packet of type '" + sendingPacketType + "' to " + ipEndPoint.Address + ":" + ipEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + sendPacket.PacketData.Length + " payload bytes.");
        }
    }
}
