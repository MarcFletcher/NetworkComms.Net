//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

#if NET4 || WINDOWS_PHONE || NETFX_CORE
using Newtonsoft.Json;
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

#if ANDROID || iOS
        [Preserve]
#endif
        private JSONSerializer() { }
                        
        #region ISerialize Members
                
        /// <inheritdoc />
        protected override void SerialiseDataObjectInt(Stream outputStream, object objectToSerialise, Dictionary<string, string> options)
        {
            if (outputStream == null)
                throw new ArgumentNullException("outputStream");

            if (objectToSerialise == null)
                throw new ArgumentNullException("objectToSerialize");

            outputStream.Seek(0, 0);
            var data = Encoding.Unicode.GetBytes(JsonConvert.SerializeObject(objectToSerialise));
            outputStream.Write(data, 0, data.Length);
            outputStream.Seek(0, 0);
        }

        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            var data = new byte[inputStream.Length];
            inputStream.Read(data, 0, data.Length);
            return JsonConvert.DeserializeObject(new String(Encoding.Unicode.GetChars(data)), resultType);
        }

        #endregion
    }
}
