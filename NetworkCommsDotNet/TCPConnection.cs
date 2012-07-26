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
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Threading.Tasks;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A TCPConnection represents each established tcp connection between two peers.
    /// </summary>
    public class TCPConnection : Connection
    {
        /// <summary>
        /// The TcpClient corresponding to this connection.
        /// </summary>
        TcpClient tcpClient;

        /// <summary>
        /// The networkstream associated with the tcpClient.
        /// </summary>
        NetworkStream tcpClientNetworkStream;

        /// <summary>
        /// Connection constructor
        /// </summary>
        /// <param name="serverSide">True if this connection was requested by a remote client.</param>
        /// <param name="connectionEndPoint">The IP information of the remote client.</param>
        public TCPConnection(ConnectionInfo connectionInfo, TcpClient tcpClient) : base (connectionInfo)
        {
            this.tcpClient = tcpClient;
            this.tcpClientNetworkStream = tcpClient.GetStream();

            throw new NotImplementedException();
        }

        /// <summary>
        /// Establish a connection with the provided TcpClient
        /// </summary>
        /// <param name="sourceClient"></param>
        protected override void EstablishConnectionInternal()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Closes a connection
        /// </summary>
        /// <param name="closeDueToError">Closing a connection due an error possibly requires a few extra steps.</param>
        /// <param name="logLocation">Optional debug parameter.</param>
        protected override void CloseConnectionInternal(bool closeDueToError, int logLocation = 0)
        {
            //The following attempts to correctly close the connection
            //Try to close the networkStream first
            try
            {
                if (tcpClientNetworkStream != null) tcpClientNetworkStream.Close();
            }
            catch (Exception)
            {
            }
            finally
            {
                tcpClientNetworkStream = null;
            }

            //Try to close the tcpClient
            try
            {
                tcpClient.Client.Disconnect(false);
                tcpClient.Client.Close();
                tcpClient.Client.Dispose();
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

            try
            {
                //If we are calling close from the listen thread we are actually in the same thread
                //We must guarantee the listen thread stops even if that means we need to nuke it
                //If we did not we may not be able to shutdown properly.
                if (incomingDataListenThread != null && incomingDataListenThread != Thread.CurrentThread && (incomingDataListenThread.ThreadState == System.Threading.ThreadState.WaitSleepJoin || incomingDataListenThread.ThreadState == System.Threading.ThreadState.Running))
                {
                    //If we have made it this far we give the ythread a further 50ms to finish before nuking.
                    if (!incomingDataListenThread.Join(50))
                    {
                        incomingDataListenThread.Abort();
                        if (NetworkComms.loggingEnabled && ConnectionInfo != null) NetworkComms.logger.Warn("Incoming data listen thread with " + ConnectionInfo.ToString() + " aborted.");
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        /// <summary>
        /// Return true if the connection is established within the provided timeout, otherwise false
        /// </summary>
        /// <param name="waitTimeoutMS"></param>
        /// <returns></returns>
        internal bool WaitForConnectionEstablish(int waitTimeoutMS)
        {
            return connectionSetupWait.WaitOne(waitTimeoutMS);
        }

        /// <summary>
        /// Starts listening for incoming data for this connection. Can choose between async or sync depending on the value of connectionListenModeIsSync
        /// </summary>
        private void StartIncomingDataListen()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Synchronous incoming connection data worker
        /// </summary>
        private void IncomingDataSyncWorker()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Asynchronous incoming connection data delegate
        /// </summary>
        /// <param name="ar"></param>
        private void IncomingPacketHandler(IAsyncResult ar)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Handle an incoming ConnectionSetup packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        private void ConnectionSetupHandler(byte[] packetDataSection)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Attempts to complete the connection establish with a minimum of locking to prevent possible deadlocking
        /// </summary>
        /// <param name="possibleExistingConnectionWithPeer_ByIdentifier"></param>
        /// <param name="possibleExistingConnectionWithPeer_ByEndPoint"></param>
        /// <returns></returns>
        private bool ConnectionSetupHandlerInner(ref bool possibleExistingConnectionWithPeer_ByIdentifier, ref bool possibleExistingConnectionWithPeer_ByEndPoint, ref Connection existingConnection)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send the provided packet to the remote peer
        /// </summary>
        /// <param name="packetTypeStr"></param>
        /// <param name="packetData"></param>
        /// <param name="destinationIPAddress"></param>
        /// <param name="receiveConfirmationRequired"></param>
        /// <returns></returns>
        private void SendPacket(Packet packet)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Send a null packet (1 byte long) to this connection. Helps keep the connection alive but also bandwidth usage to absolute minimum. If an exception is thrown the connection will be closed.
        /// </summary>
        public void SendNullPacket()
        {
            try
            {
                //Only once the connection has been established do we send null packets
                if (ConnectionInfo.ConnectionEstablished)
                {
                    //Multiple threads may try to send packets at the same time so we need this lock to prevent a thread cross talk
                    lock (sendLocker)
                    {
                        //if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Sending null packet to " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + (connectionEstablished ? " (" + ConnectionId + ")." : "."));

                        //Send a single 0 byte
                        tcpClientNetworkStream.Write(new byte[] { 0 }, 0, 1);

                        //Update the traffic time after we have written to netStream
                        ConnectionInfo.UpdateLastTrafficTime();
                    }
                }
            }
            catch (Exception)
            {
                CloseConnection(true, 19);
                //throw new CommunicationException(ex.ToString());
            }
        }

        public override void SendObject(string sendingPacketType, object objectToSend, SendReceiveOptions options)
        {
            throw new NotImplementedException();
        }

        public override returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, SendReceiveOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
