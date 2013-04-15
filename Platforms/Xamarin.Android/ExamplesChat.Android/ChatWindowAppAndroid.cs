using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace ExamplesChat.Android
{
    public class ChatWindowAppAndroid : ChatWindowApp
    {
        TextView output;
        AutoCompleteTextView input;
        Context parentContext;
        Handler handler = new Handler();

        public ChatWindowAppAndroid(Context parentContext, TextView output, AutoCompleteTextView input)
            : base("Android", NetworkCommsDotNet.ConnectionType.TCP)
        {
            this.parentContext = parentContext;
            this.output = output;
            this.input = input;
        }

        protected override void AppendLineToChatBox(string message)
        {
            handler.Post(() => { output.Text += '\n' + message; });
        }

        protected override void ClearInputLine()
        {
            handler.Post(() => { input.Text = ""; });
        }

        protected override void ShowMessage(string message)
        {
            handler.Post(() =>
                {
                    AlertDialog dialog = (new AlertDialog.Builder(parentContext)).Create();
                    dialog.SetCancelable(false); // This blocks the 'BACK' button  
                    dialog.SetMessage(message);
                    dialog.SetButton("OK", new EventHandler<DialogClickEventArgs>((obj, args) =>
                    {
                        dialog.Dismiss();
                    }));

                    dialog.Show();
                });
        }
    }
}