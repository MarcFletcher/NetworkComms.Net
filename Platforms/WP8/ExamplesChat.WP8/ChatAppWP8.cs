using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NetworkCommsDotNet;

namespace ExamplesWP8Chat
{   
    /// <summary>
    /// All NetworkComms.Net implementation can be found here and in ChatAppBase
    /// </summary>
    public class ChatAppWP8 : ChatAppBase
    {
        #region Public Fields
        /// <summary>
        /// A global link to the chatBox
        /// </summary>
        public TextBlock ChatHistory { get; private set; }

        /// <summary>
        /// A global link to the scroller
        /// </summary>
        public ScrollViewer ChatHistoryScroller { get; private set; }

        /// <summary>
        /// A global link to the input box
        /// </summary>
        public TextBox CurrentMessageInputBox { get; private set; }
        #endregion

        /// <summary>
        /// Constructor for the WP8 chat app.
        /// </summary>
        public ChatAppWP8(TextBox currentMessageInputBox, TextBlock chatHistory, ScrollViewer chatHistoryScroller)
            : base("WinPhone8", ConnectionType.TCP)
        {
            this.CurrentMessageInputBox = currentMessageInputBox;
            this.ChatHistory = chatHistory;
            this.ChatHistoryScroller = chatHistoryScroller;
        }

        #region GUI Interface Overrides
        /// <summary>
        /// Add text to the chat history
        /// </summary>
        /// <param name="message"></param>
        public override void AppendLineToChatHistory(string message)
        {
            //To ensure we can successfully append to the text box from any thread
            //we need to wrap the append within an invoke action.
            ChatHistory.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
            {
                ChatHistory.Text += messageToAdd + "\n";
                ChatHistoryScroller.ScrollToVerticalOffset(ChatHistoryScroller.ScrollableHeight);
                ChatHistoryScroller.UpdateLayout();
            }), new object[] { message });
        }

        /// <summary>
        /// Clear the chat history
        /// </summary>
        public override void ClearChatHistory()
        {
            ChatHistory.Dispatcher.BeginInvoke(new Action(() =>
            {
                ChatHistory.Text = "";
                ChatHistoryScroller.ScrollToVerticalOffset(ChatHistoryScroller.ScrollableHeight);
                ChatHistoryScroller.UpdateLayout();
            }));
        }

        /// <summary>
        /// Clear the chat input box
        /// </summary>
        public override void ClearInputLine()
        {
            CurrentMessageInputBox.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CurrentMessageInputBox.Text = "";
                }));
        }

        /// <summary>
        /// Show a message box to the user
        /// </summary>
        /// <param name="message"></param>
        public override void ShowMessage(string message)
        {
            MessageBox.Show(message);
        }
        #endregion
    }
}
