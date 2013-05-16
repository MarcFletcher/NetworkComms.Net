using System;
using System.Linq;
using System.Diagnostics;
using System.Resources;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using ExamplesWP8Chat.Resources;
using NetworkCommsDotNet;
using System.Windows.Controls;
using System.Collections.Generic;
using DPSBase;

namespace ExamplesWP8Chat
{
    public partial class App : Application
    {
        /// <summary>
        /// Provides easy access to the root frame of the Phone Application.
        /// </summary>
        /// <returns>The root frame of the Phone Application.</returns>
        public static PhoneApplicationFrame RootFrame { get; private set; }

        /// <summary>
        /// The type of connection currently used to send and recieve messages. Default is TCP.
        /// </summary>
        public ConnectionType ConnectionType { get; set; }

        /// <summary>
        /// A boolean used to track the very first initialisation
        /// </summary>
        public bool FirstInitialisation { get; set; }

        public bool LocalSeverEnabled { get; set; }
        public string ServerIPAddress { get; set; }
        public int ServerPort { get; set; }
        public string LocalName { get; set; }
        public bool UseEncryption { get; set; }

        /// <summary>
        /// A global link to the chatBox
        /// </summary>
        public TextBlock ChatBox { get; set; }

        /// <summary>
        /// A global link to the scroller
        /// </summary>
        public ScrollViewer ChatBoxScroller { get; set; }

        /// <summary>
        /// A global link to the input box
        /// </summary>
        public TextBox CurrentMessageInputBox { get; set; }

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

        /// <summary>
        /// Constructor for the Application object.
        /// </summary>
        public App()
        {
            // Global handler for uncaught exceptions.
            UnhandledException += Application_UnhandledException;

            // Standard XAML initialization
            InitializeComponent();

            // Phone-specific initialization
            InitializePhoneApplication();

            // Language display initialization
            InitializeLanguage();

            LocalSeverEnabled = false;
            ServerIPAddress = "";
            ServerPort = 10000;
            LocalName = "WindowsPhone";
            UseEncryption = false;

            // Show graphics profiling information while debugging.
            if (Debugger.IsAttached)
            {
                // Display the current frame rate counters.
                Application.Current.Host.Settings.EnableFrameRateCounter = true;

                // Show the areas of the app that are being redrawn in each frame.
                //Application.Current.Host.Settings.EnableRedrawRegions = true;

                // Enable non-production analysis visualization mode,
                // which shows areas of a page that are handed off to GPU with a colored overlay.
                //Application.Current.Host.Settings.EnableCacheVisualization = true;

                // Prevent the screen from turning off while under the debugger by disabling
                // the application's idle detection.
                // Caution:- Use this under debug mode only. Application that disables user idle detection will continue to run
                // and consume battery power when the user is not using the phone.
                PhoneApplicationService.Current.UserIdleDetectionMode = IdleDetectionMode.Disabled;
            }

            //Initialise the default values
            ConnectionType = NetworkCommsDotNet.ConnectionType.TCP;
            FirstInitialisation = true;
        }

