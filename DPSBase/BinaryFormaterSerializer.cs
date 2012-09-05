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

namespace DPSBase
{
    /// <summary>
    /// DataSerializer that uses .Net <see cref="System.Runtime.Serialization.Formatters.Binary.BinaryFormatter"/> to perform <see cref="object"/> serialization
    /// </summary>
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
        public override byte Identifier { get { return 2; } }

        /// <inheritdoc />
        protected override void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options)
        {            
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Serialize(ouputStream, objectToSerialise);
            ouputStream.Seek(0, 0);
        }

        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            return formatter.Deserialize(inputStream);
        }

        #endregion
    }
}
