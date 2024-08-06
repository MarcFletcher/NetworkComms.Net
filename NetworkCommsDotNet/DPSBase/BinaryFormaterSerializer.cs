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
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace NetworkCommsDotNet.DPSBase
{
    /// <summary>
    /// DataSerializer that uses .Net <see cref="System.Runtime.Serialization.Formatters.Binary.BinaryFormatter"/> to perform <see cref="object"/> serialization
    /// </summary>
    [DataSerializerProcessor(2)]
    public class BinaryFormaterSerializer : DataSerializer
    {
        static DataSerializer instance;

        /// <summary>
        /// Instance singleton used to access serializer instance.  Use instead <see cref="DPSManager.GetDataSerializer{T}"/>
        /// </summary>
        [Obsolete("Instance access via class obsolete, use DPSManager.GetSerializer<T>")]
        public static DataSerializer Instance
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
        
        /// <inheritdoc />
        protected override void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options)
        {
#if !NET
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Serialize(ouputStream, objectToSerialise);
            ouputStream.Seek(0, 0);
#else
            throw new NotImplementedException("Not implemented for .NET (i.e beyond framework 4.8)");
#endif
        }

        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
#if !NET
            BinaryFormatter formatter = new BinaryFormatter();
            return formatter.Deserialize(inputStream);
#else
            throw new NotImplementedException("Not implemented for .NET (i.e beyond framework 4.8)");
#endif
        }

#endregion
    }
}