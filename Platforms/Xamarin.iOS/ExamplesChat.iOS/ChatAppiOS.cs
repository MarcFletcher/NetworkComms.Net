using System;
using System.Collections.Generic;
using CoreGraphics;
using System.Linq;
using System.Text;
using Foundation;
using UIKit;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;

namespace ExamplesChat.iOS
{
    /// <summary>
    /// All NetworkComms.Net implementation can be found here and in ChatAppBase
    /// </summary>
    public class ChatAppiOS : ChatAppBase
    {
        #region Public Fields
        /// <summary>
        /// Size of the chat history view when the keyboard is hidden
        /// </summary>
		public CGRect OriginalViewSize { get; set; }

        /// <summary>
        /// Reference to the chatHistory text view.
        /// </summary>
        public UITextView ChatHistoryBox { get; private set; }

        /// <summary>
        /// Reference to the message box.
        /// </summary>
        public UITextField MessageBox { get; private set; }
        #endregion

        /// <summary>
        /// Constructor for the iOS chat app.
        /// </summary>
        public ChatAppiOS(UITextView chatHistoryBox, UITextField messageBox)
            : base("iPhone", ConnectionType.TCP)
        {
            ChatHistoryBox = chatHistoryBox;
            MessageBox = messageBox;
        }

        #region GUI Interface Overrides
        public override void AppendLineToChatHistory(string message)
        {
            ChatHistoryBox.InvokeOnMainThread(new Action(() =>
            {
                ChatHistoryBox.Text += message + Environment.NewLine;
				ChatHistoryBox.ScrollRangeToVisible(new NSRange(ChatHistoryBox.Text.Length, 1));
            }));
        }

        public override void ClearChatHistory()
        {
            ChatHistoryBox.InvokeOnMainThread(new Action(() =>
            {
                ChatHistoryBox.Text = "";
                CGPoint bottomOffset = new CGPoint(0, ChatHistoryBox.ContentSize.Height - ChatHistoryBox.Bounds.Size.Height);
                ChatHistoryBox.SetContentOffset(bottomOffset, true);
            }));
        }

        public override void ClearInputLine()
        {
            MessageBox.InvokeOnMainThread(new Action(() =>
            {
                MessageBox.Text = "";
            }));
        }

        public override void ShowMessage(string message)
        {
			//This method is not used by iOS so no need to implement it
            throw new NotImplementedException();
        }
        #endregion
    }
}
