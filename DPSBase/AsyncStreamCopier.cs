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
#if !NETFX_CORE
        /// <summary>
        /// Event raised when copy has completed
        /// </summary>
        public event EventHandler Completed;

        private byte[] bufferA = new byte[4096];
        private byte[] bufferB = new byte[4096];
        private AutoResetEvent signal = new AutoResetEvent(true);

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
            input.BeginRead(bufferA, 0, bufferA.Length, InputReadComplete, new Stream[] { input, output });
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
            
            signal.WaitOne();

            var temp = bufferA;
            bufferA = bufferB;
            bufferB = temp;

            // write synchronously
            output.BeginWrite(bufferB, 0, bytesRead, (asyncRes) => { signal.Set(); }, streams);
            input.BeginRead(bufferA, 0, bufferA.Length, InputReadComplete, streams);
        }

        private void RaiseCompleted()
        {
            if (Completed != null)
            {
                Completed(this, EventArgs.Empty);
            }
        }
#endif
        /// <summary>
        /// Copy contents of source into destination
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        public static void CopyStreamTo(Stream source, Stream destination)
        {
#if NETFX_CORE
            var t = source.CopyToAsync(destination);
            t.Wait();
#else
            var completedEvent = new ManualResetEvent(false);
            
            // copy as usual but listen for completion
            var copier = new AsyncStreamCopier();
            copier.Completed += (s, e) => completedEvent.Set();
            copier.Start(source, destination);

            completedEvent.WaitOne();
#endif
        }
    }
}
