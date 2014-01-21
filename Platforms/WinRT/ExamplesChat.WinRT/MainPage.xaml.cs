using NetworkCommsDotNet;
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

namespace ExamplesChat.WinRT
{
    /// <summary>
    /// A basic page that provides characteristics common to most applications.
    /// </summary>
    public sealed partial class MainPage : ExamplesChat.WinRT.Common.LayoutAwarePage
    {
        ChatAppWinRT chatApplication;

        public MainPage()
        {
            this.InitializeComponent();
            
            //Set the localName text to the hostname
            localNameBox.Text = HostInfo.HostName;

            //Initialise the chat application
            chatApplication = new ChatAppWinRT(currentMessageInputBox, chatBox, chatBoxScroller);

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

        private void CurrentMessageInputBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                chatApplication.SendMessage(currentMessageInputBox.Text);
        }

        private void remoteIPBox_LostFocus(object sender, RoutedEventArgs e)
        {
           chatApplication.ServerIPAddress = remoteIPBox.Text.Trim();
        }

        private void remotePortBox_LostFocus(object sender, RoutedEventArgs e)
        {
            int portNumber;
            if (int.TryParse(remotePortBox.Text, out portNumber))
                chatApplication.ServerPort = portNumber;
            else
                remotePortBox.Text = "";
        }

        private void localNameBox_LostFocus(object sender, RoutedEventArgs e)
        {            
            chatApplication.LocalName = localNameBox.Text.Trim();
        }

        private void TCPRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (TCPRadioButton != null && TCPRadioButton.IsChecked != null && (bool)TCPRadioButton.IsChecked)
            {
                //Update the application and connectionType                
                chatApplication.ConnectionType = ConnectionType.TCP;
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        private void UDPRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (UDPRadioButton != null && UDPRadioButton.IsChecked != null && (bool)UDPRadioButton.IsChecked)
            {
                //Update the application and connectionType                
                chatApplication.ConnectionType = ConnectionType.UDP;
                chatApplication.RefreshNetworkCommsConfiguration();
            }
        }

        private void encryptionBox_Checked(object sender, RoutedEventArgs e)
        {
            chatApplication.EncryptionEnabled = (bool)encryptionBox.IsChecked;
            chatApplication.RefreshNetworkCommsConfiguration();
        }

        private void enableServerBox_Checked(object sender, RoutedEventArgs e)
        {
            chatApplication.LocalServerEnabled = (bool)enableServerBox.IsChecked;
            chatApplication.RefreshNetworkCommsConfiguration();
        }       
        
    }
}
