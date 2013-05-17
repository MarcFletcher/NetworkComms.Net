using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace DPSBase
{
    /// <summary>
    /// Wrapper class for writing to streams with timeouts. Used primarily to prevent stream write deadlocks.
    /// </summary>
    public static class StreamWriteWithTimeout
    {
        /// <summary>
        /// Write the provided sendbuffer to the destination stream in chunks of writeBufferSize. Throws exception if any write takes longer than timeoutPerByteWriteMS.
        /// Allows a minimum of 20 milliseconds for any write.
        /// </summary>
        /// <param name="sendBuffer">Buffer containing data to write</param>
        /// <param name="bufferLength">The number of bytes to write</param>
        /// <param name="destinationStream">The destination stream</param>
        /// <param name="writeBufferSize">The size in bytes of each successive write</param>
        /// <param name="timeoutMSPerKBWrite">The maximum time to allow for write to complete per KB</param>
        /// <param name="minTimeoutMS">The minimum time to allow for any sized write</param>
        /// <returns>The average time in milliseconds per KB written</returns>
        public static double Write(byte[] sendBuffer, int bufferLength, Stream destinationStream, int writeBufferSize, double timeoutMSPerKBWrite, int minTimeoutMS)
        {
            if (sendBuffer == null) throw new ArgumentNullException("sendBuffer");
            if (destinationStream == null) throw new ArgumentNullException("destinationStream");

            int totalBytesCompleted = 0;
            Exception innerException = null;
            AutoResetEvent writeCompletedEvent = new AutoResetEvent(false);

            int writeWaitTimeMS = Math.Max(minTimeoutMS, (int)((writeBufferSize / 1024.0) * timeoutMSPerKBWrite));

            DateTime startTime = DateTime.Now;
            do
            {
                int writeCountBytes = (bufferLength - totalBytesCompleted < writeBufferSize ? bufferLength - totalBytesCompleted : writeBufferSize);
                destinationStream.BeginWrite(sendBuffer, totalBytesCompleted, writeCountBytes, new AsyncCallback((state)=>
                    {
                        try
                        {
                            destinationStream.EndWrite(state);
                        }
                        catch (Exception ex)
                        {
                            innerException = ex;
                        }

                        writeCompletedEvent.Set();

                    }), null);

                if (!writeCompletedEvent.WaitOne(writeWaitTimeMS))
                    throw new TimeoutException("Write timed out after " + writeWaitTimeMS.ToString() + "ms");

                if (innerException != null)
                    throw innerException;

                totalBytesCompleted += writeCountBytes;
            } while (totalBytesCompleted < bufferLength);

            if (bufferLength > 0)
                return (DateTime.Now - startTime).TotalMilliseconds * 1024.0 / bufferLength;
            else
                return 0;
        }

        /// <summary>
        /// Write the provided input stream to the destination stream in chunks of writeBufferSize. Throws exception if any write takes longer than timeoutPerByteWriteMS.
        /// Allows a minimum of 20 milliseconds for any write.
        /// </summary>
        /// <param name="inputStream">Input stream continaing data to send</param>
        /// <param name="inputStart">The start position in sendBuffer</param>
        /// <param name="inputLength">The number of bytes to write</param>
        /// <param name="destinationStream">The destination stream</param>
        /// <param name="writeBufferSize">The size in bytes of each successive write, recommended 8K</param>
        /// <param name="timeoutMSPerKBWrite">The maximum time to allow for write to complete per KB</param>
        /// <param name="minTimeoutMS">The minimum time to wait per write, this takes priority over other values.</param>
        /// <returns>The average time in milliseconds per byte written</returns>
        public static double Write(Stream inputStream, long inputStart, long inputLength, Stream destinationStream, int writeBufferSize, double timeoutMSPerKBWrite, int minTimeoutMS)
        {
            if (inputStream == null) throw new ArgumentException("inputStream");
            if (destinationStream == null) throw new ArgumentException("destinationStream");

            //Make sure we start in the right place
            inputStream.Seek(inputStart, SeekOrigin.Begin);
            long totalBytesCompleted = 0;
            Exception innerException = null;
            AutoResetEvent writeCompletedEvent = new AutoResetEvent(false);

            byte[] sendBuffer = new byte[Math.Min(inputLength, writeBufferSize)];
            int writeWaitTimeMS = Math.Max(minTimeoutMS, (int)((sendBuffer.Length / 1024.0) * timeoutMSPerKBWrite));

            DateTime startTime = DateTime.Now;
            do
            {
                long bytesRemaining = inputLength - totalBytesCompleted;

                //writeCountBytes is the total number of bytes that need to be written to the destinationStream
                //The sendBuffer.Length can never be larger than int.maxValue
                //If the sendBuffer.Length is greater than bytesRemaining we use the bytesRemaining value as an int
                int writeCountBytes = inputStream.Read(sendBuffer, 0, (sendBuffer.Length > bytesRemaining ? (int)bytesRemaining : sendBuffer.Length));

                if (writeCountBytes <= 0)
                    break;

                if (!destinationStream.CanWrite) throw new Exception("Unable to write to provided destinationStream.");

                destinationStream.BeginWrite(sendBuffer, 0, writeCountBytes, new AsyncCallback((state) =>
                {
                    try
                    {
                        destinationStream.EndWrite(state);
                    }
                    catch (Exception ex)
                    {
                        innerException = ex;
                    }

                    writeCompletedEvent.Set();

                }), null);

                if (!writeCompletedEvent.WaitOne(writeWaitTimeMS))
                    throw new TimeoutException("Write timed out after " + writeWaitTimeMS.ToString() + "ms");

                if (innerException != null)
                    throw innerException;

                totalBytesCompleted += writeCountBytes;

            } while (totalBytesCompleted < inputLength);

            if (inputLength > 0)
                return (DateTime.Now - startTime).TotalMilliseconds * 1024.0 / inputLength;
            else
                return 0;
        }
    }
}
