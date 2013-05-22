using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using DPSBase;
using NetworkCommsDotNet;

namespace ExamplesWP8Chat
{
    public partial class SettingsPage : PhoneApplicationPage
    {
        /// <summary>
        /// Initialisation of the settings page
        /// </summary>
        public SettingsPage()
        {
            InitializeComponent();

            //Set the local controls based on stored values
            ChatAppWP8 chatApplication = (App.Current as App).ChatApplication;

            ServerIPInputBox.Text = chatApplication.ServerIPAddress;
            ServerIPInputBox.Select(ServerIPInputBox.Text.Length, 0);

            ServerPortInputBox.Text = chatApplication.ServerPort.ToString();
            LocalNameInputBox.Text = chatApplication.LocalName;

            UseEncryptionCheckBox.IsChecked = chatApplication.EncryptionEnabled;
            LocalServerEnabled.IsChecked = chatApplication.LocalServerEnabled;

            if (chatApplication.ConnectionType == ConnectionType.TCP)
            {
                this.UseTCP.IsChecked = true;
                this.UseUDP.IsChecked = false;
            }
            else
            {
                this.UseUDP.IsChecked = true;
                this.UseTCP.IsChecked = false;
            }
        }

        /// <summary>
        /// Update the connectionType if the UseUDP connection mode is selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseUDP_Checked(object sender, RoutedEventArgs e)
        {
            //Update the application and connectionType
            this.UseUDP.IsChecked = true;
            this.UseTCP.IsChecked = false;
            (App.Current as App).ChatApplication.ConnectionType = ConnectionType.UDP;
        }

        /// <summary>
        /// Update the connectionType if the UseTCP connection mode is selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseTCP_Checked(object sender, RoutedEventArgs e)
        {
            //Update the application and connectionType
            this.UseTCP.IsChecked = true;
            this.UseUDP.IsChecked = false;
            (App.Current as App).ChatApplication.ConnectionType = ConnectionType.TCP;
        }

        /// <summary>
        /// Update the application settings and refresh NetworkComms.Net configuration when we go back to the main page
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BackKeyPressHandler(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Update the chatApplication values based on new values
            ChatAppWP8 chatApplication = (App.Current as App).ChatApplication;

            chatApplication.LocalServerEnabled = (bool)LocalServerEnabled.IsChecked;
            chatApplication.ServerIPAddress = ServerIPInputBox.Text;
            chatApplication.ServerPort = int.Parse(ServerPortInputBox.Text);
            chatApplication.LocalName = LocalNameInputBox.Text;
            chatApplication.EncryptionEnabled = (bool)UseEncryptionCheckBox.IsChecked;

            //To finish update the configuration with any changes
            chatApplication.RefreshNetworkCommsConfiguration();
        }
    }
}