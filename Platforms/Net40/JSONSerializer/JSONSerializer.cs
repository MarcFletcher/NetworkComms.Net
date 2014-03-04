//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

#if NET4
using System.Web.Script.Serialization;
#endif

#if WINDOWS_PHONE || NETFX_CORE
using System.Runtime.Serialization.Json;
#endif

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = MonoTouch.Foundation.PreserveAttribute;
#endif

namespace NetworkCommsDotNet.DPSBase
{
    [DataSerializerProcessor(4)]
    public class JSONSerializer : DataSerializer
    {
#if NET4
        JavaScriptSerializer serializer = new JavaScriptSerializer();
#endif

#if ANDROID || iOS
        [Preserve]
#endif
        private JSONSerializer() { }
                        
        #region ISerialize Members
                
        /// <inheritdoc />
        protected override void SerialiseDataObjectInt(Stream outputStream, object objectToSerialise, Dictionary<string, string> options)
        {
#if NET4
            if (outputStream == null)
                throw new ArgumentNullException("outputStream");

            if (objectToSerialise == null)
                throw new ArgumentNullException("objectToSerialize");

            outputStream.Seek(0, 0);

            byte[] buffer = Encoding.Unicode.GetBytes(serializer.Serialize(objectToSerialise));

            outputStream.Write(buffer, 0, buffer.Length);
            outputStream.Seek(0, 0);
#elif WINDOWS_PHONE || NETFX_CORE
            outputStream.Seek(0, 0);
            var serializer = new DataContractJsonSerializer(objectToSerialise.GetType());
            serializer.WriteObject(outputStream, objectToSerialise);
            outputStream.Seek(0, 0);
#endif

        }

        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
#if NET4
            byte[] buffer = new byte[inputStream.Length];
            inputStream.Read(buffer, 0, buffer.Length);

            char[] chars = Encoding.Unicode.GetChars(buffer);
            string res = new string(chars);
            
            return serializer.Deserialize(res, resultType);
#elif WINDOWS_PHONE || NETFX_CORE
            var serializer = new DataContractJsonSerializer(resultType);
            return serializer.ReadObject(inputStream);
#endif
        }

        #endregion
    }
}
