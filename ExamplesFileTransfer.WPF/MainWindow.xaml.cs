using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Microsoft.Win32;
using DPSBase;
using NetworkCommsDotNet;
using SevenZipLZMACompressor;
using System.Net;

namespace ExamplesWPFFileTransfer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Fields
        /// <summary>
        /// Data context for the GUI list box
        /// </summary>
        ObservableCollection<ReceivedFile> receivedFiles = new ObservableCollection<ReceivedFile>();

        /// <summary>
        /// References to received files by remote ConnectionInfo
        /// </summary>
        Dictionary<ConnectionInfo, Dictionary<string, ReceivedFile>> receivedFilesDict = new Dictionary<ConnectionInfo, Dictionary<string, ReceivedFile>>();

        /// <summary>
        /// Incoming partial data cache. Keys are ConnectionInfo, PacketSequenceNumber. Value is partial packet data.
        /// </summary>
        Dictionary<ConnectionInfo, Dictionary<long, byte[]>> incomingDataCache = new Dictionary<ConnectionInfo, Dictionary<long, byte[]>>();

        /// <summary>
        /// Incoming sendInfo cache. Keys are ConnectionInfo, PacketSequenceNumber. Value is sendInfo.
        /// </summary>
        Dictionary<ConnectionInfo, Dictionary<long, SendInfo>> incomingDataInfoCache = new Dictionary<ConnectionInfo, Dictionary<long, SendInfo>>();

        /// <summary>
        /// Custom sendReceiveOptions used for sending files. Can be changed via GUI.
        /// </summary>
        SendReceiveOptions customOptions = new SendReceiveOptions<ProtobufSerializer>();

        /// <summary>
        /// Object used for ensuring thread safety.
        /// </summary>
        object syncRoot = new object();

        /// <summary>
        /// Boolean used for suppressing errors during GUI close
        /// </summary>
        static volatile bool windowClosing = false;
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            //Set the listbox data context
            lbReceivedFiles.DataContext = receivedFiles;

            //Start listening for new TCP connections
            StartListening();
        }

        #region GUI Updates
        /// <summary>
        /// Adds a line to the GUI log window
        /// </summary>
        /// <param name="logLine"></param>
        private void AddLineToLog(string logLine)
        {
            //Use dispatcher incase method is not called from GUI thread
            logBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                logBox.Text += DateTime.Now.ToShortTimeString() + " - " + logLine + "\n";

                //Update the scroller so that we are always at the bottom
                scroller.ScrollToBottom();
            }));
        }

        /// <summary>
        /// Updates the send file progress bar
        /// </summary>
        /// <param name="percentComplete"></param>
        private void UpdateSendProgress(double percentComplete)
        {
            //Use dispatcher incase method is not called from GUI thread
            sendProgress.Dispatcher.BeginInvoke(new Action(() =>
            {
                sendProgress.Value = percentComplete;
            }));
        }

        /// <summary>
        /// Adds a new ReceivedFile to the list box data context
        /// </summary>
        /// <param name="file"></param>
        private void AddNewReceivedItem(ReceivedFile file)
        {
            //Use dispatcher incase method is not called from GUI thread
            lbReceivedFiles.Dispatcher.BeginInvoke(new Action(() =>
                {
                    receivedFiles.Add(file);
                }));
        }
        #endregion

        #region GUI Events
        /// <summary>
        /// Delete the selected ReceivedFile from the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteFile_Clicked(object sender, RoutedEventArgs e)
        {
            Button cmd = (Button)sender;
            if (cmd.DataContext is ReceivedFile)
            {
                ReceivedFile fileToDelete = (ReceivedFile)cmd.DataContext;
                lock (syncRoot)
                {
                    //Delete the ReceivedFile from the listbox data context
                    receivedFiles.Remove(fileToDelete);

                    //Delete the ReceivedFile from the internal cache
                    if (receivedFilesDict.ContainsKey(fileToDelete.SourceInfo))
                        receivedFilesDict[fileToDelete.SourceInfo].Remove(fileToDelete.Filename);

                    fileToDelete.Close();
                }

                AddLineToLog("Deleted file '" + fileToDelete.Filename + "' from '" + fileToDelete.SourceInfoStr + "'");
            }
        }

        /// <summary>
        /// Save the selected file to disk
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveFile_Clicked(object sender, RoutedEventArgs e)
        {
            Button cmd = (Button)sender;
            if (cmd.DataContext is ReceivedFile)
            {
                //Use a SaveFileDialog to request the save location
                ReceivedFile fileToSave = (ReceivedFile)cmd.DataContext;
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.FileName = fileToSave.Filename;

                //If the user selected to save the file we write it to disk
                if (saveDialog.ShowDialog() == true)
                {
                    fileToSave.SaveFileToDisk(saveDialog.FileName);
                    AddLineToLog("Saved file '" + fileToSave.Filename + "' from '" + fileToSave.SourceInfoStr + "'");
                }
            }
        }

        /// <summary>
        /// Toggles the use of compression for sending files
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UseCompression_Changed(object sender, RoutedEventArgs e)
        {
            if (this.UseCompression.IsChecked == true)
            {
                //Set the customOptions to use ProtobufSerializer as a serialiser and LZMACompressor as the only data processor
                customOptions = new SendReceiveOptions<ProtobufSerializer, LZMACompressor>();
                AddLineToLog("Enabled compression.");
            }
            else if (this.UseCompression.IsChecked == false)
            {
                //Set the customOptions to use ProtobufSerializer as a serialiser without any data processors
                customOptions = new SendReceiveOptions<ProtobufSerializer>();
                AddLineToLog("Disabled compression.");
            }
        }

        /// <summary>
        /// Correctly shutdown NetworkComms.Net if the application is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Close all files
            lock (syncRoot)
            {
                foreach (ReceivedFile file in receivedFiles)
                    file.Close();
            }

            windowClosing = true;
            NetworkComms.Shutdown();
        }
        #endregion

        #region Comms
        /// <summary>
        /// Start listening for new TCP connections
        /// </summary>
        private void StartListening()
        {
            //Trigger IncomingPartialFileData method if we receive a packet of type 'PartialFileData'
            NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("PartialFileData", IncomingPartialFileData);
            //Trigger IncomingPartialFileDataInfo method if we receive a packet of type 'PartialFileDataInfo'
            NetworkComms.AppendGlobalIncomingPacketHandler<SendInfo>("PartialFileDataInfo", IncomingPartialFileDataInfo);

            //Trigger the method OnConnectionClose so that we can do some cleanup
            NetworkComms.AppendGlobalConnectionCloseHandler(OnConnectionClose);

            //Start listening for TCP connections
            //We want to select a random port on all available adaptors so provide 
            //an IPEndPoint using IPAddress.Any and port 0.
            Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Any, 0));

            //Write out some useful debugging information the log window
            AddLineToLog("Initialised WPF file transfer example. Accepting TCP connections on:");
            foreach (IPEndPoint listenEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP))
                AddLineToLog(listenEndPoint.Address + ":" + listenEndPoint.Port);
        }

        /// <summary>
        /// Handles an incoming packet of type 'PartialFileData'
        /// </summary>
        /// <param name="header">Header associated with incoming packet</param>
        /// <param name="connection">The connection associated with incoming packet</param>
        /// <param name="data">The incoming data</param>
        private void IncomingPartialFileData(PacketHeader header, Connection connection, byte[] data)
        {
            try
            {
                SendInfo info = null;
                ReceivedFile file = null;

                //Perform this in a thread safe way
                lock (syncRoot)
                {
                    //Extract the packet sequence number from the header
                    //The header can also user defined parameters
                    long sequenceNumber = header.GetOption(PacketHeaderLongItems.PacketSequenceNumber);

                    if (incomingDataInfoCache.ContainsKey(connection.ConnectionInfo) && incomingDataInfoCache[connection.ConnectionInfo].ContainsKey(sequenceNumber))
                    {
                        //We have the associated SendInfo so we can add this data directly to the file
                        info = incomingDataInfoCache[connection.ConnectionInfo][sequenceNumber];
                        incomingDataInfoCache[connection.ConnectionInfo].Remove(sequenceNumber);

                        //Check to see if we have already recieved any files from this location
                        if (!receivedFilesDict.ContainsKey(connection.ConnectionInfo))
                            receivedFilesDict.Add(connection.ConnectionInfo, new Dictionary<string, ReceivedFile>());

                        //Check to see if we have already initialised this file
                        if (!receivedFilesDict[connection.ConnectionInfo].ContainsKey(info.Filename))
                        {
                            receivedFilesDict[connection.ConnectionInfo].Add(info.Filename, new ReceivedFile(info.Filename, connection.ConnectionInfo, info.TotalBytes));
                            AddNewReceivedItem(receivedFilesDict[connection.ConnectionInfo][info.Filename]);
                        }

                        file = receivedFilesDict[connection.ConnectionInfo][info.Filename];
                    }
                    else
                    {
                        //We do not yet have the associated SendInfo so we just add the data to the cache
                        if (!incomingDataCache.ContainsKey(connection.ConnectionInfo))
                            incomingDataCache.Add(connection.ConnectionInfo, new Dictionary<long, byte[]>());

                        incomingDataCache[connection.ConnectionInfo].Add(sequenceNumber, data);
                    }
                }

                //If we have everything we need we can add data to the ReceivedFile
                if (info != null && file != null && !file.IsCompleted)
                {
                    file.AddData(info.BytesStart, 0, data.Length, data);

                    //Perform a little cleanup
                    file = null;
                    data = null;
                    GC.Collect();
                }
                else if (info == null ^ file == null)
                    throw new Exception("Either both are null or both are set. Info is " + (info == null ? "null." : "set.") + " File is " + (file == null ? "null." : "set.") + " File is " + (file.IsCompleted ? "completed." : "not completed."));
            }
            catch (Exception ex)
            {
                //If an exception occurs we write to the log window and also create an error file
                AddLineToLog("Exception - " + ex.ToString());
                NetworkComms.LogError(ex, "IncomingPartialFileDataError");
            }
        }

        /// <summary>
        /// Handles an incoming packet of type 'PartialFileDataInfo'
        /// </summary>
        /// <param name="header">Header associated with incoming packet</param>
        /// <param name="connection">The connection associated with incoming packet</param>
        /// <param name="data">The incoming data automatically converted to a SendInfo object</param>
        private void IncomingPartialFileDataInfo(PacketHeader header, Connection connection, SendInfo info)
        {
            try
            {
                byte[] data = null;
                ReceivedFile file = null;

                //Perform this in a thread safe way
                lock (syncRoot)
                {
                    //Extract the packet sequence number from the header
                    //The header can also user defined parameters
                    long sequenceNumber = info.PacketSequenceNumber;

                    if (incomingDataCache.ContainsKey(connection.ConnectionInfo) && incomingDataCache[connection.ConnectionInfo].ContainsKey(sequenceNumber))
                    {
                        //We already have the associated data in the cache
                        data = incomingDataCache[connection.ConnectionInfo][sequenceNumber];
                        incomingDataCache[connection.ConnectionInfo].Remove(sequenceNumber);

                        //Check to see if we have already recieved any files from this location
                        if (!receivedFilesDict.ContainsKey(connection.ConnectionInfo))
                            receivedFilesDict.Add(connection.ConnectionInfo, new Dictionary<string, ReceivedFile>());

                        //Check to see if we have already initialised this file
                        if (!receivedFilesDict[connection.ConnectionInfo].ContainsKey(info.Filename))
                        {
                            receivedFilesDict[connection.ConnectionInfo].Add(info.Filename, new ReceivedFile(info.Filename, connection.ConnectionInfo, info.TotalBytes));
                            AddNewReceivedItem(receivedFilesDict[connection.ConnectionInfo][info.Filename]);
                        }

                        file = receivedFilesDict[connection.ConnectionInfo][info.Filename];
                    }
                    else
                    {
                        //We do not yet have the necessary data corresponding with this SendInfo so we add the
                        //info to the cache
                        if (!incomingDataInfoCache.ContainsKey(connection.ConnectionInfo))
                            incomingDataInfoCache.Add(connection.ConnectionInfo, new Dictionary<long, SendInfo>());

                        incomingDataInfoCache[connection.ConnectionInfo].Add(sequenceNumber, info);
                    }
                }

                //If we have everything we need we can add data to the ReceivedFile
                if (data != null && file != null && !file.IsCompleted)
                {
                    file.AddData(info.BytesStart, 0, data.Length, data);

                    //Perform a little cleanup
                    file = null;
                    data = null;
                    GC.Collect();
                }
                else if (data == null ^ file == null)
                    throw new Exception("Either both are null or both are set. Data is " + (data == null ? "null." : "set.") + " File is " + (file == null ? "null." : "set.") + " File is " + (file.IsCompleted ? "completed." : "not completed."));
            }
            catch (Exception ex)
            {
                //If an exception occurs we write to the log window and also create an error file
                AddLineToLog("Exception - " + ex.ToString());
                NetworkComms.LogError(ex, "IncomingPartialFileDataInfo");
            }
        }

        /// <summary>
        /// If a connection is closed we cleanup any incomplete ReceivedFiles
        /// </summary>
        /// <param name="conn">The closed connection</param>
        private void OnConnectionClose(Connection conn)
        {
            ReceivedFile[] filesToRemove = null;

            lock (syncRoot)
            {
                //Remove any associated data from the caches
                incomingDataCache.Remove(conn.ConnectionInfo);
                incomingDataInfoCache.Remove(conn.ConnectionInfo);

                //Remove any non completed files
                if (receivedFilesDict.ContainsKey(conn.ConnectionInfo))
                {
                    filesToRemove = (from current in receivedFilesDict[conn.ConnectionInfo] where !current.Value.IsCompleted select current.Value).ToArray();
                    receivedFilesDict[conn.ConnectionInfo] = (from current in receivedFilesDict[conn.ConnectionInfo] where current.Value.IsCompleted select current).ToDictionary(entry => entry.Key, entry => entry.Value);
                }
            }

            //Update the GUI
            lbReceivedFiles.Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (syncRoot)
                {
                    if (filesToRemove != null)
                    {
                        foreach (ReceivedFile file in filesToRemove)
                        {
                            receivedFiles.Remove(file);
                            file.Close();
                        }
                    }
                }
            }));

            //Write some usefull information the log window
            AddLineToLog("Connection closed with " + conn.ConnectionInfo.ToString());
        }

        /// <summary>
        /// Sends requested file to the remoteIP and port set in GUI
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendFileButton_Click(object sender, RoutedEventArgs e)
        {
            //Create an OpenFileDialog so that we can request the file to send
            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Multiselect = false;

            //If a file was selected
            if (openDialog.ShowDialog() == true)
            {
                //Disable the send and compression buttons
                sendFileButton.IsEnabled = false;
                UseCompression.IsEnabled = false;

                //Parse the neccessary remote information
                string filename = openDialog.FileName;
                string remoteIP = this.remoteIP.Text;
                string remotePort = this.remotePort.Text;

                //Set the send progress bar to 0
                UpdateSendProgress(0);

                //Perform the send in a task so that we don't lock the GUI
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        //Create a fileStream from the selected file
                        FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read);

                        //Wrap the fileStream in a threadSafeStream so that future operations are thread safe
                        ThreadSafeStream safeStream = new ThreadSafeStream(stream);

                        //Get the filename without the associated path information
                        string shortFileName = System.IO.Path.GetFileName(filename);

                        //Parse the remote connectionInfo
                        //We have this in a seperate try catch so that we can write a clear message to the log window
                        //if there are problems
                        ConnectionInfo remoteInfo;
                        try
                        {
                            remoteInfo = new ConnectionInfo(remoteIP, int.Parse(remotePort));
                        }
                        catch (Exception)
                        {
                            throw new InvalidDataException("Failed to parse remote IP and port. Check and try again.");
                        }

                        //Get a connection to the remote side
                        Connection connection = TCPConnection.GetConnection(remoteInfo);

                        //Break the send into 20 segments. The less segments the less overhead 
                        //but we still want the progress bar to update in sensible steps
                        long sendChunkSizeBytes = (long)(stream.Length / 20.0) + 1;

                        //Limit send chunk size to 500MB
                        long maxChunkSizeBytes = 500L*1024L*1024L;
                        if (sendChunkSizeBytes > maxChunkSizeBytes) sendChunkSizeBytes = maxChunkSizeBytes;

                        long totalBytesSent = 0;
                        do
                        {
                            //Check the number of bytes to send as the last one may be smaller
                            long bytesToSend = (totalBytesSent + sendChunkSizeBytes < stream.Length ? sendChunkSizeBytes : stream.Length - totalBytesSent);

                            //Wrap the threadSafeStream in a StreamSendWrapper so that we can get NetworkComms.Net
                            //to only send part of the stream.
                            StreamSendWrapper streamWrapper = new StreamSendWrapper(safeStream, totalBytesSent, bytesToSend);

                            //We want to record the packetSequenceNumber
                            long packetSequenceNumber;
                            //Send the select data
                            connection.SendObject("PartialFileData", streamWrapper, customOptions, out packetSequenceNumber);
                            //Send the associated SendInfo for this send so that the remote can correctly rebuild the data
                            connection.SendObject("PartialFileDataInfo", new SendInfo(shortFileName, stream.Length, totalBytesSent, packetSequenceNumber), customOptions);

                            totalBytesSent += bytesToSend;

                            //Update the GUI with our send progress
                            UpdateSendProgress((double)totalBytesSent / stream.Length);
                        } while (totalBytesSent < stream.Length);

                        //Clean up any unused memory
                        GC.Collect();

                        AddLineToLog("Completed file send to '" + connection.ConnectionInfo.ToString() + "'.");
                    }
                    catch (CommunicationException)
                    {
                        //If there is a communication exception then we just write a connection
                        //closed message to the log window
                        AddLineToLog("Failed to complete send as connection was closed.");
                    }
                    catch (Exception ex)
                    {
                        //If we get any other exception which is not an InvalidDataException
                        //we log the error
                        if (!windowClosing && ex.GetType() != typeof(InvalidDataException))
                        {
                            AddLineToLog(ex.Message.ToString());
                            NetworkComms.LogError(ex, "SendFileError");
                        }
                    }

                    //Once the send is finished reset the send progress bar
                    UpdateSendProgress(0);

                    //Once complete enable the send button again
                    sendFileButton.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        sendFileButton.IsEnabled = true;
                        UseCompression.IsEnabled = true;
                    }));
                });
            }
        }
        #endregion
    }
}