        /// <summary>
        /// Initialise networkComms.Net. We default to using TCP
        /// </summary>
        public void InitialiseNetworkComms()
        {
            if ((App.Current as App).ConnectionType == ConnectionType.TCP)
            {
                //Start listening for new incoming TCP connections
                //Parameter is true so that we listen on a random port if the default is not available
                TCPConnection.StartListening(true);

                //Write the IP addresses and ports that we are listening on to the chatBox
                AppendLineToChatBox("Enabled local server mode.\nListening for incoming TCP connections on:");
                foreach (var listenEndPoint in TCPConnection.ExistingLocalListenEndPoints())
                    AppendLineToChatBox(listenEndPoint.Address + ":" + listenEndPoint.Port);
            }
            else if ((App.Current as App).ConnectionType == ConnectionType.UDP)
            {
                //Start listening for new incoming UDP connections
                //Parameter is true so that we listen on a random port if the default is not available
                UDPConnection.StartListening(true);

                //Write the IP addresses and ports that we are listening on to the chatBox
                AppendLineToChatBox("Enabled local server mode.\nListening for incoming UDP connections on:");
                foreach (var listenEndPoint in UDPConnection.ExistingLocalListenEndPoints())
                   AppendLineToChatBox(listenEndPoint.Address + ":" + listenEndPoint.Port);
            }
            else
                AppendLineToChatBox("Error: Unable to initialise comms as an invalid connectionType was set.");

            //Add a blank line after the initialisation output
            AppendLineToChatBox("");

            //We only need to add the packet handlers once. If we change connection type calling NetworkComms.Shutdown() does not remove these.
            if ((App.Current as App).FirstInitialisation)
            {
                (App.Current as App).FirstInitialisation = false;

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
        public void PrintUsageInstructions()
        {
            AppendLineToChatBox("WPF chat usage instructions:");
            AppendLineToChatBox("");
            AppendLineToChatBox("Step 1. Open two chat applications. Other applications could be android or iOS versions.");
            AppendLineToChatBox("Step 2. Enable local server mode in a single application, see settings.");
            AppendLineToChatBox("Step 3. Provide remote server IP and port information in settings on remaining application.");
            AppendLineToChatBox("Step 4. Start chatting.");
            AppendLineToChatBox("");

            for (int i = 0; i < 5; i++)
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
            if ((App.Current as App).ServerIPAddress != "")
            {
                try { masterConnectionInfo = new ConnectionInfo((App.Current as App).ServerIPAddress, (App.Current as App).ServerPort); }
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
                try
                {
                    if (ConnectionType == ConnectionType.TCP)
                        TCPConnection.GetConnection(masterConnectionInfo).SendObject("ChatMessage", messageToSend);
                    else if (ConnectionType == ConnectionType.UDP)
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
                    if (ConnectionType == ConnectionType.TCP)
                        TCPConnection.GetConnection(info).SendObject("ChatMessage", messageToSend);
                    else if (ConnectionType == ConnectionType.UDP)
                        UDPConnection.GetConnection(info, UDPOptions.None).SendObject("ChatMessage", messageToSend);
                    else
                        throw new Exception("An invalid connectionType is set.");
                }
                catch (CommsException) { MessageBox.Show("A CommsException occured while trying to send message to " + info, "CommsException", MessageBoxButton.OK); }
            }

            return;
        }

        /// <summary>
        /// Append the provided message to the chatBox text box.
        /// </summary>
        /// <param name="message"></param>
        public void AppendLineToChatBox(string message)
        {
            //To ensure we can succesfully append to the text box from any thread
            //we need to wrap the append within an invoke action.
            ChatBox.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
            {
                ChatBox.Text += messageToAdd + "\n";
                ChatBoxScroller.ScrollToVerticalOffset(ChatBoxScroller.ScrollableHeight);
                ChatBoxScroller.UpdateLayout();
            }), new object[] { message });
        }

        // Code to execute when the application is launching (eg, from Start)
        // This code will not execute when the application is reactivated
        private void Application_Launching(object sender, LaunchingEventArgs e)
        {
        }

        // Code to execute when the application is activated (brought to foreground)
        // This code will not execute when the application is first launched
        private void Application_Activated(object sender, ActivatedEventArgs e)
        {
        }

        // Code to execute when the application is deactivated (sent to background)
        // This code will not execute when the application is closing
        private void Application_Deactivated(object sender, DeactivatedEventArgs e)
        {
        }

        // Code to execute when the application is closing (eg, user hit Back)
        // This code will not execute when the application is deactivated
        private void Application_Closing(object sender, ClosingEventArgs e)
        {
            NetworkComms.Shutdown();
        }

        // Code to execute if a navigation fails
        private void RootFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            if (Debugger.IsAttached)
            {
                // A navigation has failed; break into the debugger
                Debugger.Break();
            }
        }

