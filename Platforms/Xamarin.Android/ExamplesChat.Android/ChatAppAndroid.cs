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
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.DPSBase;

namespace ExamplesChat.Android
{
    /// <summary>
    /// All NetworkComms.Net implementation can be found here and in ChatAppBase
    /// </summary>
    public class ChatAppAndroid : ChatAppBase
    {
        #region Public Fields
        /// <summary>
        /// The chat history window. This is where all of the message appear
        /// </summary>
        public TextView ChatHistory { get; private set; }

        /// <summary>
        /// The input box where new messages are input
        /// </summary>
        public AutoCompleteTextView Input { get; private set; }

        /// <summary>
        /// The parent context of this object
        /// </summary>
        public Context ParentContext { get; private set; }
        #endregion

        #region Private Fields
        /// <summary>
        /// Handler used to post information to the parent context
        /// </summary>
        Handler handler = new Handler();
        #endregion

        /// <summary>
        /// Constructor for the Android chat app.
        /// </summary>
        public ChatAppAndroid(Context parentContext, TextView chatHistory, AutoCompleteTextView input)
            : base("Android", ConnectionType.TCP)
        {
            this.ParentContext = parentContext;
            this.ChatHistory = chatHistory;
            this.Input = input;
            Serializer = DPSManager.GetDataSerializer<NetworkCommsDotNet.DPSBase.ProtobufSerializer>();
        }

        #region GUI Interface Overrides
        /// <summary>
        /// Append the provided message to the chatBox text box.
        /// </summary>
        /// <param name="message">Message to be appended</param>
        public override void AppendLineToChatHistory(string message)
        {
            handler.Post(() => { ChatHistory.Text += System.Environment.NewLine + message; });
        }

        /// <summary>
        /// Clear all previous chat history
        /// </summary>
        /// <param name="message">Message to be appended</param>
        public override void ClearChatHistory()
        {
            handler.Post(() => { ChatHistory.Text = ""; });
        }

        /// <summary>
        /// Clears the input text box
        /// </summary>
        public override void ClearInputLine()
        {
            handler.Post(() => { Input.Text = ""; });
        }

        /// <summary>
        /// Ouput message on error
        /// </summary>
        /// <param name="message">Message to be output</param>
        public override void ShowMessage(string message)
        {
            handler.Post(() =>
                {
                    AlertDialog dialog = (new AlertDialog.Builder(ParentContext)).Create();
                    dialog.SetCancelable(false); // This blocks the 'BACK' button  
                    dialog.SetMessage(message);
                    dialog.SetButton("OK", new EventHandler<DialogClickEventArgs>((obj, args) =>
                    {
                        dialog.Dismiss();
                    }));

                    dialog.Show();
                });
        }
        #endregion
    }
}