using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace NetworkCommsDotNet.DPSBase
{
    /// <summary>
    /// Interface defining serialize/deserialize methods 
    /// </summary>
    public interface IExplicitlySerialize
    {
        /// <summary>
        /// Serializes the current <see cref="IExplicitlySerialize"/> object to the provided <see cref="System.IO.Stream"/>
        /// </summary>
        /// <param name="outputStream">The stream to serialize to</param>
        void Serialize(Stream outputStream);

        /// <summary>
        /// Deserializes from a <see cref="System.IO.Stream"/> to the current <see cref="IExplicitlySerialize"/> object
        /// </summary>
        /// <param name="inputStream">The <see cref="System.IO.Stream"/> to deserialize from</param>
        void Deserialize(Stream inputStream);
    }

    /// <summary>
    /// Serializer that will only serialize objects implementing the <see cref="IExplicitlySerialize"/> interface
    /// </summary>
    [DataSerializerProcessor(3)]    
    public class ExplicitSerializer : DataSerializer
    {
        Type explicitlySerializableType = typeof(IExplicitlySerialize);

        private ExplicitSerializer() { }

        /// <inheritdoc />
        protected override void SerialiseDataObjectInt(Stream outputStream, object objectToSerialise, Dictionary<string, string> options)
        {
            if (outputStream == null)
                throw new ArgumentNullException("outputStream");

            if (objectToSerialise == null)
                throw new ArgumentNullException("objectToSerialize");

            if (!(objectToSerialise is IExplicitlySerialize))
                throw new ArgumentException("objectToSerialize must implement IExplicitlySerialize", "objectToSerialize");

            (objectToSerialise as IExplicitlySerialize).Serialize(outputStream);
        }
        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            if (inputStream == null)
                throw new ArgumentNullException("inputStream");

            var constructor = resultType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

            if (constructor == null || !explicitlySerializableType.IsAssignableFrom(resultType))
                throw new ArgumentException("Provided type " + resultType.ToString() + " either does not have a paramerterless constrcutor or does not implement IExplicitlySerialize","resultType");

            var result = constructor.Invoke(new object[] { }) as IExplicitlySerialize;
            result.Deserialize(inputStream);

            return result;
        }
    }
}
