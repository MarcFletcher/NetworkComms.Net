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

namespace DPSBase
{
    /// <summary>
    /// <see cref="DataSerializer"/> using <see href="http://code.google.com/p/protobuf-net/">ProtoBuf-Net</see> to serialize an <see cref="object"/> to bytes
    /// </summary>
    public class ProtobufSerializer : DataSerializer
    {        
        private static int metaDataTimeoutMS = 150000;

        private ProtobufSerializer() { }
        
        #region Depreciated

        static DataSerializer instance;

        /// <summary>
        /// Instance singleton used to access <see cref="DataSerializer"/> instance.  Use instead <see cref="DPSManager.GetDataSerializer{T}"/>
        /// </summary>
        [Obsolete("Instance access via class obsolete, use DPSManager.GetDataSerializer<T>")]
        public static DataSerializer Instance
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

        #endregion
        
        #region ISerialize Members

        /// <inheritdoc />
        public override byte Identifier { get { return 1; } }

        /// <inheritdoc />
        protected override void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options)
        {               
            ProtoBuf.Serializer.NonGeneric.Serialize(ouputStream, objectToSerialise);
            ouputStream.Seek(0, 0);           
        }

        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            return ProtoBuf.Serializer.NonGeneric.Deserialize(resultType, inputStream);
        }

        #endregion
    }
}
