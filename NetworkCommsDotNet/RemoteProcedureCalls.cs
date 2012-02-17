using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using NetworkCommsDotNet;
using ProtoBuf;

namespace NetworkCommsDotNet
{
    public static class RemoteProcedureCalls
    {
        /// <summary>
        /// Wrapper class used for serialisation when running functions remotely
        /// </summary>
        [ProtoContract]
        private class RemoteCallWrapper
        {
            [ProtoMember(1)]
            public string name;
            [ProtoMember(2, DynamicType = true)]
            public List<RPCArgumentBase> args;
            [ProtoMember(3, DynamicType = true)]
            public RPCArgumentBase result;
            [ProtoMember(4)]
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
        /// Provides functions for managing proxy classes to remote objects
        /// </summary>
        public static class ProxyClassGenerator
        {
            /// <summary>
            /// Creates a remote proxy instance for the desired interface with the specified server and object identifier
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>
            /// <param name="connectionID">NetworkComms conneciton to use with server</param>
            /// <param name="name">The object identifier to use for this proxy</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T Create<T>(ShortGuid connectionID, string name) where T : class
            {
                string packetType = typeof(T).ToString() + "-NEW-RPC";
                var connectionName = NetworkComms.SendReceiveObject<string>(packetType, connectionID, false, packetType, 1000, name);
                return (T)Activator.CreateInstance(Cache<T>.Type, connectionName, connectionID);
            }

            /// <summary>
            /// Creates a remote proxy instance for the desired interface with the specified server and object identifier
            /// </summary>
            /// <typeparam name="T">The interface to use for the proxy</typeparam>
            /// <param name="serverIP">IPv4 address of the form XXX.XXX.XXX.XXX</param>
            /// <param name="portNumber">The server side port to connect to</param>
            /// <param name="name">The object identifier to use for this proxy</param>
            /// <returns>A proxy class for the interface T allowing remote procedure calls</returns>
            public static T Create<T>(string serverIP, int portNumber, string name) where T : class
            {
                var connectionId = default(ShortGuid);
                string packetType = typeof(T).ToString() + "-NEW-RPC";
                var connectionName = NetworkComms.SendReceiveObject<string>(packetType, serverIP, portNumber, false, packetType, 1000, name, ref connectionId);
                return (T)Activator.CreateInstance(Cache<T>.Type, connectionName, connectionId);
            }

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
                    var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave);
                    string moduleName = Path.ChangeExtension(an.Name, "dll");
                    var module = asm.DefineDynamicModule(moduleName, false);
                    string ns = typeof(T).Namespace;
                    if (!string.IsNullOrEmpty(ns)) ns += ".";

                    //Define our new type implementing the desired interface
                    var type = module.DefineType(ns + "grp_" + typeof(T).Name, TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.NotPublic);
                    type.AddInterfaceImplementation(typeof(T));

                    var serverConnectionName = type.DefineField("ServerConnectionName", typeof(string), FieldAttributes.Private);
                    var serverConnectionID = type.DefineField("ServerConnectionID", typeof(ShortGuid), FieldAttributes.Private);

                    //Give the type an empty constructor
                    var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new Type[] { typeof(string), typeof(ShortGuid) });
                    var il = ctor.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Stfld, serverConnectionName);
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
                        //il.Emit(OpCodes.Ldc_I4, 5);


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

                            if (args[i].ParameterType.IsByRef)
                            {
                                il.Emit(OpCodes.Ldind_Ref);

                                if (args[i].ParameterType.GetElementType().IsValueType)
                                    il.Emit(OpCodes.Box, args[i].ParameterType.GetElementType());
                            }

                            if (args[i].ParameterType.IsValueType)
                                il.Emit(OpCodes.Box, args[i].ParameterType);
                            
                            il.Emit(OpCodes.Castclass, typeof(object));
                            il.Emit(OpCodes.Stloc, objRef);
                            
