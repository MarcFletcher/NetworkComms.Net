//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using NetworkCommsDotNet.DPSBase;
using System.IO;
using LZMA;

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = Foundation.PreserveAttribute;
#endif

namespace NetworkCommsDotNet.DPSBase.SevenZipLZMACompressor
{
    /// <summary>
    /// Compressor utilizing LZMA algorithm from <see href="http://www.7-zip.org/">7zip</see>
    /// </summary>  
    [DataSerializerProcessor(1)]
    public class LZMACompressor : DataProcessor
    {
        static DataProcessor instance;

        /// <summary>
        /// Instance singleton used to access <see cref="NetworkCommsDotNet.DPSBase.DataProcessor"/> instance.  Obsolete, use instead <see cref="NetworkCommsDotNet.DPSBase.DPSManager.GetDataProcessor{T}"/>
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

#if ANDROID || iOS
        [Preserve]
#endif
        private LZMACompressor() { }
        
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
