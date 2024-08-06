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
using System.Text;
using System.Threading;
using System.Net;
using System.IO;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace NetworkCommsDotNet.Connections.TCP
{
    /// <summary>
    /// A connection object which utilises <see href="http://en.wikipedia.org/wiki/Transmission_Control_Protocol">TCP</see> to communicate between peers.
    /// </summary>
    public sealed partial class TCPConnection : IPConnection
    {
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

        /// <summary>
        /// The current incoming data buffer
        /// </summary>
        byte[] dataBuffer;

        /// <summary>
        /// TCP connection constructor
        /// </summary>
        private TCPConnection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions, TcpClient tcpClient, SSLOptions sslOptions)
            : base(connectionInfo, defaultSendReceiveOptions)
        {
            if (connectionInfo.ConnectionType != ConnectionType.TCP)
                throw new ArgumentException("Provided connectionType must be TCP.", "connectionInfo");

            dataBuffer = new byte[NetworkComms.InitialReceiveBufferSizeBytes];

            //We don't guarantee that the tcpClient has been created yet
            if (tcpClient != null) this.tcpClient = tcpClient;
            this.SSLOptions = sslOptions;
        }

        /// <inheritdoc />
        protected override void EstablishConnectionSpecific()
        {
            if (tcpClient == null) ConnectSocket();

            //We should now be able to set the connectionInfo localEndPoint
            NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteIPEndPoint, (IPEndPoint)tcpClient.Client.LocalEndPoint);
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

            tcpClient.ReceiveBufferSize = NetworkComms.MaxReceiveBufferSizeBytes;
            tcpClient.SendBufferSize = NetworkComms.SendBufferSizeBytes;

            //This disables the 'nagle algorithm'
            //http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.nodelay.aspx
            //Basically we may want to send lots of small packets (<200 bytes) and sometimes those are time critical (e.g. when establishing a connection)
            //If we leave this enabled small packets may never be sent until a suitable send buffer length threshold is passed. i.e. BAD
            tcpClient.NoDelay = true;
            tcpClient.Client.NoDelay = true;

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

            //Once the connection has been established we may want to re-enable the 'nagle algorithm' used for reducing network congestion (apparently).
            //By default we leave the nagle algorithm disabled because we want the quick through put when sending small packets
            if (EnableNagleAlgorithmForNewConnections)
            {
                tcpClient.NoDelay = false;
                tcpClient.Client.NoDelay = false;
            }
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

                //We now connect to our target
                tcpClient = new TcpClient(ConnectionInfo.RemoteEndPoint.AddressFamily);

                //Start the connection using the async version
                //This allows us to choose our own connection establish timeout
                IAsyncResult ar = tcpClient.BeginConnect(ConnectionInfo.RemoteIPEndPoint.Address, ConnectionInfo.RemoteIPEndPoint.Port, null, null);
                WaitHandle connectionWait = ar.AsyncWaitHandle;
                try
                {
                    if (!connectionWait.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS, false))
                        connectSuccess = false;
                    else
                        tcpClient.EndConnect(ar);
                }
                finally
                {
                    connectionWait.Close();
                }

                if (!connectSuccess) throw new ConnectionSetupException("Timeout waiting for remoteEndPoint to accept TCP connection.");
            }
            catch (Exception ex)
            {
                CloseConnection(true, 17);
                throw new ConnectionSetupException("Error during TCP connection establish with destination (" + ConnectionInfo + "). Destination may not be listening or connect timed out. " + ex.ToString());
            }
        }

        /// <inheritdoc />
        protected override void StartIncomingDataListen()
        {
            if (!NetworkComms.ConnectionExists(ConnectionInfo.RemoteIPEndPoint, ConnectionInfo.LocalIPEndPoint, ConnectionType.TCP, ConnectionInfo.ApplicationLayerProtocol))
            {
                CloseConnection(true, 18);
                throw new ConnectionSetupException("A connection reference by endPoint should exist before starting an incoming data listener.");
            }

            lock (SyncRoot)
            {
                if (NetworkComms.ConnectionListenModeUseSync)
                {
                    if (incomingDataListenThread == null)
                    {
                        incomingDataListenThread = new Thread(IncomingTCPDataSyncWorker);
                        //Incoming data always gets handled in a time critical fashion
                        incomingDataListenThread.Priority = NetworkComms.timeCriticalThreadPriority;
                        incomingDataListenThread.Name = "UDP_IncomingDataListener";
                        incomingDataListenThread.IsBackground = true;
                        incomingDataListenThread.Start();
                    }
                }
                else
                {
                    if (asyncListenStarted) throw new ConnectionSetupException("Async listen already started. Why has this been called twice?.");

                    asyncListenerInRead = true;
                    connectionStream.BeginRead(dataBuffer, 0, dataBuffer.Length, new AsyncCallback(IncomingTCPPacketHandler), connectionStream);

                    asyncListenStarted = true;
                }
            }

            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Listening for incoming data from " + ConnectionInfo);
        }

        /// <summary>
        /// Asynchronous incoming connection data delegate
        /// </summary>
        /// <param name="ar">The call back state object</param>
        private void IncomingTCPPacketHandler(IAsyncResult ar)
        {
            //Initialised with false so that logic still works in WP8
            bool dataAvailable = false;

            //Incoming data always gets handled in a timeCritical fashion at this point
            //Windows phone and RT platforms do not support thread priorities
            Thread.CurrentThread.Priority = NetworkComms.timeCriticalThreadPriority;

            try
            {
                Stream stream;
                if (SSLOptions.SSLEnabled)
                    stream = (SslStream)ar.AsyncState;
                else
                    stream = (NetworkStream)ar.AsyncState;

                if (!stream.CanRead)
                    throw new ObjectDisposedException("Unable to read from stream.");

                if (!asyncListenerInRead) throw new InvalidDataException("The asyncListenerInRead flag should be true. 1");
                totalBytesRead = stream.EndRead(ar) + totalBytesRead;
                asyncListenerInRead = false;

                if (SSLOptions.SSLEnabled)
                    //SSLstream does not have a DataAvailable property. We will just assume false.
                    dataAvailable = false;
                else
                    dataAvailable = ((NetworkStream)stream).DataAvailable;

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
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder for " + ConnectionInfo + ". Cached " + packetBuilder.TotalBytesCached.ToString() + " bytes, expecting " + packetBuilder.TotalBytesExpected.ToString() + " bytes.");

                        //If there is more data to get then add it to the packets lists;
                        packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);

                        //If we have more data we might as well continue reading synchronously
                        //In order to deal with data as soon as we think we have sufficient we will leave this loop
                        while (dataAvailable && packetBuilder.TotalBytesCached < packetBuilder.TotalBytesExpected)
                        {
                            int bufferOffset = 0;

                            //We need a buffer for our incoming data
                            //First we try to reuse a previous buffer
                            if (packetBuilder.TotalPartialPacketCount > 0 && packetBuilder.NumUnusedBytesMostRecentPartialPacket() > 0)
                                dataBuffer = packetBuilder.RemoveMostRecentPartialPacket(ref bufferOffset);
                            else
                            //If we have nothing to reuse we allocate a new buffer. As we are in this loop this can only be a suplementary buffer for THIS packet. 
                            //Therefore we choose a buffer size between the initial amount and the maximum amount based on the expected size
                            {
                                long additionalBytesNeeded = packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached;
                                dataBuffer = new byte[Math.Max(Math.Min(additionalBytesNeeded, NetworkComms.MaxReceiveBufferSizeBytes), NetworkComms.InitialReceiveBufferSizeBytes)];
                            }

                            totalBytesRead = stream.Read(dataBuffer, bufferOffset, dataBuffer.Length - bufferOffset) + bufferOffset;

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
                                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder for " + ConnectionInfo + ". Cached " + packetBuilder.TotalBytesCached.ToString() + " bytes, expecting " + packetBuilder.TotalBytesExpected.ToString() + " bytes.");
                                    packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                                    
                                    if (SSLOptions.SSLEnabled)
                                        //SSLstream does not have a DataAvailable property. We will just assume false.
                                        dataAvailable = false;
                                    else
                                        dataAvailable = ((NetworkStream)stream).DataAvailable;
                                }
                            }
                            else
                                break;
                        }
                    }
                }

                if (packetBuilder.TotalBytesCached > 0 && packetBuilder.TotalBytesCached >= packetBuilder.TotalBytesExpected)
                {
                    //Once we think we might have enough data we call the incoming packet handle hand off
                    //Should we have a complete packet this method will start the appropriate task
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
                        //If packetBuilder.TotalBytesExpected is 0 we know we're going to start waiting for a fresh packet. Therefore use the initial buffer size
                        if (packetBuilder.TotalBytesExpected == 0)
                            dataBuffer = new byte[NetworkComms.InitialReceiveBufferSizeBytes];
                        else
                        //Otherwise this can only be a supplementary buffer for THIS packet. Therefore we choose a buffer size between the initial amount and the maximum amount based on the expected size
                        {
                            long additionalBytesNeeded = packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached;
                            dataBuffer = new byte[Math.Max(Math.Min(additionalBytesNeeded, NetworkComms.MaxReceiveBufferSizeBytes), NetworkComms.InitialReceiveBufferSizeBytes)];
                        }

                        totalBytesRead = 0;
                    }

                    if (asyncListenerInRead) throw new InvalidDataException("The asyncListenerInRead flag should be false. 2");
                    asyncListenerInRead = true;
                    stream.BeginRead(dataBuffer, totalBytesRead, dataBuffer.Length - totalBytesRead, IncomingTCPPacketHandler, stream);
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
                LogTools.LogException(ex, "Error_TCPConnectionIncomingPacketHandler");
                CloseConnection(true, 31);
            }

            Thread.CurrentThread.Priority = ThreadPriority.Normal;
        }

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
                    {
                        //If we have nothing to reuse we allocate a new buffer
                        //If packetBuilder.TotalBytesExpected is 0 we know we're going to start waiting for a fresh packet. Therefore use the initial buffer size
                        if (packetBuilder.TotalBytesExpected == 0)
                            dataBuffer = new byte[NetworkComms.InitialReceiveBufferSizeBytes];
                        else
                        //Otherwise this can only be a supplementary buffer for THIS packet. Therefore we choose a buffer size between the initial amount and the maximum amount based on the expected size
                        {
                            long additionalBytesNeeded = packetBuilder.TotalBytesExpected - packetBuilder.TotalBytesCached;
                            dataBuffer = new byte[Math.Max(Math.Min(additionalBytesNeeded, NetworkComms.MaxReceiveBufferSizeBytes), NetworkComms.InitialReceiveBufferSizeBytes)];
                        }
                    }

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
                            if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... " + totalBytesRead.ToString() + " bytes added to packetBuilder for " + ConnectionInfo + ". Cached " + packetBuilder.TotalBytesCached.ToString() + " bytes, expecting " + packetBuilder.TotalBytesExpected.ToString() + " bytes.");

                            packetBuilder.AddPartialPacket(totalBytesRead, dataBuffer);
                        }
                    }
                    else if (totalBytesRead == 0 && (!dataAvailable || ConnectionInfo.ConnectionState == ConnectionState.Shutdown))
                    {
                        //If we read 0 bytes and there is no data available we should be shutting down
                        CloseConnection(false, -10);
                        break;
                    }

                    //If we have read some data and we have more or equal what was expected we attempt a data hand off
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
                LogTools.LogException(ex, "Error_TCPConnectionIncomingPacketHandler");
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

        /// <summary>
        /// Callback used to determine if the provided certificate should be accepted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="certificate"></param>
        /// <param name="chain"></param>
        /// <param name="sslPolicyErrors"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Certificate selection callback
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="targetHost"></param>
        /// <param name="localCertificates"></param>
        /// <param name="remoteCertificate"></param>
        /// <param name="acceptableIssuers"></param>
        /// <returns></returns>
        private X509Certificate CertificateSelectionCallback(object sender, string targetHost, X509CertificateCollection localCertificates, 
            X509Certificate remoteCertificate, string[] acceptableIssuers)
        {
            return SSLOptions.Certificate;
        }

        /// <inheritdoc />
        protected override void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0)
        {
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
                if (tcpClient.Client!=null)
                {
                    tcpClient.Client.Disconnect(false);

                    tcpClient.Client.Close();
                }
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
        }

        /// <inheritdoc />
        protected override double[] SendStreams(StreamTools.StreamSendWrapper[] streamsToSend, double maxSendTimePerKB, long totalBytesToSend)
        {
            double[] timings = new double[streamsToSend.Length];

            Stream sendingStream;
            sendingStream = connectionStream;

            for(int i=0; i<streamsToSend.Length; i++)
            {
                if (streamsToSend[i].Length > 0)
                {
                    //Write each stream
                    timings[i] = streamsToSend[i].ThreadSafeStream.CopyTo(sendingStream, streamsToSend[i].Start, streamsToSend[i].Length, NetworkComms.SendBufferSizeBytes, maxSendTimePerKB, MinSendTimeoutMS);
                    streamsToSend[i].ThreadSafeStream.Dispose();
                }
                else
                    timings[i] = 0;
            }

            if (!tcpClient.Connected)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
                
                throw new CommunicationException("TCPClient is not marked as connected after write to networkStream. Possibly indicates a dropped connection.");
            }

            return timings;
        }
    }
}
