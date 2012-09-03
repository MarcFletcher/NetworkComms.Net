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
    public class SharpZipLibGzipCompressor : DataProcessor
    {
        static DataProcessor instance;

        /// <summary>
        /// Instance singleton
        /// </summary>
        [Obsolete("Instance access via class obsolete, use WrappersHelper.GetCompressor")]
        public static DataProcessor Instance
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
        public override void ForwardProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            using (GZipOutputStream gzStream = new GZipOutputStream(outStream))
            {
                gzStream.IsStreamOwner = false;
                inStream.CopyTo(gzStream);
            }

            writtenBytes = outStream.Position;
        }

        /// <summary>
        /// Decompresses data from inBytes into outStream
        /// </summary>
        /// <param name="inBytes">Compressed data from CompressDataStream</param>
        /// <param name="outputStream">Stream to output uncompressed data to</param>
        public override void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            using (GZipInputStream zip = new GZipInputStream(inStream))
                zip.CopyTo(outStream);

            writtenBytes = outStream.Position;
        }
    }
}
