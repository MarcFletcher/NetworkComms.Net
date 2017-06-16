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
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using ProtoBuf;
using System.Threading.Tasks;
using System.Threading;
using NetworkCommsDotNet.Tools;

namespace RemoteProcedureCalls
{
    /// <summary>
    /// Interface for the RPC proxy generated on the client side. All RPC objects returned from Client.CreateRPCProxyTo{X} implement this interface
    /// </summary>
    public interface IRPCProxy : IDisposable
    {
        /// <summary>
        /// The interface the proxy implements
        /// </summary>
        Type ImplementedInterface { get; }
        
        /// <summary>
        /// The server generated object id for the remote instance
        /// </summary>
        string ServerInstanceID { get; }

        /// <summary>
        /// The NetworkComms.Net connection associated wth the proxy
        /// </summary>
        Connection ServerConnection { get; }

        /// <summary>
        /// The send receive options used when communicating with the server
        /// </summary>
        SendReceiveOptions SendReceiveOptions { get; set; }
                           
        /// <summary>
        /// The timeout for all RPC calls made with this proxy in ms
        /// </summary>
        int RPCTimeout { get; set;  }

        /// <summary>
        /// Gets a value indicating whether the <see cref="IRPCProxy"/> has been disposed of
        /// </summary>
        bool IsDisposed { get; }
    }

    /// <summary>
    /// Provides functions for managing proxy classes to remote objects client side
    /// </summary>
    public static class Client
    {
        /// <summary>
        /// Struct that helps store the cached RPC objects
        /// </summary>
        struct CachedRPCKey
        {
            public string InstanceId { get; private set; }
            public Connection Connection { get; private set; }
            public Type ImplementedInterface { get; private set; }
            public CachedRPCKey(string instanceId, Connection connection, Type implementedInterface)
                : this()
            {
                this.InstanceId = instanceId;
                this.Connection = connection;
                this.ImplementedInterface = implementedInterface;

            }

            public override bool Equals(object obj)
            {
                if (obj != null && obj is CachedRPCKey)
                {
                    var asKey = (CachedRPCKey)obj;
                    return this.InstanceId == asKey.InstanceId && this.Connection == asKey.Connection && this.ImplementedInterface == asKey.ImplementedInterface;
                }
                else
                    return false;
            }

            public override int GetHashCode()
            {
                return InstanceId.GetHashCode() ^ Connection.GetHashCode() ^ ImplementedInterface.GetHashCode();
            }
        }
        
        /// <summary>
        /// The default timeout period in ms for new RPC proxies. Default value is 1000ms 
        /// </summary>
        public static int DefaultRPCTimeout { get; set; }

        /// <summary>
        /// The timeout period allowed for creating new RPC proxies
        /// </summary>
        public static int RPCInitialisationTimeout { get; set; }

        //Object to ensure thread safety on managing the cache of client objects
        private static object cacheLocker = new object();
        //client object cache. This is keyed on the instanceId, the connection and the implemented interface
        private static Dictionary<CachedRPCKey, object> cachedInstances = new Dictionary<CachedRPCKey, object>();

        static Client()
        {
            DefaultRPCTimeout = 1000;
            RPCInitialisationTimeout = 1000;
        }
        
        /// <summary>
        /// Creates a remote proxy instance for the desired interface with the specified server and object identifier.  Instance is private to this client in the sense that no one else can
        /// use the instance on the server unless they have the instanceId returned by this method
        /// </summary>
        /// <typeparam name="I">The interface to use for the proxy</typeparam>
        /// <param name="connection">The connection over which to perform remote procedure calls</param>
        /// <param name="instanceName">The object identifier to use for this proxy</param>
        /// <param name="instanceId">Outputs the instance Id uniquely identifying this object on the server.  Can be used to re-establish connection to object if connection is dropped</param>
        /// <param name="options">SendRecieve options to use</param>
        /// <returns>A proxy class for the interface I allowing remote procedure calls</returns>
        public static I CreateProxyToPrivateInstance<I>(Connection connection, string instanceName, out string instanceId, SendReceiveOptions options = null) where I : class
        {
            //Make sure the type is an interface
            if (!typeof(I).IsInterface)
                throw new InvalidOperationException(typeof(I).Name + " is not an interface");

            string packetTypeRequest = typeof(I).Name + "-NEW-INSTANCE-RPC-CONNECTION";
            string packetTypeResponse = packetTypeRequest + "-RESPONSE";
            instanceId = connection.SendReceiveObject<string, string>(packetTypeRequest, packetTypeResponse, RPCInitialisationTimeout, instanceName);

            if (instanceId == String.Empty)
                throw new RPCException("Server not listening for new instances of type " + typeof(I).ToString());

            return Cache<I>.CreateInstance(instanceId, connection, options);
        }        

