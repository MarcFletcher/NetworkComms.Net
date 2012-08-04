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
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using NetworkCommsDotNet;
using ProtoBuf;
using System.Threading.Tasks;
using System.Threading;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Contains methods for setting up objects to be called from remote clients as well as the methods to access those objects client side
    /// </summary>
    public static class RemoteProcedureCalls
    {
        /// <summary>
        /// Wrapper class used for serialisation when running functions remotely
        /// </summary>
        [ProtoContract]
        private class RemoteCallWrapper
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
        private abstract class RPCArgumentBase
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
        private sealed class RPCArgument<T> : RPCArgumentBase
        {
            [ProtoMember(1)]
            public T Value { get; set; }
            public override object UntypedValue { get { return Value; } set { Value = (T)value; } }
        }

        /// <summary>
        /// Provides functions for managing proxy classes to remote objects client side
        /// </summary>
        public static class Client
        {
            /// <summary>
            /// Creates a remote proxy instance for the desired interface with the specified server and object identifier.  Instance is private to this client in the sense that no one else can
            /// use the instance on the server unless they have the instanceId returned by this method
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>
            /// <param name="connectionID">NetworkComms connection to use with server</param>
            /// <param name="instanceName">The object identifier to use for this proxy</param>
            /// <param name="instanceId">Outputs the instance Id uniquely identifying this object on the server.  Can be used to re-establish connection to object if connection is dropped</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T CreateProxyToPrivateInstance<T>(ShortGuid connectionID, string instanceName, out string instanceId) where T : class
            {
                //Make sure the type is an interface
                if (!typeof(T).IsInterface)
                    throw new InvalidOperationException(typeof(T).Name + " is not an interface");

                string packetType = typeof(T).ToString() + "-NEW-INSTANCE-RPC-CONNECTION";
                instanceId = NetworkComms.SendReceiveObject<string>(packetType, connectionID, false, packetType, 1000, instanceName);

                if (instanceId == String.Empty)
                    throw new RPCException("Server not listenning for new instances of type " + typeof(T).ToString());

                return (T)Activator.CreateInstance(Cache<T>.Type, instanceId, connectionID);
            }

            /// <summary>
            /// Creates a remote proxy instance for the desired interface with the specified server and object identifier.  Instance is private to this client in the sense that no one else can
            /// use the instance on the server unless they have the instanceId returned by this method
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>
            /// <param name="serverIP">IPv4 address of the form XXX.XXX.XXX.XXX</param>
            /// <param name="portNumber">The server side port to connect to</param>
            /// <param name="instanceName">The object identifier to use for this proxy</param>
            /// <param name="instanceId">Outputs the instance Id uniquely identifying this object on the server.  Can be used to re-establish connection to object if connection is dropped</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T CreateProxyToPrivateInstance<T>(string serverIP, int portNumber, string instanceName, out string instanceId) where T : class
            {
                //Make sure the type is an interface
                if (!typeof(T).IsInterface)
                    throw new InvalidOperationException(typeof(T).Name + " is not an interface");

                var connectionId = default(ShortGuid);
                string packetType = typeof(T).ToString() + "-NEW-INSTANCE-RPC-CONNECTION";
                instanceId = NetworkComms.SendReceiveObject<string>(packetType, serverIP, portNumber, false, packetType, 1000, instanceName, ref connectionId);

                if (instanceId == String.Empty)
                    throw new RPCException("Server not listenning for new instances of type " + typeof(T).ToString());

                return (T)Activator.CreateInstance(Cache<T>.Type, instanceId, connectionId);
            }

            /// <summary>
            /// Creates a remote proxy instance for the desired interface with the specified server and object identifier.  Instance is public in sense that any client can use specified name to make 
            /// calls on the same server side object 
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>

            /// <param name="instanceName">The name specified server side to identify object to create proxy to</param>
            /// <param name="instanceId">Outputs the instance Id uniquely identifying this object on the server.  Can be used to re-establish connection to object if connection is dropped</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T CreateProxyToPublicNamedInstance<T>(ShortGuid connectionID, string instanceName, out string instanceId) where T : class
            {
                //Make sure the type is an interface
                if (!typeof(T).IsInterface)
                    throw new InvalidOperationException(typeof(T).Name + " is not an interface");

                string packetType = typeof(T).ToString() + "-NEW-RPC-CONNECTION-BY-NAME";
                instanceId = NetworkComms.SendReceiveObject<string>(packetType, connectionID, false, packetType, 1000, instanceName);

                if (instanceId == String.Empty)
                    throw new RPCException("Named instance does not exist");

                return (T)Activator.CreateInstance(Cache<T>.Type, instanceId, connectionID);
            }

            /// <summary>
            /// Creates a remote proxy instance for the desired interface with the specified server and object identifier.  Instance is public in sense that any client can use specified name to make 
            /// calls on the same server side object 
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>
            /// <param name="serverIP">IPv4 address of the form XXX.XXX.XXX.XXX</param>
            /// <param name="portNumber">The server side port to connect to</param>
            /// <param name="instanceName">The name specified server side to identify object to create proxy to</param>
            /// <param name="instanceId">Outputs the instance Id uniquely identifying this object on the server.  Can be used to re-establish connection to object if connection is dropped</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T CreateProxyToPublicNamedInstance<T>(string serverIP, int portNumber, string instanceName, out string instanceId) where T : class
            {
                //Make sure the type is an interface
                if (!typeof(T).IsInterface)
                    throw new InvalidOperationException(typeof(T).Name + " is not an interface");

                var connectionId = default(ShortGuid);
                string packetType = typeof(T).ToString() + "-NEW-RPC-CONNECTION-BY-NAME";
                instanceId = NetworkComms.SendReceiveObject<string>(packetType, serverIP, portNumber, false, packetType, 1000, instanceName, ref connectionId);

                if (instanceId == String.Empty)
                    throw new RPCException("Named instance does not exist");

                return (T)Activator.CreateInstance(Cache<T>.Type, instanceId, connectionId);
            }

            /// <summary>
            /// Creates a remote proxy to an object with a specific identifier implementing the supplied interface with the specified server
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>
            /// <param name="connectionID">NetworkComms connection to use with server</param>
            /// <param name="instanceId">Unique identifier for the instance on the server</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T CreateProxyToIdInstance<T>(ShortGuid connectionID, string instanceId) where T : class
            {
                //Make sure the type is an interface
                if (!typeof(T).IsInterface)
                    throw new InvalidOperationException(typeof(T).Name + " is not an interface");

                string packetType = typeof(T).ToString() + "-NEW-RPC-CONNECTION-BY-ID";
                instanceId = NetworkComms.SendReceiveObject<string>(packetType, connectionID, false, packetType, 1000, instanceId);

                if (instanceId == String.Empty)
                    throw new RPCException("Instance with given Id not found");

                return (T)Activator.CreateInstance(Cache<T>.Type, instanceId, connectionID);
            }

            /// <summary>
            /// Creates a remote proxy to an object with a specific identifier implementing the supplied interface with the specified server
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>
            /// <param name="serverIP">IPv4 address of the form XXX.XXX.XXX.XXX</param>
            /// <param name="portNumber">The server side port to connect to</param>
            /// <param name="instanceId">Unique identifier for the instance on the server</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T CreateProxyToIdInstance<T>(string serverIP, int portNumber, string instanceId) where T : class
            {
                //Make sure the type is an interface
                if (!typeof(T).IsInterface)
                    throw new InvalidOperationException(typeof(T).Name + " is not an interface");

                var connectionId = default(ShortGuid);
                string packetType = typeof(T).ToString() + "-NEW-RPC-CONNECTION-BY-ID";
                instanceId = NetworkComms.SendReceiveObject<string>(packetType, serverIP, portNumber, false, packetType, 1000, instanceId, ref connectionId);

                if (instanceId == String.Empty)
                    throw new RPCException("Instance with given instanceId not found by server.");

                return (T)Activator.CreateInstance(Cache<T>.Type, instanceId, connectionId);
            }

            //We use this to get the private method. Should be able to get it dynamically
            private static string fullyQualifiedClassName = typeof(Client).AssemblyQualifiedName;// "NetworkCommsDotNet.RemoteProcedureCalls+ProxyClassGenerator, NetworkCommsDotNet, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null";

            /// <summary>
            /// Funky class used for dynamically creating the proxy
            /// </summary>
            /// <typeparam name="T"></typeparam>
            private static class Cache<T> where T : class
            {
                internal static readonly Type Type;
                static Cache()
                {
                    //Make sure the type is an interface
                    if (!typeof(T).IsInterface)
                        throw new InvalidOperationException(typeof(T).Name + " is not an interface");

                    //Create a new assembly dynamically
                    AssemblyName an = new AssemblyName("tmp_" + typeof(T).Name);
                    var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndCollect);
                    string moduleName = Path.ChangeExtension(an.Name, "dll");
                    var module = asm.DefineDynamicModule(moduleName, false);

                    string ns = typeof(T).Namespace;
                    if (!string.IsNullOrEmpty(ns)) ns += ".";

                    //Define our new type implementing the desired interface
                    var type = module.DefineType(ns + "grp_" + typeof(T).Name, TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.NotPublic);

                    type.AddInterfaceImplementation(typeof(T));

                    var serverInstanceId = type.DefineField("ServerInstanceID", typeof(string), FieldAttributes.Private);
                    var serverConnectionID = type.DefineField("ServerConnectionID", typeof(ShortGuid), FieldAttributes.Private);

                    //Get the methods for the reflection invocation.  MOVE OUTSIDE THIS LOOP
                    MethodInfo getTypeMethod = typeof(Type).GetMethod("GetType", new Type[] { typeof(string) });
                    MethodInfo getgetMethod = typeof(Type).GetMethod("GetMethod", new Type[] { typeof(string), typeof(BindingFlags) });
                    MethodInfo invokeMethod = typeof(MethodInfo).GetMethod("Invoke", new Type[] { typeof(object), typeof(object[]) });

                    //Give the type an empty constructor
                    var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new Type[] { typeof(string), typeof(ShortGuid) });
                    var il = ctor.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Stfld, serverInstanceId);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Stfld, serverConnectionID);
                    il.Emit(OpCodes.Ret);

                    //Loop through each method in the interface
                    foreach (var method in typeof(T).GetMethods())
                    {
                        #region Method

                        //Get the method arguements and implement as a public virtual method that we will override
                        var args = method.GetParameters();
                        var methodImpl = type.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual, method.ReturnType, Array.ConvertAll(args, arg => arg.ParameterType));
                        type.DefineMethodOverride(methodImpl, method);

                        //Get the ILGenerator for the method
                        il = methodImpl.GetILGenerator();
                        il.Emit(OpCodes.Ldarg_0);

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

                        //Declare an object that we will not set so as to get an IntPtr.Zero. There must be a better way of doing this but it works
                        LocalBuilder zeroPtr = il.DeclareLocal(typeof(object));

                        //Declare an array to hold the parameters for the reflection invocation
                        LocalBuilder reflectionParamArray = il.DeclareLocal(typeof(object[]));
                        il.Emit(OpCodes.Ldc_I4_S, 5);
                        il.Emit(OpCodes.Newarr, typeof(object));
                        il.Emit(OpCodes.Stloc, reflectionParamArray);

                        //Load the connection id into first element of array for reflection invocation of method
                        il.Emit(OpCodes.Ldloc, reflectionParamArray);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, serverConnectionID);
                        il.Emit(OpCodes.Box, serverConnectionID.FieldType);
                        il.Emit(OpCodes.Stelem_Ref);

                        //Load the handler name to call into second element of array for reflection invocation of method
                        il.Emit(OpCodes.Ldloc, reflectionParamArray);
                        il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Ldstr, typeof(T).ToString() + "-RPC-CALL");
                        il.Emit(OpCodes.Stelem_Ref);

                        //Load the connection ip into third element of array for reflection invocation of method
                        il.Emit(OpCodes.Ldloc, reflectionParamArray);
                        il.Emit(OpCodes.Ldc_I4_2);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, serverInstanceId);
                        il.Emit(OpCodes.Stelem_Ref);

                        //Load the function name to call into fourth element of array for reflection invocation of method
                        il.Emit(OpCodes.Ldloc, reflectionParamArray);
                        il.Emit(OpCodes.Ldc_I4_3);
                        il.Emit(OpCodes.Ldstr, method.Name);
                        il.Emit(OpCodes.Stelem_Ref);

                        //Load the connection ip into fith element of array for reflection invocation of method
                        il.Emit(OpCodes.Ldloc, reflectionParamArray);
                        il.Emit(OpCodes.Ldc_I4_4);
                        il.Emit(OpCodes.Ldloc, array);
                        il.Emit(OpCodes.Stelem_Ref);

                        //The method we want to call is private and static so we need binding flags as such
                        int bindingFlags = (int)(BindingFlags.Static | BindingFlags.NonPublic);

                        //Get the the type for this static class
                        il.Emit(OpCodes.Ldstr, fullyQualifiedClassName);
                        il.Emit(OpCodes.Call, getTypeMethod);

                        //Get the RemoteCallClient method
                        il.Emit(OpCodes.Ldstr, "RemoteCallClient");
                        il.Emit(OpCodes.Ldc_I4, bindingFlags);
                        il.Emit(OpCodes.Callvirt, getgetMethod);

                        //Invoke the method using reflection
                        il.Emit(OpCodes.Ldloc, zeroPtr);
                        il.Emit(OpCodes.Ldloc, reflectionParamArray);
                        il.Emit(OpCodes.Callvirt, invokeMethod);

                        //If the return type is a value type we need to unbox
                        if (method.ReturnType.IsValueType && method.ReturnType != typeof(void))
                            il.Emit(OpCodes.Unbox_Any, method.ReturnType);

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
                    foreach (var property in typeof(T).GetProperties())
                    {
                        var args = property.GetIndexParameters();
                        var propImpl = type.DefineProperty(property.Name, property.Attributes, property.PropertyType, Array.ConvertAll(args, p => p.ParameterType));

                        if (property.CanRead)
                        {
                            #region Property Get

                            MethodAttributes getsetAttr = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

                            var getMethod = type.DefineMethod("get_" + property.Name, getsetAttr, property.PropertyType, Array.ConvertAll(args, p => p.ParameterType));
                            il = getMethod.GetILGenerator();
                            il.Emit(OpCodes.Ldarg_0);

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

                            //Declare an object that we will not set so as to get an IntPtr.Zero. There must be a better way of doing this but it works
                            LocalBuilder zeroPtr = il.DeclareLocal(typeof(object));

                            //Declare an array to hold the parameters for the reflection invocation
                            LocalBuilder reflectionParamArray = il.DeclareLocal(typeof(object[]));
                            il.Emit(OpCodes.Ldc_I4_S, 5);
                            il.Emit(OpCodes.Newarr, typeof(object));
                            il.Emit(OpCodes.Stloc, reflectionParamArray);

                            //Load the connection id into first element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverConnectionID);
                            il.Emit(OpCodes.Box, serverConnectionID.FieldType);
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the handler name to call into second element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_1);
                            il.Emit(OpCodes.Ldstr, typeof(T).ToString() + "-RPC-CALL");
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the connection ip into third element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_2);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverInstanceId);
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the function name to call into fourth element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_3);
                            il.Emit(OpCodes.Ldstr, getMethod.Name);
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the connection ip into fith element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_4);
                            il.Emit(OpCodes.Ldloc, array);
                            il.Emit(OpCodes.Stelem_Ref);

                            //The method we want to call is private and static so we need binding flags as such
                            int bindingFlags = (int)(BindingFlags.Static | BindingFlags.NonPublic);

                            //Get the the type for this static class
                            il.Emit(OpCodes.Ldstr, fullyQualifiedClassName);
                            il.Emit(OpCodes.Call, getTypeMethod);

                            //Get the RemoteCallClient method
                            il.Emit(OpCodes.Ldstr, "RemoteCallClient");
                            il.Emit(OpCodes.Ldc_I4, bindingFlags);
                            il.Emit(OpCodes.Callvirt, getgetMethod);

                            //Invoke the method using reflection
                            il.Emit(OpCodes.Ldloc, zeroPtr);
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Callvirt, invokeMethod);

                            //If the return type is a value type we need to unbox
                            if (getMethod.ReturnType.IsValueType)
                                il.Emit(OpCodes.Unbox_Any, getMethod.ReturnType);

                            //Return
                            il.Emit(OpCodes.Ret);

                            propImpl.SetGetMethod(getMethod);

                            #endregion Property Get
                        }

                        if (property.CanWrite)
                        {
                            #region Property Set

                            MethodAttributes getsetAttr = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

                            var setMethod = type.DefineMethod("set_" + property.Name, getsetAttr, property.PropertyType, Array.ConvertAll(args, p => p.ParameterType));
                            il = setMethod.GetILGenerator();
                            il.Emit(OpCodes.Ldarg_0);

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

                            //Declare an object that we will not set so as to get an IntPtr.Zero. There must be a better way of doing this but it works
                            LocalBuilder zeroPtr = il.DeclareLocal(typeof(object));

                            //Declare an array to hold the parameters for the reflection invocation
                            LocalBuilder reflectionParamArray = il.DeclareLocal(typeof(object[]));
                            il.Emit(OpCodes.Ldc_I4_S, 5);
                            il.Emit(OpCodes.Newarr, typeof(object));
                            il.Emit(OpCodes.Stloc, reflectionParamArray);

                            //Load the connection id into first element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverConnectionID);
                            il.Emit(OpCodes.Box, serverConnectionID.FieldType);
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the handler name to call into second element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_1);
                            il.Emit(OpCodes.Ldstr, typeof(T).ToString() + "-RPC-CALL");
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the connection ip into third element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_2);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverInstanceId);
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the function name to call into fourth element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_3);
                            il.Emit(OpCodes.Ldstr, setMethod.Name);
                            il.Emit(OpCodes.Stelem_Ref);

                            //Load the connection ip into fith element of array for reflection invocation of method
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Ldc_I4_4);
                            il.Emit(OpCodes.Ldloc, array);
                            il.Emit(OpCodes.Stelem_Ref);

                            //The method we want to call is private and static so we need binding flags as such
                            int bindingFlags = (int)(BindingFlags.Static | BindingFlags.NonPublic);

                            //Get the the type for this static class
                            il.Emit(OpCodes.Ldstr, fullyQualifiedClassName);
                            il.Emit(OpCodes.Call, getTypeMethod);

                            //Get the RemoteCallClient method
                            il.Emit(OpCodes.Ldstr, "RemoteCallClient");
                            il.Emit(OpCodes.Ldc_I4, bindingFlags);
                            il.Emit(OpCodes.Callvirt, getgetMethod);

                            //Invoke the method using reflection
                            il.Emit(OpCodes.Ldloc, zeroPtr);
                            il.Emit(OpCodes.Ldloc, reflectionParamArray);
                            il.Emit(OpCodes.Callvirt, invokeMethod);

                            //Return
                            il.Emit(OpCodes.Ret);

                            propImpl.SetSetMethod(setMethod);

                            #endregion Property Set
                        }
                    }

                    foreach (var handler in typeof(T).GetEvents())
                    {
                        throw new InvalidOperationException(@"Events in interfaces are not supported at this time. If this is a desired feature please submit a request at http://bitbucket.org/MarcF/networkcomms.net");
                    }

                    Cache<T>.Type = type.CreateType();
                }
            }

            /// <summary>
            /// Private method for simplifying the remote procedure call.  I don't want to write this in IL!!
            /// </summary>
            /// <param name="connectionID"></param>
            /// <param name="connectionName"></param>
            /// <param name="functionToCall"></param>
            /// <param name="args"></param>
            /// <returns></returns>
            private static object RemoteCallClient(ShortGuid connectionID, string handlerType, string instanceId, string functionToCall, object[] args)
            {
                RemoteCallWrapper wrapper = new RemoteCallWrapper();
                wrapper.args = (from arg in args select RPCArgumentBase.CreateDynamic(arg)).ToList();
                wrapper.name = functionToCall;
                wrapper.instanceId = instanceId;

                wrapper = NetworkComms.SendReceiveObject<RemoteCallWrapper>(handlerType, connectionID, false, handlerType, 1000, wrapper);

                if (wrapper.Exception != null)
                    throw new RPCException(wrapper.Exception);

                for (int i = 0; i < args.Length; i++)
                    args[i] = wrapper.args[i].UntypedValue;

                if (wrapper.result != null)
                    return wrapper.result.UntypedValue;
                else
                    return null;
            }
        }

        /// <summary>
        /// Contains methods for managing objects server side which allow Remote Procedure Calls
        /// </summary>
        public static class Server
        {
            private class RPCStorageWrapper
            {
                public enum RPCObjectType
                {
                    Public,
                    Private
                }

                private object obj;

                public object RPCObject
                {
                    get
                    {
                        LastAccess = DateTime.Now;
                        return obj;
                    }
                }

                public Type InterfaceType { get; private set; }
                public DateTime LastAccess { get; private set; }
                public int TimeOut { get; private set; }
                public RPCObjectType Type { get; private set; }

                public RPCStorageWrapper(object RPCObject, Type interfaceType, RPCObjectType Type, int timeout = int.MaxValue)
                {
                    this.TimeOut = timeout;
                    this.obj = RPCObject;
                    this.LastAccess = DateTime.Now;
                    InterfaceType = interfaceType;
                    this.Type = Type;
                }
            }

            private static object locker = new object();

            static readonly int salt;
            static readonly System.Security.Cryptography.HashAlgorithm hash;

            private static Dictionary<string, RPCStorageWrapper> RPCObjects = new Dictionary<string, RPCStorageWrapper>();
            private static Dictionary<Type, int> timeoutByInterfaceType = new Dictionary<Type, int>();
            private static Dictionary<string, Delegate> addedHandlers = new Dictionary<string, Delegate>();

            static Server()
            {
                var r = System.Security.Cryptography.RandomNumberGenerator.Create();
                byte[] bytes = new byte[4];
                r.GetBytes(bytes);
                salt = BitConverter.ToInt32(bytes, 0);

                hash = System.Security.Cryptography.HMACSHA512.Create();

                AutoResetEvent watcherWaitEvent = new AutoResetEvent(false);
                Task watcher = Task.Factory.StartNew(() =>
                    {
                        do
                        {
                            lock (locker)
                            {

                                List<string> keysToRemove = new List<string>();

                                foreach (var obj in RPCObjects)
                                {
                                    if ((DateTime.Now - obj.Value.LastAccess).TotalMilliseconds > obj.Value.TimeOut)
                                        keysToRemove.Add(obj.Key);
                                }

                                RemoveRPCObjects(keysToRemove);
                            }

                        } while (!watcherWaitEvent.WaitOne(5000));

                        ShutdownAllRPC();

                    }, TaskCreationOptions.LongRunning);

                NetworkComms.OnCommsShutdown += new EventHandler<EventArgs>((sender, args) =>
                {
                    watcherWaitEvent.Set();
                    watcher.Wait();
                });
            }

            /// <summary>
            /// Registers a type for private RPC whereby each client generates it's own private instances on the server
            /// </summary>
            /// <typeparam name="T">The type of object to create new instances of for RPC.  Must implement I</typeparam>
            /// <typeparam name="I">Interface that should be provided for RPC</typeparam>
            /// <param name="timeout">If specified each RPC object created will be destroyed if it is unused for a time, in ms, specified by timeout</param>
            /// <param name="enableAutoListen">Specifies whether Network comms should automatically start listening for new connections</param>
            public static void RegisterTypeForPrivateRemoteCall<T, I>(int timeout = int.MaxValue, bool enableAutoListen = true) where T : I, new()
            {
                lock (locker)
                {

                    if (!typeof(I).IsInterface)
                        throw new InvalidOperationException(typeof(I).Name
                            + " is not an interface");

                    if (!addedHandlers.ContainsKey(typeof(I).ToString() + "-NEW-INSTANCE-RPC-CONNECTION"))
                    {
                        var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(NewInstanceRPCHandler<T, I>);

                        timeoutByInterfaceType.Add(typeof(I), timeout);

                        NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).ToString() + "-NEW-INSTANCE-RPC-CONNECTION", del);
                        addedHandlers.Add(typeof(I).ToString() + "-NEW-INSTANCE-RPC-CONNECTION", del);
                    }
                    else
                    {
                        throw new RPCException("Interface already has a type associated with it for new instance RPC");
                    }
                }
            }

            /// <summary>
            /// Registers a specfic object instance, with the supplied name, for RPC
            /// </summary>
            /// <typeparam name="T">The type of the object to register. Must implement I</typeparam>
            /// <typeparam name="I">The interface to be provided for RPC</typeparam>
            /// <param name="instance">Instance to register for RPC</param>
            /// <param name="instanceName">Name of the instance to be used by clients for RPC</param>
            /// <param name="enableAutoListen">Specifies whether Network comms should automatically start listening for new connections</param>
            public static void RegisterInstanceForPublicRemoteCall<T, I>(T instance, string instanceName, bool enableAutoListen = true) where T : I
            {
                lock (locker)
                {
                    if (!typeof(I).IsInterface)
                        throw new InvalidOperationException(typeof(I).Name
                            + " is not an interface");

                    string instanceId = BitConverter.ToString(hash.ComputeHash(BitConverter.GetBytes(((typeof(T).Name + instanceName).GetHashCode() ^ salt))));

                    if (!RPCObjects.ContainsKey(instanceId))
                        RPCObjects.Add(instanceId, new RPCStorageWrapper(instance, typeof(I), RPCStorageWrapper.RPCObjectType.Public));

                    if (!addedHandlers.ContainsKey(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-NAME"))
                    {
                        var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(RetrieveNamedRPCHandler<T, I>);

                        NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-NAME", del);
                        addedHandlers.Add(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-NAME", del);

                        if (!addedHandlers.ContainsKey(typeof(I).ToString() + "-RPC-CALL"))
                        {
                            var callDel = new NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper>(RunRPCFunctionHandler<T, I>);

                            NetworkComms.AppendGlobalIncomingPacketHandler<RemoteCallWrapper>(typeof(I).ToString() + "-RPC-CALL", callDel);
                            addedHandlers.Add(typeof(I).ToString() + "-RPC-CALL", callDel);
                        }
                    }
                    
                    if (!addedHandlers.ContainsKey(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID"))
                    {
                        var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(RetrieveByIDRPCHandler<T, I>);

                        NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID", del);
                        addedHandlers.Add(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID", del);
                    }
                }
            }

            /// <summary>
            /// Removes all private RPC objects for the specified interface type.  Stops listenning for new RPC instance connections
            /// </summary>
            /// <typeparam name="T">Object type that implements the specified interface I</typeparam>
            /// <typeparam name="I">Interface that is being implemented for RPC calls</typeparam>
            public static void RemovePrivateRPCObjectType<T, I>() where T : I, new()
            {
                lock (locker)
                {
                    if (timeoutByInterfaceType.ContainsKey(typeof(I)))
                        timeoutByInterfaceType.Remove(typeof(I));

                    var keys = (from obj in RPCObjects
                                where obj.Value.InterfaceType == typeof(I) && obj.Value.Type == RPCStorageWrapper.RPCObjectType.Private
                                select obj.Key).ToList();

                    RemoveRPCObjects(keys);

                    addedHandlers.Remove(typeof(I).ToString() + "-NEW-INSTANCE-RPC-CONNECTION");
                }
            }

            /// <summary>
            /// Disables RPC calls for the supplied named public object supplied
            /// </summary>
            /// <param name="instanceName">Instance to disable RPC for</param>
            public static void RemovePublicRPCObject(object instanceName)
            {
                lock (locker)
                {                    
                    var keys = (from obj in RPCObjects
                                where obj.Value.RPCObject == instanceName && obj.Value.Type == RPCStorageWrapper.RPCObjectType.Public
                                select obj.Key).ToList();

                    RemoveRPCObjects(keys);
                }
            }

            /// <summary>
            /// Removes all public and private RPC objects and removes all related packet handlers from NetworkComms
            /// </summary>
            public static void ShutdownAllRPC()
            {
                lock (locker)
                {
                    RemoveRPCObjects(RPCObjects.Keys.ToList());

                    var allRPCHandlersLeft = addedHandlers.Keys.ToList();

                    foreach (var handlerName in allRPCHandlersLeft)
                    {
                        NetworkComms.RemoveGlobalIncomingPacketHandler(handlerName, addedHandlers[handlerName]);
                        addedHandlers.Remove(handlerName);
                    }
                }
            }

            private static void RemoveRPCObjects(List<string> keysToRemove)
            {
                lock (locker)
                {
                    var typesToRemoveHandlersFrom = (from val in RPCObjects.Values
                                                     select val.InterfaceType).Distinct().Except(
                                                        (from key in RPCObjects.Keys.Except(keysToRemove)
                                                         select RPCObjects[key].InterfaceType));

                    foreach (var type in typesToRemoveHandlersFrom)
                    {
                        var toRemove = (from key in addedHandlers.Keys
                                        where key.StartsWith(type.ToString()) && !key.EndsWith("-NEW-INSTANCE-RPC-CONNECTION") && key.Contains("-RPC-")
                                        select key).ToArray();

                        foreach (var key in toRemove)
                        {
                            NetworkComms.RemoveGlobalIncomingPacketHandler(key, addedHandlers[key]);
                            addedHandlers.Remove(key);
                        }
                    }

                    foreach (var key in keysToRemove)
                        RPCObjects.Remove(key);
                }
            }

            #region RPC Network comms handlers

            private static void NewInstanceRPCHandler<T, I>(PacketHeader header, ConnectionInfo connectionInfo, string instanceName) where T : I, new()
            {
                lock (locker)
                {
                    var instanceId = BitConverter.ToString(hash.ComputeHash(BitConverter.GetBytes(((typeof(T).Name + instanceName + connectionInfo.NetworkIdentifier.ToString()).GetHashCode() ^ salt))));

                    if (!RPCObjects.ContainsKey(instanceId))
                        RPCObjects.Add(instanceId, new RPCStorageWrapper(new T(), typeof(I), RPCStorageWrapper.RPCObjectType.Private, timeoutByInterfaceType[typeof(I)]));

                    if (!addedHandlers.ContainsKey(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID"))
                    {
                        var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(RetrieveByIDRPCHandler<T, I>);

                        NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID", del);
                        addedHandlers.Add(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID", del);                        
                    }

                    if (!addedHandlers.ContainsKey(typeof(I).ToString() + "-RPC-CALL"))
                    {
                        var callDel = new NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper>(RunRPCFunctionHandler<T, I>);

                        NetworkComms.AppendGlobalIncomingPacketHandler<RemoteCallWrapper>(typeof(I).ToString() + "-RPC-CALL", callDel);
                        addedHandlers.Add(typeof(I).ToString() + "-RPC-CALL", callDel);
                    }

                    NetworkComms.SendObject(typeof(I).ToString() + "-NEW-INSTANCE-RPC-CONNECTION", connectionInfo.NetworkIdentifier, false, instanceId);
                }
            }

            private static void RetrieveNamedRPCHandler<T, I>(PacketHeader header, ConnectionInfo connectionInfo, string instanceName) where T : I
            {
                lock (locker)
                {
                    string instanceId = BitConverter.ToString(hash.ComputeHash(BitConverter.GetBytes(((typeof(T).Name + instanceName).GetHashCode() ^ salt))));

                    if (!RPCObjects.ContainsKey(instanceId))
                        instanceId = String.Empty;
                    else
                    {
                        var nothing = RPCObjects[instanceId].RPCObject;
                    }

                    NetworkComms.SendObject(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-NAME", connectionInfo.NetworkIdentifier, false, instanceId);
                }
            }

            private static void RetrieveByIDRPCHandler<T, I>(PacketHeader header, ConnectionInfo connectionInfo, string instanceId) where T : I
            {
                lock (locker)
                {
                    if (!RPCObjects.ContainsKey(instanceId) || RPCObjects[instanceId].InterfaceType != typeof(I))
                        instanceId = String.Empty;
                    else
                    {
                        var nothing = RPCObjects[instanceId].RPCObject;
                    }

                    NetworkComms.SendObject(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID", connectionInfo.NetworkIdentifier, false, instanceId);
                }
            }

            private static void RunRPCFunctionHandler<T, I>(PacketHeader header, ConnectionInfo connectionInfo, RemoteCallWrapper wrapper) where T : I
            {
                I instance = default(I);
                MethodInfo funcRef = typeof(I).GetMethod(wrapper.name);

                try
                {
                    lock (locker)
                    {
                        instance = (I)(RPCObjects[wrapper.instanceId].RPCObject);
                    }
                }
                catch (Exception)
                {
                    wrapper.result = null;
                    wrapper.Exception = "SERVER SIDE EXCEPTION\n\n" + "Invalid instanceID" + "\n\nEND SERVER SIDE EXCEPTION\n\n";
                    NetworkComms.SendObject(header.PacketType, connectionInfo.NetworkIdentifier, false, wrapper);
                    return;
                }

                object[] args = null;

                if (wrapper.args == null)
                    args = new object[0];
                else
                    args = (from arg in wrapper.args select arg.UntypedValue).ToArray();

                try
                {
                    wrapper.result = RPCArgumentBase.CreateDynamic(funcRef.Invoke(instance, args));
                    wrapper.args = (from arg in args select RPCArgumentBase.CreateDynamic(arg)).ToList();
                }
                catch (Exception e)
                {
                    wrapper.result = null;
                    wrapper.Exception = "SERVER SIDE EXCEPTION\n\n" + e.ToString() + "\n\nEND SERVER SIDE EXCEPTION\n\n";
                }

                NetworkComms.SendObject(header.PacketType, connectionInfo.NetworkIdentifier, false, wrapper);

            }

            #endregion
        }
    }
}
