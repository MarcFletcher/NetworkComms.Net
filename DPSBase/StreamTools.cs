using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

#if NETFX_CORE
using Windows.Storage.Streams;
using System.Threading.Tasks;
using Windows.Storage;
#endif

namespace DPSBase
{
    /// <summary>
    /// Wrapper class for writing to streams with time-outs. Used primarily to prevent stream write deadlocks.
    /// </summary>
    public static class StreamTools
    {
        #region Static Stream Tools
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

#if NETFX_CORE
                Func<Task> writeTask = new Func<Task>(async () => { await destinationStream.WriteAsync(sendBuffer, (int)totalBytesCompleted, writeCountBytes); });

                if (!writeTask().Wait(writeWaitTimeMS))
                {
#else
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
#endif
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
        /// <param name="inputStream">Input stream containing data to send</param>
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

#if NETFX_CORE
                Func<Task> writeTask = new Func<Task>(async () => { await destinationStream.WriteAsync(sendBuffer, (int)totalBytesCompleted, writeCountBytes); });

                if (!writeTask().Wait(writeWaitTimeMS))
                {
#else
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
#endif
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

#if NETFX_CORE
                    string toWrite = DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + Environment.CurrentManagedThreadId.ToString() + "] " + logString + Environment.NewLine;
                    
                    Func<Task> writeTask = new Func<Task>(async () =>
                    {
                        StorageFolder folder = ApplicationData.Current.LocalFolder;
                        StorageFile file = await folder.CreateFileAsync(fileName + ".txt", CreationCollisionOption.OpenIfExists);
                        await FileIO.AppendTextAsync(file, toWrite);
                    });

                    writeTask().Wait();
#else
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fileName + ".txt", true))
                        sw.WriteLine(DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "] " + logString);
#endif
                }
            }
            catch (Exception)
            {
                //If an error happens here, such as if the file is locked then we lucked out.
            }
        }

        /// <summary>
        /// Return the MD5 hash of the provided memory stream as a string. Stream position will be equal to the length of stream on 
        /// return, this ensures the MD5 is consistent.
        /// </summary>
        /// <param name="streamToMD5">The bytes which will be checksummed</param>
        /// <param name="start">The start position in the stream</param>
        /// <param name="length">The length in the stream to MD5</param>
        /// <returns>The MD5 checksum as a string</returns>
        public static string MD5(Stream streamToMD5, long start, int length)
        {
            if (streamToMD5 == null) throw new ArgumentNullException("streamToMD5", "Provided Stream cannot be null.");

            using (MemoryStream stream = new MemoryStream(length))
            {
                StreamTools.Write(streamToMD5, start, length, stream, 8000, 100, 2000);
                return MD5(stream);
            }
        }

        /// <summary>
        /// Return the MD5 hash of the provided byte array as a string
        /// </summary>
        /// <param name="bytesToMd5">The bytes which will be checksummed</param>
        /// <returns>The MD5 checksum as a string</returns>
        public static string MD5(byte[] bytesToMd5)
        {
            if (bytesToMd5 == null) throw new ArgumentNullException("bytesToMd5", "Provided byte[] cannot be null.");

            using (MemoryStream stream = new MemoryStream(bytesToMd5, 0, bytesToMd5.Length, false))
                return MD5(stream);
        }

        /// <summary>
        /// Return the MD5 hash of the provided memory stream as a string. Stream position will be equal to the length of stream on 
        /// return, this ensures the MD5 is consistent.
        /// </summary>
        /// <param name="streamToMD5">The bytes which will be checksummed</param>
        /// <returns>The MD5 checksum as a string</returns>
        public static string MD5(Stream streamToMD5)
        {
            if (streamToMD5 == null) throw new ArgumentNullException("streamToMD5", "Provided Stream cannot be null.");

            string resultStr;

#if NETFX_CORE
            var alg = Windows.Security.Cryptography.Core.HashAlgorithmProvider.OpenAlgorithm(Windows.Security.Cryptography.Core.HashAlgorithmNames.Md5);
            var buffer = (new Windows.Storage.Streams.DataReader(streamToMD5.AsInputStream())).ReadBuffer((uint)streamToMD5.Length);
            var hashedData = alg.HashData(buffer);
            resultStr = Windows.Security.Cryptography.CryptographicBuffer.EncodeToHexString(hashedData).Replace("-", "");
#else
            using (System.Security.Cryptography.HashAlgorithm md5 =
#if WINDOWS_PHONE
                new DPSBase.MD5Managed())
#else
 System.Security.Cryptography.MD5.Create())