        /// <summary>
        /// Creates a remote proxy instance for the desired interface with the specified server and object identifier.  Instance is public in sense that any client can use specified name to make 
        /// calls on the same server side object 
        /// </summary>
        /// <typeparam name="I">The interface to use for the proxy</typeparam>
        /// <param name="connection">The connection over which to perform remote procedure calls</param>
        /// <param name="instanceName">The name specified server side to identify object to create proxy to</param>
        /// <param name="instanceId">Outputs the instance Id uniquely identifying this object on the server.  Can be used to re-establish connection to object if connection is dropped</param>
        /// <param name="options">SendRecieve options to use</param>
        /// <returns>A proxy class for the interface I allowing remote procedure calls</returns>
        public static I CreateProxyToPublicNamedInstance<I>(Connection connection, string instanceName, out string instanceId, SendReceiveOptions options = null) where I : class
        {
            //Make sure the type is an interface
            if (!typeof(I).IsInterface)
                throw new InvalidOperationException(typeof(I).Name + " is not an interface");

            string packetTypeRequest = typeof(I).Name + "-NEW-RPC-CONNECTION-BY-NAME";
            string packetTypeResponse = packetTypeRequest + "-RESPONSE";
            instanceId = connection.SendReceiveObject<string,string>(packetTypeRequest, packetTypeResponse, RPCInitialisationTimeout, instanceName);

            if (instanceId == String.Empty)
                throw new RPCException("Named instance does not exist");

            return Cache<I>.CreateInstance(instanceId, connection, options);
        }

        /// <summary>
        /// Creates a remote proxy to an object with a specific identifier implementing the supplied interface with the specified server
        /// </summary>
        /// <typeparam name="I">The interface to use for the proxy</typeparam>
        /// <param name="connection">The connection over which to perform remote procedure calls</param>
        /// <param name="instanceId">Unique identifier for the instance on the server</param>
        /// <param name="options">SendRecieve options to use</param>
        /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
        public static I CreateProxyToIdInstance<I>(Connection connection, string instanceId, SendReceiveOptions options = null) where I : class
        {
            //Make sure the type is an interface
            if (!typeof(I).IsInterface)
                throw new InvalidOperationException(typeof(I).Name + " is not an interface");

            string packetTypeRequest = typeof(I).Name + "-NEW-RPC-CONNECTION-BY-ID";
            string packetTypeResponse = packetTypeRequest + "-RESPONSE";
            instanceId = connection.SendReceiveObject<string,string>(packetTypeRequest, packetTypeResponse, RPCInitialisationTimeout, instanceId);

            if (instanceId == String.Empty)
                throw new RPCException("Instance with given Id not found");

            return Cache<I>.CreateInstance(instanceId, connection, options);
        }

        //We use this to get the private method. Should be able to get it dynamically
        private static string fullyQualifiedClassName = typeof(Client).AssemblyQualifiedName;

        /// <summary>
        /// Funky class used for dynamically creating the proxy
        /// </summary>
        /// <typeparam name="I"></typeparam>
        private static class Cache<I> where I : class
        {
            private static readonly Type Type;
            
