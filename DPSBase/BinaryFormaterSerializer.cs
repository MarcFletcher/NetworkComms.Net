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
    /// Serializer that uss .Net built in BinaryFormatter
    /// </summary>
    public class BinaryFormaterSerializer : DataSerializer
    {
        static DataSerializer instance;

        /// <summary>
        /// Instance singleton
        /// </summary>
        [Obsolete("Instance access via class obsolete, use WrappersHelper.GetSerializer")]
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

        public override byte Identifier { get { return 2; } }
        
        /// <summary>
        /// Serializes objectToSerialize to a byte array using compression provided by compressor
        /// </summary>
        /// <typeparam name="T">Type paramter of objectToSerialize</typeparam>
        /// <param name="objectToSerialise">Object to serialize.  Must be marked Serializable</param>
        /// <param name="dataProcessors">The list of dataProcessors to use on the serialized data</param>
        /// <param name="options">An options dictionary for the data processors</param>
        /// <returns>The serialized and compressed bytes of objectToSerialize</returns>
        protected override void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options)
        {            
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Serialize(ouputStream, objectToSerialise);
            ouputStream.Seek(0, 0);
        }

        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            return formatter.Deserialize(inputStream);
        }

        #endregion
    }
}
