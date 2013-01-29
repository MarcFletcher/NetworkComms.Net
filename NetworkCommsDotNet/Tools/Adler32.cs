//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Implemenaton of the <see href="http://en.wikipedia.org/wiki/Adler-32">Adler32</see> checksum algorithm. 
    /// It is not a particularly reliable checksum compared with <see href="http://en.wikipedia.org/wiki/MD5">MD5</see> but it is about 10 times faster.
    /// NetworkCommsDotNet uses <see href="http://en.wikipedia.org/wiki/MD5">MD5</see> as its default but this class is provided should speed be the more important factor.
    /// </summary>
    public static class Adler32Checksum
    {
        /// <summary>
        /// Generate an Adler32 checksum value based on the provided byte array.
        /// </summary>
        /// <param name="buffer">Buffer for which the checksum should be calculated.</param>
        /// <returns>The checksum value</returns>
        public static long GenerateCheckSum(byte[] buffer)
        {
            uint BASE = 65521;
            uint checksum = 1;

            int count = buffer.Length;
            int offset = 0;

            uint s1 = checksum & 0xFFFF;
            uint s2 = checksum >> 16;

            while (count > 0)
            {                
                int n = 3800;
                if (n > count)
                {
                    n = count;
                }
                count -= n;
                while (--n >= 0)
                {
                    s1 = s1 + (uint)(buffer[offset++] & 0xff);
                    s2 = s2 + s1;
                }
                s1 %= BASE;
                s2 %= BASE;
            }

            checksum = (s2 << 16) | s1;

            return checksum;
        }

        /// <summary>
        /// Generate a single Adler32 checksum value based on the provided byte arrays. Checksum calculated from splitBuffer[0] onwards.
        /// </summary>
        /// <param name="splitBuffer">Buffers for which the checksum should be calculated.</param>
        /// <returns>The checksum value</returns>
        public static long GenerateCheckSum(byte[][] splitBuffer)
        {
            uint BASE = 65521;
            uint checksum = 1;

            int count = 0;
            for (int i = 0; i < splitBuffer.Length; ++i)
                count += splitBuffer[i] == null ? 0 : splitBuffer[i].Length;

            int offset = 0;
            int currentIndex = 0;

            uint s1 = checksum & 0xFFFF;
            uint s2 = checksum >> 16;

            while (count > 0)
            {
                int n = 3800;
                if (n > count)
                {
                    n = count;
                }
                count -= n;
                while (--n >= 0)
                {
                    s1 = s1 + (uint)(splitBuffer[currentIndex][offset++] & 0xff);
                    s2 = s2 + s1;

                    if (offset >= splitBuffer[currentIndex].Length)
                    {
                        offset = 0;
                        currentIndex++;
                    }
                }
                s1 %= BASE;
                s2 %= BASE;
            }

            checksum = (s2 << 16) | s1;

            return checksum;
        }
    }
}
