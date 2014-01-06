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
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using DPSBase;

#if WINDOWS_PHONE
using Windows.Networking.Sockets;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
#else
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
#endif

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A connection object which utilises <see href="http://en.wikipedia.org/wiki/Transmission_Control_Protocol">TCP</see> to communicate between peers.
    /// </summary>
    public sealed partial class TCPConnection : Connection
    {
#if WINDOWS_PHONE
        /// <summary>
        /// The windows phone socket corresponding to this connection.
        /// </summary>
        StreamSocket socket;
#else
        /// <summary>
        /// The TcpClient corresponding to this connection.
        /// </summary>
        TcpClient tcpClient;

        /// <summary>
        /// The networkstream associated with the tcpClient.
        /// </summary>
        Stream connectionStream;

        /// <summary>
        /// The SSL options associated with this connection.
        /// </summary>
        public SSLOptions SSLOptions { get; private set; }
#endif

        /// <summary>
        /// TCP connection constructor
        /// </summary>
#if WINDOWS_PHONE
        private TCPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, StreamSocket socket)
#else
        private TCPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, TcpClient tcpClient, SSLOptions sslOptions)
#endif
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            //We don't guarantee that the tcpClient has been created yet
#if WINDOWS_PHONE
            if (socket != null) this.socket = socket;
#else
            if (tcpClient != null) this.tcpClient = tcpClient;
            this.SSLOptions = sslOptions;
#endif
        }

        /// <summary>
        /// Establish the connection
        /// </summary>
        protected override void EstablishConnectionSpecific()
        {
#if WINDOWS_PHONE
            if (socket == null) ConnectSocket();

            //For the local endpoint
            var localEndPoint = new IPEndPoint(IPAddress.Parse(socket.Information.LocalAddress.CanonicalName.ToString()), int.Parse(socket.Information.LocalPort));

            //We should now be able to set the connectionInfo localEndPoint
            NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteEndPoint, localEndPoint);
            ConnectionInfo.UpdateLocalEndPointInfo(localEndPoint);

            //Set the outgoing buffer size
            socket.Control.OutboundBufferSizeInBytes = (uint)NetworkComms.SendBufferSizeBytes;
#else
            if (tcpClient == null) ConnectSocket();

            //We should now be able to set the connectionInfo localEndPoint
            NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteEndPoint, (IPEndPoint)tcpClient.Client.LocalEndPoint);
            ConnectionInfo.UpdateLocalEndPointInfo((IPEndPoint)tcpClient.Client.LocalEndPoint);

            if (SSLOptions.SSLEnabled)
                ConfigureSSLStream();
            else
                //We are going to be using the networkStream quite a bit so we pull out a reference once here
                connectionStream = tcpClient.GetStream();

            //When we tell the socket/client to close we want it to do so immediately
            //this.tcpClient.LingerState = new LingerOption(false, 0);

            //We need to set the keep alive option otherwise the connection will just die at some random time should we not be using it
            //NOTE: This did not seem to work reliably so was replaced with the keepAlive packet feature
            //this.tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            tcpClient.ReceiveBufferSize = NetworkComms.ReceiveBufferSizeBytes;
            tcpClient.SendBufferSize = NetworkComms.SendBufferSizeBytes;

            //This disables the 'nagle alogrithm'
            //http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.nodelay.aspx
            //Basically we may want to send lots of small packets (<200 bytes) and sometimes those are time critical (e.g. when establishing a connection)
            //If we leave this enabled small packets may never be sent until a suitable send buffer length threshold is passed. i.e. BAD
            tcpClient.NoDelay = true;
            tcpClient.Client.NoDelay = true;
#endif

            //Start listening for incoming data
            StartIncomingDataListen();

            //If the application layer protocol is enabled we handshake the connection
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
                ConnectionHandshake();
            else
            {
                //If there is no handshake we can now consider the connection established
                TriggerConnectionEstablishDelegates();

                //Trigger any connection setup waits
                connectionSetupWait.Set();
            }

#if !WINDOWS_PHONE
            //Once the connection has been established we may want to re-enable the 'nagle algorithm' used for reducing network congestion (apparently).
            //By default we leave the nagle algorithm disabled because we want the quick through put when sending small packets
            if (EnableNagleAlgorithmForNewConnections)
            {
                tcpClient.NoDelay = false;
                tcpClient.Client.NoDelay = false;
            }
