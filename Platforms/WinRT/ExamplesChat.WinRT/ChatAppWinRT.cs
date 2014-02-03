using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using NetworkCommsDotNet;
using Windows.UI.Xaml.Controls;
using Windows.UI.Popups;

namespace Examples.ExamplesChat.WinRT
{   
    /// <summary>
    /// All NetworkComms.Net implementation can be found here and in ChatAppBase
    /// </summary>
    public class ChatAppWinRT : ChatAppBase
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
        public ChatAppWinRT(TextBox currentMessageInputBox, TextBlock chatHistory, ScrollViewer chatHistoryScroller)
            : base("WinRT", ConnectionType.TCP)
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
            //To ensure we can succesfully append to the text box from any thread
            //we need to wrap the append within an invoke action.
            ChatHistory.Text += message + "\n";
            ChatHistoryScroller.ScrollToVerticalOffset(ChatHistoryScroller.ScrollableHeight);
            ChatHistoryScroller.UpdateLayout();
        }

        /// <summary>
        /// Clear the chat history
        /// </summary>
        public override void ClearChatHistory()
        {
            ChatHistory.Text = "";
            ChatHistoryScroller.ScrollToVerticalOffset(ChatHistoryScroller.ScrollableHeight);
            ChatHistoryScroller.UpdateLayout();
        }

        /// <summary>
        /// Clear the chat input box
        /// </summary>
        public override void ClearInputLine()
        {
            CurrentMessageInputBox.Text = "";
        }

        /// <summary>
        /// Show a message box to the user
        /// </summary>
        /// <param name="message"></param>
        public override void ShowMessage(string message)
        {
            MessageDialog md = new MessageDialog(message);
            md.Commands.Add(new UICommand("Close", new UICommandInvokedHandler((cmd) => { })));

            Func<Task> messageTask = new Func<Task>(async () =>
                {
                    await md.ShowAsync();                             
                });

            messageTask();
        }
        #endregion
    }
}
