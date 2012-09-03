//  Copyright 2011 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.ComponentModel.Composition;
using System.IO;

namespace SerializerBase
{    
    [InheritedExport(typeof(Serializer))]
    public abstract class Serializer
    {
        protected static T GetInstance<T>() where T : Serializer
        {
            //this forces helper static constructor to be called
            var instance = ProcessorManager.Instance.GetSerializer<T>() as T;

            if (instance == null)
            {
                //if the instance is null the type was not added as part of composition
                //create a new instance of T and add it to helper as a serializer

                instance = typeof(T).GetConstructor(new Type[] { }).Invoke(new object[] { }) as T;
                ProcessorManager.Instance.AddSerializer(instance);
            }

            return instance;
        }
                        
        /// <summary>
        /// Converts objectToSerialize to an array of bytes using the compression provided by compressor
        /// </summary>
        /// <typeparam name="T">Type of object to serialize</typeparam>
        /// <param name="objectToSerialise">Object to serialize</param>
        /// <param name="dataProcessors">Compression provider to use</param>
        /// <returns>Serialized array of bytes</returns>
        public byte[] SerialiseDataObject<T>(T objectToSerialise, List<DataProcessor> dataProcessors, Dictionary<string, string> options)
        {
            var baseRes = ArraySerializer.SerialiseArrayObject(objectToSerialise, dataProcessors, options);

            if (baseRes != null)
                return baseRes;

            using (MemoryStream outputStream = new MemoryStream())
            {
                SerialiseDataObjectInt(outputStream, objectToSerialise, options);

                if (dataProcessors == null || dataProcessors.Count == 0)
                    return outputStream.ToArray();
                else
                {
                    using (MemoryStream tempStream = new MemoryStream())
                    {
                        long writtenBytes;
                        dataProcessors[0].ForwardProcessDataStream(outputStream, tempStream, options, out writtenBytes);

                        if (dataProcessors.Count > 1)
                        {
                            for (int i = 1; i < dataProcessors.Count; i += 2)
                            {
                                tempStream.Seek(0, 0); tempStream.SetLength(writtenBytes);
                                outputStream.Seek(0, 0);
                                dataProcessors[i].ForwardProcessDataStream(tempStream, outputStream, options, out writtenBytes);

                                if (i + 1 < dataProcessors.Count)
                                {
                                    tempStream.Seek(0, 0);
                                    outputStream.Seek(0, 0); outputStream.SetLength(writtenBytes);
                                    dataProcessors[i + 1].ForwardProcessDataStream(outputStream, tempStream, options, out writtenBytes);
                                }
                            }
                        }

                        if (dataProcessors.Count % 2 == 0)
                        {
                            outputStream.Seek(0, 0);
                            outputStream.SetLength(writtenBytes);
                            return outputStream.ToArray();
                        }
                        else
                        {
                            tempStream.Seek(0, 0);
                            tempStream.SetLength(writtenBytes);
                            return tempStream.ToArray();
                        }
                    }                    
                }
            }            
        }

