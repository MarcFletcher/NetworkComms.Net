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
        /// Lockers for maintaing thread safe operation
        /// </summary>
        protected object sendLocker = new object();

        /// <summary>
        /// Maintains a list of sent packets for the purpose of confirmation and possible resends.
        /// </summary>
        protected object sentPacketsLocker = new object();
        protected Dictionary<string, OldSentPacket> sentPackets = new Dictionary<string, OldSentPacket>();
        protected class OldSentPacket
        {
            public int SendCount { get; private set; }
            public Packet Packet { get; private set; }

            public OldSentPacket(Packet packet)
            {
                this.Packet = packet;
                this.SendCount = 1;
            }

            public void IncrementSendCount()
            {
                SendCount++;
            }

            public override string ToString()
            {
                return "[" + Packet.PacketHeader.PacketCreationTime.ToShortTimeString() + "] " + Packet.PacketHeader.PacketType + " - " + Packet.PacketData.Length +" bytes.";
            }
        }

        /// <summary>
        /// Send an object using the connection default SendReceiveOptions
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        /// <param name="objectToSend">The object to send</param>
        public void SendObject(string sendingPacketType, object objectToSend) { SendObject(sendingPacketType, objectToSend, ConnectionDefaultSendReceiveOptions); }

        /// <summary>
        /// Send an object using the provided SendReceiveOptions
        /// </summary>
        /// <param name="sendingPacketType"></param>
        /// <param name="objectToSend"></param>
        /// <param name="options"></param>
        public void SendObject(string sendingPacketType, object objectToSend, SendReceiveOptions options)
        {
            SendPacket(new Packet(sendingPacketType, objectToSend, options));
        }

        /// <summary>
        /// Send an empty packet using the provided packetType. Usefull for signalling.
        /// </summary>
        /// <param name="sendingPacketType">The sending packet type</param>
        public void SendObject(string sendingPacketType) { SendObject(sendingPacketType, null); }

        /// <summary>
        /// Send an object using the connection default SendReceiveOptions and wait for a returned object again using default SendReceiveOptions.
        /// </summary>
        /// <typeparam name="returnObjectType">The type of return object</typeparam>
        /// <param name="sendingPacketTypeStr">The sending packet type</param>
        /// <param name="expectedReturnPacketTypeStr">The packet type which will be used for the reply</param>
        /// <param name="returnPacketTimeOutMilliSeconds">A timeout in milliseconds after which if not reply is received will throw an ExpectedReturnTimeoutException.</param>
        /// <param name="sendObject">The object to send</param>
        /// <returns></returns>
        public returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject) { return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject, null, null); }

        /// <summary>
        /// Send an object using the provided SendReceiveOptions and wait for a returned object using provided SendReceiveOptions.
        /// </summary>
        /// <typeparam name="returnObjectType">The type of return object</typeparam>
        /// <param name="sendingPacketTypeStr">The sending packet type</param>
        /// <param name="expectedReturnPacketTypeStr">The packet type which will be used for the reply</param>
        /// <param name="returnPacketTimeOutMilliSeconds">A timeout in milliseconds after which if not reply is received will throw an ExpectedReturnTimeoutException.</param>
        /// <param name="sendObject">The object to send</param>
        /// <param name="sendOptions">SendReceiveOptions to use when sending</param>
        /// <param name="receiveOptions">SendReceiveOptions used when receiving the return object</param>
        /// <returns></returns>
        public returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, SendReceiveOptions sendOptions, SendReceiveOptions receiveOptions)
        {
            returnObjectType returnObject = default(returnObjectType);

            bool remotePeerDisconnectedDuringWait = false;
            AutoResetEvent returnWaitSignal = new AutoResetEvent(false);

            #region SendReceiveDelegate
            NetworkComms.PacketHandlerCallBackDelegate<returnObjectType> SendReceiveDelegate = (packetHeader, sourceConnection, incomingObject) =>
            {
                returnObject = incomingObject;
                returnWaitSignal.Set();
            };

            //We use the following delegate to quickly force a response timeout if the remote end disconnects
            NetworkComms.ConnectionEstablishShutdownDelegate SendReceiveShutDownDelegate = (sourceConnection) =>
            {
                remotePeerDisconnectedDuringWait = true;
                returnObject = default(returnObjectType);
                returnWaitSignal.Set();
            };
            #endregion

            if (sendOptions == null) sendOptions = ConnectionDefaultSendReceiveOptions;
            if (receiveOptions == null) receiveOptions = ConnectionDefaultSendReceiveOptions;

            AppendShutdownHandler(SendReceiveShutDownDelegate);
            AppendIncomingPacketHandler(expectedReturnPacketTypeStr, SendReceiveDelegate, receiveOptions);

            Packet sendPacket = new Packet(sendingPacketTypeStr, expectedReturnPacketTypeStr, sendObject, sendOptions);
            SendPacket(sendPacket);

            //We wait for the return data here
            if (!returnWaitSignal.WaitOne(returnPacketTimeOutMilliSeconds))
            {
                RemoveIncomingPacketHandler(expectedReturnPacketTypeStr, SendReceiveDelegate);
                throw new ExpectedReturnTimeoutException("Timeout occurred after " + returnPacketTimeOutMilliSeconds + "ms waiting for response packet of type '" + expectedReturnPacketTypeStr + "'.");
            }

            RemoveIncomingPacketHandler(expectedReturnPacketTypeStr, SendReceiveDelegate);
            RemoveShutdownHandler(SendReceiveShutDownDelegate);

            if (remotePeerDisconnectedDuringWait)
                throw new ExpectedReturnTimeoutException("Remote end closed connection before data was successfully returned.");
            else
                return returnObject;
        }

        /// <summary>
        /// Send an empty packet using the connection default SendReceiveOptions and wait for a returned object. Usefull to request an object when there is no need to send anything.
        /// </summary>
        /// <typeparam name="returnObjectType">The type of return object</typeparam>
        /// <param name="sendingPacketTypeStr">The sending packet type</param>
        /// <param name="expectedReturnPacketTypeStr">The packet type which will be used for the reply</param>
        /// <param name="returnPacketTimeOutMilliSeconds">A timeout in milliseconds after which if not reply is received will throw an ExpectedReturnTimeoutException.</param>
        /// <returns></returns>
        public returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds) { return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, null, null, null); }

        /// <summary>
        /// Close the connection and trigger any associated shutdown delegates
        /// </summary>
        /// <param name="closeDueToError"></param>
        /// <param name="logLocation"></param>
        public void CloseConnection(bool closeDueToError, int logLocation = 0)
        {
            try
            {
                if (NetworkComms.loggingEnabled)
                {
                    if (closeDueToError)
                        NetworkComms.logger.Debug("Closing connection with " + ConnectionInfo + " due to error from [" + logLocation + "].");
                    else
                        NetworkComms.logger.Debug("Closing connection with " + ConnectionInfo + " from [" + logLocation + "].");
                }

                ConnectionInfo.NoteConnectionShutdown();

                //Set possible error cases
                if (closeDueToError)
                {
                    connectionSetupException = true;
                    connectionSetupExceptionStr = "Connection was closed during setup from [" + logLocation + "].";
                }

                //Ensure we are not waiting for a connection to be established if we have died due to error
                connectionSetupWait.Set();

                //Call any connection specific close requirements
                CloseConnectionSpecific(closeDueToError, logLocation);

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
                            if (NetworkComms.loggingEnabled && ConnectionInfo != null) NetworkComms.logger.Warn("Incoming data listen thread with " + ConnectionInfo + " aborted.");
                        }
                    }
                }
                catch (Exception)
                {

                }

                //Close connection my get called multiple times for a given connection depending on the reason for being closed
                bool firstClose = NetworkComms.RemoveConnectionReference(this);

                //Almost there
                //Last thing is to call any connection specific shutdown delegates
                if (firstClose && ConnectionSpecificShutdownDelegate != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered connection specific shutdown delegates with " + ConnectionInfo);
                    ConnectionSpecificShutdownDelegate(this);
                }

                //Last but not least we call any global connection shutdown delegates
                if (firstClose && NetworkComms.globalConnectionShutdownDelegates != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered global shutdown delegates with " + ConnectionInfo);
                    NetworkComms.globalConnectionShutdownDelegates(this);
                }
            }
            catch (Exception ex)
            {
                if (ex is ThreadAbortException)
                { /*Ignore the threadabort exception if we had to nuke a thread*/ }
                else
                    NetworkComms.LogError(ex, "NCError_CloseConnection", "Error closing connection with " + ConnectionInfo + ". Close called from " + logLocation + (closeDueToError ? " due to error." : "."));

                //We try to rethrow where possible but CloseConnection could very likely be called from within networkComms so we just have to be happy with a log here
            }
        }

        /// <summary>
        /// Every connection will probably have to perform connection specific shutdown tasks. This is called before the global connection close tasks.
        /// </summary>
        /// <param name="closeDueToError"></param>
        /// <param name="logLocation"></param>
        protected abstract void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0);

        /// <summary>
        /// Uses the current connection and returns a bool dependant on the remote end responding to a SendReceiveObject call within the default NetworkComms.ConnectionAliveTestTimeoutMS
        /// </summary>
        /// <returns></returns>
        public bool ConnectionAlive()
        {
            return ConnectionAlive(NetworkComms.ConnectionAliveTestTimeoutMS);
        }

        /// <summary>
        /// Uses the current connection and returns a bool dependant on the remote end responding to a SendReceiveObject call within the provided aliveRespondTimeoutMS
        /// </summary>
        /// <returns></returns>
        public bool ConnectionAlive(int aliveRespondTimeoutMS) 
        {
            long responseTime;
            return ConnectionAlive(aliveRespondTimeoutMS, out responseTime);
        }

        /// <summary>
        /// Uses the current connection and returns a bool dependant on the remote end responding to a SendReceiveObject call within the provided aliveRespondTimeoutMS
        /// </summary>
        /// <param name="aliveRespondTimeoutMS"></param>
        /// <param name="responseTimeMS">The number of milliseconds taken for a response to be recieved</param>
        /// <returns></returns>
        public bool ConnectionAlive(int aliveRespondTimeoutMS, out long responseTimeMS)
        {
            System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
            responseTimeMS = -1;

            if (!ConnectionInfo.ConnectionEstablished)
            {
                if ((DateTime.Now - ConnectionInfo.ConnectionCreationTime).Milliseconds > NetworkComms.ConnectionEstablishTimeoutMS)
                {
                    CloseConnection(false, -1);
                    return false;
                }
                else
                    return true;
            }
            else
            {
                try
                {
                    timer.Start();
                    bool returnValue = SendReceiveObject<bool>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), aliveRespondTimeoutMS, false, NetworkComms.InternalFixedSendReceiveOptions, NetworkComms.InternalFixedSendReceiveOptions);
                    timer.Stop();

                    responseTimeMS = timer.ElapsedMilliseconds;

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("ConnectionAliveTest success, response in " + timer.ElapsedMilliseconds + "ms.");

                    return returnValue;
                }
                catch (Exception)
                {
                    CloseConnection(true, 4);
                    return false;
                }
            }   
        }

        /// <summary>
        /// Send the provided packet to the remoteEndPoint. Waits for recieve confirmation if required.
        /// </summary>
        /// <param name="packet"></param>
        protected void SendPacket(Packet packet)
        {
            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Entering packet send of '" + packet.PacketHeader.PacketType + "' packetType to " + ConnectionInfo);

            //Multiple threads may try to send packets at the same time so wait one at a time here
            lock (sendLocker)
            {
                //We don't allow sends on a closed connection
                if (ConnectionInfo.ConnectionShutdown) throw new CommunicationException("Attempting to send packet on connection which has been closed or is currently closing.");

                string confirmationCheckSum = "";
                AutoResetEvent confirmationWaitSignal = new AutoResetEvent(false);
                bool remotePeerDisconnectedDuringWait = false;

                #region Delegates
                //Specify a delegate we may use if we require receive confirmation
                NetworkComms.PacketHandlerCallBackDelegate<string> confirmationDelegate = (packetHeader, connectionInfo, incomingString) =>
                {
                    //if (connectionInfo.NetworkIdentifier == this.ConnectionInfo.NetworkIdentifier && connectionInfo.RemoteEndPoint == this.ConnectionInfo.RemoteEndPoint)
                    //{
                    confirmationCheckSum = incomingString;
                    confirmationWaitSignal.Set();
                    //}
                };

                //We use the following delegate to quickly force a response timeout if the remote end disconnects during a send/wait
                NetworkComms.ConnectionEstablishShutdownDelegate ConfirmationShutDownDelegate = (connectionInfo) =>
                {
                    //if (connectionInfo.NetworkIdentifier == this.ConnectionInfo.NetworkIdentifier && connectionInfo.RemoteEndPoint == this.ConnectionInfo.RemoteEndPoint)
                    //{
                    remotePeerDisconnectedDuringWait = true;
                    confirmationWaitSignal.Set();
                    //}
                };
                #endregion

                try
                {
                    #region Prepare For Confirmation and Possible Validation
                    //Add the confirmation handler if required
                    if (packet.PacketHeader.ReceiveConfirmationRequired)
                    {
                        AppendIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), confirmationDelegate, NetworkComms.InternalFixedSendReceiveOptions);
                        AppendShutdownHandler(ConfirmationShutDownDelegate);
                    }

                    //If this packet is not a checkSumFailResend
                    if (NetworkComms.EnablePacketCheckSumValidation && packet.PacketHeader.PacketType != Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend))
                    {
                        //We only want to keep packets when they are under some provided theshold
                        //otherwise this becomes a quick 'memory leak'
                        if (packet.PacketData.Length < NetworkComms.CheckSumMismatchSentPacketCacheMaxByteLimit)
                        {
                            lock (sentPacketsLocker)
                                if (!sentPackets.ContainsKey(packet.PacketHeader.CheckSumHash))
                                    sentPackets.Add(packet.PacketHeader.CheckSumHash, new OldSentPacket(packet));
                        }
                    }
                    #endregion

                    SendPacketSpecific(packet);

                    #region SentPackets Cleanup
                    //If sent packets is greater than 40 we delete anything older than a minute
                    lock (sentPacketsLocker)
                    {
                        if (sentPackets.Count > 40)
                        {
                            sentPackets = (from current in sentPackets.Values
                                           where current.Packet.PacketHeader.PacketCreationTime < DateTime.Now.AddMinutes(-1)
                                           select new
                                           {
                                               key = current.Packet.PacketHeader.CheckSumHash,
                                               value = current
                                           }).ToDictionary(p => p.key, p => p.value);
                        }
                    }
                    #endregion

                    #region Wait For Confirmation If Required
                    //If we required receive confirmation we now wait for that confirmation
                    if (packet.PacketHeader.ReceiveConfirmationRequired)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... waiting for receive confirmation packet.");

                        if (!(confirmationWaitSignal.WaitOne(NetworkComms.PacketConfirmationTimeoutMS)))
                            throw new ConfirmationTimeoutException("Confirmation packet timeout.");

                        if (remotePeerDisconnectedDuringWait)
                            throw new ConfirmationTimeoutException("Remote end closed connection before confirmation packet was returned.");
                        else
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... confirmation packet received.");
                        }
                    }
                    #endregion

                    //Update the traffic time as late as possible incase there is a problem
                    ConnectionInfo.UpdateLastTrafficTime();
                }
                catch (ConfirmationTimeoutException)
                {
                    throw;
                }
                catch (CommunicationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new CommunicationException(ex.ToString());
                }
                finally
                {
                    if (packet.PacketHeader.ReceiveConfirmationRequired)
                    {
                        //Cleanup our delegates
                        RemoveIncomingPacketHandler(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), confirmationDelegate);
                        RemoveShutdownHandler(ConfirmationShutDownDelegate);
                    }
                }
            }

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Completed packet send of '" + packet.PacketHeader.PacketType + "' packetType to " + ConnectionInfo);
        
        }

        /// <summary>
        /// Connection specific implementation for sending packets on this connection type. Will only be called from within a lock so method does not need to implement further thread safety.
        /// </summary>
        /// <param name="packet"></param>
        protected abstract void SendPacketSpecific(Packet packet);

        /// <summary>
        /// Connection specific implementation for sending a null packets on this connection type. Will only be called from within a lock so method does not need to implement further thread safety.
        /// </summary>
        protected abstract void SendNullPacket();
    }
}
