// This file has been autogenerated from parsing an Objective-C header file added in Xcode.

using System;

using Foundation;
using UIKit;

using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;

namespace ExamplesChat.iOS
{
	public partial class Settings : UIViewController
    {
        public Settings (IntPtr handle) : base (handle)
		{
		}

        /// <summary>
        /// On load set the config as per the chat application
        /// </summary>
        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            //Remove the keyboard on a tap gesture
            var tap = new UITapGestureRecognizer();
            tap.AddTarget(() =>
            {
                this.View.EndEditing(true);
            });
            this.View.AddGestureRecognizer(tap);

            //Get a reference to the chat application
            ChatAppiOS chatApplication = ChatWindow.ChatApplication;

            //Update the settings based on previous values
            LocalServerEnabled.SetState(chatApplication.LocalServerEnabled, false);
            MasterIP.Text = chatApplication.ServerIPAddress;
            MasterPort.Text = chatApplication.ServerPort.ToString();
            LocalName.Text = chatApplication.LocalName;
            EncryptionEnabled.SetState(chatApplication.EncryptionEnabled, false);

            //Set the correct segment on the connection mode toggle
            ConnectionMode.SelectedSegment = (chatApplication.ConnectionType == ConnectionType.TCP ? 0 : 1);
        }

		/// <summary>
		/// Update the settings when the user goes back to the main interface
		/// </summary>
		/// <returns></returns>
		public override void ViewDidDisappear (bool animated)
		{
			//Get a reference to the chat application
			ChatAppiOS chatApplication = ChatWindow.ChatApplication;

			//Parse settings and store back in chat application
			chatApplication.ServerIPAddress = MasterIP.Text.Trim();

			int port = 10000;
			int.TryParse(MasterPort.Text, out port);
			chatApplication.ServerPort = port;

			chatApplication.LocalName = LocalName.Text.Trim();
			chatApplication.EncryptionEnabled = EncryptionEnabled.On;
			chatApplication.LocalServerEnabled = LocalServerEnabled.On;

			if (ConnectionMode.SelectedSegment == 0)
				chatApplication.ConnectionType = ConnectionType.TCP;
			else
				chatApplication.ConnectionType = ConnectionType.UDP;

			//Refresh the NetworkComms.Net configuration once any changes have been made
			chatApplication.RefreshNetworkCommsConfiguration();

			base.ViewDidDisappear (animated);
		}
    }
}
