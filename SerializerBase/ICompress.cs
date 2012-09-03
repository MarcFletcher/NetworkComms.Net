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
using System.IO;
using System.ComponentModel.Composition;

namespace SerializerBase
{
    [InheritedExport(typeof(DataProcessor))]
    public abstract class DataProcessor
    {
        protected static T GetInstance<T>() where T : DataProcessor
        {
            //this forces helper static constructor to be called and gets us an instance if composition worked
            var instance = ProcessorManager.Instance.GetDataProcessor<T>() as T;

            if (instance == null)
            {
                //if the instance is null the type was not added as part of composition
                //create a new instance of T and add it to helper as a compressor

                instance = typeof(T).GetConstructor(new Type[] { }).Invoke(new object[] { }) as T;
                ProcessorManager.Instance.AddDataProcessor(instance);
            }

            return instance;
        }

        /// <summary>
        /// Returns a unique identifier for the compressor type. Used in automatic serialization/compression detection
        /// </summary>
        public abstract byte Identifier { get; }

        ///// <summary>
        ///// Compress data held in a stream.  Last 8 bytes of output should contain uncompressed size of data as a ulong
        ///// </summary>
        ///// <param name="inStream">Input stream holding data to compress.  Stream must support reading and seeking</param>
        ///// <returns>Compressed data.  Last 8 bytes should contain uncompressed size of data as a ulong</returns>
        //public abstract byte[] CompressDataStreamToArray(Stream inStream, Dictionary<string, string> options);
        
        ///// <summary>
        ///// Decompress data to a stream.  Last 8 bytes of inBytes should contain uncompressed size of data as a ulong
        ///// </summary>
        ///// <param name="inBytes">Data to decompress. Last 8 bytes should contain uncompressed size of data as a ulong</param>
        ///// <param name="outputStream"></param>
        //public abstract void DecompressToStream(byte[] inBytes, Stream outputStream, Dictionary<string, string> options);

        /// <summary>
        /// Processes data held in a stream
        /// </summary>
        /// <param name="inStream"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public abstract void ForwardProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes);

        public abstract void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes);
    }
}
