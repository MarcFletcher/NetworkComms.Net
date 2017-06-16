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
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Tools;

namespace RemoteProcedureCalls
{

    /// <summary>
    /// Contains methods for managing objects server side which allow Remote Procedure Calls
    /// </summary>
    public static class Server
    {
        private class RPCClientSubscription
        {
            public Connection Connection { get; private set; }
            public Dictionary<EventInfo, Delegate> SubscribedEvents { get; private set; }
            public NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper> CallFunctionDelegate { get; private set; }
            public NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper> RemoveDelegate { get; private set; }

            public RPCClientSubscription(Connection connection, 
                NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper> callFunctionDelegate,
                NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper> removeDelegate)
            {
                this.Connection = connection;
                this.SubscribedEvents = new Dictionary<EventInfo, Delegate>();
                this.CallFunctionDelegate = callFunctionDelegate;
                this.RemoveDelegate = removeDelegate;
            }
        }

        private class RPCRemoteObject
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

            public string InstanceId {get; private set;}
            public Type InterfaceType { get; private set; }
            public DateTime LastAccess { get; private set; }
            public int TimeOut { get; private set; }
            public RPCObjectType Type { get; private set; }
            public Dictionary<ShortGuid, RPCClientSubscription> SubscribedClients { get; private set; }
        
            public RPCRemoteObject(object RPCObject, Type interfaceType, RPCObjectType Type, string instanceId, int timeout = int.MaxValue)
            {
                this.TimeOut = timeout;
                this.obj = RPCObject;
                this.LastAccess = DateTime.Now;
                InterfaceType = interfaceType;
                this.Type = Type;
                this.InstanceId = instanceId;
                SubscribedClients = new Dictionary<ShortGuid,RPCClientSubscription>();
            }

            public void AddClientSubscription<T, I>(Connection connection) where T : I
            {
                if (SubscribedClients.ContainsKey(connection.ConnectionInfo.NetworkIdentifier))
                    return;

                var events = InterfaceType.GetEvents();
                var addedHandlers = new Dictionary<EventInfo, Delegate>();

                foreach (var ev in events)
                {
                    var addMethod = ev.GetAddMethod();

                    var evGenerator = typeof(RemoteProcedureCalls.Server.RPCRemoteObject).GetMethod("GenerateEvent", BindingFlags.NonPublic | BindingFlags.Static);
                    evGenerator = evGenerator.MakeGenericMethod(ev.EventHandlerType.GetGenericArguments());

                    var handler = evGenerator.Invoke(null, new object[] { connection, InstanceId, InterfaceType, ev.Name });

                    addMethod.Invoke(obj, new object[] { handler });
                    addedHandlers.Add(ev, handler as Delegate);
                }

                var callFunctionDelegate = new NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper>(RunRPCFunctionHandler<T, I>);
                var removeDelegate = new NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper>(RPCDisconnectHandler<T, I>);

                var subscripion = new RPCClientSubscription(connection, callFunctionDelegate, removeDelegate);
                foreach(var handlerPair in addedHandlers)
                    subscripion.SubscribedEvents.Add(handlerPair.Key, handlerPair.Value);

                SubscribedClients.Add(connection.ConnectionInfo.NetworkIdentifier, subscripion);

                connection.AppendIncomingPacketHandler<RemoteCallWrapper>(InterfaceType.Name + "-RPC-CALL-" + InstanceId, callFunctionDelegate);
                connection.AppendIncomingPacketHandler<RemoteCallWrapper>(InterfaceType.Name + "-REMOVE-REFERENCE-" + InstanceId, removeDelegate);

                LastAccess = DateTime.Now;

                //If the connection is closed make sure we remove all event handlers associated with that connection so that we don't get exceptions on the event fire
                //Note we don't want to remove the client object itself
                connection.AppendShutdownHandler((clientConnection) =>
                {                    
                    RemoveClientSubscription(clientConnection);
                });
            }

            public void RemoveClientSubscription(Connection connection)
            {
                if (SubscribedClients.ContainsKey(connection.ConnectionInfo.NetworkIdentifier))
                {
                    var client = SubscribedClients[connection.ConnectionInfo.NetworkIdentifier];

                    foreach (var evPair in client.SubscribedEvents)
                    {
                        var removeMethod = evPair.Key.GetRemoveMethod();
                        removeMethod.Invoke(obj, new object[] { evPair.Value });
                    }

                    try
                    {
                        client.Connection.SendObject<string>(InterfaceType.Name + "-RPC-DISPOSE-" + InstanceId, "");
                    }
                    catch (Exception) { }
                    finally
                    {
                        LastAccess = DateTime.Now;
                        SubscribedClients.Remove(connection.ConnectionInfo.NetworkIdentifier);
                        connection.RemoveIncomingPacketHandler(InterfaceType.Name + "-RPC-CALL-" + InstanceId, client.CallFunctionDelegate);
                        connection.RemoveIncomingPacketHandler(InterfaceType.Name + "-REMOVE-REFERENCE-" + InstanceId, client.RemoveDelegate);
                    }
                }
            }