#endif
        }

        /// <summary>
        /// If we were not provided with a tcpClient on creation we need to create one
        /// </summary>
        private void ConnectSocket()
        {
            try
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Connecting TCP client with " + ConnectionInfo);

                bool connectSuccess = true;
#if WINDOWS_PHONE
                //We now connect to our target
                socket = new StreamSocket();
                socket.Control.NoDelay = EnableNagleAlgorithmForNewConnections;

                CancellationTokenSource cancelAfterTimeoutToken = new CancellationTokenSource(NetworkComms.ConnectionEstablishTimeoutMS);

                try
                {
                    if (ConnectionInfo.LocalEndPoint != null)
                    {
                        var endpointPairForConnection = new Windows.Networking.EndpointPair(new Windows.Networking.HostName(ConnectionInfo.LocalEndPoint.Address.ToString()), ConnectionInfo.LocalEndPoint.Port.ToString(),
                                                        new Windows.Networking.HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString());

                        var task = socket.ConnectAsync(endpointPairForConnection).AsTask(cancelAfterTimeoutToken.Token);
                        task.Wait();
                    }
                    else
                    {
                        var task = socket.ConnectAsync(new Windows.Networking.HostName(ConnectionInfo.RemoteEndPoint.Address.ToString()), ConnectionInfo.RemoteEndPoint.Port.ToString()).AsTask(cancelAfterTimeoutToken.Token);
                        task.Wait();
                    }
                }
                catch (Exception)
                {
                    socket.Dispose();
                    connectSuccess = false;
                }
#else
                //We now connect to our target
                tcpClient = new TcpClient(ConnectionInfo.RemoteEndPoint.AddressFamily);

                //Start the connection using the asyn version
                //This allows us to choose our own connection establish timeout
                IAsyncResult ar = tcpClient.BeginConnect(ConnectionInfo.RemoteEndPoint.Address, ConnectionInfo.RemoteEndPoint.Port, null, null);
                WaitHandle connectionWait = ar.AsyncWaitHandle;
                try
                {
                    if (!ar.AsyncWaitHandle.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS, false))
                    {
                        tcpClient.Close();
                        connectSuccess = false;
                    }

                    tcpClient.EndConnect(ar);
                }
                finally
                {
                    connectionWait.Close();
                }
#endif

                if (!connectSuccess) throw new ConnectionSetupException("Timeout waiting for remoteEndPoint to accept TCP connection.");
            }
            catch (Exception ex)
            {
                CloseConnection(true, 17);
                throw new ConnectionSetupException("Error during TCP connection establish with destination (" + ConnectionInfo + "). Destination may not be listening or connect timed out. " + ex.ToString());
            }
        }

        /// <summary>
        /// Starts listening for incoming data on this TCP connection
        /// </summary>
        protected override void StartIncomingDataListen()
        {
            if (!NetworkComms.ConnectionExists(ConnectionInfo.RemoteEndPoint, ConnectionInfo.LocalEndPoint, ConnectionType.TCP, ConnectionInfo.ApplicationLayerProtocol))
            {
                CloseConnection(true, 18);
                throw new ConnectionSetupException("A connection reference by endPoint should exist before starting an incoming data listener.");
            }

#if WINDOWS_PHONE
            var stream = socket.InputStream.AsStreamForRead();
            stream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingTCPPacketHandler), stream);   
#else
            lock (delegateLocker)
            {
                if (NetworkComms.ConnectionListenModeUseSync)
                {
                    if (incomingDataListenThread == null)
                    {
                        incomingDataListenThread = new Thread(IncomingTCPDataSyncWorker);
                        //Incoming data always gets handled in a time critical fashion
                        incomingDataListenThread.Priority = NetworkComms.timeCriticalThreadPriority;
                        incomingDataListenThread.Name = "IncomingDataListener";
                        incomingDataListenThread.Start();
                    }
                }
                else
                    connectionStream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingTCPPacketHandler), connectionStream);
            }
