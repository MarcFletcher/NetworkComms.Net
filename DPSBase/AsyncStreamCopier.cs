using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;

namespace DPSBase
{
    /// <summary>
    /// The Async Copier class reads the input Stream Async and writes Synchronously
    /// </summary>
    public class AsyncStreamCopier
    {
        /// <summary>
        /// Event raised when copy has completed
        /// </summary>
        public event EventHandler Completed;

        private byte[] buffer = new byte[4096];

        /// <summary>
        /// Initialise a new instance of the asyncStreamCopier
        /// </summary>
        public AsyncStreamCopier()
        {                        
        }

        /// <summary>
        /// Starts the async copy
        /// </summary>
        /// <param name="input">Input stream</param>
        /// <param name="output">Output stream</param>
        public void Start(Stream input, Stream output)
        {
            GetNextChunk(new Stream[] { input, output });
        }

        private void GetNextChunk(Stream[] streams)
        {
            var input = streams[0];
            input.BeginRead(buffer, 0, buffer.Length, InputReadComplete, streams);
        }

        private void InputReadComplete(IAsyncResult ar)
        {
            var streams = ar.AsyncState as Stream[];
            var input = streams[0];
            var output = streams[1];

            // input read asynchronously completed
            int bytesRead = input.EndRead(ar);
            
            if (bytesRead == 0)
            {
                RaiseCompleted();
                return;
            }

            // write synchronously
            output.Write(buffer, 0, bytesRead);

            // get next
            GetNextChunk(streams);
        }

        private void RaiseCompleted()
        {
            if (Completed != null)
            {
                Completed(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Copy contents of source into destination
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        public static void CopyStreamTo(Stream source, Stream destination)
        {
            var completedEvent = new ManualResetEvent(false);

            // copy as usual but listen for completion
            var copier = new AsyncStreamCopier();
            copier.Completed += (s, e) => completedEvent.Set();
            copier.Start(source, destination);

            completedEvent.WaitOne();
        }
    }
}
