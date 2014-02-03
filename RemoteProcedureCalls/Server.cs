//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;

namespace RemoteProcedureCalls
{

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
        public static void RegisterTypeForPrivateRemoteCall<T, I>(int timeout = int.MaxValue) where T : I, new()
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
        public static void RegisterInstanceForPublicRemoteCall<T, I>(T instance, string instanceName) where T : I
        {
            lock (locker)
            {
                if (!typeof(I).IsInterface)
                    throw new InvalidOperationException(typeof(I).Name
                        + " is not an interface");

                string instanceId = BitConverter.ToString(hash.ComputeHash(BitConverter.GetBytes(((typeof(T).Name + instanceName).GetHashCode() ^ salt))));

                if (!RPCObjects.ContainsKey(instanceId))
                {
                    //Need to add code HERE to deal with events

                    RPCObjects.Add(instanceId, new RPCStorageWrapper(instance, typeof(I), RPCStorageWrapper.RPCObjectType.Public));
                }

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
                    if (addedHandlers[handlerName] is NetworkComms.PacketHandlerCallBackDelegate<string>)
                        NetworkComms.RemoveGlobalIncomingPacketHandler<string>(handlerName, addedHandlers[handlerName] as NetworkComms.PacketHandlerCallBackDelegate<string>);
                    else
                        NetworkComms.RemoveGlobalIncomingPacketHandler<RemoteCallWrapper>(handlerName, addedHandlers[handlerName] as NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper>);

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

                    foreach (var handlerName in toRemove)
                    {
                        if (addedHandlers[handlerName] is NetworkComms.PacketHandlerCallBackDelegate<string>)
                            NetworkComms.RemoveGlobalIncomingPacketHandler<string>(handlerName, addedHandlers[handlerName] as NetworkComms.PacketHandlerCallBackDelegate<string>);
                        else
                            NetworkComms.RemoveGlobalIncomingPacketHandler<RemoteCallWrapper>(handlerName, addedHandlers[handlerName] as NetworkComms.PacketHandlerCallBackDelegate<RemoteCallWrapper>);

                        addedHandlers.Remove(handlerName);
                    }
                }

                foreach (var key in keysToRemove)
                    RPCObjects.Remove(key);
            }
        }

        #region RPC Network comms handlers

        private static void NewInstanceRPCHandler<T, I>(PacketHeader header, Connection connection, string instanceName) where T : I, new()
        {
            lock (locker)
            {
                var instanceId = BitConverter.ToString(hash.ComputeHash(BitConverter.GetBytes(((typeof(T).Name + instanceName + connection.ConnectionInfo.NetworkIdentifier.ToString()).GetHashCode() ^ salt))));

                if (!RPCObjects.ContainsKey(instanceId))
                {
                    //Need to add code HERE to deal with events

                    RPCObjects.Add(instanceId, new RPCStorageWrapper(new T(), typeof(I), RPCStorageWrapper.RPCObjectType.Private, timeoutByInterfaceType[typeof(I)]));
                }

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

                connection.SendObject(typeof(I).ToString() + "-NEW-INSTANCE-RPC-CONNECTION", instanceId);
            }
        }

        private static void RetrieveNamedRPCHandler<T, I>(PacketHeader header, Connection connection, string instanceName) where T : I
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

                connection.SendObject(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-NAME", instanceId);
            }
        }

        private static void RetrieveByIDRPCHandler<T, I>(PacketHeader header, Connection connection, string instanceId) where T : I
        {
            lock (locker)
            {
                if (!RPCObjects.ContainsKey(instanceId) || RPCObjects[instanceId].InterfaceType != typeof(I))
                    instanceId = String.Empty;
                else
                {
                    var nothing = RPCObjects[instanceId].RPCObject;
                }

                connection.SendObject(typeof(I).ToString() + "-NEW-RPC-CONNECTION-BY-ID", instanceId);
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
                    instance = (I)(RPCObjects[wrapper.instanceId].RPCObject);
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
                wrapper.Exception = "SERVER SIDE EXCEPTION\n\n" + e.ToString() + "\n\nEND SERVER SIDE EXCEPTION\n\n";
            }

            connection.SendObject(header.PacketType, wrapper);
        }

        #endregion
    }
}
