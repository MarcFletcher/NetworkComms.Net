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