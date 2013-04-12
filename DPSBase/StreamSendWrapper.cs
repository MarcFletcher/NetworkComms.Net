//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
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
            throw new NotImplementedException();
        }
    }
}
