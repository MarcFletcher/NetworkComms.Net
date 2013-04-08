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
using DPSBase;
using Microsoft.Win32;
using NetworkCommsDotNet;

namespace ExamplesWPFFileTransfer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const long sendChunkSizeBytes = 10240;
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

        public MainWindow()
        {
            InitializeComponent();

            //receivedFiles.Add(new ReceivedFile("Very long file NAME.csv", new NetworkCommsDotNet.ConnectionInfo("192.168.0.1", 10000), 1024));
            //receivedFiles.Add(new ReceivedFile("Very.csv", new NetworkCommsDotNet.ConnectionInfo("192.168.0.1", 10000), 1024));

            lbReceivedFiles.DataContext = receivedFiles;
            StartListening();
        }

        private void StartListening()
        {
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
                if (info != null && file != null)
                {
                    file.AddData(info.BytesStart, 0, data.Length, data);
                    UpdateReceiveProgress();
                }
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
                if (data != null && file != null)
                {
                    file.AddData(info.BytesStart, 0, data.Length, data);
                    UpdateReceiveProgress();
                }
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
            lock (syncRoot)
            {
                incomingDataCache.Remove(conn.ConnectionInfo);

                //Remove any non completed files
            }

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
                receivedFiles.Remove(fileToDelete);
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

        private void UpdateReceiveProgress()
        {
            lbReceivedFiles.Dispatcher.BeginInvoke(new Action(() =>
            {
                lbReceivedFiles.UpdateLayout();
            }));
        }

        private void SendFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Multiselect = false;

            if (openDialog.ShowDialog() == true)
            {
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

                            //Get a connection to the remote side
                            Connection connection = TCPConnection.GetConnection(new ConnectionInfo(remoteIP, int.Parse(remotePort)));

                            long totalBytesSent = 0;
                            do
                            {
                                long bytesToSend = (totalBytesSent + sendChunkSizeBytes < (int)stream.Length ? sendChunkSizeBytes : (int)stream.Length - totalBytesSent);

                                StreamSendWrapper streamWrapper = new StreamSendWrapper(safeStream, totalBytesSent, bytesToSend);
                                
                                long packetSequenceNumber;
                                //Send an amount of data
                                connection.SendObject("PartialFileData", streamWrapper, out packetSequenceNumber);
                                //Send the associated info for this send so that the remote can correctly rebuild the data
                                connection.SendObject("PartialFileDataInfo", new SendInfo(shortFileName, stream.Length, totalBytesSent, packetSequenceNumber));

                                totalBytesSent += bytesToSend;
                                UpdateSendProgress((double)totalBytesSent / stream.Length);
                            } while (totalBytesSent < stream.Length);
                        }
                        catch(Exception ex)
                        {
                            UpdateSendProgress(0);
                            AddLineToLog(ex.ToString());
                            NetworkComms.LogError(ex, "SendFileError");
                        }
                    });
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            NetworkComms.Shutdown();
        }
    }
}
