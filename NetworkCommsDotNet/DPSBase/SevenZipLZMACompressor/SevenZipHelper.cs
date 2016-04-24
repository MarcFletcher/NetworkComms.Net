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
using System.IO;

#if NETFX_CORE
using ApplicationException = System.Exception;
#endif

namespace LZMA
{
    class DataErrorException : ApplicationException
    {
        public DataErrorException() : base("Data Error") { }
    }

    /// <summary>
    /// The exception that is thrown when the value of an argument is outside the allowable range.
    /// </summary>
    class InvalidParamException : ApplicationException
    {
        public InvalidParamException() : base("Invalid Parameter") { }
    }

    /// <summary>
    /// Provides the fields that represent properties idenitifiers for compressing.
    /// </summary>
    enum CoderPropID
    {
        /// <summary>
        /// Specifies size of dictionary.
        /// </summary>
        DictionarySize = 0x400,
        /// <summary>
        /// Specifies size of memory for PPM*.
        /// </summary>
        UsedMemorySize,
        /// <summary>
        /// Specifies order for PPM methods.
        /// </summary>
        Order,
        /// <summary>
        /// Specifies number of postion state bits for LZMA 
        /// </summary>
        PosStateBits = 0x440,
        /// <summary>
        /// Specifies number of literal context bits for LZMA 
        /// </summary>
        LitContextBits,
        /// <summary>
        /// Specifies number of literal position bits for LZMA
        /// </summary>
        LitPosBits,
        /// <summary>
        /// Specifies number of fast bytes for LZ*.
        /// </summary>
        NumFastBytes = 0x450,
        /// <summary>
        /// Specifies match finder. LZMA: "BT2", "BT4" or "BT4B".
        /// </summary>
        MatchFinder,
        /// <summary>
        /// Specifies number of passes.
        /// </summary>
        NumPasses = 0x460,
        /// <summary>
        /// Specifies number of algorithm.
        /// </summary>
        Algorithm = 0x470,
        /// <summary>
        /// Specifies multithread mode.
        /// </summary>
        MultiThread = 0x480,
        /// <summary>
        /// Specifies mode with end marker.
        /// </summary>
        EndMarker = 0x490
    };

    internal static class SevenZipHelper
    {

        static int dictionary = 1 << 23;

        // static Int32 posStateBits = 2;
        // static  Int32 litContextBits = 3; // for normal files
        // UInt32 litContextBits = 0; // for 32-bit data
        // static  Int32 litPosBits = 0;
        // UInt32 litPosBits = 2; // for 32-bit data
        // static   Int32 algorithm = 2;
        // static    Int32 numFastBytes = 128;

        static bool eos = false;





        static CoderPropID[] propIDs = 
				{
					CoderPropID.DictionarySize,
					CoderPropID.PosStateBits,
					CoderPropID.LitContextBits,
					CoderPropID.LitPosBits,
					CoderPropID.Algorithm,
					CoderPropID.NumFastBytes,
					CoderPropID.MatchFinder,
					CoderPropID.EndMarker
				};

        // these are the default properties, keeping it simple for now:
        static object[] properties = 
				{
					(Int32)(dictionary),
					(Int32)(2),
					(Int32)(3),
					(Int32)(0),
					(Int32)(2),
					(Int32)(128),
					"bt4",
					eos
				};

#if iOS
        //For iOS we create static instances to avoid significant memory usages
        //This means we can only compress/decompress in serial but that's most likely
        //acceptable on mobile platforms.
        static object staticEncoderLocker = new object();
        static LZMA.Encoder staticEncoder = new LZMA.Encoder();

        static object staticDecoderLocker = new object();
        static LZMA.Decoder staticDecoder = new LZMA.Decoder();
#endif

        internal static byte[] Compress(byte[] inputBytes)
        {
#if iOS
            lock (staticEncoderLocker)
            {
                LZMA.Encoder encoder = staticEncoder;
#else
                LZMA.Encoder encoder = new LZMA.Encoder();
#endif

                MemoryStream inStream = new MemoryStream(inputBytes);
                MemoryStream outStream = new MemoryStream();

                encoder.SetCoderProperties(propIDs, properties);
                encoder.WriteCoderProperties(outStream);
                long fileSize = inStream.Length;
                for (int i = 0; i < 8; i++)
                    outStream.WriteByte((Byte)(fileSize >> (8 * i)));
                encoder.Code(inStream, outStream, -1, -1);
                return outStream.ToArray();
#if iOS
            }
#endif
        }

