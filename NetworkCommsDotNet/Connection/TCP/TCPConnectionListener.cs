//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DPSBase;

#if WINDOWS_PHONE
using Windows.Networking.Sockets;
#endif

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A TCP connection listener
    /// </summary>
    public class TCPConnectionListener : ConnectionListenerBase
    {
#if WINDOWS_PHONE
        StreamSocketListener listenerInstance;
#else
        /// <summary>
        /// The .net TCPListener class.
        /// </summary>
        TcpListener listenerInstance;
#endif

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sendReceiveOptions"></param>
        /// <param name="applicationLayerProtocol"></param>
        public TCPConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol)
            :base(ConnectionType.TCP, sendReceiveOptions, applicationLayerProtocol)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="desiredLocalListenIPEndPoint"></param>
        /// <param name="useRandomPortFailOver"></param>
        internal override void StartListening(IPEndPoint desiredLocalListenIPEndPoint, bool useRandomPortFailOver)
        {
            if (IsListening) throw new InvalidOperationException("Attempted to call StartListening when already listening.");

            try
            {
#if WINDOWS_PHONE
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
#if WINDOWS_PHONE
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

#if WINDOWS_PHONE
            this.LocalListenIPEndPoint = new IPEndPoint(desiredLocalListenIPEndPoint.Address, int.Parse(listenerInstance.Information.LocalPort));  
#else
            this.LocalListenIPEndPoint = (IPEndPoint)listenerInstance.LocalEndpoint;
#endif

            this.IsListening = true;
        }

        /// <summary>
        /// Stop this TCP listener
        /// </summary>
        internal override void StopListening()
        {
            IsListening = false;

            try
            {
#if WINDOWS_PHONE
                listenerInstance.Dispose();
#else
                listenerInstance.Stop();
#endif
            }
            catch (Exception) { }
        }

#if WINDOWS_PHONE
        private void newListenerInstance_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(args.Socket.Information.LocalAddress.DisplayName.ToString()), int.Parse(args.Socket.Information.LocalPort));

                ConnectionInfo newConnectionInfo = new ConnectionInfo(true, ConnectionType.TCP, new IPEndPoint(IPAddress.Parse(args.Socket.Information.RemoteAddress.DisplayName.ToString()), int.Parse(args.Socket.Information.RemotePort)), ApplicationLayerProtocol);
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
        /// Async method for picking up new incoming TCP connections
        /// </summary>
        private void TCPConnectionReceivedAsync(IAsyncResult ar)
        {
            if (!IsListening)
                return;

            try
            {
                TcpClient newTCPClient = listenerInstance.EndAcceptTcpClient(ar);
                ConnectionInfo newConnectionInfo = new ConnectionInfo(true, ConnectionType.TCP, (IPEndPoint)newTCPClient.Client.RemoteEndPoint, ApplicationLayerProtocol);

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("New TCP connection from " + newConnectionInfo);

                ThreadPool.QueueUserWorkItem(new WaitCallback((obj) =>
                {
                    #region Pickup The New Connection
                    try
                    {
                        TCPConnection.GetConnection(newConnectionInfo, SendReceiveOptions, newTCPClient, true);
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