#endif

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Listening for incoming data from " + ConnectionInfo);
        }

        /// <summary>
        /// Asynchronous incoming connection data delegate
        /// </summary>
        /// <param name="ar">The call back state object</param>
        private void IncomingTCPPacketHandler(IAsyncResult ar)
        {
            //Initialised with true so that logic still works in WP8
            bool dataAvailable = true;

#if !WINDOWS_PHONE
            //Incoming data always gets handled in a timeCritical fashion at this point
            Thread.CurrentThread.Priority = NetworkComms.timeCriticalThreadPriority;
            //int bytesRead;
#endif

            try
            {
#if WINDOWS_PHONE
                var stream = ar.AsyncState as Stream;
                var count = stream.EndRead(ar);
                totalBytesRead = count + totalBytesRead;
#else
                Stream netStream;
                if (SSLOptions.SSLEnabled)
                    netStream = (SslStream)ar.AsyncState;
                else
                    netStream = (NetworkStream)ar.AsyncState;

                if (!netStream.CanRead)
                    throw new ObjectDisposedException("Unable to read from stream.");

                totalBytesRead = netStream.EndRead(ar) + totalBytesRead;

                if (SSLOptions.SSLEnabled)
                    //SSLstream does not have a DataAvailable property. We will just assume false.
                    dataAvailable = false;
                else
                    dataAvailable = ((NetworkStream)netStream).DataAvailable;
#endif
                if (totalBytesRead > 0)
                {
                    ConnectionInfo.UpdateLastTrafficTime();

                    //If we have read a single byte which is 0 and we are not expecting other data
                    if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled && 
                        totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingPacketHandler() from " + ConnectionInfo + ". 1");
                    }
                    else
                    {
                        //if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder.");

                        //If there is more data to get then add it to the packets lists;
                        packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);

#if !WINDOWS_PHONE
                        //If we have more data we might as well continue reading syncronously
                        //In order to deal with data as soon as we think we have sufficient we will leave this loop
                        while (dataAvailable && packetBuilder.TotalBytesCached < packetBuilder.TotalBytesExpected)
                        {
                            int bufferOffset = 0;

                            //We need a buffer for our incoming data
                            //First we try to reuse a previous buffer
                            if (packetBuilder.TotalPartialPacketCount > 0 && packetBuilder.NumUnusedBytesMostRecentPartialPacket() > 0)
                                dataBuffer = packetBuilder.RemoveMostRecentPartialPacket(ref bufferOffset);
                            else
                                //If we have nothing to reuse we allocate a new buffer
                                dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];

                            totalBytesRead = netStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                            if (totalBytesRead > 0)
                            {
                                ConnectionInfo.UpdateLastTrafficTime();

                                //If we have read a single byte which is 0 and we are not expecting other data
                                if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled && totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                                {
                                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet ignored in IncomingPacketHandler() from " + ConnectionInfo + ". 2");
                                    //LastTrafficTime = DateTime.Now;
                                }
                                else
                                {
                                    //if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder for connection with " + ConnectionInfo + ". Cached " + packetBuilder.TotalBytesCached.ToString() + "B, expecting " + packetBuilder.TotalBytesExpected.ToString() + "B.");
                                    packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                                    
                                    if (SSLOptions.SSLEnabled)
                                        //SSLstream does not have a DataAvailable property. We will just assume false.
                                        dataAvailable = false;
                                    else
                                        dataAvailable = ((NetworkStream)netStream).DataAvailable;
                                }
                            }
                            else
                                break;
                        }
#endif
                    }
                }

                if (packetBuilder.TotalBytesCached > 0 && packetBuilder.TotalBytesCached >= packetBuilder.TotalBytesExpected)
                {
                    //Once we think we might have enough data we call the incoming packet handle handoff
                    //Should we have a complete packet this method will start the appriate task
                    //This method will now clear byes from the incoming packets if we have received something complete.
                    IncomingPacketHandleHandOff(packetBuilder);
                }

                if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionState == ConnectionState.Shutdown))
                    CloseConnection(false, -2);
                else
                {
                    //We need a buffer for our incoming data
                    //First we try to reuse a previous buffer
                    if (packetBuilder.TotalPartialPacketCount > 0 && packetBuilder.NumUnusedBytesMostRecentPartialPacket() > 0)
                        dataBuffer = packetBuilder.RemoveMostRecentPartialPacket(ref totalBytesRead);
                    else
                    {
                        //If we have nothing to reuse we allocate a new buffer
                        dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];
                        totalBytesRead = 0;
                    }

#if WINDOWS_PHONE
                    stream.BeginRead(dataBuffer, totalBytesRead, dataBuffer.Length - totalBytesRead, IncomingTCPPacketHandler, stream);
#else
                    netStream.BeginRead(dataBuffer, totalBytesRead, dataBuffer.Length - totalBytesRead, IncomingTCPPacketHandler, netStream);
