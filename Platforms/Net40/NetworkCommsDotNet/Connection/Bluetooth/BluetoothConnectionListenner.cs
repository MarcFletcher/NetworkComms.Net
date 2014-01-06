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
    public class BluetoothConnectionListenner : ConnectionListenerBase
    {
        BluetoothListener listenerInstance;

        public BluetoothConnectionListenner(SendReceiveOptions sendReceiveOptions,
            ApplicationLayerProtocolStatus applicationLayerProtocol)
            : base(ConnectionType.Bluetooth, sendReceiveOptions, applicationLayerProtocol)
        {

        }

        internal override void StartListening(System.Net.EndPoint desiredLocalListenIPEndPoint, bool useRandomPortFailOver)
        {
            if (IsListening) throw new InvalidOperationException("Attempted to call StartListening when already listening.");
            if(!(desiredLocalListenIPEndPoint is BluetoothEndPoint)) throw new ArgumentException("Bluetooth connections can only be made from a local BluetoothEndPoint", "desiredLocalListenIPEndPoint");

            try
            {

                listenerInstance = new BluetoothListener(desiredLocalListenIPEndPoint as BluetoothEndPoint);
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

            this.LocalListenIPEndPoint = listenerInstance.LocalEndPoint;

            this.IsListening = true;
        }

        internal override void StopListening()
        {
            IsListening = false;

            try
            {
                listenerInstance.Stop();
            }
            catch (Exception) { }
        }

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
