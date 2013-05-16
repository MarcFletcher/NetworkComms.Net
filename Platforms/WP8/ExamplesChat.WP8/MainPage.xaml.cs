using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using ExamplesWP8Chat.Resources;
using NetworkCommsDotNet;
using DPSBase;

namespace ExamplesWP8Chat
{
    public partial class MainPage : PhoneApplicationPage
    {
        // Constructor
        public MainPage()
        {
            InitializeComponent();
            (App.Current as App).ChatBox = this.chatBox;
            (App.Current as App).ChatBoxScroller = this.ChatBoxScroller;
            (App.Current as App).CurrentMessageInputBox = this.CurrentMessageInputBox;
            (App.Current as App).PrintUsageInstructions();
        }

        /// <summary>
        /// Switch to the settings page
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ApplicationBarMenuItem_Click_1(object sender, EventArgs e)
        {
            NavigationService.Navigate(new Uri("//SettingsPage.xaml", UriKind.Relative));
        }

        /// <summary>
        /// Catch text entry
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CurrentMessageInputBox_KeyDown_1(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var textBox = sender as TextBox;

            if (e.Key == System.Windows.Input.Key.Enter)
                (App.Current as App).SendMessage(textBox.Text);
        }        
    }
}