            public void RemoveAllClientSubscriptions()
            {
                var toRemove = SubscribedClients.Values.Select(client => client.Connection).ToArray();
                
                foreach (var clientConnection in toRemove)
                    RemoveClientSubscription(clientConnection);
            }

            private static EventHandler<A> GenerateEvent<A>(Connection clientConnection, string instanceId, Type interfaceType, string eventName) where A : EventArgs
            {
                return new EventHandler<A>((sender, args) =>
                {
                    var packetType = interfaceType.Name + "-RPC-LISTENER-" + instanceId;
                    RemoteCallWrapper callWrapper = new RemoteCallWrapper();
                    callWrapper.name = eventName;
                    callWrapper.instanceId = instanceId;
                    callWrapper.args = new List<RPCArgumentBase>() { RPCArgumentBase.CreateDynamic(sender), RPCArgumentBase.CreateDynamic(args) };

                    clientConnection.SendObject(packetType, callWrapper);
                });
            }
        }
                
        private static object locker = new object();

        static readonly byte[] salt;
        static readonly System.Security.Cryptography.HashAlgorithm hash;

        private static Dictionary<string, RPCRemoteObject> RPCObjectsById = new Dictionary<string, RPCRemoteObject>();
        private static Dictionary<Type, int> timeoutByInterfaceType = new Dictionary<Type, int>();
        
        private static Dictionary<Type, Delegate> newConnectionByNewInstanceHandlers = new Dictionary<Type, Delegate>();
        private static Dictionary<Type, Delegate> newConnectionByNameHandlers = new Dictionary<Type, Delegate>();
        private static Dictionary<Type, Delegate> newConnectionByIdHandlers = new Dictionary<Type, Delegate>();
        
        static Server()
        {
            var r = System.Security.Cryptography.RandomNumberGenerator.Create();
            salt = new byte[32];
            r.GetBytes(salt);

            hash = System.Security.Cryptography.HMACSHA256.Create();

            AutoResetEvent watcherWaitEvent = new AutoResetEvent(false);
            Task watcher = Task.Factory.StartNew(() =>
            {
                do
                {
                    lock (locker)
                    {

                        List<string> keysToRemove = new List<string>();

                        foreach (var obj in RPCObjectsById)
                        {
                            if (obj.Value.Type == RPCRemoteObject.RPCObjectType.Private && 
                                obj.Value.SubscribedClients.Count == 0 && 
                                (DateTime.Now - obj.Value.LastAccess).TotalMilliseconds > obj.Value.TimeOut)
                                keysToRemove.Add(obj.Key);
                        }

                        if (keysToRemove.Count != 0)
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
        /// Helper method for calculating instance ids
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static string GetInstanceId(string input)
        {
            return System.Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes((input)).Union(salt).ToArray()));
        }

        #region Register Methods

        /// <summary>
        /// Registers a type for private RPC whereby each client generates it's own private instances on the server
        /// </summary>
        /// <typeparam name="T">The type of object to create new instances of for RPC.  Must implement I</typeparam>
        /// <typeparam name="I">Interface that should be provided for RPC</typeparam>
        /// <param name="timeout">If specified each RPC object created will be destroyed if it is unused for a time, in ms, specified by timeout</param>            
        public static void RegisterTypeForPrivateRemoteCall<T, I>(int timeout = int.MaxValue) where T : I, new()
        {
            lock (locker)
            {
                if (!typeof(I).IsInterface)
                    throw new InvalidOperationException(typeof(I).Name
                        + " is not an interface");

                if (!newConnectionByNewInstanceHandlers.ContainsKey(typeof(I)))
                {
                    var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(NewInstanceRPCHandler<T, I>);

                    timeoutByInterfaceType.Add(typeof(I), timeout);

                    NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).Name + "-NEW-INSTANCE-RPC-CONNECTION", del);
                    newConnectionByNewInstanceHandlers.Add(typeof(I), del);
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
        public static void RegisterInstanceForPublicRemoteCall<T, I>(T instance, string instanceName) where T : I
        {
            lock (locker)
            {
                if (!typeof(I).IsInterface)
                    throw new InvalidOperationException(typeof(I).Name
                        + " is not an interface");

                string instanceId = GetInstanceId(typeof(T).Name + instanceName);
                
                if (!RPCObjectsById.ContainsKey(instanceId))
                {
                    RPCObjectsById.Add(instanceId, new RPCRemoteObject(instance, typeof(I), RPCRemoteObject.RPCObjectType.Public, instanceId));
                }

                if (!newConnectionByNameHandlers.ContainsKey(typeof(I)))
                {
                    var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(RetrieveNamedRPCHandler<T, I>);

                    NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).Name + "-NEW-RPC-CONNECTION-BY-NAME", del);
                    newConnectionByNameHandlers.Add(typeof(I), del);                    
                }

                if (!newConnectionByIdHandlers.ContainsKey(typeof(I)))
                {
                    var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(RetrieveByIDRPCHandler<T, I>);

                    NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).Name + "-NEW-RPC-CONNECTION-BY-ID", del);
                    newConnectionByIdHandlers.Add(typeof(I), del);
                }
            }
        }

