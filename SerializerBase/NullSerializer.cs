using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SerializerBase
{
    /// <summary>
    /// Only usefull for serializing primitive arrays to bytes. Will throw an exception otherwise
    /// </summary>
    public class NullSerializer : ISerialize
    {
        static NullSerializer instance;
        static object locker = new object();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static NullSerializer Instance
        {
            get
            {
                lock (locker)
                    if (instance == null)
                        instance = new NullSerializer();

                return instance;
            }
        }

        private NullSerializer() { }

        #region ISerialize Members

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
