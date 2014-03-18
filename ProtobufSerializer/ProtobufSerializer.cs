//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using ProtoBuf;
using System.IO;
using System.Runtime.InteropServices;
using ProtoBuf.Meta;

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = MonoTouch.Foundation.PreserveAttribute;
#endif

namespace NetworkCommsDotNet.DPSBase
{
    /// <summary>
    /// <see cref="DataSerializer"/> using <see href="http://code.google.com/p/protobuf-net/">ProtoBuf-Net</see> to serialize an <see cref="object"/> to bytes
    /// </summary>
    [DataSerializerProcessor(1)]
    public class ProtobufSerializer : DataSerializer
    {        
        private static int metaDataTimeoutMS = 150000;

#if ANDROID || iOS
        [Preserve]
#endif
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
