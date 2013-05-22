using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using MonoTouch.Foundation;
using MonoTouch.UIKit;
using NetworkCommsDotNet;

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
        public readonly RectangleF OriginalViewSize = new RectangleF(0, 0, 320, 416);

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
            ChatHistoryBox.InvokeOnMainThread(new NSAction(() =>
            {
                ChatHistoryBox.Text += message + Environment.NewLine;
                PointF bottomOffset = new PointF(0, ChatHistoryBox.ContentSize.Height - ChatHistoryBox.Bounds.Size.Height);
                ChatHistoryBox.SetContentOffset(bottomOffset, true);
            }));
        }

        public override void ClearChatHistory()
        {
            ChatHistoryBox.InvokeOnMainThread(new NSAction(() =>
            {
                ChatHistoryBox.Text = "";
                PointF bottomOffset = new PointF(0, ChatHistoryBox.ContentSize.Height - ChatHistoryBox.Bounds.Size.Height);
                ChatHistoryBox.SetContentOffset(bottomOffset, true);
            }));
        }

        public override void ClearInputLine()
        {
            MessageBox.InvokeOnMainThread(new NSAction(() =>
            {
                MessageBox.Text = "";
            }));
        }

        public override void ShowMessage(string message)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
