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
using System.Text;
using System.Net;
using System.Threading;
using System.IO;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;

#if NET35 || NET4
using NetworkCommsDotNet.Connections.Bluetooth;
#endif

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#else
using System.Net.Sockets;
#endif

namespace NetworkCommsDotNet.Connections
{
    public abstract partial class Connection
    {
        /// <summary>
        /// Connection information related to this connection.
        /// </summary>
        public ConnectionInfo ConnectionInfo { get; protected set; }

        /// <summary>
        /// A manual reset event which can be used to handle connection setup and establish.
        /// </summary>
        protected ManualResetEvent connectionSetupWait = new ManualResetEvent(false);
        /// <summary>
        /// A manual reset event which can be used to handle connection setup and establish.
        /// </summary>
        protected ManualResetEvent connectionEstablishWait = new ManualResetEvent(false);

        /// <summary>
        /// A boolean used to signal a connection setup exception.
        /// </summary>
        protected bool connectionSetupException = false;
        /// <summary>
        /// If <see cref="connectionSetupException"/> is true provides additional exception information.
        /// </summary>
        protected string connectionSetupExceptionStr = "";

        /// <summary>
        /// Create a new connection object
        /// </summary>
        /// <param name="connectionInfo">ConnectionInfo corresponding to the new connection</param>
        /// <param name="defaultSendReceiveOptions">The SendReceiveOptions which should be used as connection defaults</param>
        protected Connection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions)
        {
            //If the application layer protocol is disabled the serialiser must be NullSerializer
            //and no data processors are allowed.
            if (connectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
            {
                if (defaultSendReceiveOptions.Options.ContainsKey("ReceiveConfirmationRequired"))
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options specified the ReceiveConfirmationRequired option. Please provide compatible send receive options in order to successfully" +
                        " instantiate this unmanaged connection.", "defaultSendReceiveOptions");

                if (defaultSendReceiveOptions.DataSerializer != DPSManager.GetDataSerializer<NullSerializer>())
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options serialiser was not NullSerializer. Please provide compatible send receive options in order to successfully" +
                        " instantiate this unmanaged connection.", "defaultSendReceiveOptions");

                if (defaultSendReceiveOptions.DataProcessors.Count > 0)
                    throw new ArgumentException("Attempted to create an unmanaged connection when the provided send receive" +
                        " options contains data processors. Data processors may not be used with unmanaged connections." +
                        " Please provide compatible send receive options in order to successfully instantiate this unmanaged connection.", "defaultSendReceiveOptions");
            }

            SendTimesMSPerKBCache = new CommsMath();            
            packetBuilder = new PacketBuilder();

            //Initialise the sequence counter using the global value
            //Subsequent values on this connection are guaranteed to be sequential
            packetSequenceCounter = Interlocked.Increment(ref NetworkComms.totalPacketSendCount);

            ConnectionInfo = connectionInfo;

            if (defaultSendReceiveOptions != null)
                ConnectionDefaultSendReceiveOptions = defaultSendReceiveOptions;
            else
                ConnectionDefaultSendReceiveOptions = NetworkComms.DefaultSendReceiveOptions;

            //Add any listener specific packet handlers if required
            if (connectionInfo.ConnectionListener != null)
                connectionInfo.ConnectionListener.AddListenerPacketHandlersToConnection(this);

            if (NetworkComms.commsShutdown) throw new ConnectionSetupException("Attempting to create new connection after global NetworkComms.Net shutdown has been initiated.");

            if (ConnectionInfo.ConnectionType == ConnectionType.Undefined || ConnectionInfo.RemoteEndPoint == null)
                throw new ConnectionSetupException("ConnectionType and RemoteEndPoint must be defined within provided ConnectionInfo.");

            //If a connection already exists with this info then we can throw an exception here to prevent duplicates
            if (NetworkComms.ConnectionExists(connectionInfo.RemoteEndPoint, connectionInfo.LocalEndPoint, connectionInfo.ConnectionType, connectionInfo.ApplicationLayerProtocol))
                throw new ConnectionSetupException("A " + connectionInfo.ConnectionType.ToString() + " connection already exists with info " + connectionInfo);