#endif
                }

            }
            catch (IOException)
            {
                CloseConnection(true, 12);
            }
            catch (ObjectDisposedException)
            {
                CloseConnection(true, 13);
            }
            catch (SocketException)
            {
                CloseConnection(true, 14);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 15);
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_TCPConnectionIncomingPacketHandler");
                CloseConnection(true, 31);
            }

#if !WINDOWS_PHONE
            Thread.CurrentThread.Priority = ThreadPriority.Normal;
#endif
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Synchronous incoming connection data worker
        /// </summary>
        private void IncomingTCPDataSyncWorker()
        {
            bool dataAvailable = false;

            try
            {
                while (true)
                {
                    if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                        break;

                    int bufferOffset = 0;

                    //We need a buffer for our incoming data
                    //First we try to reuse a previous buffer
                    if (packetBuilder.TotalPartialPacketCount > 0 && packetBuilder.NumUnusedBytesMostRecentPartialPacket() > 0)
                        dataBuffer = packetBuilder.RemoveMostRecentPartialPacket(ref bufferOffset);
                    else
                        //If we have nothing to reuse we allocate a new buffer
                        dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];

                    //We block here until there is data to read
                    //When we read data we read until method returns or we fill the buffer length
                    totalBytesRead = connectionStream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

                    //Check to see if there is more data ready to be read
                    if (SSLOptions.SSLEnabled)
                        //SSLstream does not have a DataAvailable property. We will just assume false.
                        dataAvailable = false;
                    else
                        dataAvailable = ((NetworkStream)connectionStream).DataAvailable;

                    //If we read any data it gets handed off to the packetBuilder
                    if (totalBytesRead > 0)
                    {
                        ConnectionInfo.UpdateLastTrafficTime();

                        //If we have read a single byte which is 0 and we are not expecting other data
                        if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled && totalBytesRead == 1 && dataBuffer[0] == 0 && packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached == 0)
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... null packet removed in IncomingDataSyncWorker() from "+ConnectionInfo+".");
                        }
                        else
                        {
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder for connection with " + ConnectionInfo + ". Cached " + packetBuilder.TotalBytesCached.ToString() + "B, expecting " + packetBuilder.TotalBytesExpected.ToString() + "B.");

                            packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                        }
                    }
                    else if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionState == ConnectionState.Shutdown))
                    {
                        //If we read 0 bytes and there is no data available we should be shutting down
                        CloseConnection(false, -10);
                        break;
                    }

                    //If we have read some data and we have more or equal what was expected we attempt a data handoff
                    if (packetBuilder.TotalBytesCached > 0 && packetBuilder.TotalBytesCached >= packetBuilder.TotalBytesExpected)
                        IncomingPacketHandleHandOff(packetBuilder);
                }
            }
            //On any error here we close the connection
            catch (NullReferenceException)
            {
                CloseConnection(true, 7);
            }
            catch (IOException)
            {
                CloseConnection(true, 8);
            }
            catch (ObjectDisposedException)
            {
                CloseConnection(true, 9);
            }
            catch (SocketException)
            {
                CloseConnection(true, 10);
            }
            catch (InvalidOperationException)
            {
                CloseConnection(true, 11);
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "Error_TCPConnectionIncomingPacketHandler");
                CloseConnection(true, 39);
            }

            //Clear the listen thread object because the thread is about to end
            incomingDataListenThread = null;

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Incoming data listen thread ending for " + ConnectionInfo);
        }

        /// <summary>
        /// Configure the SSL stream from this connection
        /// </summary>
        private void ConfigureSSLStream()
        {
            try
            {
                if (ConnectionInfo.ServerSide)
                {
                    connectionStream = new SslStream(tcpClient.GetStream(), false,
                        new RemoteCertificateValidationCallback(CertificateValidationCallback),
                        new LocalCertificateSelectionCallback(CertificateSelectionCallback));

                    ((SslStream)connectionStream).AuthenticateAsServer(SSLOptions.Certificate, SSLOptions.RequireMutualAuthentication, SslProtocols.Default, false);
                }
                else
                {
                    X509CertificateCollection certs = new X509CertificateCollection();

                    if (SSLOptions.Certificate != null) certs.Add(SSLOptions.Certificate);

                    //If we have a certificate set we use that to authenticate
                    connectionStream = new SslStream(tcpClient.GetStream(), false,
                        new RemoteCertificateValidationCallback(CertificateValidationCallback),
                        new LocalCertificateSelectionCallback(CertificateSelectionCallback));

                    ((SslStream)connectionStream).AuthenticateAsClient(SSLOptions.CertificateName, certs, SslProtocols.Default, false);

                }
            }
            catch (AuthenticationException ex)
            {
                throw new ConnectionSetupException("SSL authentication failed. Please check configuration and try again.", ex);
            }

            SSLOptions.Authenticated = true;
        }

        private bool CertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNotAvailable && ConnectionInfo.ServerSide)
                //If the client did not provide a remote certificate it may well be because
                //we were not requesting one
                return !SSLOptions.RequireMutualAuthentication;
            else if (SSLOptions.AllowSelfSignedCertificate && //If we allows self signed certificates we make sure the errors are correct
                chain.ChainStatus.Length == 1 && //Only a single chain error
                sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors &&
                chain.ChainStatus[0].Status == X509ChainStatusFlags.UntrustedRoot)
            {
                //If we have a local certificate we compare them
                if (SSLOptions.Certificate != null)
                    return certificate.Equals(SSLOptions.Certificate);
                else
                    return true;
            }
            else
                return false;
        }

        private X509Certificate CertificateSelectionCallback(object sender, string targetHost, X509CertificateCollection localCertificates, 
            X509Certificate remoteCertificate, string[] acceptableIssuers)
        {
            return SSLOptions.Certificate;
        }
