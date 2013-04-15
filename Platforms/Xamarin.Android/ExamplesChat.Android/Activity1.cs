using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using System.Net;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace ExamplesChat.Android
{
    [Activity(Label = "ExamplesChat.Android", MainLauncher = true, Icon = "@drawable/icon")]
    public class Activity1 : Activity
    {
        ChatWindowAppAndroid chat;
        Button button;
        TextView output;
        AutoCompleteTextView input;
        Spinner connectionSpinner;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            Spinner spinner = FindViewById<Spinner>(Resource.Id.connectionTypeSpinner);

            var adapter = ArrayAdapter.CreateFromResource(this, Resource.Array.ConnectionTypes, global::Android.Resource.Layout.SimpleSpinnerItem);
            adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            spinner.Adapter = adapter;
            connectionSpinner = spinner;

            //Get our button from the layout resource,
            //and attach an event to it
            button = FindViewById<Button>(Resource.Id.sendButton);
            output = FindViewById<TextView>(Resource.Id.mainText);
            input = FindViewById<AutoCompleteTextView>(Resource.Id.messageTextInput);

            button.Click += sendButton_Click;

            chat = new ChatWindowAppAndroid(this, output, input);
            chat.MasterIPAddress = "10.0.2.2";
            chat.MasterPort = 10000;
            chat.UseEncryption = false;

            chat.InitialiseNetworkComms();
        }

        void sendButton_Click(object sender, EventArgs e)
        {
            var ipBox = FindViewById<AutoCompleteTextView>(Resource.Id.ipTextInput);
            var portBox = FindViewById<AutoCompleteTextView>(Resource.Id.portTextInput);

            var newMasterIPAddress = IPAddress.Parse(chat.MasterIPAddress);
            var newPort = chat.MasterPort;

            if (!IPAddress.TryParse(ipBox.Text, out newMasterIPAddress))
                ipBox.Text = chat.MasterIPAddress;

            if (!int.TryParse(portBox.Text, out newPort) || newPort < 1 || newPort > ushort.MaxValue)
            {
                portBox.Text = chat.MasterPort.ToString();
                newPort = chat.MasterPort;
            }

            chat.MasterIPAddress = newMasterIPAddress.ToString();
            chat.MasterPort = newPort;

            string selectedItem = connectionSpinner.SelectedItem.ToString();

            if (selectedItem == "TCP")
                chat.ConnectionType = NetworkCommsDotNet.ConnectionType.TCP;
            else if (selectedItem == "UDP")
                chat.ConnectionType = NetworkCommsDotNet.ConnectionType.UDP;
            else
                throw new Exception(selectedItem);

            chat.SendMessage(input.Text);
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            menu.Add(0, 0, 0, "Settings");
            return base.OnCreateOptionsMenu(menu);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case 0:
                    //do something useful to get at settings
                    return true;
                default:
                    return base.OnOptionsItemSelected(item);
            }
        }
    }
}