#endif
            {
                //If we don't ensure the position is consistent the MD5 changes
                streamToMD5.Seek(0, SeekOrigin.Begin);
                resultStr = BitConverter.ToString(md5.ComputeHash(streamToMD5)).Replace("-", "");
            }
#endif
            return resultStr;
        }
        #endregion

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
                if (input == null) throw new ArgumentNullException("input");
                if (output == null) throw new ArgumentNullException("output");

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

        /// <summary>
        /// Used to send all or parts of a stream. Particularly useful for sending files directly from disk etc.
        /// </summary>
        public class StreamSendWrapper : IDisposable
        {
            object streamLocker = new object();

            /// <summary>
            /// The wrapped stream
            /// </summary>
            public ThreadSafeStream ThreadSafeStream { get; set; }
            /// <summary>
            /// The start position to read from Stream
            /// </summary>
            public long Start { get; private set; }
            /// <summary>
            /// The number of bytes to read from Stream
            /// </summary>
            public long Length { get; private set; }

            /// <summary>
            /// Create a new stream wrapper and set Start and Length to encompass the entire Stream
            /// </summary>
            /// <param name="stream">The underlying stream</param>
            public StreamSendWrapper(ThreadSafeStream stream)
            {
                this.ThreadSafeStream = stream;
                this.Start = 0;
                this.Length = stream.Length;
            }

            /// <summary>
            /// Create a new stream wrapper
            /// </summary>
            /// <param name="stream">The underlying stream</param>
            /// <param name="start">The start position from where to read data</param>
            /// <param name="length">The length to read</param>
            public StreamSendWrapper(ThreadSafeStream stream, long start, long length)
            {
                if (start < 0)
                    throw new Exception("Provided start value cannot be less than 0.");

                if (length < 0)
                    throw new Exception("Provided length value cannot be less than 0.");

                this.ThreadSafeStream = stream;
                this.Start = start;
                this.Length = length;
            }

            /// <summary>
            /// Return the MD5 for the specific part of the stream only.
            /// </summary>
            /// <returns></returns>
            public string MD5CheckSum()
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    ThreadSafeStream.CopyTo(ms, Start, Length, 8000);

#if NETFX_CORE
                var alg = Windows.Security.Cryptography.Core.HashAlgorithmProvider.OpenAlgorithm(Windows.Security.Cryptography.Core.HashAlgorithmNames.Md5);
                var buffer = (new Windows.Storage.Streams.DataReader(ms.AsInputStream())).ReadBuffer((uint)ms.Length);
                var hashedData = alg.HashData(buffer);
                return Windows.Security.Cryptography.CryptographicBuffer.EncodeToHexString(hashedData).Replace("-", "");
#else
#if WINDOWS_PHONE
                using(var md5 = new DPSBase.MD5Managed())
                {
#else
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
#endif
                        return BitConverter.ToString(md5.ComputeHash(ms)).Replace("-", "");
                    }
#endif
                }
            }

            /// <summary>
            /// Dispose the internal ThreadSafeStream
            /// </summary>
            public void Dispose()
            {
                ThreadSafeStream.Dispose();
            }
        }

        /// <summary>
        /// A wrapper around a stream to ensure it can be accessed in a thread safe way. The .net implementation of Stream.Synchronized is not suitable on its own.
        /// </summary>
        public class ThreadSafeStream : Stream, IDisposable
        {
            private Stream _innerStream;
            private object streamLocker = new object();

            /// <summary>
            /// If true the internal stream will be disposed once the data has been written to the network
            /// </summary>
            public bool CloseStreamAfterSend { get; private set; }

            /// <summary>
            /// Create a thread safe stream. Once any actions are complete the stream must be correctly disposed by the user.
            /// </summary>
            /// <param name="stream">The stream to make thread safe</param>
            public ThreadSafeStream(Stream stream)
            {
                this.CloseStreamAfterSend = false;
                this._innerStream = stream;
            }

            /// <summary>
            /// Create a thread safe stream.
            /// </summary>
            /// <param name="stream">The stream to make thread safe.</param>
            /// <param name="closeStreamAfterSend">If true the provided stream will be disposed once data has been written to the network. If false the stream must be disposed of correctly by the user</param>
            public ThreadSafeStream(Stream stream, bool closeStreamAfterSend)
            {
                this.CloseStreamAfterSend = closeStreamAfterSend;
                this._innerStream = stream;
            }

            /// <summary>
            /// The total length of the internal stream
            /// </summary>
            public override long Length
            {
                get { lock (streamLocker) return _innerStream.Length; }
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                lock (streamLocker) _innerStream.SetLength(value);
            }

            /// <summary>
            /// The current position of the internal stream
            /// </summary>
            public override long Position
            {
                get { lock (streamLocker) return _innerStream.Position; }
                set { lock (streamLocker) _innerStream.Position = value; }
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                lock (streamLocker) return _innerStream.Seek(offset, origin);
            }

            /// <summary>
            /// Returns data from entire Stream
            /// </summary>
            /// <param name="numberZeroBytesPrefex">If non zero will append N 0 value bytes to the start of the returned array</param>
            /// <returns></returns>
            public byte[] ToArray(int numberZeroBytesPrefex = 0)
            {
                lock (streamLocker)
                {
                    _innerStream.Seek(0, SeekOrigin.Begin);
                    byte[] returnData = new byte[_innerStream.Length + numberZeroBytesPrefex];
                    _innerStream.Read(returnData, numberZeroBytesPrefex, returnData.Length - numberZeroBytesPrefex);
                    return returnData;
                }
            }

            /// <summary>
            /// Returns data from the specified portion of Stream
            /// </summary>
            /// <param name="start">The start position of the desired bytes</param>
            /// <param name="length">The total number of desired bytes, not including the zero byte prefix and append parameters</param>
            /// <param name="numberZeroBytesPrefix">If non zero will append N 0 value bytes to the start of the returned array</param>
            /// <param name="numberZeroBytesAppend">If non zero will append N 0 value bytes to the end of the returned array</param>
            /// <returns></returns>
            public byte[] ToArray(long start, long length, int numberZeroBytesPrefix = 0, int numberZeroBytesAppend = 0)
            {
                if (length > int.MaxValue)
                    throw new ArgumentOutOfRangeException("length", "Unable to return array whose size is larger than int.MaxValue. Consider requesting multiple smaller arrays.");

                lock (streamLocker)
                {
                    if (start + length > _innerStream.Length)
                        throw new ArgumentOutOfRangeException("length", "Provided start and length parameters reference past the end of the available stream.");

                    _innerStream.Seek(start, SeekOrigin.Begin);
                    byte[] returnData = new byte[length + numberZeroBytesPrefix + numberZeroBytesAppend];
                    _innerStream.Read(returnData, numberZeroBytesPrefix, (int)length);
                    return returnData;
                }
            }

            /// <summary>
            /// Return the MD5 hash of the current <see cref="ThreadSafeStream"/> as a string
            /// </summary>
            /// <returns></returns>
            public string MD5()
            {
                lock (streamLocker)
                    return StreamTools.MD5(_innerStream);
            }

            /// <summary>
            /// Return the MD5 hash of part of the current <see cref="ThreadSafeStream"/> as a string
            /// </summary>
            /// <param name="start">The start position in the stream</param>
            /// <param name="length">The length of stream to MD5</param>
            /// <returns></returns>
            public string MD5(long start, int length)
            {
                using (MemoryStream partialStream = new MemoryStream(length))
                {
                    lock (streamLocker)
                    {
                        StreamTools.Write(_innerStream, start, length, partialStream, 8000, 1000, 500);
                        return StreamTools.MD5(partialStream);
                    }
                }
            }

            /// <summary>
            /// Writes all provided data to the internal stream starting at the provided position with the stream
            /// </summary>
            /// <param name="data"></param>
            /// <param name="startPosition"></param>
            public void Write(byte[] data, long startPosition)
            {
                if (data == null) throw new ArgumentNullException("data");

                lock (streamLocker)
                {
                    _innerStream.Seek(startPosition, SeekOrigin.Begin);
                    _innerStream.Write(data, 0, data.Length);
                    _innerStream.Flush();
                }
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (streamLocker) _innerStream.Write(buffer, offset, count);
            }

            /// <summary>
            /// Copies data specified by start and length properties from internal stream to the provided stream.
            /// </summary>
            /// <param name="destinationStream">The destination stream to write to</param>
            /// <param name="startPosition"></param>
            /// <param name="length"></param>
            /// <param name="writeBufferSize">The buffer size to use for copying stream contents</param>
            /// <param name="minTimeoutMS">The minimum time allowed for any sized copy</param>
            /// <param name="timeoutMSPerKBWrite">The timouts in milliseconds per KB to write</param>
            /// <returns>The average time in milliseconds per byte written</returns>
            public double CopyTo(Stream destinationStream, long startPosition, long length, int writeBufferSize, double timeoutMSPerKBWrite = 1000, int minTimeoutMS = 500)
            {
                lock (streamLocker)
                    return StreamTools.Write(_innerStream, startPosition, length, destinationStream, writeBufferSize, timeoutMSPerKBWrite, minTimeoutMS);
            }

            /// <summary>
            /// Attempts to return the buffer associated with the internal stream. In certain circumstances this is more efficient
            /// than copying the stream contents into a new buffer using ToArray. If the internal stream is not a memory stream 
            /// will throw InvalidCastException. If access to the buffer is not allowed will throw an UnauthorizedAccessException.
            /// </summary>
            /// <returns></returns>
            public byte[] GetBuffer()
            {
#if NETFX_CORE
            throw new  NotImplementedException("This method has not been implemented for Win RT");
#else
                MemoryStream _innerMemoryStream = _innerStream as MemoryStream;
                if (_innerMemoryStream != null)
                    return _innerMemoryStream.GetBuffer();
                else
                    throw new InvalidCastException("Unable to return stream buffer as inner stream is not a MemoryStream.");
#endif
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                lock (streamLocker) return _innerStream.Read(buffer, offset, count);
            }

            /// <summary>
            /// Call Dispose on the internal stream
            /// </summary>
            public new void Dispose()
            {
                lock (streamLocker) _innerStream.Dispose();
            }

            /// <summary>
            /// Call Close on the internal stream
            /// </summary>
#if NETFX_CORE
        public void Close()
#else
            public new void Close()
#endif
            {
#if NETFX_CORE
            Dispose();
#else
                lock (streamLocker) _innerStream.Close();
#endif
            }

            /// <inheritdoc />
            public override bool CanRead
            {
                get { lock (streamLocker) return _innerStream.CanRead; }
            }

            /// <inheritdoc />
            public override bool CanSeek
            {
                get { lock (streamLocker) return _innerStream.CanSeek; }
            }

            /// <inheritdoc />
            public override bool CanWrite
            {
                get { lock (streamLocker) return _innerStream.CanWrite; }
            }

            /// <inheritdoc />
            public override void Flush()
            {
                lock (streamLocker) _innerStream.Flush();
            }
        }
    }
}