        // Code to execute on Unhandled Exceptions
        private void Application_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            if (Debugger.IsAttached)
            {
                // An unhandled exception has occurred; break into the debugger
                Debugger.Break();
            }
        }

        #region Phone application initialization

        // Avoid double-initialization
        private bool phoneApplicationInitialized = false;

        // Do not add any additional code to this method
        private void InitializePhoneApplication()
        {
            if (phoneApplicationInitialized)
                return;

            // Create the frame but don't set it as RootVisual yet; this allows the splash
            // screen to remain active until the application is ready to render.
            RootFrame = new PhoneApplicationFrame();
            RootFrame.Navigated += CompleteInitializePhoneApplication;

            // Handle navigation failures
            RootFrame.NavigationFailed += RootFrame_NavigationFailed;

            // Handle reset requests for clearing the backstack
            RootFrame.Navigated += CheckForResetNavigation;

            // Ensure we don't initialize again
            phoneApplicationInitialized = true;
        }

        // Do not add any additional code to this method
        private void CompleteInitializePhoneApplication(object sender, NavigationEventArgs e)
        {
            // Set the root visual to allow the application to render
            if (RootVisual != RootFrame)
                RootVisual = RootFrame;

            // Remove this handler since it is no longer needed
            RootFrame.Navigated -= CompleteInitializePhoneApplication;
        }

        private void CheckForResetNavigation(object sender, NavigationEventArgs e)
        {
            // If the app has received a 'reset' navigation, then we need to check
            // on the next navigation to see if the page stack should be reset
            if (e.NavigationMode == NavigationMode.Reset)
                RootFrame.Navigated += ClearBackStackAfterReset;
        }

        private void ClearBackStackAfterReset(object sender, NavigationEventArgs e)
        {
            // Unregister the event so it doesn't get called again
            RootFrame.Navigated -= ClearBackStackAfterReset;

            // Only clear the stack for 'new' (forward) and 'refresh' navigations
            if (e.NavigationMode != NavigationMode.New && e.NavigationMode != NavigationMode.Refresh)
                return;

            // For UI consistency, clear the entire page stack
            while (RootFrame.RemoveBackEntry() != null)
            {
                ; // do nothing
            }
        }

        #endregion

        // Initialize the app's font and flow direction as defined in its localized resource strings.
        //
        // To ensure that the font of your application is aligned with its supported languages and that the
        // FlowDirection for each of those languages follows its traditional direction, ResourceLanguage
        // and ResourceFlowDirection should be initialized in each resx file to match these values with that
        // file's culture. For example:
        //
        // AppResources.es-ES.resx
        //    ResourceLanguage's value should be "es-ES"
        //    ResourceFlowDirection's value should be "LeftToRight"
        //
        // AppResources.ar-SA.resx
        //     ResourceLanguage's value should be "ar-SA"
        //     ResourceFlowDirection's value should be "RightToLeft"
        //
        // For more info on localizing Windows Phone apps see http://go.microsoft.com/fwlink/?LinkId=262072.
        //
        private void InitializeLanguage()
        {
            try
            {
                // Set the font to match the display language defined by the
                // ResourceLanguage resource string for each supported language.
                //
                // Fall back to the font of the neutral language if the Display
                // language of the phone is not supported.
                //
                // If a compiler error is hit then ResourceLanguage is missing from
                // the resource file.
                RootFrame.Language = XmlLanguage.GetLanguage(AppResources.ResourceLanguage);

                // Set the FlowDirection of all elements under the root frame based
                // on the ResourceFlowDirection resource string for each
                // supported language.
                //
                // If a compiler error is hit then ResourceFlowDirection is missing from
                // the resource file.
                FlowDirection flow = (FlowDirection)Enum.Parse(typeof(FlowDirection), AppResources.ResourceFlowDirection);
                RootFrame.FlowDirection = flow;
            }
            catch
            {
                // If an exception is caught here it is most likely due to either
                // ResourceLangauge not being correctly set to a supported language
                // code or ResourceFlowDirection is set to a value other than LeftToRight
                // or RightToLeft.

                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                throw;
            }
        }
    }
}