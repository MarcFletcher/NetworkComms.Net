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

namespace SerializerBase
{    
    [InheritedExport(typeof(ISerialize))]
    public abstract class ISerialize
    {
        protected static T GetInstance<T>() where T : ISerialize
        {
            //this forces helper static constructor to be called
            var instance = WrappersHelper.Instance.GetSerializer<T>() as T;

            if (instance == null)
            {
                //if the instance is null the type was not added as part of composition
                //create a new instance of T and add it to helper as a serializer

                instance = typeof(T).GetConstructor(new Type[] { }).Invoke(new object[] { }) as T;
                WrappersHelper.Instance.AddSerializer(instance);
            }

            return instance;
        }
                        
        /// <summary>
        /// Converts objectToSerialize to an array of bytes using the compression provided by compressor
        /// </summary>
        /// <typeparam name="T">Type of object to serialize</typeparam>
        /// <param name="objectToSerialise">Object to serialize</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>Serialized array of bytes</returns>
        public byte[] SerialiseDataObject<T>(T objectToSerialise, ICompress compressor)
        {
            return SerialiseDataObjectInt(objectToSerialise, compressor);
        }

        /// <summary>
        /// Converts array of bytes previously serialized and compressed using compressor to an object of provided type
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize to</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>The deserialized object</returns>
        public T DeserialiseDataObject<T>(byte[] receivedObjectBytes, ICompress compressor)
        {
            return (T)DeserialiseDataObjectInt(receivedObjectBytes, typeof(T), compressor);            
        }

        /// <summary>
        /// Returns a unique identifier for the serializer type.  Used in automatic serialization/compression detection
        /// </summary>
        public abstract byte Identifier { get; }
        
        protected abstract byte[] SerialiseDataObjectInt(object objectToSerialise, ICompress compressor);
                
        protected abstract object DeserialiseDataObjectInt(byte[] receivedObjectBytes, Type resultType, ICompress compressor);
    }

    /// <summary>
    /// Abstract class that provides fastest method for serializing arrays of primitive data types.
    /// </summary>
    public static class ArraySerializer
    {
        /// <summary>
        /// Serializes objectToSerialize to a byte array using compression provided by compressor if T is an array of primitives.  Otherwise returns default value for T.  Override 
        /// to serialize other types
        /// </summary>
        /// <typeparam name="T">Type paramter of objectToSerialize.  If it is an Array will be serialized here</typeparam>
        /// <param name="objectToSerialise">Object to serialize</param>
        /// <param name="compressor">The compression provider to use</param>
        /// <returns>The serialized and compressed bytes of objectToSerialize</returns>
        public static unsafe byte[] SerialiseArrayObject(object objectToSerialise, ICompress compressor)
        {
            Type objType = objectToSerialise.GetType();

            if (objType.IsArray)
            {
                var elementType = objType.GetElementType();

                //No need to do anything for a byte array
                if (elementType == typeof(byte) && compressor.GetType() == typeof(NullCompressor))
                    return objectToSerialise as byte[];
                else if (elementType.IsPrimitive)
                {
                    byte[] output = null;

                    //Problems with garbage collector not deallocating array due to GCHandle have possibly arrisen (see ants profiler).  For Byte[] we don't need to do any of this mess
                    if (elementType == typeof(byte))
                    {
                        var asByteArray = objectToSerialise as byte[];
                        using (System.IO.MemoryStream ms = new System.IO.MemoryStream(asByteArray, true))
                        {
                            output = compressor.CompressDataStream(ms);
                        }
                    }
                    else
                    {
                        var asArray = objectToSerialise as Array;
                        GCHandle arrayHandle = GCHandle.Alloc(asArray, GCHandleType.Pinned);

                        try
                        {
                            IntPtr safePtr = Marshal.UnsafeAddrOfPinnedArrayElement(asArray, 0);

                            using (System.IO.UnmanagedMemoryStream stream = new System.IO.UnmanagedMemoryStream((byte*)safePtr, asArray.Length * Marshal.SizeOf(elementType)))
                            {
                                output = compressor.CompressDataStream(stream);
                            }
                        }
                        finally
                        {
                            arrayHandle.Free();
                        }
                    }

                    return output;
                }
            }

            return null;
        }

        /// <summary>
        /// Deserializes data object held as compressed bytes in receivedObjectBytes using compressor if desired type is an array of primitives
        /// </summary>
        /// <typeparam name="T">Type parameter of the resultant object</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>The deserialized object if it is an array, otherwise null</returns>
        public static unsafe object DeserialiseArrayObject(byte[] receivedObjectBytes, Type objType, ICompress compressor)
        {            
            if (objType.IsArray)
            {
                var elementType = objType.GetElementType();

                //No need to do anything for a byte array
                if (elementType == typeof(byte) && compressor.GetType() == typeof(NullCompressor))
                    return (object)receivedObjectBytes;
                if (elementType.IsPrimitive)
                {
                    int numElements = (int)(BitConverter.ToUInt64(receivedObjectBytes, receivedObjectBytes.Length - 8) / (ulong)Marshal.SizeOf(elementType));

                    Array resultArray = Array.CreateInstance(elementType, numElements);

                    //Problems with garbage collector not deallocating array due to GCHandle have possibly arrisen (see ants profiler).  For Byte[] we don't need to do any of this mess
                    if (elementType == typeof(byte))
                    {
                        var asByteArray = resultArray as byte[];

                        using (System.IO.MemoryStream ms = new System.IO.MemoryStream(asByteArray, true))
                        {
                            compressor.DecompressToStream(receivedObjectBytes, ms);
                        }
                    }
                    else
                    {
                        GCHandle arrayHandle = GCHandle.Alloc(resultArray, GCHandleType.Pinned);

                        try
                        {
                            IntPtr safePtr = Marshal.UnsafeAddrOfPinnedArrayElement(resultArray, 0);

                            using (System.IO.UnmanagedMemoryStream stream = new System.IO.UnmanagedMemoryStream((byte*)safePtr, resultArray.Length * Marshal.SizeOf(elementType), resultArray.Length * Marshal.SizeOf(elementType), System.IO.FileAccess.ReadWrite))
                            {
                                compressor.DecompressToStream(receivedObjectBytes, stream);
                            }
                        }
                        finally
                        {
                            arrayHandle.Free();
                        }
                    }

                    return (object)resultArray;
                }
            }

            return null;
        }

    }
}
