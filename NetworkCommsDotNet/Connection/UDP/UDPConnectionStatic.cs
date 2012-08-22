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
    public partial class UDPConnection : Connection
    {
        static object udpClientListenerLocker = new object();
        static Dictionary<IPEndPoint, UDPConnection> udpConnectionListeners = new Dictionary<IPEndPoint, UDPConnection>();

        //The rogue udp connection is used for sending ONLY if no available locally bound client is available
        static object udpRogueSenderLocker = new object();
        static UDPConnection udpRogueSender;

        const int maximumSingleDatagramSizeBytes = 65506;

        static UDPConnection()
        {
            lock (udpRogueSenderLocker)
            {
                if (udpRogueSender == null)
                    udpRogueSender = new UDPConnection(new ConnectionInfo(true, ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(IPAddress.Any, 0)), NetworkComms.DefaultSendReceiveOptions, UDPLevel.None, false);
            }
        }

        public UDPConnection CreateConnection(ConnectionInfo connectionInfo, UDPLevel level, bool establishIfRequired = true)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Listen for incoming UDP packets on the default listen port across all available IP's
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

        /// <summary>
        /// Accept new UDP packets on specified IP's and port's
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
        /// Accept new UDP packets on the specified IP and Port
        /// </summary>
        /// <param name="newLocalEndPoint"></param>
        /// <param name="useRandomPortFailOver"></param>
        public static void AddNewLocalListener(IPEndPoint newLocalEndPoint, bool useRandomPortFailOver = true)
        {
            lock (udpClientListenerLocker)
            {
                if (udpConnectionListeners.ContainsKey(newLocalEndPoint))
                    throw new CommsSetupShutdownException("Provided newLocalEndPoint already exists in udpConnectionListeners.");

                UDPConnection newListeningConnection;

                try
                {
                    newListeningConnection = new UDPConnection(new ConnectionInfo(true, ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), newLocalEndPoint), NetworkComms.DefaultSendReceiveOptions, UDPLevel.None, true);
                }
                catch (SocketException)
                {
                    if (useRandomPortFailOver)
                        newListeningConnection = new UDPConnection(new ConnectionInfo(true, ConnectionType.UDP, new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(newLocalEndPoint.Address, 0)), NetworkComms.DefaultSendReceiveOptions, UDPLevel.None, true);
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
        }

        /// <summary>
        /// Returns an endPoint corresponding to a possible local listener on the provided ipAddress. If not listening on provided IP returns null.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns></returns>
        public static IPEndPoint ExistingLocalListener(IPAddress ipAddress)
        {
            lock (udpClientListenerLocker)
                return (from current in udpConnectionListeners.Keys where current.Address.Equals(ipAddress) select current).FirstOrDefault();
        }

        private static void IncomingUDPPacketWorker()
        {
            //This is the sync udp receive thread
        }

        /// <summary>
        /// Sends a single object to the provided endPoint. IMPORTANT NOTE: You must be listening for UDP packets if you want to pick up a reply. 
        /// </summary>
        /// <param name="sendingPacketType"></param>
        /// <param name="objectToSend"></param>
        /// <param name="ipEndPoint"></param>
        /// <param name="listenForReturnPackets"></param>
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
                    IPEndPoint existingLocalEndPoint = ExistingLocalListener(((IPEndPoint)testSocket.LocalEndPoint).Address);
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
