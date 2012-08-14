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
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.ComponentModel.Composition;

namespace SerializerBase
{
    /// <summary>
    /// Serializer that uss .Net built in BinaryFormatter
    /// </summary>
    public class BinaryFormaterSerializer : ISerialize
    {
        static ISerialize instance;

        /// <summary>
        /// Instance singleton
        /// </summary>
        [Obsolete("Instance access via class obsolete, use WrappersHelper.GetSerializer")]
        public static ISerialize Instance
        {
            get
            {
                if (instance == null)
                    instance = GetInstance<BinaryFormaterSerializer>();

                return instance;
            }
        }

        private BinaryFormaterSerializer() { }

        #region ISerialize Members

        public override byte Identifier { get { return 2; } }
        
        /// <summary>
        /// Serializes objectToSerialize to a byte array using compression provided by compressor
        /// </summary>
        /// <typeparam name="T">Type paramter of objectToSerialize</typeparam>
        /// <param name="objectToSerialise">Object to serialize.  Must be marked Serializable</param>
        /// <param name="compressor">The compression provider to use</param>
        /// <returns>The serialized and compressed bytes of objectToSerialize</returns>
        protected override byte[] SerialiseDataObjectInt(object objectToSerialise, ICompress compressor)
        {
            var baseRes = ArraySerializer.SerialiseArrayObject(objectToSerialise, compressor);

            if (baseRes != null)
                return baseRes;

            BinaryFormatter formatter = new BinaryFormatter();

            using (MemoryStream mem = new MemoryStream())
            {
                formatter.Serialize(mem, objectToSerialise);
                mem.Seek(0, 0);
                return compressor.CompressDataStream(mem);
            }        
        }

        /// <summary>
        /// Deserializes data object held as compressed bytes in receivedObjectBytes using compressor and BinaryFormatter
        /// </summary>
        /// <typeparam name="T">Type parameter of the resultant object</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>The deserialized object</returns>
        protected override object DeserialiseDataObjectInt(byte[] receivedObjectBytes, Type resultType, ICompress compressor)
        {
            var baseRes = ArraySerializer.DeserialiseArrayObject(receivedObjectBytes, resultType, compressor);

            if (baseRes != null)
                return baseRes;

            BinaryFormatter formatter = new BinaryFormatter();
            MemoryStream stream = new MemoryStream();
            compressor.DecompressToStream(receivedObjectBytes, stream);
            stream.Seek(0,0);

            return formatter.Deserialize(stream);
        }

        #endregion
    }
}
