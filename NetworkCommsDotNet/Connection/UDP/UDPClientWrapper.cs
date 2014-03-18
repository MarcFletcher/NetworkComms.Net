//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using NetworkCommsDotNet.DPSBase;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Connections.UDP
{
#if WINDOWS_PHONE || NETFX_CORE
    //UdpClientThreadSafe not yet required for WP8
#else
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

#if !ANDROID && !iOS
            //By default we ignore ICMP destination unreachable packets so that we can continue to use the udp client even if we send something down a black hole
            //This is unsupported in Mono but also not required as the same behaviour is not observed.
            if (UDPConnection.IgnoreICMPDestinationUnreachable && Type.GetType("Mono.Runtime") == null)
                this.udpClient.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, new byte[] { 0, 0, 0, 0 });
#endif
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
#endif
}
