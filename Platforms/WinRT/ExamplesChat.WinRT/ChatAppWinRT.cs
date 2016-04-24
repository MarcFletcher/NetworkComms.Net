// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

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
using Windows.UI.Xaml;
using Windows.UI.Core;
using Windows.ApplicationModel.Core;

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
            ChatHistory.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ChatHistory.Text += message + "\n";
                    ChatHistoryScroller.ScrollToVerticalOffset(ChatHistoryScroller.ScrollableHeight);
                    ChatHistoryScroller.UpdateLayout();
                }).AsTask();            
        }

        /// <summary>
        /// Clear the chat history
        /// </summary>
        public override void ClearChatHistory()
        {
            ChatHistory.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ChatHistory.Text = "";
                    ChatHistoryScroller.ScrollToVerticalOffset(ChatHistoryScroller.ScrollableHeight);
                    ChatHistoryScroller.UpdateLayout();
                }).AsTask();
        }

        /// <summary>
        /// Clear the chat input box
        /// </summary>
        public override void ClearInputLine()
        {
            ChatHistory.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    CurrentMessageInputBox.Text = "";
                }).AsTask();
        }

        /// <summary>
        /// Show a message box to the user
        /// </summary>
        /// <param name="message"></param>
        public override void ShowMessage(string message)
        {
            ChatHistory.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {

                    MessageDialog md = new MessageDialog(message);
                    md.Commands.Add(new UICommand("Close", new UICommandInvokedHandler((cmd) => { })));

                    Func<Task> messageTask = new Func<Task>(async () =>
                        {
                            await md.ShowAsync();
                        });

                    messageTask();
                }).AsTask();
        }
        #endregion
    }
}
