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

namespace SerializerBase
{
    public interface ISerialize
    {
        /// <summary>
        /// Converts objectToSerialize to an array of bytes using the compression provided by compressor
        /// </summary>
        /// <typeparam name="T">Type of object to serialize</typeparam>
        /// <param name="objectToSerialise">Object to serialize</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>Serialized array of bytes</returns>
        byte[] SerialiseDataObject<T>(T objectToSerialise, ICompress compressor);

        /// <summary>
        /// Converts array of bytes previously serialized and compressed using compressor to an object of provided type
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize to</typeparam>
        /// <param name="receivedObjectBytes">Byte array containing serialized and compressed object</param>
        /// <param name="compressor">Compression provider to use</param>
        /// <returns>The deserialized object</returns>
        T DeserialiseDataObject<T>(byte[] receivedObjectBytes, ICompress compressor);
    }

    /// <summary>
    /// Abstract class that provides fastest method for serializing arrays of primitive data types.
    /// </summary>
    public abstract class ArraySerializer : ISerialize
    {
        #region ISerialize Members

        /// <summary>
        /// Serializes objectToSerialize to a byte array using compression provided by compressor if T is an array of primitives.  Otherwise returns default value for T.  Override 
        /// to serialize other types
        /// </summary>
        /// <typeparam name="T">Type paramter of objectToSerialize.  If it is an Array will be serialized here</typeparam>
        /// <param name="objectToSerialise">Object to serialize</param>
        /// <param name="compressor">The compression provider to use</param>
        /// <returns>The serialized and compressed bytes of objectToSerialize</returns>
        public unsafe virtual byte[] SerialiseDataObject<T>(T objectToSerialise, ICompress compressor)
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
                    var asArray = objectToSerialise as Array;
                    GCHandle arrayHandle = GCHandle.Alloc(asArray, GCHandleType.Pinned);
                    byte[] output = null;

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
        public unsafe virtual T DeserialiseDataObject<T>(byte[] receivedObjectBytes, ICompress compressor)
        {
            Type objType = typeof(T);

            if (objType.IsArray)
            {
                var elementType = objType.GetElementType();

                //No need to do anything for a byte array
                if (elementType == typeof(byte) && compressor.GetType() == typeof(NullCompressor))
                    return (T)((object)receivedObjectBytes);
                if (elementType.IsPrimitive)
                {
                    int numElements = (int)(BitConverter.ToUInt64(receivedObjectBytes, receivedObjectBytes.Length - 8) / (ulong)Marshal.SizeOf(elementType));

                    Array resultArray = Array.CreateInstance(elementType, numElements);
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

                    return (T)((object)resultArray);
                }
            }

            return default(T);
        }

        #endregion
    }
}