        internal static void CompressToStream(Stream inStream, Stream outStream)
        {
#if iOS
            lock (staticEncoderLocker)
            {
                LZMA.Encoder encoder = staticEncoder;
#else
                LZMA.Encoder encoder = new LZMA.Encoder();
#endif

            encoder.SetCoderProperties(propIDs, properties);
            encoder.WriteCoderProperties(outStream);
            long fileSize = inStream.Length;
            for (int i = 0; i < 8; i++)
                outStream.WriteByte((Byte)(fileSize >> (8 * i)));
            encoder.Code(inStream, outStream, -1, -1);
#if iOS
            }
#endif
        }

        internal static byte[] Decompress(byte[] inputBytes)
        {
#if iOS
            lock (staticEncoderLocker)
            {
                LZMA.Decoder decoder = staticDecoder;
#else
                LZMA.Decoder decoder = new LZMA.Decoder();
#endif

            MemoryStream newInStream = new MemoryStream(inputBytes);
            newInStream.Seek(0, 0);
            MemoryStream newOutStream = new MemoryStream();

            byte[] properties2 = new byte[5];
            if (newInStream.Read(properties2, 0, 5) != 5)
                throw (new Exception("input .lzma is too short"));
            long outSize = 0;
            for (int i = 0; i < 8; i++)
            {
                int v = newInStream.ReadByte();
                if (v < 0)
                    throw (new Exception("Can't Read 1"));
                outSize |= ((long)(byte)v) << (8 * i);
            }
            decoder.SetDecoderProperties(properties2);

            long compressedSize = newInStream.Length - newInStream.Position;
            decoder.Code(newInStream, newOutStream, compressedSize, outSize);

            byte[] b = newOutStream.ToArray();

            return b;

#if iOS
            }
#endif
        }

        internal static byte[] DecompressFromStream(Stream newInStream)
        {
#if iOS
            lock (staticEncoderLocker)
            {
                LZMA.Decoder decoder = staticDecoder;
#else
                LZMA.Decoder decoder = new LZMA.Decoder();
#endif

            newInStream.Seek(0, 0);
            MemoryStream newOutStream = new MemoryStream();

            byte[] properties2 = new byte[5];
            if (newInStream.Read(properties2, 0, 5) != 5)
                throw (new Exception("input .lzma is too short"));
            long outSize = 0;
            for (int i = 0; i < 8; i++)
            {
                int v = newInStream.ReadByte();
                if (v < 0)
                    throw (new Exception("Can't Read 1"));
                outSize |= ((long)(byte)v) << (8 * i);
            }
            decoder.SetDecoderProperties(properties2);

            long compressedSize = newInStream.Length - newInStream.Position;
            decoder.Code(newInStream, newOutStream, compressedSize, outSize);

            byte[] b = newOutStream.ToArray();

            return b;

#if iOS
            }
#endif
        }

        internal static void DecompressStreamToStream(Stream inStream, Stream outStream)
        {
#if iOS
            lock (staticEncoderLocker)
            {
                LZMA.Decoder decoder = staticDecoder;
#else
                LZMA.Decoder decoder = new LZMA.Decoder();
#endif

            inStream.Seek(0, 0);

            byte[] properties2 = new byte[5];
            if (inStream.Read(properties2, 0, 5) != 5)
                throw (new Exception("input .lzma is too short"));
            long outSize = 0;
            for (int i = 0; i < 8; i++)
            {
                int v = inStream.ReadByte();
                if (v < 0)
                    throw (new Exception("Can't Read 1"));
                outSize |= ((long)(byte)v) << (8 * i);
            }
            decoder.SetDecoderProperties(properties2);

            long compressedSize = inStream.Length - inStream.Position;
            decoder.Code(inStream, outStream, compressedSize, outSize);

#if iOS
            }
#endif
        }
    }
}
