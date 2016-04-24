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
