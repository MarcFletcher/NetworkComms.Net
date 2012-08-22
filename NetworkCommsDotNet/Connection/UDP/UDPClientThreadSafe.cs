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
    //Provides a threadsafe wrapper around a udpClient object
    public class UdpClientThreadSafe
    {
        UdpClient udpClient;
        object locker = new object();

        public UdpClientThreadSafe(UdpClient udpClient)
        {
            this.udpClient = udpClient;
        }

        public void Send(byte[] dgram, int bytes, IPEndPoint endPoint)
        {
            lock (locker)
                udpClient.Send(dgram, bytes, endPoint);
        }

        public void Connect(IPEndPoint endPoint)
        {
            lock (locker)
                udpClient.Connect(endPoint);
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
    }

}
