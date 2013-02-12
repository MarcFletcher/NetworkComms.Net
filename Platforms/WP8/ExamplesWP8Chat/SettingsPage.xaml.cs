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
        /// An optional encryption key to use should one be required.
        /// This can be changed freely but must obviously be the same
        /// for both sender and reciever.
        /// </summary>
        string encryptionKey = "ljlhjf8uyfln23490jf;m21-=scm20--iflmk;";

        public SettingsPage()
        {
            InitializeComponent();

            MasterIPInputBox.Text = (App.Current as App).MasterIPAddress;
            MasterIPInputBox.Select(MasterIPInputBox.Text.Length, 0);
            oldText = MasterIPInputBox.Text;

            MasterPortInputBox.Text = (App.Current as App).MasterPort.ToString();
            LocalNameInputBox.Text = (App.Current as App).LocalName;
            UseEncryptionCheckBox.IsChecked = (App.Current as App).UseEncryption;
        }

        private void BackKeyPressHandler(object sender, System.ComponentModel.CancelEventArgs e)
        {
            (App.Current as App).MasterIPAddress = MasterIPInputBox.Text;
            (App.Current as App).MasterPort = int.Parse(MasterPortInputBox.Text);
            (App.Current as App).LocalName = LocalNameInputBox.Text;
            (App.Current as App).UseEncryption = (bool)UseEncryptionCheckBox.IsChecked;
        }

        private string oldText;

        private void MasterIPInputBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text;
            
            if(newText.Length > oldText.Length)
            {
                if (newText.Last() == '.')
                    return;

                int dotCount = newText.Count(c => c == '.');

                string oldLastIPPortion = "";
                if (oldText.LastIndexOf('.') != oldText.Length - 1)
                    oldLastIPPortion = oldText.Substring(oldText.LastIndexOf('.') + 1);

                string newLastIPPortion = newText.Substring(newText.LastIndexOf('.') + 1);
                int lastPortion = newLastIPPortion.Length == 0 ? 0 : int.Parse(newLastIPPortion);

                if (lastPortion > 255)
                {
                    textBox.Text = oldText;
                    textBox.Select(textBox.Text.Length, 0);
                    return;
                }
                else
                {
                    if (dotCount == 3 && newLastIPPortion.Length == 4)
                    {
                        textBox.Text = oldText;
                        textBox.Select(textBox.Text.Length, 0);
                        return;
                    }

                    if (newLastIPPortion.Length == 3 && dotCount < 3)
                        textBox.Text = newText + ".";

                    oldText = textBox.Text;
                    textBox.Select(textBox.Text.Length, 0);
                    return;
                }                
            }
            else if(newText.Length < oldText.Length)
            {
                if (oldText.EndsWith("."))
                {
                    textBox.Text = newText.Substring(0, newText.Length - 1);
                    oldText = textBox.Text;
                    textBox.Select(textBox.Text.Length, 0);
                    return;
                }
                else
                {
                    oldText = textBox.Text;
                    return;
                }
            }
        }

        /// <summary>
        /// Enable encryption of all data as default
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseEncryptionBox_Checked(object sender, RoutedEventArgs e)
        {
            RijndaelPSKEncrypter.AddPasswordToOptions(NetworkComms.DefaultSendReceiveOptions.Options, encryptionKey);
            NetworkComms.DefaultSendReceiveOptions.DataProcessors.Add(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());
        }

        /// <summary>
        /// Disable encryption of all data as default
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseEncryptionBox_Unchecked(object sender, RoutedEventArgs e)
        {
            NetworkComms.DefaultSendReceiveOptions.DataProcessors.Remove(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());
        }
        
    }
}