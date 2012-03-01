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
using System.Runtime.InteropServices;
using SerializerBase;
using System.IO;

namespace QuickLZCompressor
{
    /// <summary>
    /// Compressor that utilizes native quicklz compression provided by QuickLZ library at http://www.quicklz.com/
    /// </summary>
    public class QuickLZ : ICompress
    {
        [DllImport("Dlls/quicklz150_32_1.dll", EntryPoint = "qlz_compress")]
        private static extern IntPtr qlz_compress_32(IntPtr source, byte[] destination, IntPtr size, byte[] scratch);
        [DllImport("Dlls//quicklz150_32_1.dll", EntryPoint = "qlz_decompress")]
        private static extern IntPtr qlz_decompress_32(byte[] source, byte[] destination, byte[] scratch);
        [DllImport("Dlls//quicklz150_32_1.dll", EntryPoint = "qlz_size_decompressed")]
        private static extern IntPtr qlz_size_decompressed_32(byte[] source);
        [DllImport("Dlls//quicklz150_32_1.dll", EntryPoint = "qlz_get_setting")]
        private static extern int qlz_get_setting_32(int setting);

        [DllImport("Dlls/quicklz150_64_1.dll", EntryPoint = "qlz_compress")]
        private static extern IntPtr qlz_compress_64(IntPtr source, byte[] destination, IntPtr size, byte[] scratch);
        [DllImport("Dlls//quicklz150_64_1.dll", EntryPoint = "qlz_decompress")]
        private static extern IntPtr qlz_decompress_64(byte[] source, byte[] destination, byte[] scratch);
        [DllImport("Dlls//quicklz150_64_1.dll", EntryPoint = "qlz_size_decompressed")]
        private static extern IntPtr qlz_size_decompressed_64(byte[] source);
        [DllImport("Dlls//quicklz150_64_1.dll", EntryPoint = "qlz_get_setting")]
        private static extern int qlz_get_setting_64(int setting);

        private byte[] state_compress;
        private byte[] state_decompress;

        private bool arch_x86;

        private static QuickLZ instance;
        private static object locker = new object();

        /// <summary>
        /// Testing confirmed the decompress methods within quickLZ do not appear to be thread safe. No testing done on compress but also locked incase.
        /// </summary>
        private static object compressDecompressLocker = new object();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static QuickLZ Instance
        {
            get
            {
                lock (locker)
                    if (instance == null)
                        instance = new QuickLZ();

                return instance;
            }
        }

        private QuickLZ()
        {
            arch_x86 = Marshal.SizeOf(typeof(IntPtr)) == 4;

            if (arch_x86)
                state_compress = new byte[qlz_get_setting_32(1)];
            else
                state_compress = new byte[qlz_get_setting_64(1)];

            if (QLZ_STREAMING_BUFFER == 0)
                state_decompress = state_compress;
            else
            {
                if (arch_x86)
                    state_decompress = new byte[qlz_get_setting_32(2)];
                else
                    state_decompress = new byte[qlz_get_setting_64(2)];
            }
        }

        private void Compress(byte[] Source, byte[] dest, out int destLength)
        {
            GCHandle handle = GCHandle.Alloc(Source);

            try
            {
                IntPtr sourcePtr = Marshal.UnsafeAddrOfPinnedArrayElement(Source, 0);

                if (arch_x86)
                    destLength = (int)qlz_compress_32(sourcePtr, dest, (IntPtr)Source.Length, state_compress);
                else
                    destLength = (int)qlz_compress_64(sourcePtr, dest, (IntPtr)Source.Length, state_compress);
            }
            finally
            {
                handle.Free();
            }
        }

        private void Compress(IntPtr Source, int sourceSizeInBytes, byte[] dest, out int destLength)
        {
            if (arch_x86)
                destLength = (int)qlz_compress_32(Source, dest, (IntPtr)sourceSizeInBytes, state_compress);
            else
                destLength = (int)qlz_compress_64(Source, dest, (IntPtr)sourceSizeInBytes, state_compress);
        }

        private byte[] Decompress(byte[] Source)
        {
            byte[] d = null;

            if (arch_x86)
                d = new byte[(uint)qlz_size_decompressed_32(Source)];
            else
                d = new byte[(uint)qlz_size_decompressed_64(Source)];

            uint s;

            if (arch_x86)
                s = (uint)qlz_decompress_32(Source, d, state_decompress);
            else
                s = (uint)qlz_decompress_64(Source, d, state_decompress);

            return d;
        }

        private uint QLZ_STREAMING_BUFFER
        {
            get
            {
                if (arch_x86)
                    return (uint)qlz_get_setting_32(3);
                else
                    return (uint)qlz_get_setting_64(3);
            }
        }

        #region ICompress Members

        /// <summary>
        /// Compresses data held in inStream to a byte array appended with the uncompressed size in bytes
        /// </summary>
        /// <param name="inStream">Stream containing the data to compress</param>
        /// <returns>Array of compressed data appended with size of uncompressed data</returns>
        public byte[] CompressDataStream(System.IO.Stream inStream)
        {
            /// <summary>
            /// Testing confirmed the decompress methods within quickLZ do not appear to be thread safe. No testing done on compress but also locked incase.
            /// </summary>
            lock (compressDecompressLocker)
            {
                byte[] inBytes = new byte[inStream.Length];
                inStream.Seek(0, 0);
                inStream.Read(inBytes, 0, inBytes.Length);

                byte[] temp = new byte[inBytes.Length + 400];
                int length = 0;

                Compress(inBytes, temp, out length);

                byte[] result = new byte[length + 8];
                Buffer.BlockCopy(temp, 0, result, 0, length);
                Buffer.BlockCopy(BitConverter.GetBytes((ulong)inStream.Length), 0, result, length, 8);

                return result;
            }
        }

        /// <summary>
        /// Decompresses data from inBytes array into outputStream
        /// </summary>
        /// <param name="inBytes">Compressed array from CompressDataStream method</param>
        /// <param name="outputStream">Stream to decompress into</param>
        public void DecompressToStream(byte[] inBytes, Stream outputStream)
        {
            /// <summary>
            /// Testing confirmed the decompress methods within quickLZ do not appear to be thread safe. No testing done on compress but also locked incase.
            /// </summary>
            lock (compressDecompressLocker)
            {
                var temp = Decompress(inBytes);
                outputStream.Write(temp, 0, temp.Length);
            }
        }

        #endregion
    }
}
