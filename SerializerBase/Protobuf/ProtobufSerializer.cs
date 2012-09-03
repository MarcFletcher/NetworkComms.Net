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
using System.ComponentModel.Composition;

namespace SerializerBase.Protobuf
{
    /// <summary>
    /// Serializer using ProtoBuf.Net
    /// </summary>
    public class ProtobufSerializer : Serializer
    {        
        private static int metaDataTimeoutMS = 150000;

        static Serializer instance;

        /// <summary>
        /// Instance singleton
        /// </summary>
        [Obsolete("Instance access via class obsolete, use WrappersHelper.GetSerializer")]
        public static Serializer Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = GetInstance<ProtobufSerializer>();

                    //Increase timeout to prevent errors when CPU busy
                    RuntimeTypeModel.Default.MetadataTimeoutMilliseconds = metaDataTimeoutMS;
                }

                return instance;
            }
        }
        
        private ProtobufSerializer() { }

        #region ISerialize Members

        public override byte Identifier { get { return 1; } }

        /// <summary>
        /// Serializes objectToSerialize to a byte array using compression provided by compressor
        /// </summary>
        /// <typeparam name="T">Type paramter of objectToSerialize</typeparam>
        /// <param name="objectToSerialise">Object to serialize.  Must be marked with ProtoContract and members to serialize marked as protoMembers</param>
        /// <param name="dataProcessors">The compression provider to use</param>
        /// <returns>The serialized and compressed bytes of objectToSerialize</returns>
        protected override void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options)
        {               
            ProtoBuf.Serializer.NonGeneric.Serialize(ouputStream, objectToSerialise);
            ouputStream.Seek(0, 0);           
        }

        /// <summary>
        /// Deserializes data object held as compressed bytes in receivedObjectBytes using compressor and ProtoBuf.Net
        /// </summary>
        /// <typeparam name="T">Type parameter of the resultant object</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>The deserialized object</returns>
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            return ProtoBuf.Serializer.NonGeneric.Deserialize(resultType, inputStream);
        }

        #endregion
    }
}
