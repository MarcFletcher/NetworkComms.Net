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
        public event EventHandler Completed;

        private readonly Stream input;
        private readonly Stream output;

        private byte[] buffer = new byte[4096];

        public AsyncStreamCopier(Stream input, Stream output)
        {
            this.input = input;
            this.output = output;
        }

        public void Start()
        {
            GetNextChunk();
        }

        private void GetNextChunk()
        {
            input.BeginRead(buffer, 0, buffer.Length, InputReadComplete, null);
        }

        private void InputReadComplete(IAsyncResult ar)
        {
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
            GetNextChunk();
        }

        private void RaiseCompleted()
        {
            if (Completed != null)
            {
                Completed(this, EventArgs.Empty);
            }
        }

        public static void CopyStreamTo(Stream source, Stream destination)
        {
            var completedEvent = new ManualResetEvent(false);

            // copy as usual but listen for completion
            var copier = new AsyncStreamCopier(source, destination);
            copier.Completed += (s, e) => completedEvent.Set();
            copier.Start();

            completedEvent.WaitOne();
        }
    }
}
