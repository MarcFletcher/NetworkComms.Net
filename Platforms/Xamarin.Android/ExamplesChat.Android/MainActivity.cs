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
using Android.Preferences;
using NetworkCommsDotNet.DPSBase;

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
        /// The button to bring up settings menu
        /// </summary>
        Button settingsButton;

        /// <summary>
        /// The input box where new messages are entered
        /// </summary>
        AutoCompleteTextView input;

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

            ISharedPreferences preferences = PreferenceManager.GetDefaultSharedPreferences(ApplicationContext);
            ISharedPreferencesEditor editor = preferences.Edit();
            editor.Clear();
            editor.Commit();

            //Get references to user interface controls
            chatHistory = FindViewById<TextView>(Resource.Id.mainText);
            input = FindViewById<AutoCompleteTextView>(Resource.Id.messageTextInput);
            sendButton = FindViewById<Button>(Resource.Id.sendButton);
            settingsButton = FindViewById<Button>(Resource.Id.settingsButton);

            //Append the method 'sendButton_Click' to the button click event
            sendButton.Click += sendButton_Click;

            //Append the settings button event handler
            settingsButton.Click += settingsButton_Click;

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
        /// Event triggered when the send button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void sendButton_Click(object sender, EventArgs e)
        {  
            //Send the text entered in the input box
            chatApplication.SendMessage(input.Text);
        }

        void settingsButton_Click(object sender, EventArgs e)
        {
            // TODO Auto-generated method stub
            Intent i = new Intent(ApplicationContext, typeof(NetworkCommsSettings));// UserSettingActivity.class);
            StartActivityForResult(i, 0x4655676);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == 0x4655676)
            {
                ISharedPreferences sharedPrefs = PreferenceManager.GetDefaultSharedPreferences(ApplicationContext);
                chatApplication.LocalName = sharedPrefs.GetString("prefName", "Android");
                chatApplication.ServerIPAddress = sharedPrefs.GetString("prefIPAddress", "");

                string portString = sharedPrefs.GetString("prefPort", "");
                if (portString != String.Empty)
                    chatApplication.ServerPort = int.Parse(portString);

                chatApplication.ConnectionType = (ConnectionType)Enum.Parse(typeof(ConnectionType), sharedPrefs.GetString("prefConnectionType", "TCP"));

                string serializerPref = sharedPrefs.GetString("prefSerializerType", "Protobuf");

                switch (serializerPref)
                {
                    case "Protobuf":
                        chatApplication.Serializer = DPSManager.GetDataSerializer<NetworkCommsDotNet.DPSBase.ProtobufSerializer>();
                        break;
                    default:
                        break;
                }

                chatApplication.EncryptionEnabled = sharedPrefs.GetBoolean("prefEncryption", false);
                chatApplication.LocalServerEnabled = sharedPrefs.GetBoolean("prefEnableLocalServer", false);

                chatApplication.RefreshNetworkCommsConfiguration();
            }

        }

        #endregion
    }
}

