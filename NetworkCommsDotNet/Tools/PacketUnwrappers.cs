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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SerializerBase;
using System.Net.Sockets;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Wrapper used to track the sendReceiveOptions for different packet types.
    /// </summary>
    class PacketTypeUnwrapper
    {
        string packetTypeStr;
        public SendReceiveOptions Options { get; private set; }

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
        object DeSerialize(byte[] incomingBytes, SendReceiveOptions options);

        void Process(PacketHeader packetHeader, Connection connection, object obj);
        bool EqualsDelegate(Delegate other);
    }

    class PacketTypeHandlerDelegateWrapper<T> : IPacketTypeHandlerDelegateWrapper
    {
        NetworkComms.PacketHandlerCallBackDelegate<T> innerDelegate;

        public PacketTypeHandlerDelegateWrapper(NetworkComms.PacketHandlerCallBackDelegate<T> packetHandlerDelegate)
        {
            this.innerDelegate = packetHandlerDelegate;
        }

        public object DeSerialize(byte[] incomingBytes, SendReceiveOptions options)
        {
            if (incomingBytes == null || incomingBytes.Length == 0) return null;
            else
                return options.Serializer.DeserialiseDataObject<T>(incomingBytes, options.Compressor);
        }

        public void Process(PacketHeader packetHeader, Connection connection, object obj)
        {
            innerDelegate(packetHeader, connection, (obj == null ? default(T) : (T)obj));
        }

        public bool Equals(IPacketTypeHandlerDelegateWrapper other)
        {
            if (innerDelegate == (other as PacketTypeHandlerDelegateWrapper<T>).innerDelegate)
                return true;
            else
                return false;
        }

        public bool EqualsDelegate(Delegate other)
        {
            return other as NetworkComms.PacketHandlerCallBackDelegate<T> == innerDelegate;
        }
    }
}