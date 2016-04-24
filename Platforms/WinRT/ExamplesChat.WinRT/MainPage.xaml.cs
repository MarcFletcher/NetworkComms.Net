// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Basic Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234237

namespace Examples.ExamplesChat.WinRT
{
    /// <summary>
    /// A basic page that provides characteristics common to most applications.
    /// </summary>
    public sealed partial class MainPage : ExamplesChat.WinRT.Common.LayoutAwarePage
    {
        /// <summary>
        /// An instance of the chat application
        /// </summary>
        ChatAppWinRT chatApplication;

        public MainPage()
        {
            this.InitializeComponent();

            //Initialise the chat application
            chatApplication = new ChatAppWinRT(currentMessageInputBox, chatBox, chatBoxScroller);

            //Set the localName text to the hostname
            chatApplication.LocalName = HostInfo.HostName + "_WinRT";
            localNameBox.Text = HostInfo.HostName + "_WinRT";

            //Set the initial serializer
            chatApplication.Serializer = NetworkCommsDotNet.DPSBase.DPSManager.GetDataSerializer<NetworkCommsDotNet.DPSBase.ProtobufSerializer>();

            //Print out some usage instructions
            chatApplication.PrintUsageInstructions();

            //Initialise the NetworkComms.Net settings
            chatApplication.RefreshNetworkCommsConfiguration();

            Application.Current.Suspending += Application_Suspending;
        }

        void Application_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            NetworkComms.Shutdown();
        }

        /// <summary>
        /// Populates the page with content passed during navigation.  Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="navigationParameter">The parameter value passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested.
        /// </param>
        /// <param name="pageState">A dictionary of state preserved by this page during an earlier
        /// session.  This will be null the first time a page is visited.</param>
        protected override void LoadState(Object navigationParameter, Dictionary<String, Object> pageState)
        {
            if (pageState != null)
            {
                chatBox.Text = pageState["ChatHistory"] as string;
                currentMessageInputBox.Text = pageState["CurrentMessage"] as string;
                remoteIPBox.Text = pageState["RemoteIP"] as string;
                remotePortBox.Text = pageState["RemotePort"] as string;
                localNameBox.Text = pageState["LocalName"] as string;
                
                if (pageState["TCP/UDP"] as string == "TCP")
                {
                    TCPRadioButton.IsChecked = true;
                    UDPRadioButton.IsChecked = false;
                }
                else
                {
                    TCPRadioButton.IsChecked = false;
                    UDPRadioButton.IsChecked = true;
                }

                encryptionBox.IsChecked = (bool)pageState["EncryptionEnabled"];
                enableServerBox.IsChecked = (bool)pageState["ServerEnabled"];
            }
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="pageState">An empty dictionary to be populated with serializable state.</param>
        protected override void SaveState(Dictionary<String, Object> pageState)
        {
            pageState["ChatHistory"] = chatBox.Text;
            pageState["CurrentMessage"] = currentMessageInputBox.Text;
            pageState["RemoteIP"] = remoteIPBox.Text;
            pageState["RemotePort"] = remotePortBox.Text;
            pageState["LocalName"] = localNameBox.Text;
            pageState["TCP/UDP"] = (bool)TCPRadioButton.IsChecked ? "TCP" : "UDP";
            pageState["EncryptionEnabled"] = (bool)encryptionBox.IsChecked;
            pageState["ServerEnabled"] = (bool)enableServerBox.IsChecked;
        }

        /// <summary>
        /// If the key pressed is enter then send the completed message
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CurrentMessageInputBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                chatApplication.SendMessage(currentMessageInputBox.Text);
        }

        /// <summary>
        /// Update the remote IP information when focus is lost
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void remoteIPBox_LostFocus(object sender, RoutedEventArgs e)
        {
           chatApplication.ServerIPAddress = remoteIPBox.Text.Trim();
        }

        /// <summary>
        /// Update the remote port information when focus is lost
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void remotePortBox_LostFocus(object sender, RoutedEventArgs e)
        {
            int portNumber;
            if (int.TryParse(remotePortBox.Text, out portNumber))
                chatApplication.ServerPort = portNumber;
            else
                remotePortBox.Text = "";
        }

        /// <summary>
        /// Update the local name information when focus is lost
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void localNameBox_LostFocus(object sender, RoutedEventArgs e)
        {            
            chatApplication.LocalName = localNameBox.Text.Trim();
        }

        /// <summary>
        /// Switch to TCP mode when the TCP radio button is highlighted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TCPRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (TCPRadioButton != null && TCPRadioButton.IsChecked != null && (bool)TCPRadioButton.IsChecked)
            {
                //Update the application and connectionType                
                chatApplication.ConnectionType = ConnectionType.TCP;
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        /// <summary>
        /// Switch to UDP mode when the UDP radio button is highlighted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UDPRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (UDPRadioButton != null && UDPRadioButton.IsChecked != null && (bool)UDPRadioButton.IsChecked)
            {
                //Update the application and connectionType                
                chatApplication.ConnectionType = ConnectionType.UDP;
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        /// <summary>
        /// Switch to JSON serializer when the JSON radio button is highlighted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void JSONRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (JSONRadioButton != null && JSONRadioButton.IsChecked != null && (bool)JSONRadioButton.IsChecked)
            {
                //Update the application and connectionType                
                chatApplication.Serializer = NetworkCommsDotNet.DPSBase.DPSManager.GetDataSerializer<NetworkCommsDotNet.DPSBase.JSONSerializer>();
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        /// <summary>
        /// Switch to Protobuf serializer when the Protobuf radio button is highlighted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ProtobufRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (ProtobufRadioButton != null && ProtobufRadioButton.IsChecked != null && (bool)ProtobufRadioButton.IsChecked)
            {
                //Update the application and connectionType                
                chatApplication.Serializer = NetworkCommsDotNet.DPSBase.DPSManager.GetDataSerializer<NetworkCommsDotNet.DPSBase.ProtobufSerializer>();
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        /// <summary>
        /// Enable encryption if it is selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void encryptionBox_Checked(object sender, RoutedEventArgs e)
        {
            chatApplication.EncryptionEnabled = (bool)encryptionBox.IsChecked;
            chatApplication.RefreshNetworkCommsConfiguration();
        }

        /// <summary>
        /// Enable and disable the local server mode when selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void enableServerBox_CheckedUnChecked(object sender, RoutedEventArgs e)
        {
            chatApplication.LocalServerEnabled = (bool)enableServerBox.IsChecked;
            chatApplication.RefreshNetworkCommsConfiguration();
        }        
    }
}
