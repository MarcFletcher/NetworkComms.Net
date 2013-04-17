//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

#if WINDOWS_PHONE
#else

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = MonoTouch.Foundation.PreserveAttribute;
#endif

namespace DPSBase
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

#if ANDROID || iOS
        [Preserve]
#endif
        private BinaryFormaterSerializer() { }

        #region ISerialize Members
        
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

#endif