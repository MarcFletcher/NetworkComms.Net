//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Threadsafe wrapper around a udpClient object as it may be used by multiple UDP connections at any one time
    /// </summary>
    class UdpClientThreadSafe
    {
        UdpClient udpClient;
        bool isConnected;
        object locker = new object();

        /// <summary>
        /// IOControl value used to ignore ICMP destination unreachable packets which result in the socket closing
        /// </summary>
        const int SIO_UDP_CONNRESET = -1744830452;

        public UdpClientThreadSafe(UdpClient udpClient)
        {
            this.udpClient = udpClient;

            //By default we ignore ICMP destination unreachable packets so that we can continue to use the udp client even if we send something down a black hole
            //This is unsupported in Mono but also not required as the same behaviour is not observed.
            if (UDPConnection.IgnoreICMPDestinationUnreachable && Type.GetType("Mono.Runtime") == null)
                this.udpClient.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, new byte[] { 0, 0, 0, 0 });
        }

        public void Send(byte[] dgram, int bytes, IPEndPoint endPoint)
        {
            lock (locker)
            {
                if (isConnected)
                {
                    if (!endPoint.Equals(udpClient.Client.RemoteEndPoint))
                        throw new CommunicationException("Attempted to send UDP packet to an endPoint other than that to which this UDP client is specifically connected.");
                    else
                        udpClient.Send(dgram, bytes);
                }
                else
                    udpClient.Send(dgram, bytes, endPoint);
            }
        }

        public void Connect(IPEndPoint endPoint)
        {
            lock (locker)
            {
                isConnected = true;
                udpClient.Connect(endPoint);
            }
        }

        public void AllowNatTraversal(bool allowed)
        {
            lock (locker)
                udpClient.AllowNatTraversal(allowed);
        }

        public void CloseClient()
        {
            lock (locker)
            {
                try
                {
                    udpClient.Client.Disconnect(false);
                    udpClient.Client.Close();
                    udpClient.Client.Dispose();
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
            }
        }

        public byte[] Receive(ref IPEndPoint remoteEP)
        {
            //We are best of avoiding a lock here as this call will block until data is received
            return udpClient.Receive(ref remoteEP);
        }

        public IAsyncResult BeginReceive(AsyncCallback requestCallback, object state)
        {
            lock (locker)
                return udpClient.BeginReceive(requestCallback, state);
        }

        public byte[] EndReceive(IAsyncResult asyncResult, ref IPEndPoint remoteEP)
        {
            lock (locker)
                return udpClient.EndReceive(asyncResult, ref remoteEP);
        }

        public IPEndPoint RemoteEndPoint
        {
            get
            {
                lock (locker)
                    return (IPEndPoint)udpClient.Client.RemoteEndPoint;
            }
        }

        public IPEndPoint LocalEndPoint
        {
            get
            {
                lock (locker)
                    return (IPEndPoint)udpClient.Client.LocalEndPoint;
            }
        }

        public bool DataAvailable
        {
            get
            {
                lock (locker)
                    return udpClient.Available > 0;
            }
        }
    }

}
