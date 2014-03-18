//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

#if ANDROID
using PreserveAttribute = Android.Runtime.PreserveAttribute;
#elif iOS
using PreserveAttribute = MonoTouch.Foundation.PreserveAttribute;
#endif

namespace NetworkCommsDotNet.DPSBase
{
    /// <summary>
    /// Use only when serializing only primitive arrays. Will throw an exception otherwise
    /// </summary>    
    [DataSerializerProcessor(0)]
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
#if ANDROID || iOS
        [Preserve]
#endif
        private NullSerializer() { }

        #region ISerialize Members
        
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
