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

namespace NetworkCommsDotNet
{
    /// <summary>
    /// An implemenaton of the Adler32 checksum algothrim. It's not a particularly accurate checksum compared with MD5 but it is about 10 times as fast to compute.
    /// </summary>
    public static class Adler32
    {
        /// <summary>
        /// Generate an Adler32 checksum value based on the provided byte array.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
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
    }
}
