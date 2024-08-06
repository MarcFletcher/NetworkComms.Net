﻿// 
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
using System.Net;
using System.Text;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.Net.Sockets;

namespace NetworkCommsDotNet.Connections.TCP
{
    /// <summary>
    /// A TCP connection listener
    /// </summary>
    public class TCPConnectionListener : ConnectionListenerBase
    {
        /// <summary>
        /// The .net TCPListener class.
        /// </summary>
        TcpListener listenerInstance;

        /// <summary>
        /// SSL options that are associated with this listener
        /// </summary>
        public SSLOptions SSLOptions { get; private set; }

        /// <summary>
        /// Create a new instance of a TCP listener
        /// </summary>
        /// <param name="sendReceiveOptions">The SendReceiveOptions to use with incoming data on this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="allowDiscoverable">Determines if the newly created <see cref="ConnectionListenerBase"/> will be discoverable if <see cref="Tools.PeerDiscovery"/> is enabled.</param>
        public TCPConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol, bool allowDiscoverable = false)
            :base(ConnectionType.TCP, sendReceiveOptions, applicationLayerProtocol, allowDiscoverable)
        {
            SSLOptions = new SSLOptions();
        }

        /// <summary>
        /// Create a new instance of a TCP listener
        /// </summary>
        /// <param name="sendReceiveOptions">The SendReceiveOptions to use with incoming data on this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="sslOptions">The SSLOptions to use with this listener</param>
        /// <param name="allowDiscoverable">Determines if the newly created <see cref="ConnectionListenerBase"/> will be discoverable if <see cref="Tools.PeerDiscovery"/> is enabled.</param>
        public TCPConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol, SSLOptions sslOptions, bool allowDiscoverable = false)
            : base(ConnectionType.TCP, sendReceiveOptions, applicationLayerProtocol, allowDiscoverable)
        {
            this.SSLOptions = sslOptions;
        }

        /// <inheritdoc />
        internal override void StartListening(EndPoint desiredLocalListenEndPoint, bool useRandomPortFailOver)
        {
            if (desiredLocalListenEndPoint.GetType() != typeof(IPEndPoint)) throw new ArgumentException("Invalid desiredLocalListenEndPoint type provided.", "desiredLocalListenEndPoint");
            if (IsListening) throw new InvalidOperationException("Attempted to call StartListening when already listening.");

            IPEndPoint desiredLocalListenIPEndPoint = (IPEndPoint)desiredLocalListenEndPoint;

            try
            {
                listenerInstance = new TcpListener(desiredLocalListenIPEndPoint);
                listenerInstance.Start();
                listenerInstance.BeginAcceptTcpClient(TCPConnectionReceivedAsync, null);
            }
            catch (SocketException)
            {
                //If the port we wanted is not available
                if (useRandomPortFailOver)
                {
                    try
                    {
                        listenerInstance = new TcpListener(desiredLocalListenIPEndPoint.Address, 0);
                        listenerInstance.Start();
                        listenerInstance.BeginAcceptTcpClient(TCPConnectionReceivedAsync, null);
                    }
                    catch (SocketException)
                    {
                        //If we get another socket exception this appears to be a bad IP. We will just ignore this IP
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to open a random port on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                        throw new CommsSetupShutdownException("It was not possible to open a random port on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }
                else
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to open port #" + desiredLocalListenIPEndPoint.Port.ToString() + " on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                    throw new CommsSetupShutdownException("It was not possible to open port #" + desiredLocalListenIPEndPoint.Port.ToString() + " on " + desiredLocalListenIPEndPoint.Address + ". This endPoint may not support listening or possibly try again using a different port.");
                }
            }

            this.LocalListenEndPoint = (IPEndPoint)listenerInstance.LocalEndpoint;

            this.IsListening = true;
        }

        /// <inheritdoc />
        internal override void StopListening()
        {
            IsListening = false;

            try
            {
                listenerInstance.Stop();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Async method for handling up new incoming TCP connections
        /// </summary>
        private void TCPConnectionReceivedAsync(IAsyncResult ar)
        {
            if (!IsListening)
                return;

            try
            {
                TcpClient newTCPClient = listenerInstance.EndAcceptTcpClient(ar);
                ConnectionInfo newConnectionInfo = new ConnectionInfo(ConnectionType.TCP, (IPEndPoint)newTCPClient.Client.RemoteEndPoint, (IPEndPoint)newTCPClient.Client.LocalEndPoint, ApplicationLayerProtocol, this);

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("New incoming TCP connection from " + newConnectionInfo);

                //We have to use our own thread pool here as the performance of the .Net one is awful
                NetworkComms.IncomingConnectionEstablishThreadPool.EnqueueItem(QueueItemPriority.Normal, new WaitCallback((obj) =>
                {
                    #region Pickup The New Connection
                    try
                    {
                        TCPConnection.GetConnection(newConnectionInfo, ListenerDefaultSendReceiveOptions, newTCPClient, true, SSLOptions);
                    }
                    catch (ConfirmationTimeoutException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (CommunicationException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (ConnectionSetupException)
                    {
                        //If we are the server end and we did not pick the incoming connection up then tooo bad!
                    }
                    catch (SocketException)
                    {
                        //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
                    }
                    catch (Exception ex)
                    {
                        //For some odd reason SocketExceptions don't always get caught above, so another check
                        if (ex.GetBaseException().GetType() != typeof(SocketException))
                        {
                            //Can we catch the socketException by looking at the string error text?
                            if (ex.ToString().StartsWith("System.Net.Sockets.SocketException"))
                                LogTools.LogException(ex, "ConnectionSetupError_SE");
                            else
                                LogTools.LogException(ex, "ConnectionSetupError");
                        }
                    }
                    #endregion
                }), null);
            }
            catch (SocketException)
            {
                //If this exception gets thrown its generally just a client closing a connection almost immediately after creation
            }
            catch (Exception ex)
            {
                //For some odd reason SocketExceptions don't always get caught above, so another check
                if (ex.GetBaseException().GetType() != typeof(SocketException))
                {
                    //Can we catch the socketException by looking at the string error text?
                    if (ex.ToString().StartsWith("System.Net.Sockets.SocketException"))
                        LogTools.LogException(ex, "ConnectionSetupError_SE");
                    else
                        LogTools.LogException(ex, "ConnectionSetupError");
                }
            }
            finally
            {
                listenerInstance.BeginAcceptTcpClient(TCPConnectionReceivedAsync, null);
            }
        }
    }
}
