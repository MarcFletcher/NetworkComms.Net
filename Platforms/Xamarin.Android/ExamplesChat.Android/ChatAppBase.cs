using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using NetworkCommsDotNet;
using DPSBase;

namespace ExamplesChat.Android
{
    public abstract class ChatAppBase
    {
        #region Public Fields
        /// <summary>
        /// The type of connection currently used to send and recieve messages. Default is TCP.
        /// </summary>
        public ConnectionType ConnectionType { get; set; }

        /// <summary>
        /// A boolean used to track the very first initialisation
        /// </summary>
        public bool FirstInitialisation { get; set; }

        /// <summary>
        /// The IP address of the master (server)
        /// </summary>
        public string MasterIPAddress { get; set; }

        /// <summary>
        /// The port of the master (server)
        /// </summary>
        public int MasterPort { get; set; }

        /// <summary>
        /// The local name used when sending messages
        /// </summary>
        public string LocalName { get; set; }

        /// <summary>
        /// A boolean used to track if encryption is currently being used
        /// </summary>
        public bool UseEncryption { get; set; }

        /// <summary>
        /// A global link to the chatBox
        /// </summary>
        public string ChatBox { get; set; }
        #endregion

        #region Private Fields
        /// <summary>
        /// Dictionary to keep track of which peer messages have already been written to the chat window
        /// </summary>
        Dictionary<ShortGuid, ChatMessage> lastPeerMessageDict = new Dictionary<ShortGuid, ChatMessage>();

        /// <summary>
        /// The maximum number of times a chat message will be relayed
        /// </summary>
        int relayMaximum = 3;

        /// <summary>
        /// A local counter used to track the number of messages sent from
        /// this instance.
        /// </summary>
        long messageSendIndex = 0;
        #endregion

        /// <summary>
        /// Constructor for the Application object.
        /// </summary>
        public ChatAppBase(string name, ConnectionType connectionType)
        {           
            MasterIPAddress = "";
            MasterPort = 10000;
            LocalName = name;
            UseEncryption = false;

            //Initialise the default values
            ConnectionType = connectionType;
            FirstInitialisation = true;
        }

        /// <summary>
        /// Initialise networkComms.Net. We default to using TCP
        /// </summary>
        public void InitialiseNetworkComms()
        {
            PrintUsageInstructions();

            if (ConnectionType == ConnectionType.TCP)
            {
                //Start listening for new incoming TCP connections
                //Parameter is true so that we listen on a random port if the default is not available
                TCPConnection.StartListening(true);

                //Write the IP addresses and ports that we are listening on to the chatBox
                AppendLineToChatBox("Initialising android chat example.\nAccepting TCP connections on:");
                foreach (var listenEndPoint in TCPConnection.ExistingLocalListenEndPoints())
                    AppendLineToChatBox(listenEndPoint.Address + ":" + listenEndPoint.Port);
            }
            else if (ConnectionType == ConnectionType.UDP)
            {
                //Start listening for new incoming UDP connections
                //Parameter is true so that we listen on a random port if the default is not available
                UDPConnection.StartListening(true);

                //Write the IP addresses and ports that we are listening on to the chatBox
                AppendLineToChatBox("Initialising android chat example.\nAccepting UDP connections on:");
                foreach (var listenEndPoint in UDPConnection.ExistingLocalListenEndPoints())
                   AppendLineToChatBox(listenEndPoint.Address + ":" + listenEndPoint.Port);
            }
            else
                AppendLineToChatBox("Error: Unable to initialise comms as an invalid connectionType was set.");

            //Add a blank line after the initialisation output
            AppendLineToChatBox("");

            //We only need to add the packet handlers once. If we change connection type calling NetworkComms.Shutdown() does not remove these.
            if (FirstInitialisation)
            {
                FirstInitialisation = false;

                //Configure NetworkCommsDotNet to handle and incoming packet of type 'ChatMessage'
                //e.g. If we recieve a packet of type 'ChatMessage' execute the method 'HandleIncomingChatMessage'
                NetworkComms.AppendGlobalIncomingPacketHandler<ChatMessage>("ChatMessage", HandleIncomingChatMessage);

                //Configure NetworkCommsDotNet to perform an action when a connection is closed
                //e.g. When a connection is closed execute the method 'HandleConnectionClosed'
                NetworkComms.AppendGlobalConnectionCloseHandler(HandleConnectionClosed);
            }
        }