            public static I CreateInstance(string instanceId, Connection connection, SendReceiveOptions options)
            {
                lock (cacheLocker)
                {
                    var key = new CachedRPCKey(instanceId, connection, typeof(I));
                    if (cachedInstances.ContainsKey(key))
                        return (I)cachedInstances[key];

                    //Create the instance
                    var res = (I)Activator.CreateInstance(Type, instanceId, connection, options, typeof(I), Client.DefaultRPCTimeout);

                    Dictionary<string, FieldInfo> eventFields = new Dictionary<string, FieldInfo>();

                    foreach (var ev in typeof(I).GetEvents())
                        eventFields.Add(ev.Name, Type.GetField(ev.Name, BindingFlags.NonPublic | BindingFlags.Instance));

                    //Add the packet handler to deal with incoming event firing
                    connection.AppendIncomingPacketHandler<RemoteCallWrapper>(typeof(I).Name + "-RPC-LISTENER-" + instanceId, (header, internalConnection, eventCallWrapper) =>
                        {
                            try
                            {
                                //Let's do some basic checks on the data we've been sent
                                if (eventCallWrapper == null || !eventFields.ContainsKey(eventCallWrapper.name))
                                    return;

                                var del = eventFields[eventCallWrapper.name].GetValue(res) as Delegate;

                                var sender = eventCallWrapper.args[0].UntypedValue;
                                var args = eventCallWrapper.args[1].UntypedValue;

                                del.DynamicInvoke(sender, args);
                            }
                            catch (Exception) { }

                        });

                    connection.AppendIncomingPacketHandler<string>(typeof(I).Name + "-RPC-DISPOSE-" + instanceId, (header, internalConnection, eventCallWrapper) =>
                        {
                            (res as IRPCProxy).Dispose();
                        });

                    cachedInstances[key] = res;

                    return res;
                }
            }
            
            static Cache()
            {
                ILGenerator il;

                //Make sure the type is an interface
                if (!typeof(I).IsInterface)
                    throw new InvalidOperationException(typeof(I).Name + " is not an interface");

                //Create a new assembly dynamically
                AssemblyName an = new AssemblyName("tmp_" + typeof(I).Name);
                var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndCollect);
                string moduleName = Path.ChangeExtension(an.Name, "dll");
                var module = asm.DefineDynamicModule(moduleName, false);

                string ns = typeof(I).Namespace;
                if (!string.IsNullOrEmpty(ns)) ns += ".";

                //Define our new type implementing the desired interface
                var type = module.DefineType(ns + "grp_" + typeof(I).Name, TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.NotPublic);

                //Define the interface implementations
                type.AddInterfaceImplementation(typeof(I));
                type.AddInterfaceImplementation(typeof(IRPCProxy));

                //Define private fields for the IRPCClient interface
                var serverInstanceId = type.DefineField("serverInstanceID", typeof(string), FieldAttributes.Private);
                var networkConnection = type.DefineField("serverConnection", typeof(Connection), FieldAttributes.Private);
                var sendReceiveOptions = type.DefineField("sendReceiveOptions", typeof(SendReceiveOptions), FieldAttributes.Private);
                var rpcTimeout = type.DefineField("rpcTimeout", typeof(int), FieldAttributes.Private);
                var implementedInterface = type.DefineField("implementedInterface", typeof(Type), FieldAttributes.Private);
                var isDisposed = type.DefineField("isDisposed", typeof(bool), FieldAttributes.Private);

                MethodInfo rpcCallMethod = typeof(Client).GetMethod("RemoteCallClient", BindingFlags.Static | BindingFlags.Public);
                MethodInfo rpcDestroyMethod = typeof(Client).GetMethod("DestroyRPCClient", BindingFlags.Static | BindingFlags.Public);