#endif

        /// <summary>
        /// Closes the <see cref="TCPConnection"/>
        /// </summary>
        /// <param name="closeDueToError">Closing a connection due an error possibly requires a few extra steps.</param>
        /// <param name="logLocation">Optional debug parameter.</param>
        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
#if WINDOWS_PHONE
            //Try to close the socket
            try
            {
                socket.Dispose();
            }
            catch (Exception)
            {
            }
#else
            //The following attempts to correctly close the connection
            //Try to close the networkStream first
            try
            {
                if (connectionStream != null) connectionStream.Close();
            }
            catch (Exception)
            {
            }
            finally
            {
                connectionStream = null;
            }

            //Try to close the tcpClient
            try
            {
                tcpClient.Client.Disconnect(false);
                tcpClient.Client.Close();
            }
            catch (Exception)
            {
            }

            //Try to close the tcpClient
            try
            {
                tcpClient.Close();
            }
            catch (Exception)
            {
            }
#endif
        }

        /// <summary>
        /// Sends the provided packet to the remote end point
        /// </summary>
        /// <param name="packet">Packet to send</param>
        protected override void SendPacketSpecific(Packet packet)
        {
            byte[] headerBytes;

            //If this connection does not use the applicationlayerprotocol we need to check a few things
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
            {
                //Serialise the header
                headerBytes = packet.SerialiseHeader(NetworkComms.InternalFixedSendReceiveOptions);
            }
            else
            {
                headerBytes = new byte[0];

                if (packet.PacketHeader.PacketType != Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Unmanaged))
                    throw new UnexpectedPacketTypeException("Only 'Unmanaged' packet types can be used if the NetworkComms.Net application layer protocol is disabled.");

                if (packet.PacketData.Length == 0)
                    throw new NotSupportedException("Sending a zero length array if the NetworkComms.Net application layer protocol is disabled is not supported.");
            }

            double maxSendTimePerKB = double.MaxValue;
            if (!NetworkComms.DisableConnectionSendTimeouts)
            {
                if (SendTimesMSPerKBCache.Count > MinNumSendsBeforeConnectionSpecificSendTimeout)
                    maxSendTimePerKB = Math.Max(MinimumMSPerKBSendTimeout, SendTimesMSPerKBCache.CalculateMean() + NumberOfStDeviationsForWriteTimeout * SendTimesMSPerKBCache.CalculateStdDeviation());
                else
                    maxSendTimePerKB = DefaultMSPerKBSendTimeout;
            }

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Sending a packet of type '" + packet.PacketHeader.PacketType + "' to " +
                ConnectionInfo + " containing " + headerBytes.Length.ToString() + " header bytes and " + packet.PacketData.Length.ToString() + " payload bytes. Allowing " +
                maxSendTimePerKB.ToString("0.0##") + " ms/KB for send.");
            
            DateTime startTime = DateTime.Now;

            Stream sendingStream;
#if WINDOWS_PHONE
            sendingStream = socket.OutputStream.AsStreamForWrite();
