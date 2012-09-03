using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.IO;

namespace SerializerBase
{
    /// <summary>
    /// Only usefull for serializing primitive arrays to bytes. Will throw an exception otherwise
    /// </summary>    
    public class NullSerializer : Serializer
    {
        static Serializer instance;

        /// <summary>
        /// Instance singleton
        /// </summary>        
        [Obsolete("Instance access via class obsolete, use WrappersHelper.GetSerializer")]
        public static Serializer Instance 
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

        public override byte Identifier { get { return 0; } }

        protected override void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options)
        {
            throw new InvalidOperationException("Cannot use null serializer for objects that are not arrays");
        }

        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            throw new InvalidOperationException("Cannot use null serializer for objects that are not arrays");
        }

        #endregion
    }
}