        /// <summary>
        /// Outputs the usage instructions to the chat window
        /// </summary>
        private void PrintUsageInstructions()
        {
            AppendLineToChatBox("");
            AppendLineToChatBox("Chat usage instructions:");
            AppendLineToChatBox("");
            AppendLineToChatBox("Step 1. Open atleast two chat applications. One of them could be the native windows chat example.");
            AppendLineToChatBox("Step 2. Decide which application will be the 'master', aka server.");
            AppendLineToChatBox("Step 3. Enter the masters IP address and port number into the other applications.");
            AppendLineToChatBox("Step 4. Start chatting. Don't forget to the UDP connection method.");
            AppendLineToChatBox("");
        }

        /// <summary>
        /// Performs whatever functions we might so desire when we recieve an incoming ChatMessage
        /// </summary>
        /// <param name="header">The PacketHeader corresponding with the recieved object</param>
        /// <param name="connection">The Connection from which this object was recieved</param>
        /// <param name="incomingMessage">The incoming ChatMessage we are after</param>
        private void HandleIncomingChatMessage(PacketHeader header, Connection connection, ChatMessage incomingMessage)
        {
            //We only want to write a message once to the chat window
            //Because we allow relaying and may recieve the same message twice 
            //we use our history and message indexes to ensure we have a new message
            lock (lastPeerMessageDict)
            {
                if (lastPeerMessageDict.ContainsKey(incomingMessage.SourceIdentifier))
                {
                    if (lastPeerMessageDict[incomingMessage.SourceIdentifier].MessageIndex < incomingMessage.MessageIndex)
                    {
                        //If this message index is greater than the last seen from this source we can safely
                        //write the message to the ChatBox
                        AppendLineToChatBox(incomingMessage.SourceName + " - " + incomingMessage.Message);

                        //We now replace the last recieved message with the current one
                        lastPeerMessageDict[incomingMessage.SourceIdentifier] = incomingMessage;
                    }
                }
                else
                {
                    //If we have never had a message from this source before then it has to be new
                    //by defintion
                    lastPeerMessageDict.Add(incomingMessage.SourceIdentifier, incomingMessage);
                    AppendLineToChatBox(incomingMessage.SourceName + " - " + incomingMessage.Message);
                }
            }

            //This last section of the method is the relay function
            //We start by checking to see if this message has already been relayed
            //the maximum number of times
            if (incomingMessage.RelayCount < relayMaximum)
            {
                //If we are going to relay this message we need an array of 
                //all other known connections
                var allRelayConnections = (from current in NetworkComms.GetExistingConnection() where current != connection select current).ToArray();

                //We increment the relay count before we send
                incomingMessage.IncrementRelayCount();

                //We will now send the message to every other connection
                foreach (var relayConnection in allRelayConnections)
                {
                    //We ensure we perform the send within a try catch
                    //To ensure a single failed send will not prevent the
                    //relay to all working connections.
                    try { relayConnection.SendObject("ChatMessage", incomingMessage); }
                    catch (CommsException) { /* Catch the comms exception, ignore and continue */ }
                }
            }
        }

