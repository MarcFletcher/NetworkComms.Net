using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace DPSBase
{
    /// <summary>
    /// A wrapper around a stream to ensure it can be accessed in a thread safe way. The .net implementation of Stream.Synchronized is suitable on its own.
    /// </summary>
    public class ThreadSafeStream : IDisposable
    {
        private Stream stream;
        private object streamLocker = new object();

        public ThreadSafeStream(Stream stream)
        {
            if (stream.Length > int.MaxValue)
                throw new NotImplementedException("Streams larger than 2GB not yet supported.");

            this.stream = Stream.Synchronized(stream);
        }

        public long Length
        {
            get { lock (streamLocker) return stream.Length; }
        }

        public long Position
        {
            get { lock (streamLocker) return stream.Position; }
        }

        /// <summary>
        /// Returns data from Stream.ToArray()
        /// </summary>
        /// <param name="numberZeroBytesPrefex">If non zero will append N 0 value bytes to the start of the returned array</param>
        /// <returns></returns>
        public byte[] ToArray(int numberZeroBytesPrefex = 0)
        {
            lock (streamLocker)
            {
                byte[] returnData = new byte[stream.Length + numberZeroBytesPrefex];
                stream.Read(returnData, numberZeroBytesPrefex, returnData.Length - numberZeroBytesPrefex);
                return returnData;
            }
        }

        /// <summary>
        /// Return the MD5 hash of the current <see cref="StreamSendWrapper"/> as a string
        /// </summary>
        /// <param name="streamWrapper">All specified bytes will be read for the provided stream</param>
        /// <returns></returns>
        public string MD5CheckSum()
        {
            lock (streamLocker)
            {
                System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
                return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "");
            }
        }

        /// <summary>
        /// Writes all provided data to the internal stream starting at the provided position with the stream
        /// </summary>
        /// <param name="data"></param>
        /// <param name="startPosition"></param>
        public void Write(byte[] data, int startPosition)
        {
            lock (streamLocker)
            {
                stream.Seek(startPosition, SeekOrigin.Begin);
                stream.Write(data, 0, data.Length);
            }
        }

        /// <summary>
        /// Copies data specified by start and length properties from internal stream to the provided stream.
        /// </summary>
        /// <param name="destinationStream">The destination stream to write to</param>
        /// <param name="startPosition"></param>
        /// <param name="length"></param>
        public void CopyTo(Stream destinationStream, int startPosition, int length)
        {
            lock (streamLocker)
            {
                //Initialise the buffer at either the total length or 8KB, which ever is smallest
                byte[] buffer = new byte[length > 8192 ? 8192 : length];

                //Make sure we start in the write place
                stream.Seek(startPosition, SeekOrigin.Begin);
                int totalBytesCopied = 0;
                while (true)
                {
                    int bytesRemaining = length - totalBytesCopied;
                    int read = stream.Read(buffer, 0, (buffer.Length > bytesRemaining ? bytesRemaining : buffer.Length));
                    if (read <= 0) return;
                    destinationStream.Write(buffer, 0, read);
                    totalBytesCopied += read;
                }
            }
        }

        public void Dispose()
        {
            lock (streamLocker) stream.Dispose();
        }
    }
}