        /// <summary>
        /// Converts array of bytes previously serialized and compressed using compressor to an object of provided type
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize to</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="dataProcessors">Compression provider to use</param>
        /// <returns>The deserialized object</returns>
        public T DeserialiseDataObject<T>(byte[] receivedObjectBytes, List<DataProcessor> dataProcessors, Dictionary<string, string> options)
        {
            var baseRes = ArraySerializer.DeserialiseArrayObject(receivedObjectBytes, typeof(T), dataProcessors, options);

            if (baseRes != null)
                return (T)baseRes;

            using (MemoryStream inputStream = new MemoryStream(receivedObjectBytes))
            {
                if (dataProcessors == null || dataProcessors.Count == 0)
                    return (T)DeserialiseDataObjectInt(inputStream, typeof(T), options);
                else
                {
                    using (MemoryStream tempStream = new MemoryStream())
                    {
                        long writtenBytes;
                        dataProcessors[dataProcessors.Count - 1].ReverseProcessDataStream(inputStream, tempStream, options, out writtenBytes);

                        if (dataProcessors.Count > 1)
                        {
                            for (int i = dataProcessors.Count - 2; i >= 0; i -= 2)
                            {
                                inputStream.Seek(0, 0);
                                tempStream.Seek(0, 0); tempStream.SetLength(writtenBytes);
                                dataProcessors[i].ReverseProcessDataStream(tempStream, inputStream, options, out writtenBytes);

                                if (i - 1 >= 0)
                                {
                                    inputStream.Seek(0, 0); inputStream.SetLength(writtenBytes);
                                    tempStream.Seek(0, 0);
                                    dataProcessors[i].ReverseProcessDataStream(inputStream, tempStream, options, out writtenBytes);
                                }
                            }
                        }

                        if (dataProcessors.Count % 2 == 0)
                        {
                            inputStream.Seek(0, 0);
                            inputStream.SetLength(writtenBytes);
                            return (T)DeserialiseDataObjectInt(inputStream, typeof(T), options);
                        }
                        else
                        {
                            tempStream.Seek(0, 0);
                            tempStream.SetLength(writtenBytes);
                            return (T)DeserialiseDataObjectInt(tempStream, typeof(T), options);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns a unique identifier for the serializer type.  Used in automatic serialization/compression detection
        /// </summary>
        public abstract byte Identifier { get; }
        
        protected abstract void SerialiseDataObjectInt(Stream ouputStream, object objectToSerialise, Dictionary<string, string> options);

        protected abstract object DeserialiseDataObjectInt(Stream inputStream, Type resultType, Dictionary<string, string> options);
    }

    /// <summary>
    /// Abstract class that provides fastest method for serializing arrays of primitive data types.
    /// </summary>
    static class ArraySerializer
    {
        /// <summary>
        /// Serializes objectToSerialize to a byte array using compression provided by compressor if T is an array of primitives.  Otherwise returns default value for T.  Override 
        /// to serialize other types
        /// </summary>
        /// <typeparam name="T">Type paramter of objectToSerialize.  If it is an Array will be serialized here</typeparam>
        /// <param name="objectToSerialise">Object to serialize</param>
        /// <param name="dataProcessors">The compression provider to use</param>
        /// <returns>The serialized and compressed bytes of objectToSerialize</returns>
        public static unsafe byte[] SerialiseArrayObject(object objectToSerialise, List<DataProcessor> dataProcessors, Dictionary<string, string> options)
        {
            Type objType = objectToSerialise.GetType();

            if (objType.IsArray)
            {
                var elementType = objType.GetElementType();

                //No need to do anything for a byte array
                if (elementType == typeof(byte) && (dataProcessors == null || dataProcessors.Count == 0))
                    return objectToSerialise as byte[];
                else if (elementType.IsPrimitive)
                {                                        
                    var asArray = objectToSerialise as Array;
                    GCHandle arrayHandle = GCHandle.Alloc(asArray, GCHandleType.Pinned);

                    try
                    {
                        IntPtr safePtr = Marshal.UnsafeAddrOfPinnedArrayElement(asArray, 0);
                        long writtenBytes = 0; 

                        using (MemoryStream tempStream1 = new System.IO.MemoryStream())
                        {                            
                            using (UnmanagedMemoryStream inputDataStream = new System.IO.UnmanagedMemoryStream((byte*)safePtr, asArray.Length * Marshal.SizeOf(elementType)))
                            {
                                if (dataProcessors == null || dataProcessors.Count == 0)
                                {
                                    inputDataStream.CopyTo(tempStream1);
                                    return tempStream1.ToArray();
                                }

                                dataProcessors[0].ForwardProcessDataStream(inputDataStream, tempStream1, options, out writtenBytes);
                            }

                            if (dataProcessors.Count > 1)
                            {
                                using (MemoryStream tempStream2 = new MemoryStream())
                                {
                                    for (int i = 1; i < dataProcessors.Count; i += 2)
                                    {
                                        tempStream1.Seek(0, 0); tempStream1.SetLength(writtenBytes);
                                        tempStream2.Seek(0, 0);
                                        dataProcessors[i].ForwardProcessDataStream(tempStream1, tempStream2, options, out writtenBytes);

                                        if (i + 1 < dataProcessors.Count)
                                        {
                                            tempStream1.Seek(0, 0);
                                            tempStream2.Seek(0, 0); tempStream2.SetLength(writtenBytes);
                                            dataProcessors[i].ForwardProcessDataStream(tempStream2, tempStream1, options, out writtenBytes);
                                        }
                                    }

                                    if (dataProcessors.Count % 2 == 0)
                                    {
                                        tempStream2.SetLength(writtenBytes + 8);
                                        tempStream2.Seek(writtenBytes, 0);
                                        tempStream2.Write(BitConverter.GetBytes(asArray.Length), 0, sizeof(int));
                                        return tempStream2.ToArray();
                                    }
                                    else
                                    {
                                        tempStream1.SetLength(writtenBytes + 8);
                                        tempStream1.Seek(writtenBytes, 0);
                                        tempStream1.Write(BitConverter.GetBytes(asArray.Length), 0, sizeof(int));
                                        return tempStream1.ToArray();
                                    }
                                }
                            }
                            else
                            {
                                tempStream1.SetLength(writtenBytes + 8);
                                tempStream1.Seek(writtenBytes, 0);
                                tempStream1.Write(BitConverter.GetBytes(asArray.Length), 0, sizeof(int));
                                return tempStream1.ToArray();
                            }
                        }
                    }
                    finally
                    {
                        arrayHandle.Free();
                    }                   
                }
            }

            return null;
        }

        /// <summary>
        /// Deserializes data object held as compressed bytes in receivedObjectBytes using compressor if desired type is an array of primitives
        /// </summary>
        /// <typeparam name="T">Type parameter of the resultant object</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="dataProcessors">Compression provider to use</param>
        /// <returns>The deserialized object if it is an array, otherwise null</returns>
        public static unsafe object DeserialiseArrayObject(byte[] receivedObjectBytes, Type objType, List<DataProcessor> dataProcessors, Dictionary<string, string> options)
        {
            if (objType.IsArray)
            {
                var elementType = objType.GetElementType();

                //No need to do anything for a byte array
                if (elementType == typeof(byte) && (dataProcessors == null || dataProcessors.Count == 0))
                    return (object)receivedObjectBytes;
                if (elementType.IsPrimitive)
                {
                    int numElements = (int)(BitConverter.ToUInt64(receivedObjectBytes, receivedObjectBytes.Length - sizeof(int)));

                    Array resultArray = Array.CreateInstance(elementType, numElements);

                    GCHandle arrayHandle = GCHandle.Alloc(resultArray, GCHandleType.Pinned);

                    try
                    {
                        IntPtr safePtr = Marshal.UnsafeAddrOfPinnedArrayElement(resultArray, 0);
                        long writtenBytes = 0;

                        using (System.IO.UnmanagedMemoryStream finalOutputStream = new System.IO.UnmanagedMemoryStream((byte*)safePtr, resultArray.Length * Marshal.SizeOf(elementType), resultArray.Length * Marshal.SizeOf(elementType), System.IO.FileAccess.ReadWrite))
                        {
                            using (MemoryStream inputBytesStream = new MemoryStream(receivedObjectBytes, 0, receivedObjectBytes.Length - sizeof(int)))
                            {
                                if (dataProcessors != null && dataProcessors.Count > 1)
                                {
                                    using (MemoryStream tempStream1 = new MemoryStream())
                                    {
                                        dataProcessors[dataProcessors.Count - 1].ReverseProcessDataStream(inputBytesStream, tempStream1, options, out writtenBytes);

                                        if (dataProcessors.Count > 2)
                                        {
                                            using (MemoryStream tempStream2 = new MemoryStream())
                                            {
                                                for (int i = dataProcessors.Count - 2; i > 0; i -= 2)
                                                {
                                                    tempStream1.Seek(0, 0); tempStream1.SetLength(writtenBytes);
                                                    tempStream2.Seek(0, 0);
                                                    dataProcessors[i].ReverseProcessDataStream(tempStream1, tempStream2, options, out writtenBytes);

                                                    if (i - 1 > 0)
                                                    {
                                                        tempStream1.Seek(0, 0);
                                                        tempStream2.Seek(0, 0); tempStream2.SetLength(writtenBytes);
                                                        dataProcessors[i - 1].ReverseProcessDataStream(tempStream2, tempStream1, options, out writtenBytes);
                                                    }
                                                }

                                                if (dataProcessors.Count % 2 == 0)
                                                {
                                                    tempStream1.Seek(0, 0); tempStream1.SetLength(writtenBytes);
                                                    dataProcessors[0].ReverseProcessDataStream(tempStream1, finalOutputStream, options, out writtenBytes);
                                                }
                                                else
                                                {
                                                    tempStream2.Seek(0, 0); tempStream2.SetLength(writtenBytes);
                                                    dataProcessors[0].ReverseProcessDataStream(tempStream2, finalOutputStream, options, out writtenBytes);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            tempStream1.Seek(0, 0); tempStream1.SetLength(writtenBytes);
                                            dataProcessors[0].ReverseProcessDataStream(tempStream1, finalOutputStream, options, out writtenBytes);
                                        }
                                    }
                                }
                                else
                                {
                                    if (dataProcessors != null && dataProcessors.Count == 1)
                                        dataProcessors[0].ReverseProcessDataStream(inputBytesStream, finalOutputStream, options, out writtenBytes);
                                    else
                                        inputBytesStream.CopyTo(finalOutputStream);
                                }
                            }
                        }
                    }
                    finally
                    {
                        arrayHandle.Free();
                    }

                    return (object)resultArray;
                }
            }

            return null;
        }

    }
}
