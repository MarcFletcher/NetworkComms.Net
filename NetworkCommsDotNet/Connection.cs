using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// NetworkComms maintains a top level Connection object for shared methods
    /// </summary>
    public abstract class Connection
    {
        /// <summary>
        /// Information related to this connection.
        /// </summary>
        public ConnectionInfo ConnectionInfo { get; protected set; }

        /// <summary>
        /// The default send recieve options used for this connection
        /// </summary>
        public SendReceiveOptions ConnectionDefaultSendReceiveOptions { get; set; }

        /// <summary>
        /// A multicast function delegate for maintaining connection specific shutdown delegates
        /// </summary>
        protected NetworkComms.ConnectionShutdownDelegate ConnectionSpecificShutdownDelegate { get; set; }

        /// <summary>
        /// The packet builder for this connection
        /// </summary>
        protected ConnectionPacketBuilder PacketBuilder { get; set; }

        /// <summary>
        /// Connection setup parameters
        /// </summary>
        protected ManualResetEvent connectionSetupWait = new ManualResetEvent(false);
        protected ManualResetEvent connectionEstablishWait = new ManualResetEvent(false);

        protected volatile bool connectionSetupException = false;
        protected string connectionSetupExceptionStr = "";

        /// <summary>
        /// Maintains a list of sent packets for the purpose of confirmation and possible resends.
        /// </summary>
        protected object sentPacketsLocker = new object();
        protected Dictionary<string, OldSentPacket> sentPackets = new Dictionary<string, OldSentPacket>();
        protected class OldSentPacket
        {
            public int sendCount = 1;
            public Packet packet;

            public OldSentPacket(Packet packet)
            {
                this.packet = packet;
            }
        }

        /// <summary>
        /// Lockers for maintaing thread safe operation
        /// </summary>
        protected object sendLocker = new object();
        protected object delegateLocker = new object();

        /// <summary>
        /// We only allow internal classes to create connection instances
        /// </summary>
        public Connection() { }

        public void EstablishConnection() { }

        /// <summary>
        /// Close the connection and trigger any associated shutdown delegates
        /// </summary>
        /// <param name="closeDueToError"></param>
        /// <param name="logLocation"></param>
        public void CloseConnection(bool closeDueToError, int logLocation = 0) 
        {
            try
            {
                if (closeDueToError)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Closing connection with " + ConnectionInfo.RemoteEndPoint.Address + " due to error [" + logLocation + "] - (" + (ConnectionInfo == null ? "NA" : ConnectionInfo.RemoteNetworkIdentifier.ToString()) + ")");
                    else
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Closing connection with " + ConnectionInfo.RemoteEndPoint.Address + " [" + logLocation + "] - (" + (ConnectionInfo == null ? "NA" : ConnectionInfo.RemoteNetworkIdentifier.ToString()) + ")");
                }

                ConnectionInfo.ConnectionShutdown = true;

                //Set possible error cases
                if (closeDueToError)
                {
                    connectionSetupException = true;
                    connectionSetupExceptionStr = "Connection has been closed.";
                }

                //Ensure we are not waiting for a connection to be established if we have died due to error
                connectionSetupWait.Set();

                //Now call any connection specific shutdown tasks
                CloseConnectionSpecific(closeDueToError, logLocation);

                //Close connection my get called multiple times for a given connection depending on the reason for being closed
                bool firstClose = false;

                //Once we think we have closed the connection it's time to get rid of our other references
                lock (NetworkComms.globalDictAndDelegateLocker)
                {
                    if (ConnectionInfo != null)
                    {
                        //We establish whether we have already done this step
                        //if (NetworkComms.allConnectionsById.ContainsKey(RemoteConnectionInfo.NetworkIdentifier))
                        if (NetworkComms.GetConnection(ConnectionInfo.RemoteNetworkIdentifier, ConnectionInfo.ConnectionType) != null)
                            //Maintain a reference if this is our first connection close
                            firstClose = true;

                    }

                }

                //Almost there
                //Last thing is to call any connection specific shutdown delegates
                if (firstClose && ConnectionSpecificShutdownDelegate != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered connection specific shutdown delegate for " + ConnectionInfo.RemoteEndPoint.Address + " (" + ConnectionInfo.RemoteNetworkIdentifier.ToString() + ")");
                    ConnectionSpecificShutdownDelegate(ConnectionInfo.RemoteNetworkIdentifier);
                }

                //Last but not least we call any global connection shutdown delegates
                if (firstClose && NetworkComms.globalConnectionShutdownDelegates != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered global shutdown delegate for " + ConnectionInfo.RemoteEndPoint.Address + " (" + ConnectionInfo.RemoteNetworkIdentifier.ToString() + ")");
                    NetworkComms.globalConnectionShutdownDelegates(ConnectionInfo.RemoteNetworkIdentifier);
                }
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "ConnectionShutdownError");
            }
        }

        /// <summary>
        /// Every connection will probably have to perform connection specific shutdown tasks. This is called before the global connection close tasks.
        /// </summary>
        /// <param name="closeDueToError"></param>
        /// <param name="logLocation"></param>
        protected abstract void CloseConnectionSpecific(bool closeDueToError, int logLocation = 0);

        /// <summary>
        /// Send the provided object with the connection default options
        /// </summary>
        /// <param name="sendingPacketType"></param>
        /// <param name="objectToSend"></param>
        public void SendObject(string sendingPacketType, object objectToSend) { SendObject(sendingPacketType, objectToSend, ConnectionDefaultSendReceiveOptions); }

        /// <summary>
        /// Send the provided object with the provided options
        /// </summary>
        /// <param name="sendingPacketType"></param>
        /// <param name="objectToSend"></param>
        public abstract void SendObject(string sendingPacketType, object objectToSend, SendReceiveOptions options);

        /// <summary>
        /// Sends an empty packet using the provided packetType. Usefull for signalling
        /// </summary>
        /// <param name="sendingPacketType"></param>
        public void SendObject(string sendingPacketType) { SendObject(sendingPacketType, null); }

        /// <summary>
        /// Sends the provided object with the connection default options and waits for return object
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <returns>The expected return object</returns>
        public returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject) { return SendReceiveObject<returnObjectType>(sendingPacketTypeStr, receiveConfirmationRequired, expectedReturnPacketTypeStr, returnPacketTimeOutMilliSeconds, sendObject); }

        /// <summary>
        /// Sends the provided object with the provided options and waits for return object.
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="options">Send recieve options to use</param>
        /// <returns>The expected return object</returns>
        public abstract returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, SendReceiveOptions options);

        /// <summary>
        /// Uses the current connection to ensure the remote end responds within the provided aliveRespondTimeoutMS
        /// </summary>
        /// <returns></returns>
        public bool CheckConnectionAliveState(int aliveRespondTimeoutMS) 
        {
            DateTime startTime = DateTime.Now;

            if (!ConnectionInfo.ConnectionEstablished)
            {
                if ((DateTime.Now - ConnectionInfo.ConnectionCreationTime).Milliseconds > NetworkComms.connectionEstablishTimeoutMS)
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
                    bool returnValue = SendReceiveObject<bool>(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), false, Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), aliveRespondTimeoutMS, false, ConnectionDefaultSendReceiveOptions);

                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("ConnectionAliveTest success, response in " + (DateTime.Now - startTime).TotalMilliseconds + "ms");

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
        /// Attempts to use the data provided in packetBuilder to recreate something usefull. If we don't have enough data yet that value is set in packetBuilder.
        /// </summary>
        /// <param name="packetBuilder"></param>
        protected void IncomingPacketHandleHandOff(ConnectionPacketBuilder packetBuilder)
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... checking for completed packet with " + packetBuilder.TotalBytesRead + " bytes read.");

                //Loop until we are finished with this packetBuilder
                int loopCounter = 0;
                while (true)
                {
                    //If we have ended up with a null packet at the front, probably due to some form of concatentation we can pull it off here
                    //It is possible we have concatenation of several null packets along with real data so we loop until the firstByte is greater than 0
                    if (packetBuilder.FirstByte() == 0)
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... null packet removed in IncomingPacketHandleHandOff(), loop index - " + loopCounter);
                        //LastTrafficTime = DateTime.Now;

                        packetBuilder.ClearNTopBytes(1);

                        //Reset the expected bytes to 0 so that the next check starts from scratch
                        packetBuilder.TotalBytesExpected = 0;

                        //If we have run out of data completely then we can return immediately
                        if (packetBuilder.TotalBytesRead == 0) return;
                    }
                    else
                    {
                        //First determine the expected size of a header packet
                        int packetHeaderSize = packetBuilder.FirstByte() + 1;

                        //Do we have enough data to build a header?
                        if (packetBuilder.TotalBytesRead < packetHeaderSize)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... ... more data required for complete packet header.");

                            //Set the expected number of bytes and then return
                            packetBuilder.TotalBytesExpected = packetHeaderSize;
                            return;
                        }

                        //We have enough for a header
                        PacketHeader topPacketHeader = new PacketHeader(packetBuilder.ReadDataSection(1, packetHeaderSize - 1), NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);

                        //Idiot test
                        if (topPacketHeader.PacketType == null)
                            throw new SerialisationException("packetType value in packetHeader should never be null");

                        //We can now use the header to establish if we have enough payload data
                        //First case is when we have not yet received enough data
                        if (packetBuilder.TotalBytesRead < packetHeaderSize + topPacketHeader.PayloadPacketSize)
                        {
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... ... more data required for complete packet payload.");

                            //Set the expected number of bytes and then return
                            packetBuilder.TotalBytesExpected = packetHeaderSize + topPacketHeader.PayloadPacketSize;
                            return;
                        }
                        //Second case is we have enough data
                        else if (packetBuilder.TotalBytesRead >= packetHeaderSize + topPacketHeader.PayloadPacketSize)
                        {
                            //We can either have exactly the right amount or even more than we were expecting
                            //We may have too much data if we are sending high quantities and the packets have been concatenated
                            //no problem!!

                            //Build the necessary task input data
                            object[] completedData = new object[2];
                            completedData[0] = topPacketHeader;
                            completedData[1] = packetBuilder.ReadDataSection(packetHeaderSize, topPacketHeader.PayloadPacketSize);

                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Received packet of type '" + topPacketHeader.PacketType + "' from " + RemoteClientIP + (ConnectionInfo == null ? "" : " (" + ConnectionInfo.RemoteNetworkIdentifier.ToString() + ")") + ", containing " + packetHeaderSize + " header bytes and " + topPacketHeader.PayloadPacketSize + " payload bytes.");

                            if (NetworkComms.reservedPacketTypeNames.Contains(topPacketHeader.PacketType))
                            {
                                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... handling packet type '" + topPacketHeader.PacketType + "' inline. Loop index - " + loopCounter);
                                //If this is a reserved packetType we call the method inline so that it gets dealt with immediately
                                CompleteIncomingPacketWorker(completedData);
                            }
                            else
                            {
                                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... launching task to handle packet type '" + topPacketHeader.PacketType + "'. Loop index - " + loopCounter);
                                //If not a reserved packetType we run the completion in a seperate task so that this thread can continue to receive incoming data
                                Task.Factory.StartNew(CompleteIncomingPacketWorker, completedData);
                            }

                            //We clear the bytes we have just handed off
                            if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Removing " + (packetHeaderSize + topPacketHeader.PayloadPacketSize).ToString() + " bytes from incoming packet buffer.");
                            packetBuilder.ClearNTopBytes(packetHeaderSize + topPacketHeader.PayloadPacketSize);

                            //Reset the expected bytes to 0 so that the next check starts from scratch
                            packetBuilder.TotalBytesExpected = 0;

                            //If we have run out of data completely then we can return immediately
                            if (packetBuilder.TotalBytesRead == 0) return;
                        }
                        else
                            throw new CommunicationException("This should be impossible!");
                    }

                    loopCounter++;
                }
            }
            catch (Exception ex)
            {
                //Any error, throw an exception.
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("A fatal exception occured in IncomingPacketHandleHandOff(), connection with " + ConnectionEndPoint.Address + ":" + ConnectionEndPoint.Port + " be closed. See log file for more information.");

                NetworkComms.LogError(ex, "CommsError");
                CloseConnection(true, 16);
            }
        }

        /// <summary>
        /// Handle an incoming CheckSumFailResend packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        private void CheckSumFailResendHandler(byte[] packetDataSection)
        {
            //If we have been asked to resend a packet then we just go through the list and resend it.
            OldSentPacket packetToReSend;
            lock (sentPacketsLocker)
            {
                string checkSumRequested = NetworkComms.internalFixedSerializer.DeserialiseDataObject<string>(packetDataSection, NetworkComms.internalFixedCompressor);

                if (sentPackets.ContainsKey(checkSumRequested))
                    packetToReSend = sentPackets[checkSumRequested];
                else
                    throw new CheckSumException("There was no packet sent with a matching check sum");
            }

            //If we have already tried resending the packet 10 times something has gone horribly wrong
            if (packetToReSend.sendCount > 10) throw new CheckSumException("Packet sent resulted in a catastropic checksum check exception.");

            if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn(" ... resending packet due to MD5 mismatch.");

            //Increment send count and then resend
            packetToReSend.sendCount++;
            SendPacket(packetToReSend.packet);
        }

        /// <summary>
        /// Once we have received all incoming data we can handle it further.
        /// </summary>
        /// <param name="packetBytes"></param>
        private void CompleteIncomingPacketWorker(object packetBytes)
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... packet hand off task started.");

                //LastTrafficTime = DateTime.Now;

                //Check for a shutdown connection
                if (ConnectionInfo.ConnectionShutdown) return;

                //Idiot check
                if (packetBytes == null) throw new NullReferenceException("Provided object packetBytes should really not be null.");

                //Unwrap with an idiot check
                object[] completedData = packetBytes as object[];
                if (completedData == null) throw new NullReferenceException("Type cast to object[] failed in CompleteIncomingPacketWorker.");

                //Unwrap with an idiot check
                PacketHeader packetHeader = completedData[0] as PacketHeader;
                if (packetHeader == null) throw new NullReferenceException("Type cast to PacketHeader failed in CompleteIncomingPacketWorker.");

                //Unwrap with an idiot check
                byte[] packetDataSection = completedData[1] as byte[];
                if (packetDataSection == null) throw new NullReferenceException("Type cast to byte[] failed in CompleteIncomingPacketWorker.");

                //We only look at the check sum if we want to and if it has been set by the remote end
                if (NetworkComms.EnablePacketCheckSumValidation && packetHeader.CheckSumHash.Length > 0)
                {
                    //Validate the checkSumhash of the data
                    if (packetHeader.CheckSumHash != NetworkComms.MD5Bytes(packetDataSection))
                    {
                        if (NetworkComms.loggingEnabled) NetworkComms.logger.Warn(" ... corrupted packet header detected.");

                        //We have corruption on a resend request, something is very wrong so we throw an exception.
                        if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend)) throw new CheckSumException("Corrupted md5CheckFailResend packet received.");

                        //Instead of throwing an exception we can request the packet to be resent
                        Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend), false, packetHeader.CheckSumHash, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                        SendPacket(returnPacket);

                        //We need to wait for the packet to be resent before going further
                        return;
                    }
                }

                //Remote end may have requested packet receive confirmation so we send that now
                if (packetHeader.ReceiveConfirmationRequired)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace(" ... sending requested receive confirmation packet.");

                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.Confirmation), false, packetHeader.CheckSumHash, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                    SendPacket(returnPacket);
                }

                //We can now pass the data onto the correct delegate
                //First we have to check for our reserved packet types
                //The following large sections have been factored out to make reading and debugging a little easier
                if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.CheckSumFailResend))
                    CheckSumFailResendHandler(packetDataSection);
                else if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.ConnectionSetup))
                    ConnectionSetupHandler(packetDataSection);
                else if (packetHeader.PacketType == Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket) && (NetworkComms.internalFixedSerializer.DeserialiseDataObject<bool>(packetDataSection, NetworkComms.internalFixedCompressor)) == false)
                {
                    //If we have received a ping packet from the originating source we reply with true
                    Packet returnPacket = new Packet(Enum.GetName(typeof(ReservedPacketType), ReservedPacketType.AliveTestPacket), false, true, NetworkComms.internalFixedSerializer, NetworkComms.internalFixedCompressor);
                    SendPacket(returnPacket);
                }

                //We allow users to add their own custom handlers for reserved packet types here
                //else
                if (true)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Triggering handlers for packet of type '" + packetHeader.PacketType + "' from " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port);

                    NetworkComms.TriggerPacketHandler(packetHeader, this.ConnectionInfo.RemoteNetworkIdentifier, packetDataSection);

                    //This is a really bad place to put a garbage collection, comment left in so that it does'nt get added again at some later date
                    //We don't want the CPU to JUST be trying to garbage collect the WHOLE TIME
                    //GC.Collect();
                }
            }
            catch (CommunicationException)
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("A communcation exception occured in CompleteIncomingPacketWorker(), connection with " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port + " be closed.");
                CloseConnection(true, 2);
            }
            catch (Exception ex)
            {
                //If anything goes wrong here all we can really do is log the exception
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Fatal("An unhandled exception occured in CompleteIncomingPacketWorker(), connection with " + ConnectionInfo.RemoteEndPoint.Address + ":" + ConnectionInfo.RemoteEndPoint.Port + " be closed. See log file for more information.");

                NetworkComms.LogError(ex, "CommsError");
                CloseConnection(true, 3);
            }
        }

        /// <summary>
        /// Add a connection specific shutdown delegate
        /// </summary>
        /// <param name="handlerToAppend"></param>
        public void AppendConnectionSpecificShutdownHandler(NetworkComms.ConnectionShutdownDelegate handlerToAppend)
        {
            lock (delegateLocker)
            {
                if (ConnectionSpecificShutdownDelegate == null)
                    ConnectionSpecificShutdownDelegate = handlerToAppend;
                else
                    ConnectionSpecificShutdownDelegate += handlerToAppend;

                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Added connection specific shutdown delegate to connection with id " + (!ConnectionInfo.ConnectionEstablished ? "NA" : this.ConnectionInfo.RemoteNetworkIdentifier.ToString()));
            }
        }

        /// <summary>
        /// Remove a connection specific shutdown delegate.
        /// </summary>
        /// <param name="handlerToRemove"></param>
        public void RemoveConnectionSpecificShutdownHandler(NetworkComms.ConnectionShutdownDelegate handlerToRemove)
        {
            lock (delegateLocker)
            {
                ConnectionSpecificShutdownDelegate -= handlerToRemove;
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Removed connection specific shutdown delegate to connection with id " + (!ConnectionInfo.ConnectionEstablished ? "NA" : this.ConnectionInfo.RemoteNetworkIdentifier.ToString()));
            }
        }
    }

    /// <summary>
    /// When a packet is broken into multiple variable chunks this class allows us to rebuild the entire object before continuing
    /// </summary>
    internal class ConnectionPacketBuilder
    {
        List<byte[]> packets = new List<byte[]>();
        List<int> packetActualBytes = new List<int>();

        int totalBytesRead = 0;
        int totalBytesExpected = 0;

        public int TotalBytesRead
        {
            get { return totalBytesRead; }
        }

        public ConnectionPacketBuilder()
        {

        }

        /// <summary>
        /// The total number of bytes expected to complete a whole packet
        /// </summary>
        public int TotalBytesExpected
        {
            get { return totalBytesExpected; }
            set { totalBytesExpected = value; }
        }

        /// <summary>
        /// Clears n bytes from recorded packets, starting at the beginning
        /// </summary>
        /// <param name="numBytesToRemove"></param>
        public void ClearNTopBytes(int numBytesToRemove)
        {
            if (numBytesToRemove > 0)
            {
                if (numBytesToRemove > totalBytesRead)
                    throw new CommunicationException("Attempting to remove more bytes than exist in the ConnectionPacketBuilder");

                int bytesRemoved = 0;

                //We will always remove bytes in order of the entries
                for (int i = 0; i < packets.Count; i++)
                {
                    if (packetActualBytes[i] > numBytesToRemove - bytesRemoved)
                    {
                        //Remove the necessary bytes from this packet and rebuild
                        //New array length is the original length minus the amount we need to remove
                        byte[] newPacketByteArray = new byte[packetActualBytes[i] - (numBytesToRemove - bytesRemoved)];
                        Buffer.BlockCopy(packets[i], numBytesToRemove - bytesRemoved, newPacketByteArray, 0, newPacketByteArray.Length);

                        bytesRemoved += packetActualBytes[i] - newPacketByteArray.Length;
                        packets[i] = newPacketByteArray;
                        packetActualBytes[i] = newPacketByteArray.Length;

                        //Stop removing data here
                        break;
                    }
                    else if (i > packets.Count - 1)
                    {
                        //When i == (packet.Count - 1) I would expect the above if condition to always be true
                        throw new CommunicationException("This should be impossible.");
                    }
                    else
                    {
                        //If we want to remove this entire packet we can just set the list reference to null
                        bytesRemoved += packetActualBytes[i];
                        packets[i] = null;
                        packetActualBytes[i] = -1;
                    }
                }

                if (bytesRemoved != numBytesToRemove)
                    throw new CommunicationException("bytesRemoved should really equal the requested numBytesToRemove");

                //Reset the totalBytesRead
                totalBytesRead -= bytesRemoved;

                //Get rid of any null packets
                packets = (from current in packets
                           where current != null
                           select current).ToList();

                packetActualBytes = (from current in packetActualBytes
                                     where current > -1
                                     select current).ToList();

                //This is a really bad place to put a garbage collection as it hammers the CPU
                //GC.Collect();
            }
        }

        /// <summary>
        /// Appends a new packet to the packetBuilder
        /// </summary>
        /// <param name="packetBytes"></param>
        /// <param name="packet"></param>
        public void AddPacket(int packetBytes, byte[] packet)
        {
            totalBytesRead += packetBytes;

            packets.Add(packet);
            packetActualBytes.Add(packetBytes);
        }

        /// <summary>
        /// Returns the most recent added packet to the builder and removes it from the list.
        /// Used to more efficiently ustilise allocated arrays.
        /// </summary>
        /// <returns></returns>
        public byte[] RemoveMostRecentPacket(ref int lastPacketBytesRead)
        {
            if (packets.Count > 0)
            {
                int lastPacketIndex = packets.Count - 1;

                lastPacketBytesRead = packetActualBytes[lastPacketIndex];
                byte[] returnArray = packets[lastPacketIndex];

                totalBytesRead -= packetActualBytes[lastPacketIndex];

                packets.RemoveAt(lastPacketIndex);
                packetActualBytes.RemoveAt(lastPacketIndex);

                return returnArray;
            }
            else
                throw new Exception("Unable to remove most recent packet as packet list is empty.");
        }

        /// <summary>
        /// Returns the number of packets currently in the packetbuilder
        /// </summary>
        /// <returns></returns>
        public int CurrentPacketCount()
        {
            return packets.Count;
        }

        /// <summary>
        /// Returns the number of unused bytes from the most recently added packet
        /// </summary>
        /// <returns></returns>
        public int NumUnusedBytesMostRecentPacket()
        {
            if (packets.Count > 0)
            {
                int lastPacketIndex = packets.Count - 1;
                return packets[lastPacketIndex].Length - packetActualBytes[lastPacketIndex];
            }
            else
                throw new Exception("Unable to return requested size as packet list is empty.");
        }

        /// <summary>
        /// Returns the value of the front byte.
        /// </summary>
        /// <returns></returns>
        public byte FirstByte()
        {
            return packets[0][0];
        }

        /// <summary>
        /// Copies all data in the packetbuilder into a new array and returns
        /// </summary>
        /// <returns></returns>
        public byte[] GetAllData()
        {
            byte[] returnArray = new byte[totalBytesRead];

            int currentStart = 0;
            for (int i = 0; i < packets.Count; i++)
            {
                Buffer.BlockCopy(packets[i], 0, returnArray, currentStart, packetActualBytes[i]);
                currentStart += packetActualBytes[i];
            }

            return returnArray;
        }

        /// <summary>
        /// Copies the requested number of bytes from the packetbuilder into a new array and returns
        /// </summary>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public byte[] ReadDataSection(int startIndex, int length)
        {
            byte[] returnArray = new byte[length];
            int runningTotal = 0, writeTotal = 0;
            int startingPacketIndex;

            int firstPacketStartIndex = 0;
            //First find the correct starting packet
            for (startingPacketIndex = 0; startingPacketIndex < packets.Count; startingPacketIndex++)
            {
                if (startIndex - runningTotal <= packetActualBytes[startingPacketIndex])
                {
                    firstPacketStartIndex = startIndex - runningTotal;
                    break;
                }
                else
                    runningTotal += packetActualBytes[startingPacketIndex];
            }

            //Copy the bytes of interest
            for (int i = startingPacketIndex; i < packets.Count; i++)
            {
                if (i == startingPacketIndex)
                {
                    if (length > packetActualBytes[i] - firstPacketStartIndex)
                        //If we want from some starting point to the end of the packet
                        Buffer.BlockCopy(packets[i], firstPacketStartIndex, returnArray, writeTotal, packetActualBytes[i] - firstPacketStartIndex);
                    else
                    {
                        //We only want part of the packet
                        Buffer.BlockCopy(packets[i], firstPacketStartIndex, returnArray, writeTotal, length);
                        writeTotal += length;
                        break;
                    }

                    writeTotal = packetActualBytes[i] - firstPacketStartIndex;
                }
                else
                {
                    //We are no longer on the first packet
                    if (packetActualBytes[i] + writeTotal >= length)
                    {
                        //We have reached the last packet of interest
                        Buffer.BlockCopy(packets[i], 0, returnArray, writeTotal, length - writeTotal);
                        writeTotal += length - writeTotal;
                        break;
                    }
                    else
                    {
                        Buffer.BlockCopy(packets[i], 0, returnArray, writeTotal, packetActualBytes[i]);
                        writeTotal += packetActualBytes[i];
                    }
                }
            }

            if (writeTotal != length)
                throw new Exception("Not enough data available in packetBuilder to complete request.");

            return returnArray;
        }
    }

    /// <summary>
    /// The type of connection
    /// </summary>
    public enum ConnectionType : byte
    {
        TCP,
        UDPUnmanaged,

        //We may support others in future such as SSH, FTP, SCP etc.
    }
}
