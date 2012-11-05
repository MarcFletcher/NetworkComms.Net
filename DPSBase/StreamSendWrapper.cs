using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace DPSBase
{
    /// <summary>
    /// Used to send all or parts of a stream. Particularly usefull for sending files directly from disk etc.
    /// </summary>
    public class StreamSendWrapper
    {
        object streamLocker = new object();

        /// <summary>
        /// The wrapped stream
        /// </summary>
        public ThreadSafeStream ThreadSafeStream { get; set; }
        /// <summary>
        /// The start position to read from Stream
        /// </summary>
        public int Start { get; private set; }
        /// <summary>
        /// The number of bytes to read from Stream
        /// </summary>
        public int Length { get; private set; }

        /// <summary>
        /// Create a new stream wrapper and set Start and Length to encompass the entire Stream
        /// </summary>
        /// <param name="stream">The underlying stream</param>
        public StreamSendWrapper(ThreadSafeStream stream)
        {
            if (Length > int.MaxValue) throw new NotImplementedException("Streams larger than 2GB are not yet supported.");

            this.ThreadSafeStream = stream;
            this.Start = 0;
            this.Length = (int)stream.Length;
        }

        /// <summary>
        /// Create a new stream wrapper
        /// </summary>
        /// <param name="stream">The underlying stream</param>
        /// <param name="start">The start position from where to read data</param>
        /// <param name="length">The length to read</param>
        public StreamSendWrapper(ThreadSafeStream stream, long start, long length)
        {
            if (Start > int.MaxValue) throw new NotImplementedException("Streams larger than 2GB are not yet supported.");
            if (Length > int.MaxValue) throw new NotImplementedException("Streams larger than 2GB are not yet supported.");

            this.ThreadSafeStream = stream;
            this.Start = (int)start;
            this.Length = (int)length;
        }

        /// <summary>
        /// Return the MD5 for the specific part of the stream only.
        /// </summary>
        /// <returns></returns>
        public string MD5CheckSum()
        {
            throw new NotImplementedException();
        }
    }
}
