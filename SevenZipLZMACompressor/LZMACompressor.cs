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
using DPSBase;
using System.IO;
using LZMA;
using System.ComponentModel.Composition;

namespace SevenZipLZMACompressor
{
    /// <summary>
    /// Compressor utilizing LZMA algorithm from 7Zip http://www.7-zip.org/    
    /// </summary>    
    public class LZMACompressor : DataProcessor
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
                    instance = GetInstance<LZMACompressor>();

                return instance;
            }
        }

        private LZMACompressor() { }

        public override byte Identifier { get { return 1; } }

        /// <summary>
        /// Compresses data in inStream to a byte array appending uncompressed data size
        /// </summary>
        /// <param name="inStream">Stream contaiing data to compress</param>
        /// <returns>Compressed data appended with uncompressed data size</returns>
        public override void ForwardProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            SevenZipHelper.CompressToStream(inStream, outStream);
            writtenBytes = outStream.Position;
        }

        /// <summary>
        /// Decompresses data from inBytes into outStream
        /// </summary>
        /// <param name="inBytes">Compressed data from CompressDataStream</param>
        /// <param name="outputStream">Stream to output uncompressed data to</param>
        public override void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {            
            SevenZipHelper.DecompressStreamToStream(inStream, outStream);
            writtenBytes = outStream.Position;
        }
        
    }
}
