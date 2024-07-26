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

#if !NET2 && !NET

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;

using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using InTheHand.Net;
using InTheHand.Net.Sockets;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Bluetooth.AttributeIds;

namespace NetworkCommsDotNet.Connections.Bluetooth
{
    /// <summary>
    /// A Bluetooth connection listener
    /// </summary>
    public class BluetoothConnectionListener : ConnectionListenerBase
    {
        public static class NetworkCommsBTAttributeId
        {
            public const ServiceAttributeId NetworkCommsEndPoint = unchecked((ServiceAttributeId)0xda9799e8);
        }

        /// <summary>
        /// The local Bluetooth listener
        /// </summary>
        BluetoothListener listenerInstance;

        /// <summary>
        /// Create a new instance of BluetoothConnectionListener
        /// </summary>
        /// <param name="sendReceiveOptions">The send receive options to use for this listener.</param>
        /// <param name="applicationLayerProtocol">If enabled NetworkComms.Net uses a custom 
        /// application layer protocol to provide useful features such as inline serialisation, 
        /// transparent packet transmission, remote peer handshake and information etc. We strongly 
        /// recommend you enable the NetworkComms.Net application layer protocol.</param>
        /// <param name="allowDiscoverable">Determines if the newly created <see cref="ConnectionListenerBase"/> will be discoverable if <see cref="Tools.PeerDiscovery"/> is enabled.</param>
        public BluetoothConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol, bool allowDiscoverable = false)
            : base(ConnectionType.Bluetooth, sendReceiveOptions, applicationLayerProtocol, allowDiscoverable)
        {

        }

        /// <inheritdoc />
        internal override void StartListening(EndPoint desiredLocalListenEndPoint, bool useRandomPortFailOver)
        {
            if (IsListening) throw new InvalidOperationException("Attempted to call StartListening when already listening.");
            if(!(desiredLocalListenEndPoint is BluetoothEndPoint)) throw new ArgumentException("Bluetooth connections can only be made from a local BluetoothEndPoint", "desiredLocalListenIPEndPoint");

            try
            {
                ServiceRecordBuilder bldr = new ServiceRecordBuilder();
                bldr.AddServiceClass((desiredLocalListenEndPoint as BluetoothEndPoint).Service);
                if (IsDiscoverable)
                    bldr.AddCustomAttribute(new ServiceAttribute(NetworkCommsBTAttributeId.NetworkCommsEndPoint, ServiceElement.CreateNumericalServiceElement(ElementType.UInt8, 1)));

                listenerInstance = new BluetoothListener(desiredLocalListenEndPoint as BluetoothEndPoint, bldr.ServiceRecord);

                listenerInstance.Start();
                listenerInstance.BeginAcceptBluetoothClient(BluetoothConnectionReceivedAsync, null);
            }
            catch (SocketException)
            {
                //If the port we wanted is not available
                if (useRandomPortFailOver)
                {
                    try
                    {
                        Guid service = Guid.NewGuid();

                        ServiceRecordBuilder bldr = new ServiceRecordBuilder();
                        bldr.AddServiceClass(service);
                        if (IsDiscoverable)
                            bldr.AddCustomAttribute(new ServiceAttribute(NetworkCommsBTAttributeId.NetworkCommsEndPoint, ServiceElement.CreateNumericalServiceElement(ElementType.UInt8, 1)));

                        listenerInstance = new BluetoothListener(new BluetoothEndPoint((desiredLocalListenEndPoint as BluetoothEndPoint).Address, service), bldr.ServiceRecord);
                        listenerInstance.Start();
                        listenerInstance.BeginAcceptBluetoothClient(BluetoothConnectionReceivedAsync, null);
                    }
                    catch (SocketException)
                    {
                        //If we get another socket exception this appears to be a bad IP. We will just ignore this IP
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to open a random port on " + desiredLocalListenEndPoint + ". This endPoint may not support listening or possibly try again using a different port.");
                        throw new CommsSetupShutdownException("It was not possible to open a random port on " + desiredLocalListenEndPoint + ". This endPoint may not support listening or possibly try again using a different port.");
                    }
                }
                else
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Error("It was not possible to listen on " + desiredLocalListenEndPoint.ToString() + ". This endPoint may not support listening or possibly try again using a different port.");
                    throw new CommsSetupShutdownException("It was not possible to listen on " + desiredLocalListenEndPoint.ToString() + ". This endPoint may not support listening or possibly try again using a different port.");
                }
            }

            this.LocalListenEndPoint = desiredLocalListenEndPoint;

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
        /// Handle a new incoming bluetooth connection
        /// </summary>
        /// <param name="ar"></param>
        private void BluetoothConnectionReceivedAsync(IAsyncResult ar)
        {
            if (!IsListening)
                return;

            try
            {
                var newBTClient = listenerInstance.EndAcceptBluetoothClient(ar);
                ConnectionInfo newConnectionInfo = new ConnectionInfo(ConnectionType.Bluetooth, newBTClient.Client.RemoteEndPoint, newBTClient.Client.LocalEndPoint, ApplicationLayerProtocol, this);

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("New bluetooth connection from " + newConnectionInfo);

                NetworkComms.IncomingConnectionEstablishThreadPool.EnqueueItem(QueueItemPriority.Normal, new WaitCallback((obj) =>
                {
                    #region Pickup The New Connection
                    try
                    {
                        BluetoothConnection.GetConnection(newConnectionInfo, ListenerDefaultSendReceiveOptions, newBTClient, true);
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
                listenerInstance.BeginAcceptBluetoothClient(BluetoothConnectionReceivedAsync, null);
            }
        }
    }
}
#endif