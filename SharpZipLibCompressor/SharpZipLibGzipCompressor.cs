// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
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
