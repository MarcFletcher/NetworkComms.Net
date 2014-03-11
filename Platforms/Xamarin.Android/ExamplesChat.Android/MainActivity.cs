using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using System.Net;
using System.Collections.Generic;
using System.IO;
using NetworkCommsDotNet.Connections;

namespace ExamplesChat.Android
{
    [Activity(Label = "ExamplesChat.Android", MainLauncher = true, Icon = "@drawable/icon")]
    public class Activity1 : Activity
    {
        #region Private Fields
        /// <summary>
        /// An instance of the chat applications
        /// </summary>
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

        /// <summary>
        /// The checkbox which can be used to enable local server mode
        /// </summary>
        CheckBox enableLocalServerCheckBox;
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
            enableLocalServerCheckBox = FindViewById<CheckBox>(Resource.Id.enableLocalServer);

            //Set the connection type selection drop down options
            ArrayAdapter adapter = ArrayAdapter.CreateFromResource(this, Resource.Array.ConnectionTypes, global::Android.Resource.Layout.SimpleSpinnerItem);
            adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            connectionTypeSelector.Adapter = adapter;

            //Append the method 'connectionType_Selected' to the connection type selected event
            connectionTypeSelector.ItemSelected += connectionType_Selected;

            //Append the method 'sendButton_Click' to the button click event
            sendButton.Click += sendButton_Click;

            //Append the method 'enableLocalServerCheckBox_CheckedChange' when the enable
            //local server checkbox state is changed
            enableLocalServerCheckBox.CheckedChange += enableLocalServerCheckBox_CheckedChange;

            //Initialise the chat application
            chatApplication = new ChatAppAndroid(this, chatHistory, input);

            //Print the usage instructions
            chatApplication.PrintUsageInstructions();

            //Initialise NetworkComms.Net but without a local server
            chatApplication.RefreshNetworkCommsConfiguration();

            //Uncomment this line to enable logging
            //EnableLogging();
        }

        /// <summary>
        /// Enable NetworkComms.Net logging. Useful for debugging.
        /// </summary>
        void EnableLogging()
        {
            //We will create the log file in the root external storage directory
            string sdCardDir = global::Android.OS.Environment.ExternalStorageDirectory.AbsolutePath;
            string logFileName = Path.Combine(sdCardDir, "NetworkCommsLog.txt");

            chatApplication.AppendLineToChatHistory(System.Environment.NewLine + "Logging enabled to " + logFileName);

            NetworkCommsDotNet.Tools.ILogger logger = new NetworkCommsDotNet.Tools.LiteLogger(NetworkCommsDotNet.Tools.LiteLogger.LogMode.LogFileOnly, logFileName);
            NetworkCommsDotNet.NetworkComms.EnableLogging(logger);
        }

        #region Event Handlers
        /// <summary>
        /// Event triggered when the enable local server checkbox is changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void enableLocalServerCheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
        {
            //Update the local server enabled state and then refresh the network configuration
            chatApplication.LocalServerEnabled = enableLocalServerCheckBox.Checked;
            chatApplication.RefreshNetworkCommsConfiguration();
        }

        /// <summary>
        /// Event triggered when the send button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void sendButton_Click(object sender, EventArgs e)
        {
            //Parse the ip address box
            if (ipTextBox.Text != "")
            {
                IPAddress newMasterIPAddress;
                if (IPAddress.TryParse(ipTextBox.Text, out newMasterIPAddress))
                    //If the parse was successful we can update the chat application
                    chatApplication.ServerIPAddress = newMasterIPAddress.ToString();
                else
                    //If the parse failed set the ipTextBox back to the the previous good value
                    ipTextBox.Text = chatApplication.ServerIPAddress;
            }
            else
                //If no server IP has been entered we ensure the chat application has a blank address
                chatApplication.ServerIPAddress = "";

            //Parse the port number
            if (portTextBox.Text != "")
            {
                int newPort;
                bool portParseResult = int.TryParse(portTextBox.Text, out newPort);
                if (!portParseResult || newPort < 1 || newPort > ushort.MaxValue)
                    //If the parse failed we set the portTextBox back to the previous good value
                    portTextBox.Text = chatApplication.ServerPort.ToString();
                else
                    chatApplication.ServerPort = newPort;
            }
            else
                chatApplication.ServerPort = -1;

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
            //Parse the connection type
            string selectedItem = connectionTypeSelector.SelectedItem.ToString();
            if (selectedItem == "TCP")
                chatApplication.ConnectionType = ConnectionType.TCP;
            else if (selectedItem == "UDP")
                chatApplication.ConnectionType = ConnectionType.UDP;

            //Update the NetworkComms.Net configuration
            chatApplication.RefreshNetworkCommsConfiguration();
        }
        #endregion
    }
}

