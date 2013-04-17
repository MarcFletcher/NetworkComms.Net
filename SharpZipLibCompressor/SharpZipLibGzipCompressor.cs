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
using ICSharpCode.SharpZipLib.GZip;
using DPSBase;
using System.IO;

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = MonoTouch.Foundation.PreserveAttribute;
#endif

namespace SharpZipLibCompressor
{
    /// <summary>
    /// Compresor using Gzip compression from <see href="http://www.icsharpcode.net/opensource/sharpziplib/">SharpZipLib</see>
    /// </summary>
    [DataSerializerProcessor(2)]
    public class SharpZipLibGzipCompressor : DataProcessor
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
                    instance = GetInstance<SharpZipLibGzipCompressor>();

                return instance;
            }
        }
#if ANDROID || iOS
        [Preserve]
#endif
        private SharpZipLibGzipCompressor() { }
        
        /// <inheritdoc />
        public override void ForwardProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            using (GZipOutputStream gzStream = new GZipOutputStream(outStream))
            {
                gzStream.IsStreamOwner = false;
                AsyncStreamCopier.CopyStreamTo(inStream, gzStream);
            }

            writtenBytes = outStream.Position;
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            using (GZipInputStream zip = new GZipInputStream(inStream))
                AsyncStreamCopier.CopyStreamTo(zip, outStream);

            writtenBytes = outStream.Position;
        }
    }
}
