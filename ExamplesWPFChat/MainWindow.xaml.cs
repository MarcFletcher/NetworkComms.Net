using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NetworkCommsDotNet;
using DPSBase;
using SevenZipLZMACompressor;

namespace ExamplesWPFChat
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// The type of connection currently used to send and recieve messages. Default is TCP.
        /// </summary>
        ConnectionType connectionType = ConnectionType.TCP;

        /// <summary>
        /// A boolean used to track the very first initialisation
        /// </summary>
        bool firstInitialisation = true;

        /// <summary>
        /// Dictionary to keep track of which peer messages have already been written to the chat window
        /// </summary>
        Dictionary<ShortGuid, ChatMessage> lastPeerMessageDict = new Dictionary<ShortGuid, ChatMessage>();

        /// <summary>
        /// The maximum number of times a chat message will be relayed
        /// </summary>
        int relayMaximum = 3;

        /// <summary>
        /// An optional encryption key to use should one be required.
        /// This can be changed freely but must obviously be the same
        /// for both sender and reciever.
        /// </summary>
        string encryptionKey = "ljlhjf8uyfln23490jf;m21-=scm20--iflmk;";

        /// <summary>
        /// A local counter used to track the number of messages sent from
        /// this instance.
        /// </summary>
        long messageSendIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
            InitialiseNetworkComms();
        }

        /// <summary>
        /// Initialise networkComms.Net. We default to using TCP
        /// </summary>
        private void InitialiseNetworkComms()
        {
            if (connectionType == ConnectionType.TCP)
            {
                //Start listening for new incoming TCP connections
                //Parameter is true so that we listen on a random port if the default is not available
                TCPConnection.StartListening(true);

                //Write the IP addresses and ports that we are listening on to the chatBox
                chatBox.AppendText("Initialising WPF chat example. Accepting TCP connections on:\n");
                foreach (var listenEndPoint in TCPConnection.ExistingLocalListenEndPoints())
                    chatBox.AppendText(listenEndPoint.Address + ":" + listenEndPoint.Port + "\n");
            }
            else if (connectionType == ConnectionType.UDP)
            {
                //Start listening for new incoming UDP connections
                //Parameter is true so that we listen on a random port if the default is not available
                UDPConnection.StartListening(true);

                //Write the IP addresses and ports that we are listening on to the chatBox
                chatBox.AppendText("Initialising WPF chat example. Accepting UDP connections on:\n");
                foreach (var listenEndPoint in UDPConnection.ExistingLocalListenEndPoints())
                    chatBox.AppendText(listenEndPoint.Address + ":" + listenEndPoint.Port + "\n");
            }
            else
                chatBox.AppendText("Error: Unable to initialise comms as an invalid connectionType was set.");

            //Add a blank line after the initialisation output
            chatBox.AppendText("\n");

            //We only need to add the packet handlers once. If we change connection type calling NetworkComms.Shutdown() does not remove these.
            if (firstInitialisation)
            {
                firstInitialisation = false;

                //Set the default Local Name box using to the local host name
                localName.Text = NetworkComms.HostName;

                //Configure NetworkCommsDotNet to handle and incoming packet of type 'ChatMessage'
                //e.g. If we recieve a packet of type 'ChatMessage' execute the method 'HandleIncomingChatMessage'
                NetworkComms.AppendGlobalIncomingPacketHandler<ChatMessage>("ChatMessage", HandleIncomingChatMessage);

                //Configure NetworkCommsDotNet to perform an action when a connection is closed
                //e.g. When a connection is closed execute the method 'HandleConnectionClosed'
                NetworkComms.AppendGlobalConnectionCloseHandler(HandleConnectionClosed);
            }
        }

        /// <summary>
        /// Append the provided message to the chatBox text box.
        /// </summary>
        /// <param name="message"></param>
        private void AppendLineToChatBox(string message)
        {
            //To ensure we can succesfully append to the text box from any thread
            //we need to wrap the append within an invoke action.
            chatBox.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
            {
                chatBox.AppendText(messageToAdd + "\n");
                chatBox.ScrollToEnd();
            }), new object[] { message });
        }

        /// <summary>
        /// Refresh the messagesFrom text box using the recent message history.
        /// </summary>
        private void RefreshMessagesFromBox()
        {
            //We will perform a lock here to ensure the text box is only
            //updated one thread at  time
            lock (lastPeerMessageDict)
            {
                //Use a linq experssion to extract an array of all current users from lastPeerMessageDict
                string[] currentUsers = (from current in lastPeerMessageDict.Values orderby current.SourceName select current.SourceName).ToArray();

                //To ensure we can succesfully append to the text box from any thread
                //we need to wrap the append within an invoke action.
                this.messagesFrom.Dispatcher.BeginInvoke(new Action<string[]>((users) =>
                {
                    //First clear the text box
                    messagesFrom.Text = "";

                    //Now write out each username
                    foreach (var username in users)
                        messagesFrom.AppendText(username + "\n");
                }), new object[] { currentUsers });
            }
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

            //Once we have written to the ChatBox we refresh the MessagesFromWindow
            RefreshMessagesFromBox();

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

            //Refresh the messages from box to reflect this disconnection
            RefreshMessagesFromBox();
        }

        /// <summary>
        /// Send a message.
        /// </summary>
        private void SendMessage()
        {
            //If we have tried to send a zero length string we just return
            if (messageText.Text.Trim() == "") return;

            //We may or may not have entered some master connection information
            ConnectionInfo masterConnectionInfo = null;
            if (masterIP.Text != "")
            {
                try { masterConnectionInfo = new ConnectionInfo(masterIP.Text.Trim(), int.Parse(masterPort.Text)); }
                catch (Exception)
                {
                    MessageBox.Show("Failed to parse the master IP and port. Please ensure it is correct and try again", "Master IP & Port Parse Error", MessageBoxButton.OK);
                    return;
                }
            }

            //We wrap everything we want to send in the ChatMessage class we created
            ChatMessage messageToSend = new ChatMessage(NetworkComms.NetworkIdentifier, localName.Text, messageText.Text, messageSendIndex++);

            //We add our own message to the message history incase it gets relayed back to us
            lock (lastPeerMessageDict) lastPeerMessageDict[NetworkComms.NetworkIdentifier] = messageToSend;

            //We write our own message to the chatBox
            AppendLineToChatBox(messageToSend.SourceName + " - " + messageToSend.Message);

            //We refresh the MessagesFrom box so that it includes our own name
            RefreshMessagesFromBox();

            //We clear the text within the messageText box.
            this.messageText.Text = "";

            //If we provided master information we send to the master first
            if (masterConnectionInfo != null)
            {
                //We perform the send within a try catch to ensure the application continues to run if there is a problem.
                try 
                { 
                    if(connectionType == ConnectionType.TCP)
                        TCPConnection.GetConnection(masterConnectionInfo).SendObject("ChatMessage", messageToSend); 
                    else if (connectionType == ConnectionType.UDP)
                        UDPConnection.GetConnection(masterConnectionInfo, UDPOptions.None).SendObject("ChatMessage", messageToSend);
                    else
                        throw new Exception("An invalid connectionType is set.");
                }
                catch (CommsException) { MessageBox.Show("A CommsException occured while trying to send message to " + masterConnectionInfo, "CommsException", MessageBoxButton.OK); }
            }

            //If we have any other connections we now send the message to those as well
            //This ensures that if we are the master everyone who is connected to us gets our message
            var otherConnectionInfos = (from current in NetworkComms.AllConnectionInfo() where current != masterConnectionInfo select current).ToArray();
            foreach (ConnectionInfo info in otherConnectionInfos)
            {
                //We perform the send within a try catch to ensure the application continues to run if there is a problem.
                try 
                {
                    if (connectionType == ConnectionType.TCP)
                        TCPConnection.GetConnection(info).SendObject("ChatMessage", messageToSend); 
                    else if (connectionType == ConnectionType.UDP)
                        UDPConnection.GetConnection(info, UDPOptions.None).SendObject("ChatMessage", messageToSend);
                    else
                        throw new Exception("An invalid connectionType is set.");
                }
                catch (CommsException) { MessageBox.Show("A CommsException occured while trying to send message to " + info, "CommsException", MessageBoxButton.OK); }
            }
        }

        /// <summary>
        /// Send any entered message when we click the send button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        /// <summary>
        /// Send any entered message when we press enter or return
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MessageText_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
                SendMessage();
        }

        /// <summary>
        /// Enable encryption of all data as default
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseEncryptionBox_Checked(object sender, RoutedEventArgs e)
        {
            RijndaelPSKEncrypter.AddPasswordToOptions(NetworkComms.DefaultSendReceiveOptions.Options, encryptionKey);
            NetworkComms.DefaultSendReceiveOptions.DataProcessors.Add(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());
        }

        /// <summary>
        /// Disable encryption of all data as default
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseEncryptionBox_Unchecked(object sender, RoutedEventArgs e)
        {
            NetworkComms.DefaultSendReceiveOptions.DataProcessors.Remove(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());
        }

        /// <summary>
        /// Correctly shutdown NetworkCommsDotNet when closing the WPF application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Ensure we shutdown comms when we are finished
            NetworkComms.Shutdown();
        }

        /// <summary>
        /// Use UDP for all communication
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseUDP_Checked(object sender, RoutedEventArgs e)
        {
            if (this.UseTCP!=null && this.UseTCP.IsChecked != null && !(bool)this.UseTCP.IsChecked)
            {
                //Update the application and connectionType
                connectionType = ConnectionType.UDP;

                //Shutdown comms and clear any existing chat messages
                NetworkComms.Shutdown();
                chatBox.Clear();

                //Initialise network comms using the new connection type
                InitialiseNetworkComms();
            }
        }

        /// <summary>
        /// Use TCP for all communication
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseTCP_Checked(object sender, RoutedEventArgs e)
        {
            if (this.UseUDP!= null && this.UseUDP.IsChecked != null && !(bool)this.UseUDP.IsChecked)
            {
                //Update the application and connectionType
                connectionType = ConnectionType.TCP;

                //Shutdown comms and clear any existing chat messages
                NetworkComms.Shutdown();
                chatBox.Clear();

                //Initialise network comms using the new connection type
                InitialiseNetworkComms();
            }
        }
    }
}