#else
            sendingStream = connectionStream;
#endif

            //We only need to write the header if we have prepared the necessary bytes
            double headerWriteTime = 0;
            if (headerBytes.Length > 0)
            {
                headerWriteTime = StreamWriteWithTimeout.Write(headerBytes, headerBytes.Length, sendingStream, NetworkComms.SendBufferSizeBytes, maxSendTimePerKB, MinSendTimeoutMS);
                SendTimesMSPerKBCache.AddValue(headerWriteTime, headerBytes.Length);
            }
            
            //We can now write the payload data
            double dataWriteTime = 0;
            if (packet.PacketData.Length > 0)
                dataWriteTime = packet.PacketData.ThreadSafeStream.CopyTo(sendingStream, packet.PacketData.Start, packet.PacketData.Length, NetworkComms.SendBufferSizeBytes, maxSendTimePerKB, MinSendTimeoutMS);

#if WINDOWS_PHONE
            sendingStream.Flush();
#endif

            //We record each send independantly as if one is considerably larger than 
            //the other it will provide a much more reliable rate
            SendTimesMSPerKBCache.AddValue(dataWriteTime, packet.PacketData.Length);
            SendTimesMSPerKBCache.TrimList(MaxNumSendTimes);

            //Correctly dispose the stream if we are finished with it
            if (packet.PacketData.ThreadSafeStream.CloseStreamAfterSend)
                packet.PacketData.ThreadSafeStream.Close();

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + ((headerBytes.Length + packet.PacketData.Length)/1024.0).ToString("0.000") + "KB written to TCP netstream at average of " + (((headerBytes.Length + packet.PacketData.Length) / 1024.0) / (DateTime.Now - startTime).TotalSeconds).ToString("0.000") + "KB/s. Current:" + ((headerWriteTime + dataWriteTime)/2).ToString("0.00") + " ms/KB, AVG:" + SendTimesMSPerKBCache.CalculateMean().ToString("0.00")+ " ms/KB.");

#if !WINDOWS_PHONE
            if (!tcpClient.Connected)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
                throw new CommunicationException("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
            }
#endif
        }

        /// <summary>
        /// Send a null packet (1 byte) to the remotEndPoint. Helps keep the TCP connection alive while ensuring the bandwidth usage is an absolute minimum. If an exception is thrown the connection will be closed.
        /// </summary>
        protected override void SendNullPacket()
        {
            //We can't send null packets if the application layer is disabled
            //as we have no way to distinquish them on the receiving side
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Ignoring null packet send to " + ConnectionInfo + " as the application layer protocol is disabled.");
                return;
            }

            try
            {
                //Only once the connection has been established do we send null packets
                if (ConnectionInfo.ConnectionState == ConnectionState.Established)
                {
                    //Multiple threads may try to send packets at the same time so we need this lock to prevent a thread cross talk
                    lock (sendLocker)
                    {
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Sending null packet to " + ConnectionInfo);

                        //Send a single 0 byte
                        double maxSendTimePerKB = double.MaxValue;
                        if (!NetworkComms.DisableConnectionSendTimeouts)
                        {
                            if (SendTimesMSPerKBCache.Count > MinNumSendsBeforeConnectionSpecificSendTimeout)
                                maxSendTimePerKB = Math.Max(MinimumMSPerKBSendTimeout, SendTimesMSPerKBCache.CalculateMean() + NumberOfStDeviationsForWriteTimeout * SendTimesMSPerKBCache.CalculateStdDeviation());
                            else
                                maxSendTimePerKB = DefaultMSPerKBSendTimeout;
                        }

#if WINDOWS_PHONE
                        var stream = socket.OutputStream.AsStreamForWrite();
                        StreamWriteWithTimeout.Write(new byte[] { 0 }, 1, stream, 1, maxSendTimePerKB, MinSendTimeoutMS);
                        stream.Flush();
#else
                        StreamWriteWithTimeout.Write(new byte[] { 0 }, 1, connectionStream, 1, maxSendTimePerKB, MinSendTimeoutMS);
#endif

                        //Update the traffic time after we have written to netStream
                        ConnectionInfo.UpdateLastTrafficTime();
                    }
                }

                //If the connection is shutdown we should call close
                if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown) CloseConnection(false, -8);
            }
            catch (Exception)
            {
                CloseConnection(true, 19);
            }
        }
    }
}