                            il.Emit(OpCodes.Ldloc, array);
                            il.Emit(OpCodes.Ldc_I4_S, i);
                            il.Emit(OpCodes.Ldloc, objRef);
                            il.Emit(OpCodes.Stelem_Ref);
                        }

                        //Store connection information for the remote call as arguments                    
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, serverConnectionID);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, serverConnectionName);

                        //Store the method name of the remote call as an arguement
                        il.Emit(OpCodes.Ldstr, method.Name);
                        
                        ////Get a handle on the send method
                        MethodInfo remoteCallMethod = typeof(ProxyClassGenerator).GetMethod("RemoteCallClient", BindingFlags.NonPublic | BindingFlags.Static);

                        ////Load the array pointer as an arguement
                        il.Emit(OpCodes.Ldloc, array);

                        ////Run the send method which will push the return value onto the execution stack
                        il.Emit(OpCodes.Call, remoteCallMethod);

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

                            //Store connection information for the remote call as arguments                    
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverConnectionID);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverConnectionName);

                            //Store the method name of the remote call as an arguement
                            il.Emit(OpCodes.Ldstr, getMethod.Name);

                            ////Get a handle on the send method
                            MethodInfo remoteCallMethod = typeof(ProxyClassGenerator).GetMethod("RemoteCallClient", BindingFlags.NonPublic | BindingFlags.Static);

                            ////Load the array pointer as an arguement
                            il.Emit(OpCodes.Ldloc, array);

                            ////Run the send method which will push the return value onto the execution stack
                            il.Emit(OpCodes.Call, remoteCallMethod);

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

                            //Store connection information for the remote call as arguments                    
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverConnectionID);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, serverConnectionName);

                            //Store the method name of the remote call as an arguement
                            il.Emit(OpCodes.Ldstr, setMethod.Name);

                            ////Get a handle on the send method
                            MethodInfo remoteCallMethod = typeof(ProxyClassGenerator).GetMethod("RemoteCallClient", BindingFlags.NonPublic | BindingFlags.Static);

                            ////Load the array pointer as an arguement
                            il.Emit(OpCodes.Ldloc, array);

                            ////Run the send method which will push the return value onto the execution stack
                            il.Emit(OpCodes.Call, remoteCallMethod);
                            
                            //Return
                            il.Emit(OpCodes.Ret);

                            propImpl.SetSetMethod(setMethod);

                            #endregion Property Set
                        }
                    }

                    foreach (var handler in typeof(T).GetEvents())
                    {
                        throw new InvalidOperationException("Events in the interface are not supported at this time");
                    }

                    Cache<T>.Type = type.CreateType();
                }
            }

            /// <summary>
            /// NEEDS HIDING
            /// </summary>
            /// <param name="connectionID"></param>
            /// <param name="connectionName"></param>
            /// <param name="functionToCall"></param>
            /// <param name="args"></param>
            /// <returns></returns>
            public static object RemoteCallClient(ShortGuid connectionID, string connectionName, string functionToCall, object[] args)
            {
                RemoteCallWrapper wrapper = new RemoteCallWrapper();
                wrapper.args = (from arg in args select RPCArgumentBase.CreateDynamic(arg)).ToList();
                wrapper.name = functionToCall;

                wrapper = NetworkComms.SendReceiveObject<RemoteCallWrapper>(connectionName, connectionID, false, connectionName, 1000, wrapper);

                if (wrapper.Exception != null)
                    throw new Exception(wrapper.Exception);

                for (int i = 0; i < args.Length; i++)
                    args[i] = wrapper.args[i].UntypedValue;

                if (wrapper.result != null)
                    return wrapper.result.UntypedValue;
                else
                    return null;
            }
        }

        /// <summary>
        /// Registers an Object type for remote calling through a supplied interface
        /// </summary>
        /// <typeparam name="T">The object type to use for remote calls. Must implement interface I</typeparam>
        /// <typeparam name="I">The interface that will be remoted</typeparam>
        public static void RegisterTypeForRemoteCall<T, I>(bool enableAutoListen = true) where T : I, new()
        {
            if (!typeof(I).IsInterface)
                throw new InvalidOperationException(typeof(I).Name
                    + " is not an interface");

            NetworkComms.AppendIncomingPacketHandler<string>(typeof(I).ToString() + "-NEW-RPC", (header, connectionId, connectionName) =>
                {
                    string connectionNameFull = connectionName + "-" + connectionId.ToString();
                    T instance = new T();

                    string handlerName = typeof(I).Name + "-" + connectionNameFull + "-RPC";

                    NetworkComms.AppendIncomingPacketHandler<RemoteCallWrapper>(handlerName, (headerInner, connectionIDInner, wrapper) =>
                    {
                        MethodInfo funcRef = typeof(T).GetMethod(wrapper.name);

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

                        NetworkComms.SendObject(handlerName, connectionIDInner, false, wrapper);
                    });

                    NetworkComms.SendObject(typeof(I).ToString() + "-NEW-RPC", connectionId, false, handlerName);
                });
        }
    }
}
