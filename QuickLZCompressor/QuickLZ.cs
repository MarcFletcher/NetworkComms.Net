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
using System.Runtime.InteropServices;
using NetworkCommsDotNet.DPSBase;
using System.IO;

namespace QuickLZCompressor
{
    /// <summary>
    /// Compressor that utilizes native quicklz compression provided by the <see href="http://www.quicklz.com/">QuickLZ</see> library 
    /// </summary>
    [DataSerializerProcessor(3)]
    public class QuickLZ : DataProcessor
    {
        private static string DllDir = Path.Combine(Path.GetTempPath(), "QuickLZTemp");
        private static string dllName = Marshal.SizeOf(typeof(IntPtr)) == 8 ? "QuickLZCompressor.Dlls.quicklz150_64_1.dll" : "QuickLZCompressor.Dlls.quicklz150_32_1.dll";
        private static string dllFullName = Path.Combine(DllDir, Marshal.SizeOf(typeof(IntPtr)) == 8 ? "quicklz150_64_1.dll" : "quicklz150_32_1.dll");

        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr hModule);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr qlz_compress_del(IntPtr source, byte[] destination, IntPtr size, byte[] scratch);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr qlz_decompress_del(byte[] source, byte[] destination, byte[] scratch);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr qlz_size_compressed_del(byte[] source);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int qlz_get_settings_del(int setting);

        private static qlz_compress_del qlz_compress;
        private static qlz_decompress_del qlz_decompress;
        private static qlz_size_compressed_del qlz_size_decompressed;
        private static qlz_get_settings_del qlz_get_setting;

        private byte[] state_compress;
        private byte[] state_decompress;
                       
        /// <summary>
        /// Testing confirmed the decompress methods within quickLZ do not appear to be thread safe. No testing done on compress but also locked incase.
        /// </summary>
        private static object compressDecompressLocker = new object();

        static DataProcessor instance;

        /// <summary>
        /// Instance singleton used to access <see cref="NetworkCommsDotNet.DPSBase.DataProcessor"/> instance.  Obsolete, use instead <see cref="NetworkCommsDotNet.DPSBase.DPSManager.GetDataProcessor{T}"/>
        /// </summary>
        [Obsolete("Instance access via class obsolete, use DPSManager.GetDataProcessor<T>")]
        public static DataProcessor Instance
        {
            get
            {
                if (!Available)
                    throw new NotSupportedException("QuickLZ is not supported on this platform");

                if (instance == null)
                    instance = GetInstance<QuickLZ>();

                return instance;
            }
        }

        /// <summary>
        /// Returns true if running in windows and the native <see href="http://www.quicklz.com/">QuickLZ</see> library is available for execution.  False otherwise.  All function calls to <see cref="QuickLZCompressor.QuickLZ"/> will fail if Available returns false
        /// </summary>
        public static bool Available { get; private set; }

        static QuickLZ()
        {
            //This is a check to see if we're running in linux
            if (System.Environment.OSVersion.Platform == PlatformID.MacOSX ||
                System.Environment.OSVersion.Platform == PlatformID.Unix ||
                (int)System.Environment.OSVersion.Platform == 128)
            {
                Available = false;
                return;
            }

            try
            {

                if (!Directory.Exists(DllDir) || !File.Exists(dllFullName))
                {
                    if (!Directory.Exists(DllDir))
                        Directory.CreateDirectory(DllDir);

                    if (!File.Exists(dllFullName))
                    {
                        using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(dllName))
                        {
                            using (FileStream fs = new FileStream(dllFullName, FileMode.CreateNew))
                            {
                                byte[] bytes = new byte[s.Length];
                                s.Read(bytes, 0, bytes.Length);
                                fs.Write(bytes, 0, bytes.Length);
                            }
                        }
                    }
                }

                IntPtr dllPtr = LoadLibrary(dllFullName);

                IntPtr procAddress = GetProcAddress(dllPtr, "qlz_compress");
                qlz_compress = (qlz_compress_del)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(qlz_compress_del));
                procAddress = GetProcAddress(dllPtr, "qlz_decompress");
                qlz_decompress = (qlz_decompress_del)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(qlz_decompress_del));
                procAddress = GetProcAddress(dllPtr, "qlz_size_decompressed");
                qlz_size_decompressed = (qlz_size_compressed_del)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(qlz_size_compressed_del));
                procAddress = GetProcAddress(dllPtr, "qlz_get_setting");
                qlz_get_setting = (qlz_get_settings_del)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(qlz_get_settings_del));

                AppDomain.CurrentDomain.ProcessExit += new EventHandler((o, e) =>
                {
                    try
                    {
                        if (dllPtr != IntPtr.Zero)
                            FreeLibrary(dllPtr);
                    }
                    catch (Exception) { }
                });

                Available = true;
            }
            catch (EntryPointNotFoundException)
            {
                Available = false;
            }                
            catch (Exception ex)
            {
                Available = false;
                using (StreamWriter sw = new StreamWriter("quickLZLoadError.txt", false))
                    sw.WriteLine(ex.ToString());
            }
        }

        private QuickLZ()
        {
            if (!Available)
                return;

            state_compress = new byte[qlz_get_setting(1)];

            if (QLZ_STREAMING_BUFFER == 0)
                state_decompress = state_compress;
            else
                state_decompress = new byte[qlz_get_setting(2)];
        }

        private void Compress(byte[] Source, byte[] dest, out int destLength)
        {
            GCHandle handle = GCHandle.Alloc(Source);

            try
            {
                IntPtr sourcePtr = Marshal.UnsafeAddrOfPinnedArrayElement(Source, 0);
                destLength = (int)qlz_compress(sourcePtr, dest, (IntPtr)Source.Length, state_compress);
            }
            finally
            {
                handle.Free();
            }
        }

        private void Compress(IntPtr Source, int sourceSizeInBytes, byte[] dest, out int destLength)
        {
            destLength = (int)qlz_compress(Source, dest, (IntPtr)sourceSizeInBytes, state_compress);
        }

        private byte[] Decompress(byte[] Source)
        {
            byte[] d = null;

            d = new byte[(uint)qlz_size_decompressed(Source)];

            uint s;

            s = (uint)qlz_decompress(Source, d, state_decompress);

            return d;
        }

        private uint QLZ_STREAMING_BUFFER
        {
            get
            {
                return (uint)qlz_get_setting(3);
            }
        }

        #region ICompress Members

        /// <inheritdoc />
        public override void ForwardProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            if (!Available)
                throw new NotSupportedException("QuickLZ is not supported on this platform");

            // Testing confirmed the decompress methods within quickLZ do not appear to be thread safe. No testing done on compress but also locked incase.            
            lock (compressDecompressLocker)
            {
                byte[] inBytes = new byte[inStream.Length];
                inStream.Read(inBytes, 0, inBytes.Length);

                byte[] temp = new byte[inBytes.Length + 400];
                int length = 0;
                
                Compress(inBytes, temp, out length);
                outStream.Write(temp, 0, length);

                writtenBytes = length;
                
            }
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            if (!Available)
                throw new NotSupportedException("QuickLZ is not supported on this platform");

            // Testing confirmed the decompress methods within quickLZ do not appear to be thread safe. No testing done on compress but also locked incase.            
            lock (compressDecompressLocker)
            {
                byte[] inBytes = new byte[inStream.Length];
                inStream.Read(inBytes, 0, (int)inStream.Length);
                
                var temp = Decompress(inBytes);
                outStream.Write(temp, 0, temp.Length);
                writtenBytes = temp.Length;
            }
        }

        #endregion
    }
}
