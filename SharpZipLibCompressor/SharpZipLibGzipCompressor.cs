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
using ICSharpCode.SharpZipLib.GZip;
using SerializerBase;
using System.IO;
using System.ComponentModel.Composition;

namespace SharpZipLibCompressor
{
    /// <summary>
    /// Compresor using Gzip compression from SharpZipLib http://www.icsharpcode.net/opensource/sharpziplib/
    /// </summary>
    [Export(typeof(ICompress))]
    public class SharpZipLibGzipCompressor : ICompress
    {
        static ICompress instance;

        /// <summary>
        /// Instance singleton
        /// </summary>
        [Obsolete("Instance access via class obsolete, use WrappersHelper.GetCompressor")]
        public static ICompress Instance
        {
            get
            {
                if (instance == null)
                    instance = GetInstance<SharpZipLibGzipCompressor>();

                return instance;
            }
        }

        private SharpZipLibGzipCompressor() { }

        public override byte Identifier { get { return 2; } }

        /// <summary>
        /// Compresses data in inStream to a byte array appending uncompressed data size
        /// </summary>
        /// <param name="inStream">Stream contaiing data to compress</param>
        /// <returns>Compressed data appended with uncompressed data size</returns>
        public override byte[] CompressDataStream(Stream inStream)
        {
            using (MemoryStream realOutStream = new MemoryStream())
            {
                using (GZipOutputStream outStream = new GZipOutputStream(realOutStream))
                {
                    outStream.IsStreamOwner = false;
                    inStream.CopyTo(outStream);
                }

                ulong nBytes = (ulong)inStream.Length;
                realOutStream.Write(BitConverter.GetBytes(nBytes), 0, 8);

                return realOutStream.ToArray();
            }            
        }

        /// <summary>
        /// Decompresses data from inBytes into outStream
        /// </summary>
        /// <param name="inBytes">Compressed data from CompressDataStream</param>
        /// <param name="outputStream">Stream to output uncompressed data to</param>
        public override void DecompressToStream(byte[] inBytes, Stream outputStream)
        {
            using (MemoryStream memIn = new MemoryStream(inBytes, 0, inBytes.Length - 8, false))
            {
                using (GZipInputStream zip = new GZipInputStream(memIn))
                {
                    zip.CopyTo(outputStream);
                }

                outputStream.Seek(0, 0);
            }
        }
    }
}
