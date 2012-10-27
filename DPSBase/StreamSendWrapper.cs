using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace DPSBase
{
    /// <summary>
    /// Used to send all or parts of a stream. Usefull for sending files directly from disk etc.
    /// </summary>
    public class StreamSendWrapper
    {
        /// <summary>
        /// The wrapped stream
        /// </summary>
        public Stream Stream { get; private set; }
        /// <summary>
        /// The start position to read from Stream
        /// </summary>
        public int Start { get; private set; }
        /// <summary>
        /// The number of bytes to read from Stream
        /// </summary>
        public int Length { get; private set; }

        public StreamSendWrapper(Stream stream, int start, int length)
        {
            this.Stream = stream;
            this.Start = start;
            this.Length = length;
        }

        /// <summary>
        /// Read all data from the stream ignoring start and length properties
        /// </summary>
        /// <returns></returns>
        public byte[] AllBytes()
        {
            byte[] returnData = new byte[Stream.Length];
            Stream.Read(returnData, 0, returnData.Length);
            return returnData;
        }

        /// <summary>
        /// Returns data from the stream specified by start and length properties
        /// </summary>
        /// <returns></returns>
        public byte[] Bytes()
        {
            byte[] returnData = new byte[Length];
            Stream.Read(returnData, Start, returnData.Length);
            return returnData;
        }
    }
}
