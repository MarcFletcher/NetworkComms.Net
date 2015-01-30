//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

#if WINDOWS_PHONE || NETFX_CORE
#else

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = Foundation.PreserveAttribute;
#endif

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