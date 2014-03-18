//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.DPSBase;

namespace Examples.ExamplesChat.WPF
{
    /// <summary>
    /// All NetworkComms.Net implementation can be found here and in ChatAppBase
    /// </summary>
    public class ChatAppWPF : ChatAppBase
    {
        #region Public Fields
        /// <summary>
        /// Reference to the chat history text block 
        /// </summary>
        public TextBlock ChatHistory { get; private set; }

        /// <summary>
        /// Reference to the scrollviewer 
        /// </summary>
        public ScrollViewer Scroller { get; private set; }

        /// <summary>
        /// Reference to the messages from text box
        /// </summary>
        public TextBox MessagesFrom { get; private set; }

        /// <summary>
        /// Reference to the messages text text box
        /// </summary>
        public TextBox MessagesText { get; private set; }
        #endregion

        /// <summary>
        /// Constructor for the WPF chat app.
        /// </summary>
        public ChatAppWPF(TextBlock chatHistory, ScrollViewer scroller, TextBox messagesFrom, TextBox messagesText)
            : base (HostInfo.HostName, ConnectionType.TCP)
        {
            this.ChatHistory = chatHistory;
            this.Scroller = scroller;
            this.MessagesFrom = messagesFrom;
            this.MessagesText = messagesText;
            this.Serializer = DPSManager.GetDataSerializer<ProtobufSerializer>();
        }

        /// <summary>
        /// Refresh the messagesFrom text box using the recent message history.
        /// </summary>
        public void RefreshMessagesFromBox()
        {
            //We will perform a lock here to ensure the text box is only
            //updated one thread at  time
            lock (lastPeerMessageDict)
            {
                //Use a linq expression to extract an array of all current users from lastPeerMessageDict
                string[] currentUsers = (from current in lastPeerMessageDict.Values orderby current.SourceName select current.SourceName).ToArray();

                //To ensure we can successfully append to the text box from any thread
                //we need to wrap the append within an invoke action.
                MessagesFrom.Dispatcher.BeginInvoke(new Action<string[]>((users) =>
                {
                    //First clear the text box
                    MessagesFrom.Text = "";

                    //Now write out each username
                    foreach (var username in users)
                        MessagesFrom.AppendText(username + "\n");
                }), new object[] { currentUsers });
            }
        }

        /// <summary>
        /// Append the provided message to the chatBox text box including the provided formatting.
        /// </summary>
        /// <param name="message"></param>
        public void AppendLineToChatHistory(System.Drawing.FontStyle style, string text, bool addNewLine)
        {
            //To ensure we can successfully append to the text box from any thread
            //we need to wrap the append within an invoke action.
            ChatHistory.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (style == System.Drawing.FontStyle.Regular)
                    ChatHistory.Inlines.Add(new Run(text));
                else if (style == System.Drawing.FontStyle.Bold)
                    ChatHistory.Inlines.Add(new Bold(new Run(text)));
                else if (style == System.Drawing.FontStyle.Italic)
                    ChatHistory.Inlines.Add(new Italic(new Run(text)));
                else
                    ChatHistory.Inlines.Add(new Run("Error: Attempted to add unknown text with unknown font style."));

                if (addNewLine)
                    ChatHistory.Inlines.Add("\n");
                Scroller.ScrollToBottom();
            }));
        }

        /// <summary>
        /// Performs whatever functions we might so desire when we receive an incoming ChatMessage
        /// </summary>
        /// <param name="header">The PacketHeader corresponding with the received object</param>
        /// <param name="connection">The Connection from which this object was received</param>
        /// <param name="incomingMessage">The incoming ChatMessage we are after</param>
        protected override void HandleIncomingChatMessage(PacketHeader header, Connection connection, ChatMessage incomingMessage)
        {
            //We call the base that handles everything
            base.HandleIncomingChatMessage(header, connection, incomingMessage);

            //Once the base is complete we refresh the messages from box
            RefreshMessagesFromBox();
        }

        #region GUI Interface Overrides
        /// <summary>
        /// Append the provided message to the chatBox text box.
        /// </summary>
        /// <param name="message"></param>
        public override void AppendLineToChatHistory(string message)
        {
            AppendLineToChatHistory(System.Drawing.FontStyle.Regular, message, true);
        }

        /// <summary>
        /// Clear the chat history window
        /// </summary>
        public override void ClearChatHistory()
        {
            ChatHistory.Dispatcher.BeginInvoke(new Action(() =>
            {
                ChatHistory.Inlines.Clear();
                Scroller.ScrollToBottom();
            }));
        }

        /// <summary>
        /// Clear the text message input box
        /// </summary>
        public override void ClearInputLine()
        {
            MessagesText.Dispatcher.BeginInvoke(new Action(() =>
            {
                MessagesText.Text = "";
            }));
        }

        /// <summary>
        /// Show a message as an alternative to adding text to chat history
        /// </summary>
        /// <param name="message"></param>
        public override void ShowMessage(string message)
        {
            MessageBox.Show(message);
        }
        #endregion
    }
}