        #endregion

        #region Remove methods

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

                var keys = (from obj in RPCObjectsById
                            where obj.Value.InterfaceType == typeof(I) && obj.Value.Type == RPCRemoteObject.RPCObjectType.Private
                            select obj.Key).ToList();

                RemoveRPCObjects(keys);

                newConnectionByNewInstanceHandlers.Remove(typeof(I));
            }
        }

        /// <summary>
        /// Disables RPC calls for the supplied named public object supplied
        /// </summary>
        /// <param name="instance">Instance to disable RPC for</param>
        public static void RemovePublicRPCObject(object instance)
        {
            lock (locker)
            {
                var objects = (from obj in RPCObjectsById
                               where obj.Value.RPCObject == instance && obj.Value.Type == RPCRemoteObject.RPCObjectType.Public
                               select obj).ToList();

                var keys = objects.Select(obj => obj.Key).ToList();

                
                RemoveRPCObjects(keys);

                var distinctTypes = (from obj in objects
                                     select obj.Value.InterfaceType).Distinct().ToList();
                
                var typesToRemove = distinctTypes.Except(RPCObjectsById.Where(rpcObj => rpcObj.Value.Type == RPCRemoteObject.RPCObjectType.Public).Select(rpcObj => rpcObj.Value.InterfaceType)).ToList();

                foreach (var type in typesToRemove)
                    newConnectionByNameHandlers.Remove(type);

                typesToRemove = distinctTypes.Except(RPCObjectsById.Select(rpcObj => rpcObj.Value.InterfaceType).Distinct()).ToList();

                foreach (var type in typesToRemove)
                    newConnectionByIdHandlers.Remove(type);
            }
        }

        /// <summary>
        /// Removes all public and private RPC objects and removes all related packet handlers from NetworkComms
        /// </summary>
        public static void ShutdownAllRPC()
        {
            lock (locker)
            {
                //We first remove all objects and the comms handlers specifically associated with them. This will include all connection specific handlers
                RemoveRPCObjects(RPCObjectsById.Keys.ToList());

                //
                foreach (var handlerPair in newConnectionByNewInstanceHandlers)
                {
                    string name = handlerPair.Key.Name + "-NEW-INSTANCE-RPC-CONNECTION";
                    NetworkComms.RemoveGlobalIncomingPacketHandler(name, handlerPair.Value as NetworkComms.PacketHandlerCallBackDelegate<string>);
                }

                foreach (var handlerPair in newConnectionByNameHandlers)
                {
                    string name = handlerPair.Key.Name + "-NEW-RPC-CONNECTION-BY-NAME";
                    NetworkComms.RemoveGlobalIncomingPacketHandler(name, handlerPair.Value as NetworkComms.PacketHandlerCallBackDelegate<string>);
                }

                foreach (var handlerPair in newConnectionByIdHandlers)
                {
                    string name = handlerPair.Key.Name + "-NEW-RPC-CONNECTION-BY-ID";
                    NetworkComms.RemoveGlobalIncomingPacketHandler(name, handlerPair.Value as NetworkComms.PacketHandlerCallBackDelegate<string>);
                }

                newConnectionByNewInstanceHandlers = new Dictionary<Type, Delegate>();
                newConnectionByNameHandlers = new Dictionary<Type, Delegate>();
                newConnectionByIdHandlers = new Dictionary<Type, Delegate>();
            }
        }

        private static void RemoveRPCObjects(List<string> keysToRemove)
        {
            lock (locker)
            {
                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    var toRemove = RPCObjectsById[keysToRemove[i]];
                    toRemove.RemoveAllClientSubscriptions();
                    RPCObjectsById.Remove(keysToRemove[i]);
                }
            }
        }

        #endregion

        #region RPC Network comms handlers

        private static void NewInstanceRPCHandler<T, I>(PacketHeader header, Connection connection, string instanceName) where T : I, new()
        {
            lock (locker)
            {                   
                string instanceId = GetInstanceId(typeof(T).Name + instanceName + connection.ConnectionInfo.NetworkIdentifier.ToString());

                RPCRemoteObject wrapper = null;
                if (!RPCObjectsById.TryGetValue(instanceId, out wrapper))
                {
                    var instance = new T();
                    wrapper = new RPCRemoteObject(instance, typeof(I), RPCRemoteObject.RPCObjectType.Private, instanceId, timeoutByInterfaceType[typeof(I)]);
                    RPCObjectsById.Add(instanceId, wrapper);
                }

                wrapper.AddClientSubscription<T, I>(connection);

                if (!newConnectionByIdHandlers.ContainsKey(typeof(I)))
                {
                    var del = new NetworkComms.PacketHandlerCallBackDelegate<string>(RetrieveByIDRPCHandler<T, I>);

                    NetworkComms.AppendGlobalIncomingPacketHandler<string>(typeof(I).Name + "-NEW-RPC-CONNECTION-BY-ID", del);
                    newConnectionByIdHandlers.Add(typeof(I), del);
                }
                
                string returnPacketType = header.GetOption(PacketHeaderStringItems.RequestedReturnPacketType);
                connection.SendObject(returnPacketType, instanceId);
            }
        }

        private static void RetrieveNamedRPCHandler<T, I>(PacketHeader header, Connection connection, string instanceName) where T : I
        {
            lock (locker)
            {
                string instanceId = GetInstanceId(typeof(T).Name + instanceName);

                if (!RPCObjectsById.ContainsKey(instanceId))
                    instanceId = String.Empty;
                else
                {
                    var instanceWrapper = RPCObjectsById[instanceId];
                    instanceWrapper.AddClientSubscription<T, I>(connection);
                }

                string returnPacketType = header.GetOption(PacketHeaderStringItems.RequestedReturnPacketType);
                connection.SendObject(returnPacketType, instanceId);
            }
        }

        private static void RetrieveByIDRPCHandler<T, I>(PacketHeader header, Connection connection, string instanceId) where T : I
        {
            lock (locker)
            {
                if (!RPCObjectsById.ContainsKey(instanceId) || RPCObjectsById[instanceId].InterfaceType != typeof(I))
                    instanceId = String.Empty;
                else
                {
                    var instanceWrapper = RPCObjectsById[instanceId];
                    instanceWrapper.AddClientSubscription<T, I>(connection);
                }

                string returnPacketType = header.GetOption(PacketHeaderStringItems.RequestedReturnPacketType);
                connection.SendObject(returnPacketType, instanceId);
            }
        }

        private static void RunRPCFunctionHandler<T, I>(PacketHeader header, Connection connection, RemoteCallWrapper wrapper) where T : I
        {
            I instance = default(I);
            MethodInfo funcRef = typeof(I).GetMethod(wrapper.name);

            try
            {
                lock (locker)
                {
                    instance = (I)(RPCObjectsById[wrapper.instanceId].RPCObject);
                }
            }
            catch (Exception)
            {
                wrapper.result = null;
                wrapper.Exception = "SERVER SIDE EXCEPTION\n\n" + "Invalid instanceID" + "\n\nEND SERVER SIDE EXCEPTION\n\n";
                connection.SendObject(header.PacketType, wrapper);
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

                if (e.InnerException != null)
                    e = e.InnerException;

                wrapper.Exception = "SERVER SIDE EXCEPTION\n\n" + e.ToString() + "\n\nEND SERVER SIDE EXCEPTION\n\n";
            }

            string returnPacketType = header.GetOption(PacketHeaderStringItems.RequestedReturnPacketType);
            connection.SendObject(returnPacketType, wrapper);
        }

        private static void RPCDisconnectHandler<T, I>(PacketHeader header, Connection connection, RemoteCallWrapper wrapper) where T : I
        {
            lock (locker)
            {
                if (!RPCObjectsById.ContainsKey(wrapper.instanceId))
                    return;

                var rpcObject = RPCObjectsById[wrapper.instanceId];
                rpcObject.RemoveClientSubscription(connection);                
            }
        }

        #endregion
    }
}
