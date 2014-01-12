#if !NET2

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using InTheHand.Net.Sockets;
using System.Net;
using InTheHand.Net;
using System.Net.Sockets;
using DPSBase;
using System.Threading;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A Bluetooth connection listener
    /// </summary>
    public class BluetoothConnectionListener : ConnectionListenerBase
    {
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
        public BluetoothConnectionListener(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol)
            : base(ConnectionType.Bluetooth, sendReceiveOptions, applicationLayerProtocol)
        {

        }

        /// <inheritdoc />
        internal override void StartListening(EndPoint desiredLocalListenEndPoint, bool useRandomPortFailOver)
        {
            if (IsListening) throw new InvalidOperationException("Attempted to call StartListening when already listening.");
            if(!(desiredLocalListenEndPoint is BluetoothEndPoint)) throw new ArgumentException("Bluetooth connections can only be made from a local BluetoothEndPoint", "desiredLocalListenIPEndPoint");

            try
            {

                listenerInstance = new BluetoothListener(desiredLocalListenEndPoint as BluetoothEndPoint);
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
                        throw new NotImplementedException();

                        //listenerInstance = new BluetoothListener(;
                        //listenerInstance.Start();
                        //listenerInstance.BeginAcceptBluetoothClient(TCPConnectionReceivedAsync, null);
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

            this.LocalListenEndPoint = listenerInstance.LocalEndPoint;

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
                var newTCPClient = listenerInstance.EndAcceptBluetoothClient(ar);
                ConnectionInfo newConnectionInfo = new ConnectionInfo(true, ConnectionType.TCP, (IPEndPoint)newTCPClient.Client.RemoteEndPoint, ApplicationLayerProtocol);

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Info("New TCP connection from " + newConnectionInfo);

                ThreadPool.QueueUserWorkItem(new WaitCallback((obj) =>
                {
                    #region Pickup The New Connection
                    try
                    {
                        BluetoothConnection.GetConnection(newConnectionInfo, SendReceiveOptions, newTCPClient, true);
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
                listenerInstance.BeginAcceptBluetoothClient(BluetoothConnectionReceivedAsync, null);
            }
        }
    }
}

#endif