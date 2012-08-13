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
    public partial class UDPConnection : Connection
    {
        static object udpClientListenerLocker = new object();
        static Dictionary<IPEndPoint, UdpClient> udpClientListeners = new Dictionary<IPEndPoint, UdpClient>();

        static object udpClientSenderLocker = new object();
        static UdpClient udpClientSender;
        const int maximumSingleDatagramSizeBytes = 65506;

        static UDPConnection()
        {
            lock (udpClientSenderLocker)
            {
                if (udpClientSender == null)
                    udpClientSender = new UdpClient(new IPEndPoint(IPAddress.Any, 10500));
            }
        }

        /// <summary>
        /// Listen for incoming udp connections on the default port across all adaptors
        /// </summary>
        public static void AddNewLocalListener()
        {
            List<IPAddress> localIPs = NetworkComms.AllAvailableLocalIPs();

            if (NetworkComms.ListenOnAllAllowedInterfaces)
            {
                try
                {
                    foreach (IPAddress ip in localIPs)
                    {
                        try
                        {
                            AddNewLocalListener(new IPEndPoint(ip, NetworkComms.DefaultListenPort), false);
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
                AddNewLocalListener(new IPEndPoint(localIPs[0], NetworkComms.DefaultListenPort), true);
        }

        public static void AddNewLocalListener(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true)
        {
            lock (udpClientListenerLocker)
            {
                if (udpClientListeners.ContainsKey(newLocalEndPoint))
                    throw new CommsSetupShutdownException("Provided newLocalEndPoint already exists in udpClientListeners.");

                UdpClient newListenerInstance;

                try
                {
                    newListenerInstance = new UdpClient(newLocalEndPoint);
                    newListenerInstance.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), newListenerInstance);
                }
                catch (SocketException)
                {
                    //If the port we wanted is not available
                    if (useRandomPortFailOver)
                    {
                        newListenerInstance = new UdpClient(new IPEndPoint(newLocalEndPoint.Address, 0));
                        newListenerInstance.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), newListenerInstance);
                    }
                    else
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Error("It was not possible to open port #" + newLocalEndPoint.Port + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                        throw new CommsSetupShutdownException("It was not possible to open port #" + newLocalEndPoint.Port + " on " + newLocalEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }

                if (udpClientListeners.ContainsKey((IPEndPoint)newListenerInstance.Client.LocalEndPoint))
                    throw new CommsSetupShutdownException("Unable to add new UDP listenerInstance to udpClientListeners as there is an existing entry.");
                else
                {
                    //If we were succesfull we can add the new localEndPoint to our dict
                    udpClientListeners.Add((IPEndPoint)newListenerInstance.Client.LocalEndPoint, newListenerInstance);
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Info("Added new UDP localEndPoint - " + newLocalEndPoint.Address + ":" + newLocalEndPoint.Port);
                }
            }
        }

        /// <summary>
        /// Accept new TCP connections on specified IP's and port's
        /// </summary>
        /// <param name="localEndPoint"></param>
        public static void AddNewLocalListener(List<IPEndPoint> localEndPoints, bool useRandomPortFailOver = true)
        {
            try
            {
                foreach (var endPoint in localEndPoints)
                    AddNewLocalListener(endPoint, useRandomPortFailOver);
            }
            catch (Exception)
            {
                //If there is an exception here we remove any added listeners and then rethrow
                Shutdown();
                throw;
            }
        }

        /// <summary>
        /// Returns an endPoint corresponding to a possible local listener on the provided ipAddress. If not listening on provided IP returns null.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns></returns>
        public static IPEndPoint ExistingLocalListener(IPAddress ipAddress)
        {
            lock (udpClientListenerLocker)
                return (from current in udpClientListeners.Keys where current.Address.Equals(ipAddress) select current).FirstOrDefault();
        }

        private static void IncomingUDPPacketHandler(IAsyncResult ar)
        {
            UdpClient client = (UdpClient)ar.AsyncState;
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] receivedBytes = client.EndReceive(ar, ref endPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Recieved " + receivedBytes.Length + " bytes via UDP from " + endPoint.Address + ":" + endPoint.Port + ".");

            //Listen for more udp packets!!
            client.BeginReceive(new AsyncCallback(IncomingUDPPacketHandler), client);
        }

        private static void IncomingUDPPacketWorker()
        {
            //This is the sync udp receive thread
        }

        public static void SendObject(string sendingPacketType, object objectToSend, IPEndPoint ipEndPoint)
        {
            //Packet sendPacket = new Packet(sendingPacketType, objectToSend, NetworkComms.DefaultSendReceiveOptions);
            Packet sendPacket = new Packet(sendingPacketType, objectToSend, NetworkComms.InternalFixedSendReceiveOptions);
            
            //To keep memory copies to a minimum we send the header and payload in two calls to networkStream.Write
            byte[] headerBytes = sendPacket.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);

            //We are limited in size for the isolated send
            if (headerBytes.Length + sendPacket.PacketData.Length > maximumSingleDatagramSizeBytes)
                throw new CommunicationException("Attempted to send a udp packet whose serialised size was " + (headerBytes.Length + sendPacket.PacketData.Length) + " bytes. The maximum size for a single UDP send is " + maximumSingleDatagramSizeBytes + ". Consider using a TCP connection to send this object.");

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Sending a UDP packet of type '" + sendingPacketType + "' to " + ipEndPoint.Address + ":" + ipEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + sendPacket.PacketData.Length + " payload bytes.");

            //Prepare the single byte array to send
            byte[] udpDatagram = new byte[headerBytes.Length + sendPacket.PacketData.Length];

            Buffer.BlockCopy(headerBytes, 0, udpDatagram, 0, headerBytes.Length);
            Buffer.BlockCopy(sendPacket.PacketData, 0, udpDatagram, headerBytes.Length, sendPacket.PacketData.Length);

            lock (udpClientSenderLocker)
                udpClientSender.Send(udpDatagram, udpDatagram.Length, ipEndPoint);

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Completed send of a UDP packet of type '" + sendingPacketType + "' to " + ipEndPoint.Address + ":" + ipEndPoint.Port + " containing " + headerBytes.Length + " header bytes and " + sendPacket.PacketData.Length + " payload bytes.");
        }
    }
}
