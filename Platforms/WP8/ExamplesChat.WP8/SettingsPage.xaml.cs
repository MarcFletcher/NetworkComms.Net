//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Connections;

namespace Examples.ExamplesChat.WP8
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
                this.TCPRadioButton.IsChecked = true;
            else
                this.UDPRadioButton.IsChecked = true;

            if (chatApplication.Serializer.GetType() == typeof(ProtobufSerializer))
                this.ProtobufRadioButton.IsChecked = true;
            else
                this.JSONRadioButton.IsChecked = true;
        }
        
        /// <summary>
        /// Update the connectionType if the radio buttons are changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ConnectionType_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as RadioButton).Content.ToString() == "TCP" && (bool)(sender as RadioButton).IsChecked)
                (App.Current as App).ChatApplication.ConnectionType = ConnectionType.TCP;
            else
                (App.Current as App).ChatApplication.ConnectionType = ConnectionType.UDP;
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

        private void SerializationMethod_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as RadioButton).Content.ToString() == "Protobuf" && (bool)(sender as RadioButton).IsChecked)
                (App.Current as App).ChatApplication.Serializer = DPSManager.GetDataSerializer<ProtobufSerializer>();
            else
                (App.Current as App).ChatApplication.Serializer = DPSManager.GetDataSerializer<JSONSerializer>();
        }
    }
}