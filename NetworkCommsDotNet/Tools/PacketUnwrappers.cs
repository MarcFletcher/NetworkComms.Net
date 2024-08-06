// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
// 

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Connections;
using System.IO;
using System.Net.Sockets;

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Wrapper used to track the SendReceiveOptions for different packet types.
    /// </summary>
    class PacketTypeUnwrapper
    {
        string packetTypeStr;

        /// <summary>
        /// The SendReceiveOptions associated with this unwrapper.
        /// </summary>
        public SendReceiveOptions Options { get; private set; }

        /// <summary>
        /// Initialise a new PacketTypeUnwrapper
        /// </summary>
        /// <param name="packetTypeStr">The packet type of this unwrapper</param>
        /// <param name="options">The SendReceiveOptions to use with this unwrapper</param>
        public PacketTypeUnwrapper(string packetTypeStr, SendReceiveOptions options)
        {
            this.packetTypeStr = packetTypeStr;
            this.Options = options;
        }
    }

    /// <summary>
    /// The following packetTypeHandlerDelegateWrappers are required so that we can do the totally general and awesome object cast on deserialise.
    /// If there is a way of achieving the same without these wrappers please let us know.
    /// </summary>
    interface IPacketTypeHandlerDelegateWrapper : IEquatable<IPacketTypeHandlerDelegateWrapper>
    {
        object DeSerialize(MemoryStream incomingBytes, SendReceiveOptions options);

        void Process(PacketHeader packetHeader, Connection connection, object obj);
        bool EqualsDelegate(Delegate other);
    }

    class PacketTypeHandlerDelegateWrapper<incomingObjectType> : IPacketTypeHandlerDelegateWrapper
    {
        NetworkComms.PacketHandlerCallBackDelegate<incomingObjectType> innerDelegate;

        public PacketTypeHandlerDelegateWrapper(NetworkComms.PacketHandlerCallBackDelegate<incomingObjectType> packetHandlerDelegate)
        {
            this.innerDelegate = packetHandlerDelegate;
        }

        public object DeSerialize(MemoryStream incomingBytes, SendReceiveOptions options)
        {
            if (incomingBytes == null) return null;
            //if (incomingBytes == null || incomingBytes.Length == 0) return null;
            else
            //{
                //if (options.DataSerializer == null)
                //    throw new ArgumentNullException("options", "The provided options.DataSerializer was null. Cannot continue with deserialise.");

                return options.DataSerializer.DeserialiseDataObject<incomingObjectType>(incomingBytes, options.DataProcessors, options.Options);
            //}
        }

        public void Process(PacketHeader packetHeader, Connection connection, object obj)
        {
            innerDelegate(packetHeader, connection, (obj == null ? default(incomingObjectType) : (incomingObjectType)obj));
        }

        public bool Equals(IPacketTypeHandlerDelegateWrapper other)
        {
            if (innerDelegate == (other as PacketTypeHandlerDelegateWrapper<incomingObjectType>).innerDelegate)
                return true;
            else
                return false;
        }

        public bool EqualsDelegate(Delegate other)
        {
            return other as NetworkComms.PacketHandlerCallBackDelegate<incomingObjectType> == innerDelegate;
        }
    }
}