using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NetworkCommsDotNet;
using DPSBase;
using SevenZipLZMACompressor;
using System.Windows.Threading;

namespace ExamplesWPFChat
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// An instance of the chat application
        /// </summary>
        ChatAppWPF chatApplication;

        public MainWindow()
        {
            InitializeComponent();

            //Set the localName text to the hostname
            localName.Text = HostInfo.HostName;

            //Initialise the chat application
            chatApplication = new ChatAppWPF(chatBox, scroller, messagesFrom, messageText);

            //Print out some usage instructions
            chatApplication.PrintUsageInstructions();

            //Initialise the NetworkComms.Net settings
            chatApplication.RefreshNetworkCommsConfiguration();
        }

        #region Event Handlers
        /// <summary>
        /// Send any entered message when we click the send button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            chatApplication.SendMessage(messageText.Text);
        }

        /// <summary>
        /// Send any entered message when we press enter or return
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MessageText_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
                chatApplication.SendMessage(messageText.Text);
        }

        /// <summary>
        /// Enable or disable encryption of all data as default
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseEncryptionBox_CheckedUnchecked(object sender, RoutedEventArgs e)
        {
            chatApplication.EncryptionEnabled = (bool)useEncryptionBox.IsChecked;
            chatApplication.RefreshNetworkCommsConfiguration();
        }

        /// <summary>
        /// Correctly shutdown NetworkCommsDotNet when closing the WPF application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Ensure we shutdown comms when we are finished
            NetworkComms.Shutdown();
        }

        /// <summary>
        /// Use UDP for all communication
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseUDP_Checked(object sender, RoutedEventArgs e)
        {
            if (this.UseTCP!=null && this.UseTCP.IsChecked != null && !(bool)this.UseTCP.IsChecked)
            {
                //Update the application and connectionType
                this.UseTCP.IsChecked = false;
                this.UseUDP.IsChecked = true;
                chatApplication.ConnectionType = ConnectionType.UDP;
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        /// <summary>
        /// Use TCP for all communication
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseTCP_Checked(object sender, RoutedEventArgs e)
        {
            if (this.UseUDP!= null && this.UseUDP.IsChecked != null && !(bool)this.UseUDP.IsChecked)
            {
                //Update the application and connectionType
                this.UseTCP.IsChecked = true;
                this.UseUDP.IsChecked = false;
                chatApplication.ConnectionType = ConnectionType.TCP;
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        /// <summary>
        /// Update the local server enabled settings
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EnableLocalServer_CheckedUnChecked(object sender, RoutedEventArgs e)
        {
            chatApplication.LocalServerEnabled = (bool)enableLocalServer.IsChecked;
            chatApplication.RefreshNetworkCommsConfiguration();
        }

        /// <summary>
        /// Update the server IP if changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ServerIP_LostFocus(object sender, RoutedEventArgs e)
        {
            chatApplication.ServerIPAddress = serverIP.Text.Trim();
        }

        /// <summary>
        /// Update the server port if changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ServerPort_LostFocus(object sender, RoutedEventArgs e)
        {
            int portNumber;
            if (int.TryParse(serverPort.Text, out portNumber))
                chatApplication.ServerPort = portNumber;
            else
                serverPort.Text = "";
        }

        /// <summary>
        /// Update the local name if changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LocalName_LostFocus(object sender, RoutedEventArgs e)
        {
            chatApplication.LocalName = localName.Text.Trim();
        }
        #endregion
    }
}
