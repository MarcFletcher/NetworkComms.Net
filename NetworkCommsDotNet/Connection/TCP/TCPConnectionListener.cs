//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

#if WINDOWS_PHONE || NETFX_CORE
using Windows.Networking.Sockets;
#endif

namespace NetworkCommsDotNet.Connections.TCP
{
    /// <summary>
    /// A TCP connection listener
    /// </summary>
    public class TCPConnectionListener : ConnectionListenerBase
    {
#if WINDOWS_PHONE || NETFX_CORE
        /// <summary>
        /// The equivalent TCPListener class in windows phone
        /// </summary>
        StreamSocketListener listenerInstance;
#else
        /// <summary>
        /// The .net TCPListener class.
        /// </summary>
        TcpListener listenerInstance;

        /// <summary>
        /// SSL options that are associated with this listener
        /// </summary>
        public SSLOptions SSLOptions { get; private set; }
#endif

        /// <summary>
        /// Create a new instance of a TCP listener
        /// </summary>
        /// <param name="sendReceiveOptions">The SendReceiveOptions to use with incoming data on this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        public TCPConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol, bool isDiscoverable = false)
            :base(ConnectionType.TCP, sendReceiveOptions, applicationLayerProtocol, isDiscoverable)
        {
#if !WINDOWS_PHONE && !NETFX_CORE
            SSLOptions = new SSLOptions();
#endif
        }

#if !WINDOWS_PHONE && !NETFX_CORE
        /// <summary>
        /// Create a new instance of a TCP listener
        /// </summary>
        /// <param name="sendReceiveOptions">The SendReceiveOptions to use with incoming data on this listener</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="sslOptions">The SSLOptions to use with this listener</param>
        public TCPConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol, SSLOptions sslOptions, bool isDiscoverable = false)
            : base(ConnectionType.TCP, sendReceiveOptions, applicationLayerProtocol, isDiscoverable)
        {
            this.SSLOptions = sslOptions;
        }
#endif

        /// <inheritdoc />
        internal override void StartListening(EndPoint desiredLocalListenEndPoint, bool useRandomPortFailOver)
        {
            if (desiredLocalListenEndPoint.GetType() != typeof(IPEndPoint)) throw new ArgumentException("Invalid desiredLocalListenEndPoint type provided.", "desiredLocalListenEndPoint");
            if (IsListening) throw new InvalidOperationException("Attempted to call StartListening when already listening.");

            IPEndPoint desiredLocalListenIPEndPoint = (IPEndPoint)desiredLocalListenEndPoint;

            try
            {
#if WINDOWS_PHONE || NETFX_CORE
                listenerInstance = new StreamSocketListener();
                listenerInstance.ConnectionReceived += newListenerInstance_ConnectionReceived;
                listenerInstance.BindEndpointAsync(new Windows.Networking.HostName(desiredLocalListenIPEndPoint.Address.ToString()), desiredLocalListenIPEndPoint.Port.ToString()).AsTask().Wait();
#else
                listenerInstance = new TcpListener(desiredLocalListenIPEndPoint);
                listenerInstance.Start();
                listenerInstance.BeginAcceptTcpClient(TCPConnectionReceivedAsync, null);
#endif
            }
            catch (SocketException)
            {
                //If the port we wanted is not available
                if (useRandomPortFailOver)
                {
                    try
                    {
#if WINDOWS_PHONE || NETFX_CORE
                        listenerInstance.BindEndpointAsync(new Windows.Networking.HostName(desiredLocalListenIPEndPoint.Address.ToString()), "").AsTask().Wait(); 
#else
                        listenerInstance = new TcpListener(desiredLocalListenIPEndPoint.Address, 0);
                        listenerInstance.Start();
                        listenerInstance.BeginAcceptTcpClient(TCPConnectionReceivedAsync, null);
#endif
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

#if WINDOWS_PHONE || NETFX_CORE
            this.LocalListenEndPoint = new IPEndPoint(desiredLocalListenIPEndPoint.Address, int.Parse(listenerInstance.Information.LocalPort));  
#else
            this.LocalListenEndPoint = (IPEndPoint)listenerInstance.LocalEndpoint;
#endif
            if (IsDiscoverable)
                PeerDiscovery.EnableDiscoverable(PeerDiscovery.DiscoveryMethod.UDPBroadcast);

            this.IsListening = true;
        }

        /// <inheritdoc />
        internal override void StopListening()
        {
            IsListening = false;

            try
            {
#if WINDOWS_PHONE || NETFX_CORE
                listenerInstance.Dispose();
#else
                listenerInstance.Stop();
#endif
            }
            catch (Exception) { }
        }

#if WINDOWS_PHONE || NETFX_CORE
        private void newListenerInstance_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(args.Socket.Information.LocalAddress.DisplayName.ToString()), int.Parse(args.Socket.Information.LocalPort));
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(args.Socket.Information.RemoteAddress.DisplayName.ToString()), int.Parse(args.Socket.Information.RemotePort));

                ConnectionInfo newConnectionInfo = new ConnectionInfo(ConnectionType.TCP, remoteEndPoint, localEndPoint, ApplicationLayerProtocol, this);
                TCPConnection.GetConnection(newConnectionInfo, NetworkComms.DefaultSendReceiveOptions, args.Socket, true);
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
                        NetworkComms.LogError(ex, "ConnectionSetupError_SE");
                    else
                        NetworkComms.LogError(ex, "ConnectionSetupError");
                }
            }
        }
#else
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

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("New TCP connection from " + newConnectionInfo);

                ThreadPool.QueueUserWorkItem(new WaitCallback((obj) =>
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
                                NetworkComms.LogError(ex, "ConnectionSetupError_SE");
                            else
                                NetworkComms.LogError(ex, "ConnectionSetupError");
                        }
                    }
                    #endregion
                }));
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
                        NetworkComms.LogError(ex, "ConnectionSetupError_SE");
                    else
                        NetworkComms.LogError(ex, "ConnectionSetupError");
                }
            }
            finally
            {
                listenerInstance.BeginAcceptTcpClient(TCPConnectionReceivedAsync, null);
            }
        }
#endif
    }
}
