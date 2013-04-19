using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using System.Net;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.IO;

namespace ExamplesChat.Android
{
    [Activity(Label = "ExamplesChat.Android", MainLauncher = true, Icon = "@drawable/icon")]
    public class Activity1 : Activity
    {
        #region Private Fields
        //The class used for chat functionality
        //This helps keep the networking code independent of the GUI
        ChatAppAndroid chatApplication;

        /// <summary>
        /// The chat history window. This is where all of the messages get displayed
        /// </summary>
        TextView chatHistory;

        /// <summary>
        /// The send button
        /// </summary>
        Button sendButton;
        
        /// <summary>
        /// The input box where new messages are entered
        /// </summary>
        AutoCompleteTextView input;

        /// <summary>
        /// The texbox containing the master ip address (server)
        /// </summary>
        AutoCompleteTextView ipTextBox;

        /// <summary>
        /// The texbox containing the master port number (server)
        /// </summary>
        AutoCompleteTextView portTextBox;

        /// <summary>
        /// The spinner (drop down) menu for selecting the connection type to use
        /// </summary>
        Spinner connectionTypeSelector;
        #endregion

        /// <summary>
        /// Method runs after the application has been launched
        /// </summary>
        /// <param name="bundle"></param>
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            //Get references to user interface controls
            connectionTypeSelector = FindViewById<Spinner>(Resource.Id.connectionTypeSpinner);
            chatHistory = FindViewById<TextView>(Resource.Id.mainText);
            input = FindViewById<AutoCompleteTextView>(Resource.Id.messageTextInput);
            ipTextBox = FindViewById<AutoCompleteTextView>(Resource.Id.ipTextInput);
            portTextBox = FindViewById<AutoCompleteTextView>(Resource.Id.portTextInput);
            sendButton = FindViewById<Button>(Resource.Id.sendButton);

            //Append the method 'sendButton_Click' to the button click event
            sendButton.Click += sendButton_Click;

            //Set the connection type selection drop down options
            ArrayAdapter adapter = ArrayAdapter.CreateFromResource(this, Resource.Array.ConnectionTypes, global::Android.Resource.Layout.SimpleSpinnerItem);
            adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            connectionTypeSelector.Adapter = adapter;

            //Append the method 'connectionType_Selected' to the connection type selected event
            connectionTypeSelector.ItemSelected += connectionType_Selected;

            //Uncomment this line to enable logging
            //EnableLogging();

            //Initialise the chat application
            chatApplication = new ChatAppAndroid(this, chatHistory, input);
            chatApplication.MasterIPAddress = "";
            chatApplication.MasterPort = 10000;
            chatApplication.UseEncryption = false;

            //Initialise NetworkComms.Net
            chatApplication.InitialiseNetworkComms();
        }

        /// <summary>
        /// Event triggered when the send button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void sendButton_Click(object sender, EventArgs e)
        {
            //Parse the ip address box
            IPAddress newMasterIPAddress;
            if (!IPAddress.TryParse(ipTextBox.Text, out newMasterIPAddress))
                //If the parse failed set the ipTextBox back to the the previous good value
                ipTextBox.Text = chatApplication.MasterIPAddress;
            else
                chatApplication.MasterIPAddress = newMasterIPAddress.ToString();

            //Parse the port number
            int newPort;
            if (!int.TryParse(portTextBox.Text, out newPort) || newPort < 1 || newPort > ushort.MaxValue)
                //If the parse failed we set the portTextBox back to the previous good value
                portTextBox.Text = chatApplication.MasterPort.ToString();
            else 
                chatApplication.MasterPort = newPort;

            //Send the text entered in the input box
            chatApplication.SendMessage(input.Text);
        }

        /// <summary>
        /// Checks if the selected connection type has changed. If changed reset the example to use the new connection type.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void connectionType_Selected(object sender, EventArgs e)
        {
            bool connectionTypeChanged = false;

            //Parse the connection type
            string selectedItem = connectionTypeSelector.SelectedItem.ToString();
            if (selectedItem == "TCP" && chatApplication.ConnectionType == NetworkCommsDotNet.ConnectionType.UDP)
            {
                connectionTypeChanged = true;
                chatApplication.ConnectionType = NetworkCommsDotNet.ConnectionType.TCP;
            }
            else if (selectedItem == "UDP" && chatApplication.ConnectionType == NetworkCommsDotNet.ConnectionType.TCP)
            {
                connectionTypeChanged = true;
                chatApplication.ConnectionType = NetworkCommsDotNet.ConnectionType.UDP;
            }

            //If the connection type has been changed we reset NetworkComms.Net
            //NetworkComms.Net can support both connection types simultaneously but for the purposes
            //of the example we only want to use one at a time
            if (connectionTypeChanged)
            {
                //Shutdown NetworkComms.Net
                NetworkCommsDotNet.NetworkComms.Shutdown();

                //Clear any previous chat history
                chatApplication.ClearChatHistory();

                //Initialise network comms using the new connection type
                chatApplication.InitialiseNetworkComms();
            }
        }

        /// <summary>
        /// Enable NetworkComms.Net logging
        /// </summary>
        void EnableLogging()
        {
            var sdCardDir = global::Android.OS.Environment.ExternalStorageDirectory.AbsolutePath;
            var commsDir = Path.Combine(sdCardDir, "NetworkComms");

            if(!Directory.Exists(commsDir))
                Directory.CreateDirectory(commsDir);
            
            var logFileName = Path.Combine(commsDir, "log.txt");

            chatHistory.Text += "\n" + "Logging enabled to " + logFileName;
                
            NetworkCommsDotNet.NetworkComms.EnableLogging(logFileName);
        }
    }
}

