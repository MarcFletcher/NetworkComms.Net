using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;

namespace ExamplesWPFFileTransfer
{
    class ReceivedFile : INotifyPropertyChanged
    {
        public string Filename { get; private set; }
        public ConnectionInfo SourceInfo { get; private set; }
        public long SizeBytes { get; private set; }
        public int ReceivedBytes { get; private set; }

        public double CompletedPercent
        {
            get { return (double)ReceivedBytes / SizeBytes; }
            set { throw new Exception("An attempt to modify readonly value."); }
        }

        public string SourceInfoStr
        {
            get { return "[" + SourceInfo.RemoteEndPoint.Address + " - " + SourceInfo.RemoteEndPoint.Port + "]"; }
        }

        public bool IsCompleted
        {
            get { return ReceivedBytes == SizeBytes; }
        }

        object SyncRoot = new object();
        MemoryStream data;

        public event PropertyChangedEventHandler PropertyChanged;

        // This method is called by the Set accessor of each property. 
        // The CallerMemberName attribute that is applied to the optional propertyName 
        // parameter causes the property name of the caller to be substituted as an argument. 
        private void NotifyPropertyChanged(string propertyName = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        public ReceivedFile(string filename, ConnectionInfo sourceInfo, long sizeBytes)
        {
            this.Filename = filename;
            this.SourceInfo = sourceInfo;
            this.SizeBytes = sizeBytes;

            if (this.SizeBytes > int.MaxValue)
                throw new NotSupportedException("The provided sizeBytes is not supported for this example.");

            data = new MemoryStream((int)sizeBytes);;
        }

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

        public void SaveFileToDisk(string saveLocation)
        {
            if (ReceivedBytes != SizeBytes)
                throw new Exception("Attempted to save out file before data is complete.");

            lock (SyncRoot)
                File.WriteAllBytes(saveLocation, data.ToArray());
        }
    }
}
