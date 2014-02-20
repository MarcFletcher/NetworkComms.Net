using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

#if NETFX_CORE
using System.Linq;
#endif

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

            outputStream.Seek(0, 0);

            if (!(objectToSerialise is IExplicitlySerialize))
            {
                if (objectToSerialise is bool)
                    outputStream.Write(BitConverter.GetBytes((bool)objectToSerialise), 0, sizeof(bool));
                else if (objectToSerialise is byte)
                    outputStream.WriteByte((byte)objectToSerialise);
                else if (objectToSerialise is sbyte)
                    outputStream.WriteByte((byte)((int)(sbyte)objectToSerialise + 128));
                else if (objectToSerialise is Int16)
                    outputStream.Write(BitConverter.GetBytes((short)objectToSerialise), 0, sizeof(short));
                else if (objectToSerialise is UInt16)
                    outputStream.Write(BitConverter.GetBytes((ushort)objectToSerialise), 0, sizeof(ushort));
                else if (objectToSerialise is Int32)
                    outputStream.Write(BitConverter.GetBytes((int)objectToSerialise), 0, sizeof(int));
                else if (objectToSerialise is UInt32)
                    outputStream.Write(BitConverter.GetBytes((uint)objectToSerialise), 0, sizeof(uint));
                else if (objectToSerialise is Int64)
                    outputStream.Write(BitConverter.GetBytes((long)objectToSerialise), 0, sizeof(long));
                else if (objectToSerialise is UInt64)
                    outputStream.Write(BitConverter.GetBytes((ulong)objectToSerialise), 0, sizeof(ulong));
                else if (objectToSerialise is IntPtr)
                    outputStream.Write(BitConverter.GetBytes(((IntPtr)objectToSerialise).ToInt64()), 0, sizeof(long));
                else if (objectToSerialise is UIntPtr)
                    outputStream.Write(BitConverter.GetBytes(((UIntPtr)objectToSerialise).ToUInt64()), 0, sizeof(ulong));
                else if (objectToSerialise is Char)
                    outputStream.Write(BitConverter.GetBytes((char)objectToSerialise), 0, sizeof(char));
                else if (objectToSerialise is Single)
                    outputStream.Write(BitConverter.GetBytes((float)objectToSerialise), 0, sizeof(float));
                else if (objectToSerialise is Double)
                    outputStream.Write(BitConverter.GetBytes((double)objectToSerialise), 0, sizeof(double));
                else if (objectToSerialise is String)
                {
                    string obj = (string)objectToSerialise;

                    byte[] objData = Encoding.UTF8.GetBytes(obj);
                    byte[] objLengthData = BitConverter.GetBytes(objData.Length);

                    outputStream.Write(objLengthData, 0, objLengthData.Length);
                    outputStream.Write(objData, 0, objData.Length);
                }
                else
                    throw new ArgumentException("objectToSerialize must implement IExplicitlySerialize", "objectToSerialize");
            }
            else
                (objectToSerialise as IExplicitlySerialize).Serialize(outputStream);

            outputStream.Seek(0, 0);
        }
        /// <inheritdoc />
        protected override object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options)
        {
            if (inputStream == null)
                throw new ArgumentNullException("inputStream");

#if NETFX_CORE
            var constructor = (from ctor in resultType.GetTypeInfo().DeclaredConstructors
                             where ctor.GetParameters().Length == 0
                             select ctor).FirstOrDefault();

            if (constructor == null || !explicitlySerializableType.GetTypeInfo().IsAssignableFrom(resultType.GetTypeInfo()))
#else
            var constructor = resultType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

            if (constructor == null || !explicitlySerializableType.IsAssignableFrom(resultType))
#endif
            {
                byte[] buffer = new byte[8];

                if (resultType == typeof(bool))
                { 
                    inputStream.Read(buffer, 0, sizeof(bool)); 
                    return BitConverter.ToBoolean(buffer, 0); 
                }
                else if (resultType == typeof(byte))
                {
                    return (byte)inputStream.ReadByte();
                }
                else if (resultType == typeof(sbyte))
                {
                    return (sbyte)(inputStream.ReadByte() - 128);
                }
                else if (resultType == typeof(Int16))
                {
                    inputStream.Read(buffer, 0, sizeof(short));
                    return BitConverter.ToInt16(buffer, 0);
                }
                else if (resultType == typeof(UInt16))
                {
                    inputStream.Read(buffer, 0, sizeof(ushort));
                    return BitConverter.ToUInt16(buffer, 0);
                }
                else if (resultType == typeof(Int32))
                {
                    inputStream.Read(buffer, 0, sizeof(int));
                    return BitConverter.ToInt32(buffer, 0);
                }
                else if (resultType == typeof(UInt32))
                {
                    inputStream.Read(buffer, 0, sizeof(uint));
                    return BitConverter.ToUInt32(buffer, 0);
                }
                else if (resultType == typeof(Int64))
                {
                    inputStream.Read(buffer, 0, sizeof(long));
                    return BitConverter.ToInt64(buffer, 0);
                }
                else if (resultType == typeof(UInt64))
                {
                    inputStream.Read(buffer, 0, sizeof(ulong));
                    return BitConverter.ToUInt64(buffer, 0);
                }
                else if (resultType == typeof(IntPtr))
                {
                    inputStream.Read(buffer, 0, sizeof(long));
                    return new IntPtr(BitConverter.ToInt64(buffer, 0));
                }
                else if (resultType == typeof(UIntPtr))
                {
                    inputStream.Read(buffer, 0, sizeof(ulong));
                    return new UIntPtr(BitConverter.ToUInt64(buffer, 0));
                }
                else if (resultType == typeof(Char))
                {
                    inputStream.Read(buffer, 0, sizeof(char));
                    return BitConverter.ToChar(buffer, 0);
                }
                else if (resultType == typeof(Single))
                {
                    inputStream.Read(buffer, 0, sizeof(float));
                    return BitConverter.ToSingle(buffer, 0);
                }
                else if (resultType == typeof(Double))
                {
                    inputStream.Read(buffer, 0, sizeof(double));
                    return BitConverter.ToDouble(buffer, 0);
                }
                else if (resultType == typeof(String))
                {
                    inputStream.Read(buffer, 0, sizeof(int));
                    byte[] objData = new byte[BitConverter.ToInt32(buffer, 0)];
                    inputStream.Read(objData, 0, objData.Length);

                    return new String(Encoding.UTF8.GetChars(objData));
                }
                else
                    throw new ArgumentException("Provided type " + resultType.ToString() + " either does not have a parameterless constructor or does not implement IExplicitlySerialize", "resultType");
            }

            var result = constructor.Invoke(new object[] { }) as IExplicitlySerialize;
            result.Deserialize(inputStream);

            return result;
        }
    }
}
