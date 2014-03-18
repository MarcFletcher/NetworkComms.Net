//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

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
using Examples.ExamplesChat.WP8.Resources;

namespace Examples.ExamplesChat.WP8
{
    public partial class MainPage : PhoneApplicationPage
    {
        public MainPage()
        {
            InitializeComponent();

            //Initialise the chat application instance here as we have easy access to the GUI fields
            (App.Current as App).ChatApplication = new ChatAppWP8(CurrentMessageInputBox, chatBox, ChatBoxScroller);

            //Print out the usage instructions
            (App.Current as App).ChatApplication.PrintUsageInstructions();

            //Refresh the network configuration to ensure we are ready to send messages
            (App.Current as App).ChatApplication.RefreshNetworkCommsConfiguration();

#if DEBUG
            //Set debug timeouts
            SetDebugTimeouts();
#endif
        }

        /// <summary>
        /// Switch to the settings page when selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ApplicationBarMenuItem_Click_1(object sender, EventArgs e)
        {
            NavigationService.Navigate(new Uri("//SettingsPage.xaml", UriKind.Relative));
        }

        /// <summary>
        /// Send a message when the user presses enter/return
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CurrentMessageInputBox_KeyDown_1(object sender, System.Windows.Input.KeyEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (e.Key == System.Windows.Input.Key.Enter)
                (App.Current as App).ChatApplication.SendMessage(textBox.Text);
        }

        /// <summary>
        /// Increase default timeouts so that we can easily step through code when running the examples in debug mode.
        /// </summary>
        private static void SetDebugTimeouts()
        {
            NetworkComms.ConnectionEstablishTimeoutMS = int.MaxValue;
            NetworkComms.PacketConfirmationTimeoutMS = int.MaxValue;
            NetworkComms.ConnectionAliveTestTimeoutMS = int.MaxValue;
        }
    }
}