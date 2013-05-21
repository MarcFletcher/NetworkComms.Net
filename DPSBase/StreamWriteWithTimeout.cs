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

            int writeWaitTimeMS = Math.Max(minTimeoutMS, (int)(((bufferLength < writeBufferSize ? bufferLength : writeBufferSize) / 1024.0) * timeoutMSPerKBWrite));

            System.Diagnostics.Stopwatch timerTotal = new System.Diagnostics.Stopwatch();
            timerTotal.Start();
             
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
                {
//#if !WINDOWS_PHONE
//                    using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
//                        AppendStringToLogFile("WriteWithTimeLog_" + process.Id, "Write timed out after " + writeWaitTimeMS.ToString() + "ms, while writing " + writeCountBytes + " bytes.");
//#endif
                    throw new TimeoutException("Write timed out after " + writeWaitTimeMS.ToString() + "ms");
                }


                if (innerException != null)
                    throw innerException;

                totalBytesCompleted += writeCountBytes;
            } while (totalBytesCompleted < bufferLength);

            timerTotal.Stop();

            double writeTimePerKBms = 0;
            if (bufferLength > 0)
                writeTimePerKBms = (double)timerTotal.ElapsedMilliseconds * 1024.0 / bufferLength;

//#if !WINDOWS_PHONE
//            using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
//                AppendStringToLogFile("WriteWithTimeLog_" + process.Id, "Write succeded using " + writeWaitTimeMS.ToString() + "ms, using buffer of " + sendBuffer.Length.ToString() + " bytes, average write time was " + writeTimePerKBms.ToString("0.00") + " ms/KB.  timeoutMSPerKBWrite was " + timeoutMSPerKBWrite);
//#endif

            return writeTimePerKBms;
        }

        /// <summary>
        /// Write the provided input stream to the destination stream in chunks of writeBufferSize. Throws exception if any write takes longer than timeoutPerByteWriteMS.
        /// </summary>
        /// <param name="inputStream">Input stream continaing data to send</param>
        /// <param name="inputStart">The start position in sendBuffer</param>
        /// <param name="inputLength">The number of bytes to write</param>
        /// <param name="destinationStream">The destination stream</param>
        /// <param name="writeBufferSize">The size in bytes of each successive write, recommended 8K</param>
        /// <param name="timeoutMSPerKBWrite">The maximum time to allow for write to complete per KB</param>
        /// <param name="minTimeoutMS">The minimum time to wait per write, this takes priority over other values.</param>
        /// <returns>The average time in milliseconds per KB written</returns>
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

            System.Diagnostics.Stopwatch timerTotal = new System.Diagnostics.Stopwatch();
            timerTotal.Start();

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
                {
//#if !WINDOWS_PHONE
//                    using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
//                        AppendStringToLogFile("WriteWithTimeLog_" + process.Id, "Write timed out after " + writeWaitTimeMS.ToString() + "ms, while writing " + writeCountBytes + " bytes.");
//#endif
                    throw new TimeoutException("Write timed out after " + writeWaitTimeMS.ToString() + "ms");
                }


                if (innerException != null)
                    throw innerException;

                totalBytesCompleted += writeCountBytes;

            } while (totalBytesCompleted < inputLength);

            timerTotal.Stop();

            double writeTimePerKBms = 0;
            if (inputLength > 0)
                writeTimePerKBms = (double)timerTotal.ElapsedMilliseconds * 1024.0 / inputLength;

//#if !WINDOWS_PHONE
//            using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
//                AppendStringToLogFile("WriteWithTimeLog_" + process.Id, "Write succeded using " + writeWaitTimeMS.ToString() + "ms, using buffer of " + sendBuffer.Length.ToString() + " bytes, average write time was " + writeTimePerKBms.ToString("0.00") + " ms/KB.  timeoutMSPerKBWrite was " + timeoutMSPerKBWrite);
//#endif

            return writeTimePerKBms;
        }

        /// <summary>
        /// Locker for LogError() which ensures thread safe saves.
        /// </summary>
        static object errorLocker = new object();

        /// <summary>
        /// Appends the provided logString to end of fileName.txt. If the file does not exist it will be created.
        /// </summary>
        /// <param name="fileName">The filename to use. The extension .txt will be appended automatically</param>
        /// <param name="logString">The string to append.</param>
        static void AppendStringToLogFile(string fileName, string logString)
        {
            try
            {
                lock (errorLocker)
                {
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fileName + ".txt", true))
                        sw.WriteLine(DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "] " + logString);
                }
            }
            catch (Exception)
            {
                //If an error happens here, such as if the file is locked then we lucked out.
            }
        }
    }
}
