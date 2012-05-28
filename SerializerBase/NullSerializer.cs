using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace SerializerBase
{
    /// <summary>
    /// Only usefull for serializing primitive arrays to bytes. Will throw an exception otherwise
    /// </summary>
    [Export(typeof(ISerialize))]    
    public class NullSerializer : ISerialize
    {
        static ISerialize instance;

        /// <summary>
        /// Instance singleton
        /// </summary>
        public static ISerialize Instance 
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

        protected override byte[] SerialiseDataObjectInt(object objectToSerialise, ICompress compressor)
        {
            var baseRes = ArraySerializer.SerialiseArrayObject(objectToSerialise, compressor);

            if (baseRes != null)
                return baseRes;
            else
                throw new Exception("Null serializer can only be used to serialize primitive arrays.");
        }

        protected override object DeserialiseDataObjectInt(byte[] receivedObjectBytes, Type resultType, ICompress compressor)
        {
            var baseRes = ArraySerializer.DeserialiseArrayObject(receivedObjectBytes, resultType, compressor);

            if (baseRes != null)
                return baseRes;
            else
                throw new Exception("Null serializer can only be used to deserialize primitive arrays.");
        }

        #endregion
    }
}
