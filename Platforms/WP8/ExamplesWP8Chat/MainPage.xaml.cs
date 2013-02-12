using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using ExamplesWP8Chat.Resources;
using NetworkCommsDotNet;
using DPSBase;

namespace ExamplesWP8Chat
{
    public partial class MainPage : PhoneApplicationPage
    {
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
        
        // Constructor
        public MainPage()
        {
            InitializeComponent();

            DPSBase.DPSManager.AddDataProcessor<SevenZipLZMACompressor.LZMACompressor>();
            
            //Start listening for new incoming TCP connections
            //Parameters is true so that we listen on a random port if the default is not available
            TCPConnection.StartListening(true);

            //Write the IP addresses and ports that we are listening on to the chatBox
            chatBox.Text += "Initialising WPF chat example\n Accepting connections on:\n";
            foreach (var listenEndPoint in TCPConnection.ExistingLocalListenEndPoints())
                chatBox.Text += listenEndPoint.Address + ":" + listenEndPoint.Port + "\n";

            //Add a blank line after the initialisation output
            chatBox.Text += "\n";
                        
            //Configure NetworkCommsDotNet to handle and incoming packet of type 'ChatMessage'
            //e.g. If we recieve a packet of type 'ChatMessage' execute the method 'HandleIncomingChatMessage'
            NetworkComms.AppendGlobalIncomingPacketHandler<ChatMessage>("ChatMessage", HandleIncomingChatMessage);

            //Configure NetworkCommsDotNet to perform an action when a connection is closed
            //e.g. When a connection is closed execute the method 'HandleConnectionClosed'
            NetworkComms.AppendGlobalConnectionCloseHandler(HandleConnectionClosed);
        }

        private void SendMessage(string toSend)
        {
            //If we have tried to send a zero length string we just return
            if (toSend.Trim() == "") return;

            //We may or may not have entered some master connection information
            ConnectionInfo masterConnectionInfo = null;
            if ((App.Current as App).MasterIPAddress != "")
            {
                try { masterConnectionInfo = new ConnectionInfo((App.Current as App).MasterIPAddress, (App.Current as App).MasterPort); }
                catch (Exception)
                {
                    MessageBox.Show("Failed to parse the master IP and port. Please ensure it is correct and try again", "Master IP & Port Parse Error", MessageBoxButton.OK);
                    return;
                }
            }

            //We wrap everything we want to send in the ChatMessage class we created
            ChatMessage messageToSend = new ChatMessage(NetworkComms.NetworkIdentifier, (App.Current as App).LocalName, toSend, messageSendIndex++);

            //We add our own message to the message history incase it gets relayed back to us
            lock (lastPeerMessageDict) lastPeerMessageDict[NetworkComms.NetworkIdentifier] = messageToSend;

            //We write our own message to the chatBox
            AppendLineToChatBox(messageToSend.SourceName + " - " + messageToSend.Message);

            //Clear the input box text
            CurrentMessageInputBox.Text = "";

            //If we provided master information we send to the master first
            if (masterConnectionInfo != null)
            {
                //We perform the send within a try catch to ensure the application continues to run if there is a problem.
                try { TCPConnection.GetConnection(masterConnectionInfo).SendObject("ChatMessage", messageToSend); }
                catch (CommsException) { MessageBox.Show("A CommsException occured while trying to send message to " + masterConnectionInfo, "CommsException", MessageBoxButton.OK); }
            }

            //If we have any other connections we now send the message to those as well
            //This ensures that if we are the master everyone who is connected to us gets our message
            var otherConnectionInfos = (from current in NetworkComms.AllConnectionInfo() where current != masterConnectionInfo select current).ToArray();
            foreach (ConnectionInfo info in otherConnectionInfos)
            {
                //We perform the send within a try catch to ensure the application continues to run if there is a problem.
                try { TCPConnection.GetConnection(info).SendObject("ChatMessage", messageToSend); }
                catch (CommsException) { MessageBox.Show("A CommsException occured while trying to send message to " + info, "CommsException", MessageBoxButton.OK); }
            }

            return;
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
                chatBox.Text += messageToAdd + "\n";
                ChatBoxScroller.ScrollToVerticalOffset(ChatBoxScroller.ScrollableHeight);
            }), new object[] { message });
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

        private void ApplicationBarMenuItem_Click_1(object sender, EventArgs e)
        {
            NavigationService.Navigate(new Uri("//SettingsPage.xaml", UriKind.Relative));
        }

        private void CurrentMessageInputBox_KeyDown_1(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var textBox = sender as TextBox;

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SendMessage(textBox.Text);
            }
        }        
    }
}