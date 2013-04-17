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
    public class ChatAppAndroid : ChatAppBase
    {
        /// <summary>
        /// The chat history window. This is where all of the message appear
        /// </summary>
        TextView chatHistory;

        /// <summary>
        /// The input box where new messages are input
        /// </summary>
        AutoCompleteTextView input;

        /// <summary>
        /// The parent context of this object
        /// </summary>
        Context parentContext;

        /// <summary>
        /// Handler used to post information to the parent context
        /// </summary>
        Handler handler = new Handler();

        public ChatAppAndroid(Context parentContext, TextView chatHistory, AutoCompleteTextView input)
            : base("Android", NetworkCommsDotNet.ConnectionType.TCP)
        {
            this.parentContext = parentContext;
            this.chatHistory = chatHistory;
            this.input = input;
        }

        /// <summary>
        /// Append the provided message to the chatBox text box.
        /// </summary>
        /// <param name="message">Message to be appended</param>
        public override void AppendLineToChatBox(string message)
        {
            handler.Post(() => { chatHistory.Text += System.Environment.NewLine + message; });
        }

        /// <summary>
        /// Clear all previous chat history
        /// </summary>
        /// <param name="message">Message to be appended</param>
        public override void ClearChatHistory()
        {
            handler.Post(() => { chatHistory.Text = ""; });
        }

        /// <summary>
        /// Clears the input text box
        /// </summary>
        public override void ClearInputLine()
        {
            handler.Post(() => { input.Text = ""; });
        }

        /// <summary>
        /// Ouput message on error
        /// </summary>
        /// <param name="message">Message to be output</param>
        public override void ShowMessage(string message)
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