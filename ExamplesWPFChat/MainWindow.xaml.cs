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
        /// Dictionary to keep track of which peer messages have already been written to the chat window
        /// </summary>
        Dictionary<ShortGuid, ChatMessage> lastPeerMessageDict = new Dictionary<ShortGuid, ChatMessage>();

        /// <summary>
        /// The maximum number of times a chat message will be relayed
        /// </summary>
        int relayMaximum = 3;

        /// <summary>
        /// An encryption key to use should one be required
        /// </summary>
        string encryptionKey = "123456789s";

        /// <summary>
        /// A local counter used to track the number of messages sent
        /// </summary>
        long messageSendIndex = 0;

        public MainWindow()
        {
            InitializeComponent();

            //Start listening for incoming connections and print IPs to chatBox
            chatBox.AppendText("Initialising WPF chat example. Accepting connections on:\n");
            TCPConnection.StartListening(true);
            foreach (var listenEndPoint in TCPConnection.ExistingLocalListenEndPoints())
                chatBox.AppendText(listenEndPoint.Address + ":" + listenEndPoint.Port + "\n");
            
            //Add a blank line after the initialisation output
            chatBox.AppendText("\n");

            //Set the default username to the local host
            localName.Text = NetworkComms.HostName;

            //Configure incoming message packetHandler
            NetworkComms.AppendGlobalIncomingPacketHandler<ChatMessage>("ChatMessage", HandleIncomingChatMessage);

            //Configure connection close handler
            NetworkComms.AppendGlobalConnectionCloseHandler(HandleConnectionClosed);
        }

        private void AppendLineToChatBox(string message)
        {
            //Invoke the text append from within the wpf thread
            chatBox.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
            {
                chatBox.AppendText(messageToAdd + "\n");
                chatBox.ScrollToEnd();
            }), new object[] { message });
        }

        private void UpdateOtherUsers()
        {
            lock (lastPeerMessageDict)
            {
                string[] currentUsers = (from current in lastPeerMessageDict.Values orderby current.SourceName select current.SourceName).ToArray();

                //Invoke the text append from within the wpf thread
                this.messagesFrom.Dispatcher.BeginInvoke(new Action<string[]>((users) =>
                {
                    messagesFrom.Text = "";
                    foreach (var username in users)
                        messagesFrom.AppendText(username + "\n");
                }), new object[] { currentUsers });
            }
        }

        private void HandleConnectionClosed(Connection connection)
        {
            lock (lastPeerMessageDict)
            {
                ShortGuid remoteIdentifier = connection.ConnectionInfo.NetworkIdentifier;
                if (lastPeerMessageDict.ContainsKey(remoteIdentifier))
                    AppendLineToChatBox("Connection with '" + lastPeerMessageDict[remoteIdentifier].SourceName + "' has been closed.");
                else
                    AppendLineToChatBox("Connection with '" + connection.ToString() + "' has been closed.");

                lastPeerMessageDict.Remove(connection.ConnectionInfo.NetworkIdentifier);
            }

            UpdateOtherUsers();
        }

        private void HandleIncomingChatMessage(PacketHeader header, Connection connection, ChatMessage incomingMessage)
        {
            //We only want to write every unique message once
            //Depending on packet sequence number write message to chatBox
            lock (lastPeerMessageDict)
            {
                if (lastPeerMessageDict.ContainsKey(incomingMessage.SourceIdentifier))
                {
                    if (lastPeerMessageDict[incomingMessage.SourceIdentifier].MessageIndex < incomingMessage.MessageIndex)
                    {
                        AppendLineToChatBox(incomingMessage.SourceName + " - " + incomingMessage.Message);
                        lastPeerMessageDict[incomingMessage.SourceIdentifier] = incomingMessage;
                    }
                }
                else
                {
                    lastPeerMessageDict.Add(incomingMessage.SourceIdentifier, incomingMessage);
                    AppendLineToChatBox(incomingMessage.SourceName + " - " + incomingMessage.Message);
                }
            }

            UpdateOtherUsers();

            //Increment relaycount
            var allRelayConnections = (from current in NetworkComms.GetExistingConnection() where current != connection select current).ToArray();

            //Possibly relay to other peers
            if (incomingMessage.RelayCount < relayMaximum)
            {
                incomingMessage.IncrementRelayCount();
                foreach (var relayConnection in allRelayConnections)
                {
                    try { relayConnection.SendObject("ChatMessage", incomingMessage); }
                    catch (CommsException) { /* Catch the exception, ignore and continue */ }
                }
            }
        }

        private void SendMessage()
        {
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

            ChatMessage messageToSend = new ChatMessage(NetworkComms.NetworkIdentifier, localName.Text, messageText.Text, messageSendIndex++);

            //We add our own message to the dict incase it gets relayed back round to us
            lock (lastPeerMessageDict) lastPeerMessageDict[NetworkComms.NetworkIdentifier] = messageToSend;

            UpdateOtherUsers();
            AppendLineToChatBox(messageToSend.SourceName + " - " + messageToSend.Message);
            this.messageText.Text = "";

            //If we have master information we send to the master
            if (masterConnectionInfo != null)
            {
                try { TCPConnection.GetConnection(masterConnectionInfo).SendObject("ChatMessage", messageToSend); }
                catch (CommsException) { MessageBox.Show("A CommsException occured while trying to send message to " + masterConnectionInfo, "CommsException", MessageBoxButton.OK); }
            }

            //If we have any other connections we send to those as well
            var otherConnectionInfos = (from current in NetworkComms.AllConnectionInfo() where current != masterConnectionInfo select current).ToArray();
            foreach (ConnectionInfo info in otherConnectionInfos)
            {
                try { TCPConnection.GetConnection(info).SendObject("ChatMessage", messageToSend); }
                catch (CommsException) { MessageBox.Show("A CommsException occured while trying to send message to " + info, "CommsException", MessageBoxButton.OK); }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Ensure we shutdown comms when we are finished
            NetworkComms.Shutdown();
        }

        private void sendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void messageText_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
                SendMessage();
        }

        private void useEncryptionBox_Checked(object sender, RoutedEventArgs e)
        {
            NetworkComms.RemoveGlobalIncomingPacketHandler("ChatMessage");
            Dictionary<string, string> options = new Dictionary<string, string>();
            RijndaelPSKEncrypter.AddPasswordToOptions(options, encryptionKey);
            NetworkComms.DefaultSendReceiveOptions = new SendReceiveOptions<ProtobufSerializer, LZMACompressor, RijndaelPSKEncrypter>(options);
            NetworkComms.AppendGlobalIncomingPacketHandler<ChatMessage>("ChatMessage", HandleIncomingChatMessage);
        }

        private void useEncryptionBox_Unchecked(object sender, RoutedEventArgs e)
        {
            NetworkComms.RemoveGlobalIncomingPacketHandler("ChatMessage");
            NetworkComms.DefaultSendReceiveOptions = new SendReceiveOptions<ProtobufSerializer, LZMACompressor>();
            NetworkComms.AppendGlobalIncomingPacketHandler<ChatMessage>("ChatMessage", HandleIncomingChatMessage);
        }
    }
}
