//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using NetworkCommsDotNet;
using Windows.UI.Xaml.Controls;
using Windows.UI.Popups;
using NetworkCommsDotNet.Connections;

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
