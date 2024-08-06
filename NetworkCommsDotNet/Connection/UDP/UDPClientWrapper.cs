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
using NetworkCommsDotNet.DPSBase;
using System.Net.Sockets;

namespace NetworkCommsDotNet.Connections.UDP
{

    /// <summary>
    /// Internal wrapper around a udpClient object so that we can easily manage usage.
    /// </summary>
    class UdpClientWrapper
    {
        UdpClient udpClient;
        bool isConnected;
        object locker = new object();

        /// <summary>
        /// IOControl value used to ignore ICMP destination unreachable packets which result in the socket closing
        /// </summary>
        const int SIO_UDP_CONNRESET = -1744830452;

        public UdpClientWrapper(UdpClient udpClient)
        {
            this.udpClient = udpClient;

            this.udpClient.EnableBroadcast = true;

            //By default we ignore ICMP destination unreachable packets so that we can continue to use the udp client even if we send something down a black hole
            //This is unsupported in Mono but also not required as the same behaviour is not observed.
            if (UDPConnection.IgnoreICMPDestinationUnreachable && Type.GetType("Mono.Runtime") == null)
                this.udpClient.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, new byte[] { 0, 0, 0, 0 });
        }

        public void Send(byte[] dgram, int bytes, IPEndPoint endPoint)
        {
            //lock (locker)
            //{
                if (isConnected)
                {
                    if (!endPoint.Equals(udpClient.Client.RemoteEndPoint))
                        throw new CommunicationException("Attempted to send UDP packet to an endPoint other than that to which this UDP client is specifically connected.");
                    else
                        udpClient.Send(dgram, bytes);
                }
                else
                    udpClient.Send(dgram, bytes, endPoint);
            //}
        }

        public void Connect(IPEndPoint endPoint)
        {
            //lock (locker)
            //{
                isConnected = true;
                udpClient.Connect(endPoint);
            //}
        }

        public void CloseClient()
        {
            //lock (locker)
            //{
                try
                {
                    udpClient.Client.Disconnect(false);
                    udpClient.Client.Close();                    
                }
                catch (Exception)
                {
                }

                //Try to close the udpClient
                try
                {
                    udpClient.Close();
                }
                catch (Exception)
                {
                }
            //}
        }

        public byte[] Receive(ref IPEndPoint remoteEP)
        {
            //We are best of avoiding a lock here as this call will block until data is received
            return udpClient.Receive(ref remoteEP);
        }

        public IAsyncResult BeginReceive(AsyncCallback requestCallback, object state)
        {
            //lock (locker)
                return udpClient.BeginReceive(requestCallback, state);
        }

        public byte[] EndReceive(IAsyncResult asyncResult, ref IPEndPoint remoteEP)
        {
            //lock (locker)
                return udpClient.EndReceive(asyncResult, ref remoteEP);
        }

        public AddressFamily ClientAddressFamily
        {
            get
            {
                //lock (locker)
                    return udpClient.Client.AddressFamily;
            }
        }

        public IPEndPoint RemoteIPEndPoint
        {
            get
            {
                //lock (locker)
                    return (IPEndPoint)udpClient.Client.RemoteEndPoint;
            }
        }

        public IPEndPoint LocalIPEndPoint
        {
            get
            {
                //lock (locker)
                    return (IPEndPoint)udpClient.Client.LocalEndPoint;
            }
        }

        public bool DataAvailable
        {
            get
            {
                //lock (locker)
                    return udpClient.Available > 0;
            }
        }
    }

}