                //Give the type an empty constructor
                var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new Type[] { typeof(string), typeof(Connection), typeof(SendReceiveOptions), typeof(Type), typeof(int) });
                il = ctor.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, serverInstanceId);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Stfld, networkConnection);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Stfld, sendReceiveOptions);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_S, 4);
                il.Emit(OpCodes.Stfld, implementedInterface);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_S, 5);
                il.Emit(OpCodes.Stfld, rpcTimeout);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stfld, isDisposed);
                il.Emit(OpCodes.Ret);

                #region IRPCClient members

                MethodAttributes propertyEventMethodAttributes = MethodAttributes.Public |
                     MethodAttributes.Virtual |
                     MethodAttributes.SpecialName |
                     MethodAttributes.HideBySig;

                //Next we implement local properties of IRPCClient interface
                foreach (var property in typeof(IRPCProxy).GetProperties())
                {
                    var args = property.GetIndexParameters();
                    var propImpl = type.DefineProperty(property.Name, property.Attributes, property.PropertyType, Array.ConvertAll(args, p => p.ParameterType));

                    FieldBuilder underlyingField = null;

                    switch (property.Name)
                    {
                        case "ServerInstanceID":
                            underlyingField = serverInstanceId;
                            break;
                        case "ServerConnection":
                            underlyingField = networkConnection;
                            break;
                        case "RPCTimeout":
                            underlyingField = rpcTimeout;
                            break;
                        case "ImplementedInterface":
                            underlyingField = implementedInterface;
                            break;
                        case "SendReceiveOptions":
                            underlyingField = sendReceiveOptions;
                            break;
                        case "IsDisposed":
                            underlyingField = isDisposed;
                            break;
                        default:
                            throw new RPCException("Error initialising IRPCClient property");
                    }

                    if (property.CanRead)
                    {
                        #region Property Get

                        var getMethod = type.DefineMethod("get_" + property.Name, propertyEventMethodAttributes, property.PropertyType, Array.ConvertAll(args, p => p.ParameterType));
                        il = getMethod.GetILGenerator();

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, underlyingField);

                        //Return
                        il.Emit(OpCodes.Ret);

                        propImpl.SetGetMethod(getMethod);

                        #endregion Property Get
                    }

                    if (property.CanWrite)
                    {
                        #region Property Set

                        var argTypes = args.Select(a => a.ParameterType).ToList();
                        argTypes.Add(property.PropertyType);

                        var setMethod = type.DefineMethod("set_" + property.Name, propertyEventMethodAttributes, typeof(void), argTypes.ToArray());
                        il = setMethod.GetILGenerator();

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Stfld, underlyingField);

                        //Return
                        il.Emit(OpCodes.Ret);

                        propImpl.SetSetMethod(setMethod);

                        #endregion Property Set
                    }
                }

                foreach (var method in typeof(IDisposable).GetMethods())
                {
                    //Get the method arguements and implement as a public virtual method that we will override
                    var args = method.GetParameters();
                    var methodImpl = type.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual, method.ReturnType, Array.ConvertAll(args, arg => arg.ParameterType));
                    type.DefineMethodOverride(methodImpl, method);

                    //Get the ILGenerator for the method
                    il = methodImpl.GetILGenerator();

                    //Load "this" onto the evaluation stack
                    il.Emit(OpCodes.Ldarg_0);

                    //Close the reference on the server side
                    il.EmitCall(OpCodes.Call, rpcDestroyMethod, null);
                                        
                    //return
                    il.Emit(OpCodes.Ret);
                }

                #endregion

                //Loop through each method in the interface but exclude any event methods
                foreach (var method in typeof(I).GetMethods().Where(m => (m.Attributes & MethodAttributes.SpecialName) == 0))
                {
                    #region Method
                    
                    //Get the method arguements and implement as a public virtual method that we will override
                    var args = method.GetParameters();
                    var methodImpl = type.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual, method.ReturnType, Array.ConvertAll(args, arg => arg.ParameterType));
                    type.DefineMethodOverride(methodImpl, method);

                    //Get the ILGenerator for the method
                    il = methodImpl.GetILGenerator();

                    //Create a local array to store the parameters
                    LocalBuilder array = il.DeclareLocal(typeof(object[]));

                    //Allocate the array and store reference in local variable above
                    il.Emit(OpCodes.Ldc_I4_S, args.Length);
                    il.Emit(OpCodes.Newarr, typeof(object));
                    il.Emit(OpCodes.Stloc, array);

                    //Temporary variable to help store casted objects
                    LocalBuilder objRef = il.DeclareLocal(typeof(object));

                    //Loop through the arguements to the function and store in the array.  Boxing of value types is performced as necessary
                    for (int i = 0; i < args.Length; i++)
                    {
                        //Load the ith arguement onto the stack
                        il.Emit(OpCodes.Ldarg, i + 1);

                        //If the arguement was an out or ref parameter we need to do some additional work
                        if (args[i].ParameterType.IsByRef)
                        {
                            //Load the arguement reference onto the stack
                            il.Emit(OpCodes.Ldind_Ref);

                            //Box the arguement if necessary (might not be needed judging by documentational of last call
                            if (args[i].ParameterType.GetElementType().IsValueType)
                                il.Emit(OpCodes.Box, args[i].ParameterType.GetElementType());
                        }

                        //if the arguement was a value type we need to box it
                        if (args[i].ParameterType.IsValueType)
                            il.Emit(OpCodes.Box, args[i].ParameterType);

                        //Next cast the arguement to object
                        il.Emit(OpCodes.Castclass, typeof(object));
                        il.Emit(OpCodes.Stloc, objRef);

                        //Store the arguement in the arguements array
                        il.Emit(OpCodes.Ldloc, array);
                        il.Emit(OpCodes.Ldc_I4_S, i);
                        il.Emit(OpCodes.Ldloc, objRef);
                        il.Emit(OpCodes.Stelem_Ref);
                    }

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldstr, method.Name);
                    il.Emit(OpCodes.Ldloc, array);

                    il.EmitCall(OpCodes.Call, rpcCallMethod, null);

                    //If the return type is a value type we need to unbox
                    if (method.ReturnType.IsValueType && method.ReturnType != typeof(void))
                        il.Emit(OpCodes.Unbox_Any, method.ReturnType);

                    //If the return value is void we need to pop the result from the invoke reflection call
                    if (method.ReturnType == typeof(void))
                        il.Emit(OpCodes.Pop);

                    //If the return type is a reference type cast back to the correct type
                    if (!method.ReturnType.IsValueType)
                        il.Emit(OpCodes.Castclass, method.ReturnType);

                    //If any ref or out paramters were defined we need to set their values
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i].ParameterType.IsByRef)
                        {
                            //Get the address from the method argument to put the resultant value
                            il.Emit(OpCodes.Ldarg, i + 1);

                            //Load the boxed untyped result from the object[] we created
                            il.Emit(OpCodes.Ldloc, array);
                            il.Emit(OpCodes.Ldc_I4_S, i);
                            il.Emit(OpCodes.Ldelem, typeof(object));

                            //Cast back to the definitive type 
                            il.Emit(OpCodes.Castclass, args[i].ParameterType.GetElementType());

                            //Unbox if necessary
                            if (args[i].ParameterType.GetElementType().IsValueType)
                                il.Emit(OpCodes.Unbox_Any, args[i].ParameterType.GetElementType());

                            //Store the result
                            il.Emit(OpCodes.Stobj, args[i].ParameterType.GetElementType());
                        }
                    }
                    
                    //Return
                    il.Emit(OpCodes.Ret);

                    #endregion Method
                }
                
                //Next we should implement remote properties
                foreach (var property in typeof(I).GetProperties())
                {
                    var args = property.GetIndexParameters();
                    var propImpl = type.DefineProperty(property.Name, property.Attributes, property.PropertyType, Array.ConvertAll(args, p => p.ParameterType));

                    if (property.CanRead)
                    {
                        #region Property Get

                        var getMethod = type.DefineMethod("get_" + property.Name, propertyEventMethodAttributes, property.PropertyType, Array.ConvertAll(args, p => p.ParameterType));
                        il = getMethod.GetILGenerator();

                        //Create a local array to store the parameters
                        LocalBuilder array = il.DeclareLocal(typeof(object[]));

                        //Allocate the array and store reference in local variable above
                        il.Emit(OpCodes.Ldc_I4_S, args.Length);
                        il.Emit(OpCodes.Newarr, typeof(object));
                        il.Emit(OpCodes.Stloc, array);

                        LocalBuilder objRef = il.DeclareLocal(typeof(object));

                        //Loop through the arguements to the function and store in the array.  Boxing of value types is performced as necessary
                        for (int i = 0; i < args.Length; i++)
                        {
                            il.Emit(OpCodes.Ldarg, i + 1);

                            if (args[i].ParameterType.IsValueType)
                                il.Emit(OpCodes.Box, args[i].ParameterType);

                            il.Emit(OpCodes.Castclass, typeof(object));
                            il.Emit(OpCodes.Stloc, objRef);

                            il.Emit(OpCodes.Ldloc, array);
                            il.Emit(OpCodes.Ldc_I4_S, i);
                            il.Emit(OpCodes.Ldloc, objRef);
                            il.Emit(OpCodes.Stelem_Ref);
                        }

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldstr, getMethod.Name);
                        il.Emit(OpCodes.Ldloc, array);

                        il.EmitCall(OpCodes.Call, rpcCallMethod, null);

                        //If the return type is a value type we need to unbox
                        if (getMethod.ReturnType.IsValueType)
                            il.Emit(OpCodes.Unbox_Any, getMethod.ReturnType);

                        if (!getMethod.ReturnType.IsValueType)
                            il.Emit(OpCodes.Castclass, getMethod.ReturnType);

                        //Return
                        il.Emit(OpCodes.Ret);

                        propImpl.SetGetMethod(getMethod);

                        #endregion Property Get
                    }

                    if (property.CanWrite)
                    {
                        #region Property Set

                        var argTypes = args.Select(a => a.ParameterType).ToList();
                        argTypes.Add(property.PropertyType);

                        var setMethod = type.DefineMethod("set_" + property.Name, propertyEventMethodAttributes, typeof(void), argTypes.ToArray());
                        il = setMethod.GetILGenerator();
                        
                        //Create a local array to store the parameters
                        LocalBuilder array = il.DeclareLocal(typeof(object[]));

                        //Allocate the array and store reference in local variable above
                        il.Emit(OpCodes.Ldc_I4_S, argTypes.Count);
                        il.Emit(OpCodes.Newarr, typeof(object));
                        il.Emit(OpCodes.Stloc, array);

                        LocalBuilder objRef = il.DeclareLocal(typeof(object));

                        //Loop through the arguements to the function and store in the array.  Boxing of value types is performced as necessary
                        for (int i = 0; i < argTypes.Count; i++)
                        {
                            il.Emit(OpCodes.Ldarg, i + 1);

                            if (argTypes[i].IsValueType)
                                il.Emit(OpCodes.Box, argTypes[i]);

                            il.Emit(OpCodes.Castclass, typeof(object));
                            il.Emit(OpCodes.Stloc, objRef);

                            il.Emit(OpCodes.Ldloc, array);
                            il.Emit(OpCodes.Ldc_I4_S, i);
                            il.Emit(OpCodes.Ldloc, objRef);
                            il.Emit(OpCodes.Stelem_Ref);
                        }

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldstr, setMethod.Name);
                        il.Emit(OpCodes.Ldloc, array);

                        il.EmitCall(OpCodes.Call, rpcCallMethod, null);

                        il.Emit(OpCodes.Pop);

                        //Return
                        il.Emit(OpCodes.Ret);

                        propImpl.SetSetMethod(setMethod);

                        #endregion Property Set
                    }
                }

                if (typeof(I).GetEvents().Count() != 0)
                {
                    //throw new InvalidOperationException("RPC events are not supported at this time");

                    foreach (var handler in typeof(I).GetEvents())
                    {
                        //Implement the event
                        var evImpl = type.DefineEvent(handler.Name, handler.Attributes, handler.EventHandlerType);
                        //And then the underlying field
                        var eventField = type.DefineField(handler.Name, handler.EventHandlerType, FieldAttributes.Private);

                        //Get the methods for adding and removing delegates
                        var delegateCombineMethod = typeof(Delegate).GetMethod("Combine", new Type[] { typeof(Delegate), typeof(Delegate) });
                        var delegateRemoveMethod = typeof(Delegate).GetMethod("Remove", new Type[] { typeof(Delegate), typeof(Delegate) });

                        //This is used to keep things thread safe
                        var compareExchange = typeof(Interlocked).GetMethods().Where(info => info.Name == "CompareExchange" && info.IsGenericMethod).First().MakeGenericMethod(handler.EventHandlerType);

                        //We will then define the add method
                        #region Event Add Method

                        MethodBuilder method = type.DefineMethod("add_" + handler.Name, propertyEventMethodAttributes, null, new Type[] { handler.EventHandlerType });
                        method.DefineParameter(0, ParameterAttributes.Retval, null);
                        method.DefineParameter(1, ParameterAttributes.In, "value");

                        evImpl.SetAddOnMethod(method);

                        il = method.GetILGenerator();
                        il.DeclareLocal(handler.EventHandlerType);
                        il.DeclareLocal(handler.EventHandlerType);
                        il.DeclareLocal(handler.EventHandlerType);
                        il.DeclareLocal(typeof(bool));

                        Label loop = il.DefineLabel();

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, eventField);
                        il.Emit(OpCodes.Stloc_0);

                        il.EmitWriteLine("Built");

                        il.MarkLabel(loop);// loop start (head: IL_0007)

                        il.Emit(OpCodes.Ldloc_0);
                        il.Emit(OpCodes.Stloc_1);
                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Ldarg_1);

                        il.Emit(OpCodes.Call, delegateCombineMethod);
                        il.Emit(OpCodes.Castclass, handler.EventHandlerType);

                        il.Emit(OpCodes.Stloc_2);
                        il.Emit(OpCodes.Ldarg_0);

                        il.Emit(OpCodes.Ldflda, eventField);

                        il.Emit(OpCodes.Ldloc_2);
                        il.Emit(OpCodes.Ldloc_1);

                        // How to do this?
                        //IL_001e: call !!0 [mscorlib]System.Threading.Interlocked::CompareExchange<class [mscorlib]System.EventHandler`1<class [mscorlib]System.ConsoleCancelEventArgs>>(!!0&, !!0, !!0)
                        //il.Emit(OpCodes.Call, !!0 compareExchange(!!0&,!!0, !!0));
                        il.Emit(OpCodes.Call, compareExchange);

                        il.Emit(OpCodes.Stloc_0);
                        il.Emit(OpCodes.Ldloc_0);
                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Stloc_3);
                        il.Emit(OpCodes.Ldloc_3);
                        il.Emit(OpCodes.Brtrue_S, loop);
                        // end loop
                        il.Emit(OpCodes.Ret);

                        #endregion

                        //Next define the remove method
                        #region Event Remove Method

                        method = type.DefineMethod("remove_" + handler.Name, propertyEventMethodAttributes, null, new Type[] { handler.EventHandlerType });
                        method.DefineParameter(0, ParameterAttributes.Retval, null);
                        method.DefineParameter(1, ParameterAttributes.In, "value");

                        evImpl.SetRemoveOnMethod(method);

                        il = method.GetILGenerator();
                        il.DeclareLocal(handler.EventHandlerType);
                        il.DeclareLocal(handler.EventHandlerType);
                        il.DeclareLocal(handler.EventHandlerType);
                        il.DeclareLocal(typeof(bool));

                        loop = il.DefineLabel();

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, eventField);
                        il.Emit(OpCodes.Stloc_0);

                        il.EmitWriteLine("Built");

                        il.MarkLabel(loop);// loop start (head: IL_0007)

                        il.Emit(OpCodes.Ldloc_0);
                        il.Emit(OpCodes.Stloc_1);
                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Ldarg_1);

                        il.Emit(OpCodes.Call, delegateCombineMethod);
                        il.Emit(OpCodes.Castclass, handler.EventHandlerType);

                        il.Emit(OpCodes.Stloc_2);
                        il.Emit(OpCodes.Ldarg_0);

                        il.Emit(OpCodes.Ldflda, eventField);

                        il.Emit(OpCodes.Ldloc_2);
                        il.Emit(OpCodes.Ldloc_1);

                        // How to do this?
                        //IL_001e: call !!0 [mscorlib]System.Threading.Interlocked::CompareExchange<class [mscorlib]System.EventHandler`1<class [mscorlib]System.ConsoleCancelEventArgs>>(!!0&, !!0, !!0)
                        //il.Emit(OpCodes.Call, !!0 compareExchange(!!0&,!!0, !!0));
                        il.Emit(OpCodes.Call, compareExchange);

                        il.Emit(OpCodes.Stloc_0);
                        il.Emit(OpCodes.Ldloc_0);
                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Stloc_3);
                        il.Emit(OpCodes.Ldloc_3);
                        il.Emit(OpCodes.Brtrue_S, loop);
                        // end loop
                        il.Emit(OpCodes.Ret);

                        #endregion
                    }

                }

                Cache<I>.Type = type.CreateType();
            }

        }

        /// <summary>
        /// Private method for simplifying the remote procedure call.  I don't want to write this in IL!!
        /// </summary>
        /// <param name="clientObject"></param>
        /// <param name="functionToCall"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static object RemoteCallClient(IRPCProxy clientObject, string functionToCall, object[] args)
        {
            if (!clientObject.IsDisposed)
            {
                var connection = clientObject.ServerConnection;

                RemoteCallWrapper wrapper = new RemoteCallWrapper();
                wrapper.args = (from arg in args select RPCArgumentBase.CreateDynamic(arg)).ToList();
                wrapper.name = functionToCall;
                wrapper.instanceId = clientObject.ServerInstanceID;
                var guid = ShortGuid.NewGuid();

                string packetTypeRequest = clientObject.ImplementedInterface.Name + "-RPC-CALL-" + wrapper.instanceId;
                string packetTypeResponse = packetTypeRequest + "-" + guid;

                SendReceiveOptions options = clientObject.SendReceiveOptions;

                if (options != null)
                    wrapper = connection.SendReceiveObject<RemoteCallWrapper, RemoteCallWrapper>(packetTypeRequest, packetTypeResponse, clientObject.RPCTimeout, wrapper, options, options);
                else
                    wrapper = connection.SendReceiveObject<RemoteCallWrapper, RemoteCallWrapper>(packetTypeRequest, packetTypeResponse, clientObject.RPCTimeout, wrapper);
                
                if (wrapper.Exception != null)
                    throw new RPCException(wrapper.Exception);

                for (int i = 0; i < args.Length; i++)
                    args[i] = wrapper.args[i].UntypedValue;

                if (wrapper.result != null)
                    return wrapper.result.UntypedValue;
                else
                    return null;
            }
            else
                throw new ObjectDisposedException("clientObject", "RPC object has already been disposed of and cannot be reused");
        }

        /// <summary>
        /// Causes the provided <see cref="IRPCProxy"/> instance to be disposed
        /// </summary>
        /// <param name="clientObject">The <see cref="IRPCProxy"/> to dispose</param>
        public static void DestroyRPCClient(IRPCProxy clientObject)
        {
            if (!clientObject.IsDisposed)
            {
                clientObject.GetType().GetField("isDisposed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(clientObject, true);

                var connection = clientObject.ServerConnection;

                if (connection.ConnectionInfo.ConnectionState != ConnectionState.Shutdown)
                {
                    string packetTypeRequest = clientObject.ImplementedInterface.Name + "-REMOVE-REFERENCE-" + clientObject.ServerInstanceID;

                    RemoteCallWrapper wrapper = new RemoteCallWrapper();
                    wrapper.args = new List<RPCArgumentBase>();
                    wrapper.name = null;
                    wrapper.instanceId = clientObject.ServerInstanceID;

                    //Tell the server that we are no longer listenning
                    try { connection.SendObject<RemoteCallWrapper>(packetTypeRequest, wrapper); }
                    catch (Exception) { }

                    //Next remove the event packet handler
                    try { connection.RemoveIncomingPacketHandler(clientObject.ImplementedInterface.Name + "-RPC-LISTENER-" + clientObject.ServerInstanceID); }
                    catch (Exception) { }

                    //Next remove the server side dispose handler
                    try { connection.RemoveIncomingPacketHandler(clientObject.ImplementedInterface.Name + "-RPC-DISPOSE-" + clientObject.ServerInstanceID); }
                    catch (Exception) { }
                }

                //Finally remove the object from the cache. This guarentees that if we try to get the instance again at some time in the future
                //we won't end up with a disposed RPC object
                lock (cacheLocker)
                {
                    var cacheKey = new CachedRPCKey(clientObject.ServerInstanceID, clientObject.ServerConnection, clientObject.ImplementedInterface);

                    try { cachedInstances.Remove(cacheKey); }
                    catch (Exception) { }
                }
            }
        }
    }
}