            //We add a reference in the constructor to ensure any duplicate connection problems are picked up here
            NetworkComms.AddConnectionReferenceByRemoteEndPoint(this);
        }

        /// <summary>
        /// Establish this connection
        /// </summary>
        public void EstablishConnection()
        {
            try
            {
                bool connectionAlreadyEstablishing = false;
                lock (_syncRoot)
                {
                    if (ConnectionInfo.ConnectionState == ConnectionState.Established) return;
                    else if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown) throw new ConnectionSetupException("Attempting to re-establish a closed connection. Please create a new connection instead.");
                    else if (ConnectionInfo.ConnectionState == ConnectionState.Establishing)
                        connectionAlreadyEstablishing = true;
                    else
                        ConnectionInfo.NoteStartConnectionEstablish();
                }

                if (connectionAlreadyEstablishing)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Waiting for connection with " + ConnectionInfo + " to be established.");
                    if (!WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS))
                        throw new ConnectionSetupException("Timeout waiting for connection to be successfully established.");
                }
                else
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace("Establishing new connection with " + ConnectionInfo);

                    EstablishConnectionSpecific();

                    if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown) throw new ConnectionSetupException("Connection was closed immediately after handshake. This can occur if a different thread used and subsequently closed this connection.");

                    //Once the above has been done the last step is to allow other threads to use the connection
                    ConnectionInfo.NoteCompleteConnectionEstablish();

                    //Not all connection types will have a known remote network identifier
                    if (ConnectionInfo.NetworkIdentifier != ShortGuid.Empty)
                        NetworkComms.AddConnectionReferenceByIdentifier(this);

                    connectionEstablishWait.Set();

                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Trace(" ... connection successfully established with " + ConnectionInfo);
                }
            }
            catch (SocketException e)
            {
                //If anything goes wrong we close the connection.
                CloseConnection(true, 43);
                throw new ConnectionSetupException(e.ToString());
            }
            catch (Exception ex)
            {
                //If anything goes wrong we close the connection.
                CloseConnection(true, 44);

                //For some odd reason not all SocketExceptions get caught above, so another check here
                if (ex.GetBaseException().GetType() == typeof(SocketException))
                    throw new ConnectionSetupException(ex.ToString());
                else
                    throw;
            }
        }

        /// <summary>
        /// Any connection type specific establish tasks. Should call at least ConnectionHandshake() or TriggerConnectionEstablishDelegates();
        /// </summary>
        protected abstract void EstablishConnectionSpecific();

        /// <summary>
        /// Performs a connection handshake with the remote end of the connection.
        /// Exchanges network identifier and any listener whose IPAddress matches the connection localEndPoint IPAddress.
        /// </summary>
        protected void ConnectionHandshake()
        {
            if (ConnectionInfo.ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Disabled)
                throw new CommunicationException("Attempted to perform handshake on connection where the application protocol has been disabled.");

            //If we are server side and we have just received an incoming connection we need to return a connection identifier
            //This id will be used in all future connections from this machine
            if (ConnectionInfo.ServerSide)
            {
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Waiting for client connnectionInfo from " + ConnectionInfo);

                //Wait for the client to send its identification
                if (!connectionSetupWait.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS))
                    throw new ConnectionSetupException("Timeout waiting for client connectionInfo with " + ConnectionInfo + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                if (connectionSetupException)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Connection setup exception. ServerSide with " + ConnectionInfo + ", " + connectionSetupExceptionStr);
                    throw new ConnectionSetupException("ServerSide. " + connectionSetupExceptionStr);
                }

                //Trigger the connection establish delegates before replying to the connection establish 
                TriggerConnectionEstablishDelegates();
            }
            else
            {
                //If we are client side part of the handshake is to inform the server of a potential local listener
                //Get a list of existing listeners
                List<EndPoint> existingLocalListeners = null;
                if (ConnectionInfo.LocalEndPoint is IPEndPoint)
                    existingLocalListeners = Connection.ExistingLocalListenEndPoints(ConnectionInfo.ConnectionType, new IPEndPoint(ConnectionInfo.LocalIPEndPoint.Address, 0));
#if NET4 || NET35
                else if (ConnectionInfo.LocalEndPoint is InTheHand.Net.BluetoothEndPoint)
                    existingLocalListeners = Connection.ExistingLocalListenEndPoints(ConnectionInfo.ConnectionType, new InTheHand.Net.BluetoothEndPoint(ConnectionInfo.LocalBTEndPoint.Address, ConnectionInfo.LocalBTEndPoint.Service));
#endif

                //Check to see if we have a local listener for matching the local endpoint address
                //If we are client side we use this local listener in our reply to the server
                EndPoint selectedExistingLocalListenerEndPoint = null;
                if (existingLocalListeners != null && // If we have a suitable local listener
                    existingLocalListeners.Count > 0 && // If we have a suitable local listener
                    !existingLocalListeners.Contains(ConnectionInfo.RemoteEndPoint)) //If this is not an application loop back connection
                    selectedExistingLocalListenerEndPoint = (existingLocalListeners.Contains(ConnectionInfo.LocalEndPoint) ? ConnectionInfo.LocalEndPoint : existingLocalListeners[0]);

                //During this exchange we may note an update local listen port
                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Sending connnectionInfo to " + ConnectionInfo);

                //Pull-out the parameters we want to send to the server
                //Doing it here rather than all in the following Send Object line keeps it clearer
                EndPoint selectedLocalListenerEndPoint = (selectedExistingLocalListenerEndPoint != null ? selectedExistingLocalListenerEndPoint : ConnectionInfo.LocalEndPoint);
                bool connectable = selectedExistingLocalListenerEndPoint != null;

                //As the client we initiated the connection we now forward our local node identifier to the server
                //If we are listening we include our local listen port as well
                SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), new ConnectionInfo(ConnectionInfo.ConnectionType,
                    NetworkComms.NetworkIdentifier,
                    selectedLocalListenerEndPoint,
                    connectable),
                    NetworkComms.InternalFixedSendReceiveOptions);

                //Wait here for the server end to return its own identifier
                if (!connectionSetupWait.WaitOne(NetworkComms.ConnectionEstablishTimeoutMS))
                    throw new ConnectionSetupException("Timeout waiting for server connnectionInfo from " + ConnectionInfo + ". Connection created at " + ConnectionInfo.ConnectionCreationTime.ToString("HH:mm:ss.fff") + ", its now " + DateTime.Now.ToString("HH:mm:ss.f"));

                //If we are client side we can update the localEndPoint for this connection to reflect what the remote end might see if we are also listening
                if (selectedExistingLocalListenerEndPoint != null && selectedExistingLocalListenerEndPoint != ConnectionInfo.LocalEndPoint)
                {
                    //We should now be able to set the connectionInfo localEndPoint
                    NetworkComms.UpdateConnectionReferenceByEndPoint(this, ConnectionInfo.RemoteEndPoint, selectedExistingLocalListenerEndPoint);
                    ConnectionInfo.UpdateLocalEndPointInfo(selectedExistingLocalListenerEndPoint);
                }

                if (connectionSetupException)
                {
                    if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Connection setup exception. ClientSide with " + ConnectionInfo + ", " + connectionSetupExceptionStr);
                    throw new ConnectionSetupException("ClientSide. " + connectionSetupExceptionStr);
                }

                //Trigger the connection establish delegates once the server has replied to the connection establish
                TriggerConnectionEstablishDelegates();
            }
        }

        /// <summary>
        /// Trigger connection establish delegates.
        /// </summary>
        protected void TriggerConnectionEstablishDelegates()
        {
            //Call asynchronous connection establish delegates here
            if (NetworkComms.globalConnectionEstablishDelegatesAsync != null)
            {
                NetworkComms.CommsThreadPool.EnqueueItem(QueueItemPriority.Normal, new WaitCallback((obj) =>
                {
                    Connection connectionParam = obj as Connection;
                    NetworkComms.globalConnectionEstablishDelegatesAsync(connectionParam);
                }), this);
            }

            //Call synchronous connection establish delegates here
            if (NetworkComms.globalConnectionEstablishDelegatesSync != null)
                NetworkComms.globalConnectionEstablishDelegatesSync(this);
        }

        /// <summary>
        /// Return true if the connection is established within the provided timeout, otherwise false
        /// </summary>
        /// <param name="waitTimeoutMS">Wait time in milliseconds before returning</param>
        /// <returns>True if the wait was triggered, false otherwise after the provided timeout.</returns>
        protected bool WaitForConnectionEstablish(int waitTimeoutMS)
        {
            if (ConnectionInfo.ConnectionState == ConnectionState.Established)
                return true;
            else
            {
                if (NetworkComms.LoggingEnabled)
                    NetworkComms.Logger.Trace("Waiting for new connection to be successfully established before continuing with " + ConnectionInfo);

                if (ConnectionInfo.ConnectionState == ConnectionState.Shutdown)
                    throw new ConnectionShutdownException("Attempted to wait for connection establish on a connection that is already shutdown.");

                return connectionSetupWait.WaitOne(waitTimeoutMS);
            }
        }

        /// <summary>
        /// Handle an incoming ConnectionSetup packet type
        /// </summary>
        /// <param name="packetDataSection">Serialised handshake data</param>
        internal void ConnectionSetupHandler(MemoryStream packetDataSection)
        {
            //We should never be trying to handshake an established connection
            ConnectionInfo remoteConnectionInfo = NetworkComms.InternalFixedSendReceiveOptions.DataSerializer.DeserialiseDataObject<ConnectionInfo>(packetDataSection,
                NetworkComms.InternalFixedSendReceiveOptions.DataProcessors, NetworkComms.InternalFixedSendReceiveOptions.Options);

            if (ConnectionInfo.ConnectionType != remoteConnectionInfo.ConnectionType)
            {
                connectionSetupException = true;
                connectionSetupExceptionStr = "Remote connectionInfo provided connectionType did not match expected connection type.";
            }
            else
            {
                //We use the following bool to track a possible existing connection which needs closing
                bool possibleClashWithExistingConnection = false;
                Connection existingConnection = null;

                //We first try to establish everything within this lock in one go
                //If we can't quite complete the establish we have to come out of the lock at try to sort the problem
                bool connectionEstablishedSuccess = ConnectionSetupHandlerFinal(remoteConnectionInfo, ref possibleClashWithExistingConnection, ref existingConnection);

                //If we were not successful at establishing the connection we need to sort it out!
                if (!connectionEstablishedSuccess && !connectionSetupException)
                {
                    if (existingConnection == null) throw new Exception("Connection establish issues and existingConnection was left as null.");

                    if (possibleClashWithExistingConnection)
                    {
                        //If we have a clash by endPoint we test the existing connection
                        if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Existing connection with " + ConnectionInfo + ". Testing existing connection.");
                        if (existingConnection.ConnectionAlive(1000))
                        {
                            //If the existing connection comes back as alive we don't allow this one to go any further
                            //This might happen if two peers try to connect to each other at the same time
                            connectionSetupException = true;
                            connectionSetupExceptionStr = " ... existing live connection at provided end point for this connection (" + ConnectionInfo + "), there should be no need for a second.";
                        }
                    }

                    //We only try again if we did not log an exception
                    if (!connectionSetupException)
                    {
                        //Once we have tried to sort the problem we can try to finish the establish one last time
                        connectionEstablishedSuccess = ConnectionSetupHandlerFinal(remoteConnectionInfo, ref possibleClashWithExistingConnection, ref existingConnection);

                        //If we still failed then that's it for this establish
                        if (!connectionEstablishedSuccess && !connectionSetupException)
                        {
                            connectionSetupException = true;
                            connectionSetupExceptionStr = "Attempted to establish connection with " + ConnectionInfo + ", but due to an existing connection this was not possible.";
                        }
                    }
                }

                //If we are server side and we receive a successful connection setup we can respond to here
                if (connectionEstablishedSuccess && ConnectionInfo.ServerSide)
                    SendObject(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup), new ConnectionInfo(ConnectionInfo.ConnectionType, NetworkComms.NetworkIdentifier, ConnectionInfo.LocalEndPoint, true), NetworkComms.InternalFixedSendReceiveOptions);
            }

            //Trigger any setup waits
            connectionSetupWait.Set();
        }

        /// <summary>
        /// Attempts to complete the connection establish with a minimum of locking to avoid possible deadlocking
        /// </summary>
        /// <param name="remoteConnectionInfo"><see cref="ConnectionInfo"/> corresponding with remoteEndPoint</param>
        /// <param name="possibleClashWithExistingConnection">True if a connection already exists with provided remoteEndPoint</param>
        /// <param name="existingConnection">A reference to an existing connection if it exists</param>
        /// <returns>True if connection is successfully setup, otherwise false</returns>
        private bool ConnectionSetupHandlerFinal(ConnectionInfo remoteConnectionInfo, ref bool possibleClashWithExistingConnection, ref Connection existingConnection)
        {
            lock (NetworkComms.globalDictAndDelegateLocker)
            {
                List<Connection> connectionByEndPoint = NetworkComms.GetExistingConnection(ConnectionInfo.RemoteEndPoint, ConnectionInfo.LocalEndPoint, ConnectionInfo.ConnectionType, ConnectionInfo.ApplicationLayerProtocol);

                //If we no longer have the original endPoint reference (set in the constructor) then the connection must have been closed already
                if (connectionByEndPoint.Count == 0)
                {
                    connectionSetupException = true;
                    connectionSetupExceptionStr = "Connection setup received after connection closure with " + ConnectionInfo;
                }
                else
                {
                    //COMMENT: As of version 3.0.0 we have allowed loop back connections where the identifier is the same
                    //We need to check for a possible GUID clash
                    //Probability of a clash is approx 0.1% if 1E19 connections are maintained simultaneously (This many connections has not be tested ;))
                    //but hey, we live in a crazy world!
                    //if (remoteConnectionInfo.NetworkIdentifier == NetworkComms.NetworkIdentifier)
                    //{
                    //    connectionSetupException = true;
                    //    connectionSetupExceptionStr = "Remote peer has same network identifier to local, " + remoteConnectionInfo.NetworkIdentifier + ". A real duplication is vanishingly improbable so this exception has probably been thrown because the local and remote application are the same.";
                    //}
                    //else 
                    if (connectionByEndPoint[0] != this)
                    {
                        possibleClashWithExistingConnection = true;
                        existingConnection = connectionByEndPoint[0];
                    }
                    else if (connectionByEndPoint[0].ConnectionInfo.NetworkIdentifier != ShortGuid.Empty &&
                        connectionByEndPoint[0].ConnectionInfo.NetworkIdentifier != remoteConnectionInfo.NetworkIdentifier)
                    {
                        //We are in the same connection, so don't need to throw and exception but the remote network identifier 
                        //has changed.
                        //This can happen for connection types where the local connection (this) may not have been closed
                        //when the remote peer closed. We need to trigger the connection close delegates with the old info, update
                        //the connection info and then call the establish delegates
                        #region Reset Connection without closing
                        //Call the connection close delegates
                        try
                        {
                            //Almost there
                            //Last thing is to call any connection specific shutdown delegates
                            if (ConnectionSpecificShutdownDelegate != null)
                            {
                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Triggered connection specific shutdown delegates with " + ConnectionInfo);
                                ConnectionSpecificShutdownDelegate(this);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogTools.LogException(ex, "ConnectionSpecificShutdownDelegateError", "Error while executing connection specific shutdown delegates for " + ConnectionInfo + ". Ensure any shutdown exceptions are caught in your own code.");
                        }

                        try
                        {
                            //Last but not least we call any global connection shutdown delegates
                            if (NetworkComms.globalConnectionShutdownDelegates != null)
                            {
                                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Debug("Triggered global shutdown delegates with " + ConnectionInfo);
                                NetworkComms.globalConnectionShutdownDelegates(this);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogTools.LogException(ex, "GlobalConnectionShutdownDelegateError", "Error while executing global connection shutdown delegates for " + ConnectionInfo + ". Ensure any shutdown exceptions are caught in your own code.");
                        }

                        EndPoint newRemoteEndPoint;
                        if (this.ConnectionInfo.RemoteEndPoint.GetType() == typeof(IPEndPoint) &&
                            remoteConnectionInfo.LocalEndPoint.GetType() == typeof(IPEndPoint))
                            newRemoteEndPoint = new IPEndPoint(this.ConnectionInfo.RemoteIPEndPoint.Address, remoteConnectionInfo.LocalIPEndPoint.Port);
                        else
                            throw new NotImplementedException("ConnectionSetupHandlerFinal not implemented for EndPoints of type " + this.ConnectionInfo.RemoteEndPoint.GetType());

                        NetworkComms.UpdateConnectionReferenceByEndPoint(this, newRemoteEndPoint, this.ConnectionInfo.LocalEndPoint);

                        ConnectionInfo.UpdateInfoAfterRemoteHandshake(remoteConnectionInfo, newRemoteEndPoint);

                        //Trigger the establish delegates
                        TriggerConnectionEstablishDelegates();
                        #endregion

                        return true;
                    }
                    else
                    {
                        //Update the connection info
                        //We never change the this.ConnectionInfo.RemoteEndPoint.Address as there might be NAT involved
                        //We may update the port however
                        EndPoint newRemoteEndPoint;
                        if (this is IPConnection)
                            newRemoteEndPoint = new IPEndPoint(this.ConnectionInfo.RemoteIPEndPoint.Address, remoteConnectionInfo.LocalIPEndPoint.Port);
#if NET35 || NET4
                        else if (this is BluetoothConnection)
                            newRemoteEndPoint = ConnectionInfo.RemoteBTEndPoint;
#endif
                        else
                            throw new NotImplementedException("ConnectionSetupHandlerFinal not implemented for EndPoints of type " + this.ConnectionInfo.RemoteEndPoint.GetType());

                        NetworkComms.UpdateConnectionReferenceByEndPoint(this, newRemoteEndPoint, this.ConnectionInfo.LocalEndPoint);

                        ConnectionInfo.UpdateInfoAfterRemoteHandshake(remoteConnectionInfo, newRemoteEndPoint);

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns ConnectionInfo.ToString
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return ConnectionInfo.ToString();
        }
    }
}
