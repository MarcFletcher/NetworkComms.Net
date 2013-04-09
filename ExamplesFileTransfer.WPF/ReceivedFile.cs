using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NetworkCommsDotNet;
using System.ComponentModel;
using System.IO;

namespace ExamplesWPFFileTransfer
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
        public int ReceivedBytes { get; private set; }

        /// <summary>
        /// Getter which returns the completion of this file, between 0 and 1
        /// </summary>
        public double CompletedPercent
        {
            get { return (double)ReceivedBytes / SizeBytes; }
            set { throw new Exception("An attempt to modify readonly value."); }
        }

        /// <summary>
        /// A formatted string of the SourceInfo
        /// </summary>
        public string SourceInfoStr
        {
            get { return "[" + SourceInfo.RemoteEndPoint.Address + ":" + SourceInfo.RemoteEndPoint.Port + "]"; }
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
        /// A memorystream used to build the file
        /// </summary>
        MemoryStream data;

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

            if (this.SizeBytes > int.MaxValue)
                throw new NotSupportedException("The provided sizeBytes is not supported for this example.");

            data = new MemoryStream((int)sizeBytes);;
        }

        /// <summary>
        /// Add data to file
        /// </summary>
        /// <param name="dataStart">Where to start writing this data to the interal memoryStream</param>
        /// <param name="bufferStart">Where to start copying data from buffer</param>
        /// <param name="bufferLength">The number of bytes to copy from buffer</param>
        /// <param name="buffer">Buffer containing data to add</param>
        public void AddData(long dataStart, long bufferStart, long bufferLength, byte[] buffer)
        {
            if (bufferStart > int.MaxValue)
                throw new NotSupportedException("The provided bufferStart is not supported for this example.");

            if (bufferLength > int.MaxValue)
                throw new NotSupportedException("The provided bufferLength is not supported for this example.");

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

            lock (SyncRoot)
                File.WriteAllBytes(saveLocation, data.ToArray());
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
