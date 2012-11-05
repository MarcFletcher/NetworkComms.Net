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
using ProtoBuf;

namespace RemoteProcedureCalls
{
    /// <summary>
    /// Wrapper class used for serialisation when running functions remotely
    /// </summary>
    [ProtoContract]
    class RemoteCallWrapper
    {
        [ProtoMember(1)]
        public string instanceId;
        [ProtoMember(2)]
        public string name;
        [ProtoMember(3, DynamicType = true)]
        public List<RPCArgumentBase> args;
        [ProtoMember(4, DynamicType = true)]
        public RPCArgumentBase result;
        [ProtoMember(5)]
        public string Exception;

    }

    /// <summary>
    /// Cheeky base class used in order to allow us to send an array of objects using Protobuf-net
    /// </summary>
    [ProtoContract]
    abstract class RPCArgumentBase
    {
        public abstract object UntypedValue { get; set; }
        public static RPCArgument<T> Create<T>(T value)
        {
            return new RPCArgument<T> { Value = value };
        }
        public static RPCArgumentBase CreateDynamic(object value)
        {
            if (value != null)
            {
                Type type = value.GetType();
                RPCArgumentBase param = (RPCArgumentBase)Activator.CreateInstance(typeof(RPCArgument<>).MakeGenericType(type));
                param.UntypedValue = value;
                return param;
            }
            else
                return null;
        }
    }

    /// <summary>
    /// Cheeky derived class used in order to allow us to send an array of objects using Protobuf-net
    /// </summary>
    [ProtoContract]
    sealed class RPCArgument<T> : RPCArgumentBase
    {
        [ProtoMember(1)]
        public T Value { get; set; }
        public override object UntypedValue { get { return Value; } set { Value = (T)value; } }
    }
}
