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
    public abstract class Connection
    {
        /// <summary>
        /// Information related to this connection.
        /// </summary>
        public ConnectionInfo ConnectionInfo { get; protected set; }

        /// <summary>
        /// The default send receive options used for this connection
        /// </summary>
        public SendReceiveOptions ConnectionDefaultSendReceiveOptions { get; set; }

        /// <summary>
        /// A multicast function delegate for maintaining connection specific shutdown delegates
        /// </summary>
        protected NetworkComms.ConnectionShutdownDelegate ConnectionSpecificShutdownDelegate { get; set; }

        /// <summary>
        /// A connection specific incoming packet handler dictionary. These are called before any applicable global handlers
        /// </summary>
        protected Dictionary<string, List<NetworkComms.IPacketTypeHandlerDelegateWrapper>> incomingPacketHandlers = new Dictionary<string, List<NetworkComms.IPacketTypeHandlerDelegateWrapper>>();

        #region Incoming Data
        /// <summary>
        /// The packet builder for this connection
        /// </summary>
        protected ConnectionPacketBuilder PacketBuilder { get; set; }

        /// <summary>
        /// The current incoming data buffer
        /// </summary>
        byte[] dataBuffer;

        /// <summary>
        /// The total bytes read so far within dataBuffer
        /// </summary>
        int totalBytesRead;

        /// <summary>
        /// The thread listening for incoming data should we be using synchronous methods.
        /// </summary>
        protected Thread incomingDataListenThread = null;
        #endregion

        #region Connection Setup
        /// <summary>
        /// Connection setup parameters
        /// </summary>
        protected ManualResetEvent connectionSetupWait = new ManualResetEvent(false);
        protected ManualResetEvent connectionEstablishWait = new ManualResetEvent(false);

        protected volatile bool connectionSetupException = false;
        protected string connectionSetupExceptionStr = "";
        #endregion

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
        public Connection(ConnectionInfo connectionInfo) 
        {
            this.ConnectionInfo = connectionInfo;
        }

        public void EstablishConnection()
        {
            try
            {
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Trace("Establishing connection with " + ConnectionInfo.ToString());

                DateTime establishStartTime = DateTime.Now;

                if (ConnectionInfo.ConnectionEstablished || ConnectionInfo.ConnectionShutdown)
                    throw new ConnectionSetupException("Attempting to re-establish an already established or closed connection.");

                if (NetworkComms.commsShutdown)
                    throw new ConnectionSetupException("Attempting to establish new connection while comms is shutting down.");

                EstablishConnectionInternal();

                throw new NotImplementedException();
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

        protected abstract void EstablishConnectionInternal();

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
                        NetworkComms.logger.Debug("Closing connection with " + ConnectionInfo.ToString() + " due to error from [" + logLocation + "].");
                    else
                        NetworkComms.logger.Debug("Closing connection with " + ConnectionInfo.ToString() + " from [" + logLocation + "].");
                }

                ConnectionInfo.ConnectionShutdown = true;

                //Set possible error cases
                if (closeDueToError)
                {
                    connectionSetupException = true;
                    connectionSetupExceptionStr = "Connection was closed during setup from ["+logLocation+"].";
                }

                //Ensure we are not waiting for a connection to be established if we have died due to error
                connectionSetupWait.Set();

                //Call any connection specific close requirements
                CloseConnectionInternal(closeDueToError, logLocation);

                //Close connection my get called multiple times for a given connection depending on the reason for being closed
                bool firstClose = false;

                //Ensure connection references are removed from networkComms
                //Once we think we have closed the connection it's time to get rid of our other references
                lock (NetworkComms.globalDictAndDelegateLocker)
                {
                    #region Update NetworkComms Connection Dictionaries
                    //We establish whether we have already done this step
                    if ((NetworkComms.allConnectionsById.ContainsKey(ConnectionInfo.RemoteNetworkIdentifier) &&
                        NetworkComms.allConnectionsById[ConnectionInfo.RemoteNetworkIdentifier].ContainsKey(ConnectionInfo.ConnectionType) &&
                        NetworkComms.allConnectionsById[ConnectionInfo.RemoteNetworkIdentifier][ConnectionInfo.ConnectionType].Contains(this))
                        ||
                        (NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionInfo.RemoteEndPoint) &&
                        NetworkComms.allConnectionsByEndPoint[ConnectionInfo.RemoteEndPoint].ContainsKey(ConnectionInfo.ConnectionType)))
                    {
                        //Maintain a reference if this is our first connection close
                        firstClose = true;
                    }

                    //Keep a reference of the connection for possible debugging later
                    if (NetworkComms.oldConnectionIdToConnectionInfo.ContainsKey(ConnectionInfo.RemoteNetworkIdentifier))
                    {
                        if (NetworkComms.oldConnectionIdToConnectionInfo[ConnectionInfo.RemoteNetworkIdentifier].ContainsKey(ConnectionInfo.ConnectionType))
                            NetworkComms.oldConnectionIdToConnectionInfo[ConnectionInfo.RemoteNetworkIdentifier][ConnectionInfo.ConnectionType].Add(ConnectionInfo);
                        else
                            NetworkComms.oldConnectionIdToConnectionInfo[ConnectionInfo.RemoteNetworkIdentifier].Add(ConnectionInfo.ConnectionType, new List<ConnectionInfo>() { ConnectionInfo });
                    }
                    else
                        NetworkComms.oldConnectionIdToConnectionInfo.Add(ConnectionInfo.RemoteNetworkIdentifier, new Dictionary<ConnectionType, List<ConnectionInfo>>() { { ConnectionInfo.ConnectionType, new List<ConnectionInfo>() { ConnectionInfo } } });

                    if (NetworkComms.allConnectionsById.ContainsKey(ConnectionInfo.RemoteNetworkIdentifier) &&
                            NetworkComms.allConnectionsById[ConnectionInfo.RemoteNetworkIdentifier].ContainsKey(ConnectionInfo.ConnectionType))
                    {
                        if (!NetworkComms.allConnectionsById[ConnectionInfo.RemoteNetworkIdentifier][ConnectionInfo.ConnectionType].Contains(this))
                            throw new ConnectionShutdownException("A reference to the connection being closed was not found in the allConnectionsById dictionary.");
                        else
                            NetworkComms.allConnectionsById[ConnectionInfo.RemoteNetworkIdentifier][ConnectionInfo.ConnectionType].Remove(this);
                    }

                    //We can now remove this connection by end point as well
                    if (NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionInfo.RemoteEndPoint) && NetworkComms.allConnectionsByEndPoint[ConnectionInfo.RemoteEndPoint].ContainsKey(ConnectionInfo.ConnectionType))
                        NetworkComms.allConnectionsByEndPoint[ConnectionInfo.RemoteEndPoint].Remove(ConnectionInfo.ConnectionType);

                    //If this was the last connection type for this endpoint we can remove the endpoint reference as well
                    if (NetworkComms.allConnectionsByEndPoint.ContainsKey(ConnectionInfo.RemoteEndPoint) && NetworkComms.allConnectionsByEndPoint[ConnectionInfo.RemoteEndPoint].Count == 0)
                        NetworkComms.allConnectionsByEndPoint.Remove(ConnectionInfo.RemoteEndPoint);
                    #endregion
                }

                //Almost there
                //Last thing is to call any connection specific shutdown delegates
                if (firstClose && ConnectionSpecificShutdownDelegate != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered connection specific shutdown delegates with " + ConnectionInfo.ToString());
                    ConnectionSpecificShutdownDelegate(this.ConnectionInfo);
                }

                //Last but not least we call any global connection shutdown delegates
                if (firstClose && NetworkComms.globalConnectionShutdownDelegates != null)
                {
                    if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Triggered global shutdown delegates with " + ConnectionInfo.ToString());
                    NetworkComms.globalConnectionShutdownDelegates(this.ConnectionInfo);
                }
            }
            catch (Exception ex)
            {
                if (ex is ThreadAbortException)
                { /*Ignore the threadabort exception if we had to nuke a thread*/ }
                else
                    NetworkComms.LogError(ex, "NCError_CloseConnection", "Error closing connection with " + ConnectionInfo.ToString() + ". Close called from " + logLocation + (closeDueToError ? " due to error." : "."));
            
                //We try to rethrow where possible but CloseConnection could very likely be called from within networkComms so we just have to be happy with a log here
            }
        }

        /// <summary>
        /// Every connection will probably have to perform connection specific shutdown tasks. This is called before the global connection close tasks.
        /// </summary>
        /// <param name="closeDueToError"></param>
        /// <param name="logLocation"></param>
        protected abstract void CloseConnectionInternal(bool closeDueToError, int logLocation = 0);

        /// <summary>
        /// Send the provided object with the connection default options
        /// </summary>
        /// <param name="sendingPacketType"></param>
        /// <param name="objectToSend"></param>
        public void SendObject(string sendingPacketType, object objectToSend) { SendObject(sendingPacketType, objectToSend, ConnectionDefaultSendReceiveOptions); }

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
        /// Send the provided object with the provided options
        /// </summary>
        /// <param name="sendingPacketType"></param>
        /// <param name="objectToSend"></param>
        public abstract void SendObject(string sendingPacketType, object objectToSend, SendReceiveOptions options);

        /// <summary>
        /// Sends an empty packet using the provided packetType. Usefull for signalling.
        /// </summary>
        /// <param name="sendingPacketType"></param>
        public void SendObject(string sendingPacketType) { SendObject(sendingPacketType, null); }

        /// <summary>
        /// Sends the provided object with the provided options and waits for return object.
        /// </summary>
        /// <typeparam name="returnObjectType">The expected return object type, i.e. string, int[], etc</typeparam>
        /// <param name="sendingPacketTypeStr">Packet type to use during send</param>
        /// <param name="receiveConfirmationRequired">If true will throw an exception if object is not received at destination within PacketConfirmationTimeoutMS timeout. This may be significantly less than the provided returnPacketTimeOutMilliSeconds.</param>
        /// <param name="expectedReturnPacketTypeStr">Expected packet type used for return object</param>
        /// <param name="returnPacketTimeOutMilliSeconds">Time to wait in milliseconds for return object</param>
        /// <param name="sendObject">Object to send</param>
        /// <param name="options">Send receive options to use</param>
        /// <returns>The expected return object</returns>
        public abstract returnObjectType SendReceiveObject<returnObjectType>(string sendingPacketTypeStr, bool receiveConfirmationRequired, string expectedReturnPacketTypeStr, int returnPacketTimeOutMilliSeconds, object sendObject, SendReceiveOptions options);

        /// <summary>
        /// Uses the current connection and returns a bool dependant on the remote end responding within the provided aliveRespondTimeoutMS
        /// </summary>
        /// <returns></returns>
        public bool ConnectionAliveState(int aliveRespondTimeoutMS) 
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
            throw new NotImplementedException();
        }

        /// <summary>
        /// Handle an incoming CheckSumFailResend packet type
        /// </summary>
        /// <param name="packetDataSection"></param>
        private void CheckSumFailResendHandler(byte[] packetDataSection)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Once we have received all incoming data we can handle it further.
        /// </summary>
        /// <param name="packetBytes"></param>
        private void CompleteIncomingPacketWorker(object packetBytes)
        {
            throw new NotImplementedException();
        }

        #region Connection Specific Packet and Shutdown Handler Methods
        public static void AppendIncomingPacketHandler<T>(string packetTypeStr, NetworkComms.PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer, ISerialize packetTypeStrSerializer, ICompress packetTypeStrCompressor, bool enableAutoListen = true)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Add a new incoming packet handler using default serializer and compressor. Multiple handlers for the same packet type are allowed
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be used</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to use</param>
        /// <param name="enableAutoListen">If true will enable comms listening after delegate has been added</param>
        public static void AppendGlobalIncomingPacketHandler<T>(string packetTypeStr, NetworkComms.PacketHandlerCallBackDelegate<T> packetHandlerDelgatePointer, bool enableAutoListen = true)
        {
            AppendIncomingPacketHandler<T>(packetTypeStr, packetHandlerDelgatePointer, null, null, enableAutoListen);
        }

        /// <summary>
        /// Removes the provided delegate for the specified packet type
        /// </summary>
        /// <typeparam name="T">The object type expected by packetHandlerDelgatePointer</typeparam>
        /// <param name="packetTypeStr">Packet type for which this delegate should be removed</param>
        /// <param name="packetHandlerDelgatePointer">The delegate to remove</param>
        public static void RemoveIncomingPacketHandler(string packetTypeStr, Delegate packetHandlerDelgatePointer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all delegates for the provided packet type
        /// </summary>
        /// <param name="packetTypeStr">Packet type for which all delegates should be removed</param>
        public static void RemoveAllPacketHandlers(string packetTypeStr)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes all delegates for all packet types
        /// </summary>
        public static void RemoveAllPacketHandlers()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Add a connection specific shutdown delegate
        /// </summary>
        /// <param name="handlerToAppend"></param>
        public void AppendShutdownHandler(NetworkComms.ConnectionShutdownDelegate handlerToAppend)
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
        public void RemoveShutdownHandler(NetworkComms.ConnectionShutdownDelegate handlerToRemove)
        {
            lock (delegateLocker)
            {
                ConnectionSpecificShutdownDelegate -= handlerToRemove;
                if (NetworkComms.loggingEnabled) NetworkComms.logger.Debug("Removed connection specific shutdown delegate to connection with id " + (!ConnectionInfo.ConnectionEstablished ? "NA" : this.ConnectionInfo.RemoteNetworkIdentifier.ToString()));
            }
        }
        #endregion
    }

    /// <summary>
    /// When a packet is broken into multiple variable chunks this class allows us to rebuild the entire object before continuing
    /// </summary>
    public class ConnectionPacketBuilder
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
