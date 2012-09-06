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
using System.ComponentModel.Composition;
using System.IO;

namespace DPSBase
{
    /// <summary>
    /// Use only when serializing only primitive arrays. Will throw an exception otherwise
    /// </summary>    
    public class NullSerializer : DataSerializer
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
                    instance = GetInstance<NullSerializer>();

                return instance;
            }
        }

        private NullSerializer() { }

        #region ISerialize Members

        /// <inheritdoc />
        public override byte Identifier { get { return 0; } }

        /// <inheritdoc />
        protected override void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options)
        {
            throw new InvalidOperationException("Cannot use null serializer for objects that are not arrays of primitives");
        }

        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            throw new InvalidOperationException("Cannot use null serializer for objects that are not arrays of primitives");
        }

        #endregion
    }
}
