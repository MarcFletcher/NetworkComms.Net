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
    /// Compressor utilizing LZMA algorithm from <see href="http://www.7-zip.org/">7zip</see>
    /// </summary>    
    public class LZMACompressor : DataProcessor
    {
        static DataProcessor instance;

        /// <summary>
        /// Instance singleton used to access <see cref="DPSBase.DataProcessor"/> instance.  Obsolete, use instead <see cref="DPSBase.DPSManager.GetDataProcessor{T}"/>
        /// </summary>
        [Obsolete("Instance access via class obsolete, use DPSManager.GetDataProcessor<T>")]
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

        /// <inheritdoc />
        public override byte Identifier { get { return 1; } }

        /// <inheritdoc />
        public override void ForwardProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            SevenZipHelper.CompressToStream(inStream, outStream);
            writtenBytes = outStream.Position;
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {            
            SevenZipHelper.DecompressStreamToStream(inStream, outStream);
            writtenBytes = outStream.Position;
        }
        
    }
}
