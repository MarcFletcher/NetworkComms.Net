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

using NetworkCommsDotNet;
using System.ComponentModel;
using System.IO;

namespace Examples.ExamplesFileTransfer.WPF
{
    /// <summary>
    /// A local class which can be used to populate the WPF list box
    /// </summary>
    class ReceivedFile : INotifyPropertyChanged
    {
        /// <summary>
        /// The name of the file
        /// </summary>
        public string Filename { get; private set; }

        /// <summary>
        /// The connectionInfo corresponding with the source
        /// </summary>
        public ConnectionInfo SourceInfo { get; private set; }

        /// <summary>
        /// The total size in bytes of the file
        /// </summary>
        public long SizeBytes { get; private set; }

        /// <summary>
        /// The total number of bytes received so far
        /// </summary>
        public long ReceivedBytes { get; private set; }

        /// <summary>
        /// Getter which returns the completion of this file, between 0 and 1
        /// </summary>
        public double CompletedPercent
        {
            get { return (double)ReceivedBytes / SizeBytes; }

            //This set is required for the application to work
            set { throw new Exception("An attempt to modify read-only value."); }
        }

        /// <summary>
        /// A formatted string of the SourceInfo
        /// </summary>
        public string SourceInfoStr
        {
            get { return "[" + SourceInfo.RemoteEndPoint.ToString() + "]"; }
        }

        /// <summary>
        /// Returns true if the completed percent equals 1
        /// </summary>
        public bool IsCompleted
        {
            get { return ReceivedBytes == SizeBytes; }
        }

        /// <summary>
        /// Private object used to ensure thread safety
        /// </summary>
        object SyncRoot = new object();

        /// <summary>
        /// A memory stream used to build the file
        /// </summary>
        Stream data;

        /// <summary>
        ///Event subscribed to by GUI for updates
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Create a new ReceivedFile
        /// </summary>
        /// <param name="filename">Filename associated with this file</param>
        /// <param name="sourceInfo">ConnectionInfo corresponding with the file source</param>
        /// <param name="sizeBytes">The total size in bytes of this file</param>
        public ReceivedFile(string filename, ConnectionInfo sourceInfo, long sizeBytes)
        {
            this.Filename = filename;
            this.SourceInfo = sourceInfo;
            this.SizeBytes = sizeBytes;

            //We create a file on disk so that we can receive large files
            data = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 8 * 1024, FileOptions.DeleteOnClose);
        }

        /// <summary>
        /// Add data to file
        /// </summary>
        /// <param name="dataStart">Where to start writing this data to the internal memoryStream</param>
        /// <param name="bufferStart">Where to start copying data from buffer</param>
        /// <param name="bufferLength">The number of bytes to copy from buffer</param>
        /// <param name="buffer">Buffer containing data to add</param>
        public void AddData(long dataStart, int bufferStart, int bufferLength, byte[] buffer)
        {
            lock (SyncRoot)
            {
                data.Seek(dataStart, SeekOrigin.Begin);
                data.Write(buffer, (int)bufferStart, (int)bufferLength);

                ReceivedBytes += (int)(bufferLength - bufferStart);
            }

            NotifyPropertyChanged("CompletedPercent");
            NotifyPropertyChanged("IsCompleted");
        }

        /// <summary>
        /// Saves the completed file to the provided saveLocation
        /// </summary>
        /// <param name="saveLocation">Location to save file</param>
        public void SaveFileToDisk(string saveLocation)
        {
            if (ReceivedBytes != SizeBytes)
                throw new Exception("Attempted to save out file before data is complete.");

            if (!File.Exists(Filename))
                throw new Exception("The transferred file should have been created within the local application directory. Where has it gone?");

            File.Delete(saveLocation);
            File.Copy(Filename, saveLocation);
        }

        /// <summary>
        /// Closes and releases any resources maintained by this file
        /// </summary>
        public void Close()
        {
            try
            {
                data.Dispose();
            }
            catch (Exception) { }

            try
            {
                data.Close();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Triggers a GUI update on a property change
        /// </summary>
        /// <param name="propertyName"></param>
        private void NotifyPropertyChanged(string propertyName = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
