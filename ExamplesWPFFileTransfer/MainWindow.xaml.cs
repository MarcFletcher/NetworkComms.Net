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

namespace ExamplesWPFFileTransfer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ObservableCollection<ReceivedFile> receivedFiles = new ObservableCollection<ReceivedFile>();

        /// <summary>
        /// An object used for ensuring thread safety.
        /// </summary>
        object syncRoot = new object();

        /// <summary>
        /// Incoming partial data cache. Keys are ConnectionInfo, PacketSequenceNumber. Value is partial packet data.
        /// </summary>
        Dictionary<ConnectionInfo, Dictionary<long, byte[]>> incomingDataCache = new Dictionary<ConnectionInfo, Dictionary<long, byte[]>>();

        /// <summary>
        /// Incoming sendInfo cache. Keys are ConnectionInfo, PacketSequenceNumber. Value is sendInfo.
        /// </summary>
        Dictionary<ConnectionInfo, Dictionary<long, SendInfo>> incomingDataInfoCache = new Dictionary<ConnectionInfo, Dictionary<long, SendInfo>>();

        /// <summary>
        /// References to recieved files by ConnectionInfo
        /// </summary>
        Dictionary<ConnectionInfo, Dictionary<string, ReceivedFile>> receivedFilesDict = new Dictionary<ConnectionInfo, Dictionary<string, ReceivedFile>>();

        SendReceiveOptions customOptions = new SendReceiveOptions<ProtobufSerializer>();

        static volatile bool windowClosing = false;

        public MainWindow()
        {
            InitializeComponent();

            lbReceivedFiles.DataContext = receivedFiles;
            StartListening();
        }

        private void StartListening()
        {
            //NLog.Config.LoggingConfiguration logConfig = new NLog.Config.LoggingConfiguration();
            //NLog.Targets.FileTarget fileTarget = new NLog.Targets.FileTarget();
            //fileTarget.FileName = "${basedir}/ExamplesConsoleLog_" + NetworkComms.NetworkIdentifier + ".txt";
            //fileTarget.Layout = "${date:format=HH\\:mm\\:ss} [${threadid} - ${level}] - ${message}";

            //logConfig.AddTarget("file", fileTarget);

            //logConfig.LoggingRules.Add(new NLog.Config.LoggingRule("*", NLog.LogLevel.Trace, fileTarget));
            //NetworkComms.EnableLogging(logConfig);

            NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("PartialFileData", IncomingPartialFileData);
            NetworkComms.AppendGlobalIncomingPacketHandler<SendInfo>("PartialFileDataInfo", IncomingPartialFileDataInfo);

            NetworkComms.AppendGlobalConnectionCloseHandler(OnConnectionClose);
            TCPConnection.StartListening(true);

            AddLineToLog("Initialised WPF file transfer example. Accepting TCP connections on:");
            foreach (var listenEndPoint in TCPConnection.ExistingLocalListenEndPoints())
                AddLineToLog(listenEndPoint.Address + ":" + listenEndPoint.Port);
        }

        private void IncomingPartialFileData(PacketHeader header, Connection connection, byte[] data)
        {
            try
            {
                SendInfo info = null;
                ReceivedFile file = null;

                lock (syncRoot)
                {
                    long sequenceNumber = header.GetOption(PacketHeaderLongItems.PacketSequenceNumber);
                    if (incomingDataInfoCache.ContainsKey(connection.ConnectionInfo) && incomingDataInfoCache[connection.ConnectionInfo].ContainsKey(sequenceNumber))
                    {
                        //We have the info so we can add this data directly to the file
                        info = incomingDataInfoCache[connection.ConnectionInfo][sequenceNumber];
                        incomingDataInfoCache[connection.ConnectionInfo].Remove(sequenceNumber);

                        //Check to see if we have already recieved any files from this location
                        if (!receivedFilesDict.ContainsKey(connection.ConnectionInfo))
                            receivedFilesDict.Add(connection.ConnectionInfo, new Dictionary<string,ReceivedFile>());

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
                        if (!incomingDataCache.ContainsKey(connection.ConnectionInfo))
                            incomingDataCache.Add(connection.ConnectionInfo, new Dictionary<long, byte[]>());

                        incomingDataCache[connection.ConnectionInfo].Add(sequenceNumber, data);
                    }
                }

                //Merge the data
                if (info != null && file != null && !file.IsCompleted)
                    file.AddData(info.BytesStart, 0, data.Length, data);
                else if (info == null ^ file == null)
                    throw new Exception("Either both are null or both are set. This is an impossible exception!");
            }
            catch (Exception ex)
            {
                AddLineToLog("Exception - " + ex.ToString());
                NetworkComms.LogError(ex, "IncomingPartialFileDataError");
            }
        }

        private void IncomingPartialFileDataInfo(PacketHeader header, Connection connection, SendInfo info)
        {
            try
            {
                byte[] data = null;
                ReceivedFile file = null;

                lock (syncRoot)
                {
                    long sequenceNumber = info.PacketSequenceNumber;
                    if (incomingDataCache.ContainsKey(connection.ConnectionInfo) && incomingDataCache[connection.ConnectionInfo].ContainsKey(sequenceNumber))
                    {
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
                        if (!incomingDataInfoCache.ContainsKey(connection.ConnectionInfo))
                            incomingDataInfoCache.Add(connection.ConnectionInfo, new Dictionary<long,SendInfo>());

                        incomingDataInfoCache[connection.ConnectionInfo].Add(sequenceNumber, info);
                    }
                }

                //Merge the data
                if (data != null && file != null && !file.IsCompleted)
                    file.AddData(info.BytesStart, 0, data.Length, data);
                else if (data == null ^ file == null)
                    throw new Exception("Either both are null or both are set. This is an impossible exception!");
            }
            catch (Exception ex)
            {
                AddLineToLog("Exception - " + ex.ToString());
                NetworkComms.LogError(ex, "IncomingPartialFileDataInfo");
            }
        }

        /// <summary>
        /// If a connection is closed we remove any files we may have from that peer
        /// </summary>
        /// <param name="conn"></param>
        private void OnConnectionClose(Connection conn)
        {
            ReceivedFile[] filesToRemove = null;

            lock (syncRoot)
            {
                //Remove all data from the caches
                incomingDataCache.Remove(conn.ConnectionInfo);
                incomingDataInfoCache.Remove(conn.ConnectionInfo);

                //Remove any non completed files
                if (receivedFilesDict.ContainsKey(conn.ConnectionInfo))
                {
                    filesToRemove = (from current in receivedFilesDict[conn.ConnectionInfo] where !current.Value.IsCompleted select current.Value).ToArray();
                    receivedFilesDict[conn.ConnectionInfo] = (from current in receivedFilesDict[conn.ConnectionInfo] where current.Value.IsCompleted select current).ToDictionary(entry => entry.Key, entry => entry.Value);
                }
            }

            lbReceivedFiles.Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (syncRoot)
                {
                    if (filesToRemove != null)
                    {
                        foreach (ReceivedFile file in filesToRemove)
                            receivedFiles.Remove(file);
                    }
                }
            }));

            AddLineToLog("Connection closed with " + conn.ConnectionInfo.ToString());
        }

        private void AddNewReceivedItem(ReceivedFile file)
        {
            lbReceivedFiles.Dispatcher.BeginInvoke(new Action(() =>
                {
                    receivedFiles.Add(file);
                }));
        }

        private void DeleteFile_Clicked(object sender, RoutedEventArgs e)
        {
            Button cmd = (Button)sender;
            if (cmd.DataContext is ReceivedFile)
            {
                ReceivedFile fileToDelete = (ReceivedFile)cmd.DataContext;
                lock (syncRoot)
                {
                    receivedFiles.Remove(fileToDelete);

                    if (receivedFilesDict.ContainsKey(fileToDelete.SourceInfo))
                        receivedFilesDict[fileToDelete.SourceInfo].Remove(fileToDelete.Filename);
                }

                AddLineToLog("Deleted file '" + fileToDelete.Filename + "' from '" + fileToDelete.SourceInfoStr + "'");
            }
        }

        private void SaveFile_Clicked(object sender, RoutedEventArgs e)
        {
            Button cmd = (Button)sender;
            if (cmd.DataContext is ReceivedFile)
            {
                ReceivedFile fileToSave = (ReceivedFile)cmd.DataContext;
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.FileName = fileToSave.Filename;

                if (saveDialog.ShowDialog() == true)
                {
                    fileToSave.SaveFileToDisk(saveDialog.FileName);
                    AddLineToLog("Saved file '" + fileToSave.Filename + "' from '" + fileToSave.SourceInfoStr + "'");
                }
            }
        }

        private void UseCompression_Changed(object sender, RoutedEventArgs e)
        {
            if (this.UseCompression.IsChecked == true)
            {
                customOptions = new SendReceiveOptions<ProtobufSerializer, LZMACompressor>();
                AddLineToLog("Enabled compression.");
            }
            else if (this.UseCompression.IsChecked == false)
            {
                customOptions = new SendReceiveOptions<ProtobufSerializer>();
                AddLineToLog("Disabled compression.");
            }
        }

        private void AddLineToLog(string logLine)
        {
            logBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                logBox.Text += DateTime.Now.ToShortTimeString() + " - " + logLine + "\n";
                scroller.ScrollToBottom();
            }));
        }

        private void UpdateSendProgress(double percentComplete)
        {
            sendProgress.Dispatcher.BeginInvoke(new Action(() =>
                {
                    sendProgress.Value = percentComplete;
                }));
        }

        private void SendFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Multiselect = false;

            if (openDialog.ShowDialog() == true)
            {
                sendFileButton.IsEnabled = false;
                UseCompression.IsEnabled = false;

                string filename = openDialog.FileName;
                string remoteIP = this.remoteIP.Text;
                string remotePort = this.remotePort.Text;

                UpdateSendProgress(0);

                //Perform the send in a task so that we don't hold up the GUI
                Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read);
                            ThreadSafeStream safeStream = new ThreadSafeStream(stream);

                            string shortFileName = System.IO.Path.GetFileName(filename);

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

                            long sendChunkSizeBytes = (int)(stream.Length / 20.0) + 1;
                            long totalBytesSent = 0;
                            do
                            {
                                long bytesToSend = (totalBytesSent + sendChunkSizeBytes < (int)stream.Length ? sendChunkSizeBytes : (int)stream.Length - totalBytesSent);

                                StreamSendWrapper streamWrapper = new StreamSendWrapper(safeStream, totalBytesSent, bytesToSend);
                                
                                long packetSequenceNumber;
                                //Send an amount of data
                                connection.SendObject("PartialFileData", streamWrapper, customOptions, out packetSequenceNumber);
                                //Send the associated info for this send so that the remote can correctly rebuild the data
                                connection.SendObject("PartialFileDataInfo", new SendInfo(shortFileName, stream.Length, totalBytesSent, packetSequenceNumber), customOptions);

                                totalBytesSent += bytesToSend;
                                UpdateSendProgress((double)totalBytesSent / stream.Length);
                            } while (totalBytesSent < stream.Length);

                            AddLineToLog("Completed file send to '" + connection.ConnectionInfo.ToString() + "'.");
                        }
                        catch(Exception ex)
                        {
                            if (!windowClosing)
                            {
                                UpdateSendProgress(0);
                                AddLineToLog(ex.Message.ToString());
                                NetworkComms.LogError(ex, "SendFileError");
                            }
                        }

                        //Once complete enable the send button again
                        sendFileButton.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                sendFileButton.IsEnabled = true;
                                UseCompression.IsEnabled = true;
                            }));
                    });
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            windowClosing = true;
            NetworkComms.Shutdown();
        }
    }
}
