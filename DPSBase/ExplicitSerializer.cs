using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NetworkCommsDotNet.DPSBase
{
    public interface IExplicitlySerialize
    {
        void Serialize(Stream outputStream);
        void Deserialize(Stream inputStream);
    }

    [DataSerializerProcessor(3)]
    public class ExplicitSerializer : DataSerializer
    {
        Type explicitlySerializableType = typeof(IExplicitlySerialize);

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

        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            if (inputStream == null)
                throw new ArgumentNullException("inputStream");

            var constructor = resultType.GetConstructor(System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);

            if (constructor == null || explicitlySerializableType.IsAssignableFrom(resultType))
                throw new ArgumentException("Provided type " + resultType.ToString() + " either does not have a paramerterless constrcutor or does not implement IExplicitlySerialize","resultType");

            var result = constructor.Invoke(new object[] { }) as IExplicitlySerialize;
            result.Deserialize(inputStream);

            return result;
        }
    }
}
