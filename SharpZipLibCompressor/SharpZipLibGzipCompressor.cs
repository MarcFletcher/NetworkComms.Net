//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using NetworkCommsDotNet.DPSBase;
using System.IO;
using NetworkCommsDotNet.Tools;

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
        /// Instance singleton used to access <see cref="NetworkCommsDotNet.DPSBase.DataProcessor"/> instance.  Obsolete, use instead <see cref="NetworkCommsDotNet.DPSBase.DPSManager.GetDataProcessor{T}"/>
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
                byte[] buffer = new byte[4096];

                while (true)
                {                    
                    int readCount = inStream.Read(buffer, 0, buffer.Length);

                    if (readCount == 0)
                        break;

                    gzStream.Write(buffer, 0, readCount);
                }
            }

            writtenBytes = outStream.Position;
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            using (GZipInputStream zip = new GZipInputStream(inStream))
            {
                zip.IsStreamOwner = false;
                byte[] buffer = new byte[4096];

                while (true)
                {
                    var readCount = zip.Read(buffer, 0, buffer.Length);

                    if (readCount == 0)
                        break;

                    outStream.Write(buffer, 0, readCount);                    
                }
            }

            writtenBytes = outStream.Position;
        }
    }
}
