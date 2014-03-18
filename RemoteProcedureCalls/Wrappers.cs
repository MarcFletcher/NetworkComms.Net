//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

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
