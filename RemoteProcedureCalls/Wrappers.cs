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
