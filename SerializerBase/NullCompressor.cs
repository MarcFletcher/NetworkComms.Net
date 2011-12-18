//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace SerializerBase
{
    /// <summary>
    /// Compressor that does no compression. Simply appends the size of the input data on compression and strips this information on decompression
    /// </summary>
    public class NullCompressor : ICompress
    {
        static NullCompressor instance;
        static object locker = new object();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static NullCompressor Instance
        {
            get
            {
                lock (locker)
                {
                    if (instance == null)
                        instance = new NullCompressor();
                }

                return instance;
            }
        }

        private NullCompressor() { }
        
        /// <summary>
        /// Performs no compression just appends the size of the input data
        /// </summary>
        /// <param name="inStream">Stream containing input data</param>
        /// <returns>Array of input data bytes appended with data size</returns>
        public byte[] CompressDataStream(System.IO.Stream inStream)
        {
            inStream.Seek(0, 0);

            byte[] outBuffer = new byte[inStream.Length + 8];
            inStream.Read(outBuffer, 0, (int)inStream.Length);

            Buffer.BlockCopy(BitConverter.GetBytes((ulong)inStream.Length), 0, outBuffer, (int)inStream.Length, 8);
            
            return outBuffer;
        }

        /// <summary>
        /// Performcs no decompression simply strips last 8 bytes that should have contained data size
        /// </summary>
        /// <param name="inBytes">Bytes to decompress</param>
        /// <param name="outputStream">Stream to write output data to</param>
        public void DecompressToStream(byte[] inBytes, Stream outputStream)
        {
            outputStream.Write(inBytes, 0, inBytes.Length - 8);
        }
        
    }
}
