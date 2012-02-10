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
using ProtoBuf;
using System.IO;
using System.Runtime.InteropServices;
using ProtoBuf.Meta;

namespace SerializerBase.Protobuf
{
    /// <summary>
    /// Serializer using ProtoBuf.Net
    /// </summary>
    public class ProtobufSerializer : ArraySerializer
    {
        static ProtobufSerializer instance;
        static object locker = new object();

        private static int metaDataTimeoutMS = 150000;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static ProtobufSerializer Instance
        {
            get
            {
                lock (locker)
                    if (instance == null)
                        instance = new ProtobufSerializer();

                return instance;
            }
        }

        private ProtobufSerializer() { }

        #region ISerialize Members

        /// <summary>
        /// Serializes objectToSerialize to a byte array using compression provided by compressor
        /// </summary>
        /// <typeparam name="T">Type paramter of objectToSerialize</typeparam>
        /// <param name="objectToSerialise">Object to serialize.  Must be marked with ProtoContract and members to serialize marked as protoMembers</param>
        /// <param name="compressor">The compression provider to use</param>
        /// <returns>The serialized and compressed bytes of objectToSerialize</returns>
        public override byte[] SerialiseDataObject<T>(T objectToSerialise, ICompress compressor)
        {
            //Increase timeout to prevent errors when CPU busy
            RuntimeTypeModel.Default.MetadataTimeoutMilliseconds = metaDataTimeoutMS;

            var baseRes = base.SerialiseDataObject<T>(objectToSerialise, compressor);

            if (baseRes != null)
                return baseRes;

            MemoryStream memIn = new MemoryStream();
            Serializer.Serialize(memIn, objectToSerialise);
            memIn.Seek(0, 0); 
          
            return compressor.CompressDataStream(memIn);
        }

        /// <summary>
        /// Deserializes data object held as compressed bytes in receivedObjectBytes using compressor and ProtoBuf.Net
        /// </summary>
        /// <typeparam name="T">Type parameter of the resultant object</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>The deserialized object</returns>
        public override T DeserialiseDataObject<T>(byte[] receivedObjectBytes, ICompress compressor)
        {
            //Increase timeout to prevent errors when CPU busy
            RuntimeTypeModel.Default.MetadataTimeoutMilliseconds = metaDataTimeoutMS;

            var baseRes = base.DeserialiseDataObject<T>(receivedObjectBytes, compressor);

            if (!Equals(baseRes, default(T)))
                return baseRes;

            MemoryStream stream = new MemoryStream();
            compressor.DecompressToStream(receivedObjectBytes, stream);
            stream.Seek(0, 0);

            return Serializer.Deserialize<T>(stream);
        }

        #endregion
    }
}
