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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SerializerBase;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// NetworkComms maintains a top level Connection object for shared methods
    /// </summary>
    public abstract partial class Connection
    {
        /// <summary>
        /// Information related to this connection.
        /// </summary>
        public ConnectionInfo ConnectionInfo { get; protected set; }

        protected ManualResetEvent connectionSetupWait = new ManualResetEvent(false);
        protected ManualResetEvent connectionEstablishWait = new ManualResetEvent(false);

        protected volatile bool connectionSetupException = false;
        protected string connectionSetupExceptionStr = "";

        /// <summary>
        /// Create a new connection object
        /// </summary>
        /// <param name="connectionInfo"></param>
        protected Connection(ConnectionInfo connectionInfo, SendReceiveOptions defaultSendReceiveOptions)
        {
            dataBuffer = new byte[NetworkComms.ReceiveBufferSizeBytes];
            packetBuilder = new ConnectionPacketBuilder();

            ConnectionInfo = connectionInfo;

            if (defaultSendReceiveOptions != null)
                ConnectionDefaultSendReceiveOptions = defaultSendReceiveOptions;
            else
                ConnectionDefaultSendReceiveOptions = NetworkComms.DefaultSendReceiveOptions;

            if (NetworkComms.commsShutdown) throw new ConnectionSetupException("Attempting to create new connection after global comms shutdown has been initiated.");

            if (ConnectionInfo.ConnectionType == ConnectionType.Undefined || ConnectionInfo.RemoteEndPoint == null)
                throw new ConnectionSetupException("ConnectionType and RemoteEndPoint must be defined within provided ConnectionInfo.");

            //If a connection already exists with this info then we can throw an exception here to prevent duplicates
            if (NetworkComms.ConnectionExists(connectionInfo.RemoteEndPoint, connectionInfo.ConnectionType))
                throw new ConnectionSetupException("A connection already exists with " + ConnectionInfo);

            //We add a reference in the constructor to ensure any duplicate connection problems are picked up here
            NetworkComms.AddConnectionByEndPointReference(this);
        }

        /// <summary>
        /// Establish this connection
        /// </summary>
        public void EstablishConnection()
        {
            try
            {
                bool connectionEstablishing = false;
                lock (delegateLocker)
                {
                    if (ConnectionInfo.ConnectionEstablished) return;
                    else if (ConnectionInfo.ConnectionShutdown) throw new ConnectionSetupException("Attempting to re-establish a closed connection.");
                    else if (ConnectionInfo.ConnectionEstablishing)
                        connectionEstablishing = true;
                    else
                        ConnectionInfo.NoteStartConnectionEstablish();
                }

                if (connectionEstablishing)
                {
                    if (!WaitForConnectionEstablish(NetworkComms.ConnectionEstablishTimeoutMS))
                        throw new ConnectionSetupException("Timeout waiting for connection to be succesfully established.");
                }
                else
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Establishing connection with " + ConnectionInfo);

                    DateTime establishStartTime = DateTime.Now;

                    //if (NetworkComms.commsShutdown) throw new ConnectionSetupException("Attempting to establish new connection while comms is shutting down.");

                    EstablishConnectionSpecific();

                    if (ConnectionInfo.ConnectionShutdown) throw new ConnectionSetupException("Connection was closed during establish handshake.");

                    if (ConnectionInfo.NetworkIdentifier == ShortGuid.Empty)
                        throw new ConnectionSetupException("Remote network identifier should have been set by this point.");

                    //Once the above has been done the last step is to allow other threads to use the connection
                    ConnectionInfo.NoteCompleteConnectionEstablish();
                    NetworkComms.AddConnectionByIdentifierReference(this);
                    connectionEstablishWait.Set();

                    //Call the establish delegate if one is set
                    if (NetworkComms.globalConnectionEstablishDelegates != null)
                        NetworkComms.globalConnectionEstablishDelegates(ConnectionInfo);
                }
            }
            catch (SocketException e)
            {
                //If anything goes wrong we close the connection.
                CloseConnection(true, 5);
                throw new ConnectionSetupException(e.ToString());
            }
            catch (Exception ex)
            {
                //If anything goes wrong we close the connection.
                CloseConnection(true, 6);

                //For some odd reason not all SocketExceptions get caught above, so another check here
                if (ex.GetBaseException().GetType() == typeof(SocketException))
                    throw new ConnectionSetupException(ex.ToString());
                else
                    throw;
            }
        }

        /// <summary>
        /// Any connection type specific establish tasks
        /// </summary>
        protected abstract void EstablishConnectionSpecific();

        /// <summary>
        /// Return true if the connection is established within the provided timeout, otherwise false
        /// </summary>
        /// <param name="waitTimeoutMS"></param>
        /// <returns></returns>
        protected bool WaitForConnectionEstablish(int waitTimeoutMS)
        {
            return connectionSetupWait.WaitOne(waitTimeoutMS);
        }

        /// <summary>
        /// Handle an incoming ConnectionSetup packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        private void ConnectionSetupHandler(byte[] packetDataSection)
        {
            //We should never be trying to handshake an established connection
            ConnectionInfo remoteConnectionInfo = NetworkComms.InternalFixedSendReceiveOptions.Serializer.DeserialiseDataObject<ConnectionInfo>(packetDataSection, NetworkComms.InternalFixedSendReceiveOptions.Compressor);

            if (ConnectionInfo.ConnectionType != remoteConnectionInfo.ConnectionType)
            {
                connectionSetupException = true;
                connectionSetupExceptionStr = "Remote connectionInfo provided connectionType did not match expected connection type.";
            }
            else
            {
                //We use the following bool to track a possible existing connection which needs closing
                bool possibleClashConnectionWithPeer_ByEndPoint = false;
                Connection existingConnection = null;

                //We first try to establish everything within this lock in one go
                //If we can't quite complete the establish we have to come out of the lock at try to sort the problem
                bool connectionEstablishedSuccess = ConnectionSetupHandlerFinal(remoteConnectionInfo, ref possibleClashConnectionWithPeer_ByEndPoint, ref existingConnection);

                //If we were not succesfull at establishing the connection we need to sort it out!
                if (!connectionEstablishedSuccess && !connectionSetupException)
                {
                    if (existingConnection == null) throw new Exception("Connection establish issues and existingConnection was left as null.");

                    if (possibleClashConnectionWithPeer_ByEndPoint)
                    {
                        //If we have a clash by endPoint we test the existing connection
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Existing connection with " + ConnectionInfo + ". Testing existing connection.");
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
                        connectionEstablishedSuccess = ConnectionSetupHandlerFinal(remoteConnectionInfo, ref possibleClashConnectionWithPeer_ByEndPoint, ref existingConnection);

                        //If we still failed then that's it for this establish
                        if (!connectionEstablishedSuccess && !connectionSetupException)
                        {
                            connectionSetupException = true;
                            connectionSetupExceptionStr = "Attempted to establish conneciton with " + ConnectionInfo + ", but due to an existing connection this was not possible.";
                        }
                    }
                }
            }

            //Trigger any setup waits
            connectionSetupWait.Set();
        }

        /// <summary>
        /// Attempts to complete the connection establish with a minimum of locking to prevent possible deadlocking
        /// </summary>
        /// <param name="possibleClashConnectionWithPeer_ByIdentifier"></param>
        /// <param name="possibleClashConnectionWithPeer_ByEndPoint"></param>
        /// <returns></returns>
        private bool ConnectionSetupHandlerFinal(ConnectionInfo remoteConnectionInfo, ref bool possibleClashConnectionWithPeer_ByEndPoint, ref Connection existingConnection)
        {
            try
            {
                lock (NetworkComms.globalDictAndDelegateLocker)
                {
                    Connection connectionByEndPoint = NetworkComms.RetrieveConnection(ConnectionInfo.RemoteEndPoint, ConnectionInfo.ConnectionType);

                    //If we no longer have the original endPoint reference (set in the constructor) then the connection must have been closed already
                    if (connectionByEndPoint == null)
                    {
                        connectionSetupException = true;
                        connectionSetupExceptionStr = "Connection setup received after connection closure with " + ConnectionInfo;
                    }
                    else
                    {
                        //We need to check for a possible GUID clash
                        //Probability of a clash is approx 0.1% if 1E19 connection are maintained simultaneously (This many connections has not be tested ;))
                        //It's far more likely we have a strange scenario where a remote peer is trying to establish a second independant connection (which should not really happen in the first place)
                        //but hey, we live in a crazy world!
                        if (remoteConnectionInfo.NetworkIdentifier == NetworkComms.NetworkNodeIdentifier)
                        {
                            connectionSetupException = true;
                            connectionSetupExceptionStr = "Remote peer has same network idendifier to local, " + remoteConnectionInfo.NetworkIdentifier + ". Although technically near impossible some special (engineered) scenarios make this more probable.";
                        }
                        else if (connectionByEndPoint != this)
                        {
                            possibleClashConnectionWithPeer_ByEndPoint = true;
                            existingConnection = connectionByEndPoint;
                        }
                        else
                        {
                            //Update the connection info
                            NetworkComms.UpdateConnectionByEndPointReference(this, remoteConnectionInfo.LocalEndPoint);
                            ConnectionInfo.UpdateInfoAfterRemoteHandshake(remoteConnectionInfo);

                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "ConnectionSetupHandlerInnerError");
            }

            return false;
        }
    }
}
