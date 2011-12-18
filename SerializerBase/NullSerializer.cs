using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SerializerBase
{
    /// <summary>
    /// Only usefull for serializing primitive arrays to bytes. Will throw an exception otherwise
    /// </summary>
    public class NullSerializer : ArraySerializer
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

        public override byte[] SerialiseDataObject<T>(T objectToSerialise, ICompress compressor)
        {
            var baseRes = base.SerialiseDataObject<T>(objectToSerialise, compressor);

            if (baseRes != null)
                return baseRes;
            else
                throw new Exception("Null serializer can only be used to serialize primitive arrays.");
        }

        public override T DeserialiseDataObject<T>(byte[] receivedObjectBytes, ICompress compressor)
        {
            var baseRes = base.DeserialiseDataObject<T>(receivedObjectBytes, compressor);

            if (!Equals(baseRes, default(T)))
                return baseRes;
            else
                throw new Exception("Null serializer can only be used to deserialize primitive arrays.");
        }

        #endregion
    }
}