        /// <summary>
        /// Performs whatever functions we might so desire when an existing connection is closed.
        /// </summary>
        /// <param name="connection">The closed connection</param>
        private void HandleConnectionClosed(Connection connection)
        {
            //We are going to write a message to the ChatBox when a user disconnects
            //We perform the following within a lock so that threads proceed one at a time
            lock (lastPeerMessageDict)
            {
                //Extract the remoteIdentifier from the closed connection
                ShortGuid remoteIdentifier = connection.ConnectionInfo.NetworkIdentifier;

                //If at some point we recieved a message with this identifier we can
                //include the sourcename in the dissconnection message.
                if (lastPeerMessageDict.ContainsKey(remoteIdentifier))
                    AppendLineToChatBox("Connection with '" + lastPeerMessageDict[remoteIdentifier].SourceName + "' has been closed.");
                else
                    AppendLineToChatBox("Connection with '" + connection.ToString() + "' has been closed.");

                //Last thing is to remove this entry from our message history
                lastPeerMessageDict.Remove(connection.ConnectionInfo.NetworkIdentifier);
            }

        }
        
        /// <summary>
        /// Send a message.
        /// </summary>
        public void SendMessage(string toSend)
        {
            //If we have tried to send a zero length string we just return
            if (toSend.Trim() == "") return;

            //We may or may not have entered some master connection information
            ConnectionInfo masterConnectionInfo = null;
            if (MasterIPAddress != "")
            {
                try { masterConnectionInfo = new ConnectionInfo(MasterIPAddress, MasterPort); }
                catch (Exception)
                {
                    ShowMessage("Failed to parse the master IP and port. Please ensure it is correct and try again");
                    return;
                }
            }

            //We wrap everything we want to send in the ChatMessage class we created
            ChatMessage messageToSend = new ChatMessage(NetworkComms.NetworkIdentifier, LocalName, toSend, messageSendIndex++);

            //We add our own message to the message history incase it gets relayed back to us
            lock (lastPeerMessageDict) lastPeerMessageDict[NetworkComms.NetworkIdentifier] = messageToSend;

            //We write our own message to the chatBox
            AppendLineToChatBox(messageToSend.SourceName + " - " + messageToSend.Message);

            //Clear the input box text
            ClearInputLine();

            //If we provided master information we send to the master first
            if (masterConnectionInfo != null)
            {
                //We perform the send within a try catch to ensure the application continues to run if there is a problem.
                try
                {
                    if (ConnectionType == ConnectionType.TCP)
                        TCPConnection.GetConnection(masterConnectionInfo).SendObject("ChatMessage", messageToSend);
                    else if (ConnectionType == ConnectionType.UDP)
                        UDPConnection.GetConnection(masterConnectionInfo, UDPOptions.None).SendObject("ChatMessage", messageToSend);
                    else
                        throw new Exception("An invalid connectionType is set.");
                }
                catch (CommsException) { ShowMessage("A CommsException occured while trying to send message to " + masterConnectionInfo); }
            }

            //If we have any other connections we now send the message to those as well
            //This ensures that if we are the master everyone who is connected to us gets our message
            var otherConnectionInfos = (from current in NetworkComms.AllConnectionInfo() where current != masterConnectionInfo select current).ToArray();
            foreach (ConnectionInfo info in otherConnectionInfos)
            {
                //We perform the send within a try catch to ensure the application continues to run if there is a problem.
                try
                {
                    if (ConnectionType == ConnectionType.TCP)
                        TCPConnection.GetConnection(info).SendObject("ChatMessage", messageToSend);
                    else if (ConnectionType == ConnectionType.UDP)
                        UDPConnection.GetConnection(info, UDPOptions.None).SendObject("ChatMessage", messageToSend);
                    else
                        throw new Exception("An invalid connectionType is set.");
                }
                catch (CommsException) { ShowMessage("A CommsException occured while trying to send message to " + info); }
            }

            return;
        }

        /// <summary>
        /// Append the provided message to the chatBox text box.
        /// </summary>
        /// <param name="message">Message to be appended</param>
        public abstract void AppendLineToChatBox(string message);

        /// <summary>
        /// Clears all previous chat history
        /// </summary>
        public abstract void ClearChatHistory();

        /// <summary>
        /// Clears the input text box
        /// </summary>
        public abstract void ClearInputLine();

        /// <summary>
        /// Ouput message on error
        /// </summary>
        /// <param name="message">Message to be output</param>
        public abstract void ShowMessage(string message);
    }
}