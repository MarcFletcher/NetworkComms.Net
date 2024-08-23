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
using DistributedFileSystem;
using System.Threading;
using System.Net;
using System.IO;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;

namespace Examples.ExamplesConsole
{
    static class DFSTest
    {
        static bool hostMode;

        /// <summary>
        /// The distributed file system (DFS) allows for the high performance distribution of large files
        /// within a cluster of peers. This sytem replicates the behaviour the bitTorrent protocol by using 
        /// NetworkCommsDotNet. This example demonstrates the DFS in action.
        /// </summary>
        public static void RunExample()
        {
            //Select mode
            Console.WriteLine("Distributed File System (DFS) Example ...\n");

            Console.WriteLine("Please select host or peer mode:");
            Console.WriteLine("1 - Host Mode (Original source of data)");
            Console.WriteLine("2 - Peer Mode (Builds data and then acts as subhost)");

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) hostMode = true;
            else hostMode = false;

            if (hostMode)
            {
                //Prepare DFS in host mode
                #region ServerMode
                Console.WriteLine("\n ... host mode selected.");
                Console.WriteLine("\nPlease enter how large the test data packet should be in MB and press return, e.g. 50:");
                int numberMegsToCreate = int.Parse(Console.ReadLine());

                //Fill a byte[] with random data
                DateTime startTime = DateTime.Now;
                Random randGen = new Random();
                byte[] someRandomData = new byte[numberMegsToCreate * 1024 * 1024];
                randGen.NextBytes(someRandomData);

                Console.WriteLine("\n ... successfully created a {0}MB test packet.", ((double)someRandomData.Length / (1024.0 * 1024.0)).ToString("0.###"));

                object listLocker = new object();
                List<IPEndPoint> connectedClients = new List<IPEndPoint>();

                //Initialise the DFS before creating the test object to ensure the correct port and IP are used as the seed
                DFS.Initialise(10000);

                //Create the item to be distributed
                List<ConnectionInfo> seedConnectionInfoList = (from current in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP) select new ConnectionInfo(ConnectionType.TCP, NetworkComms.NetworkIdentifier, current, true)).ToList();

                MemoryStream itemData = new MemoryStream(someRandomData, 0, someRandomData.Length, false, true);
                DistributedItem newItem = new DistributedItem("exampleItem", "exampleItem", itemData, seedConnectionInfoList, DataBuildMode.Disk_Blocks);

                NetworkComms.ConnectionEstablishShutdownDelegate clientEstablishDelegate = (connection) =>
                {
                    lock (listLocker)
                        connectedClients.Remove((IPEndPoint)connection.ConnectionInfo.RemoteEndPoint);

                    Console.WriteLine("Client " + connection.ConnectionInfo + " connected.");
                };

                NetworkComms.ConnectionEstablishShutdownDelegate clientShutdownDelegate = (connection) =>
                {
                    lock (listLocker)
                        connectedClients.Remove((IPEndPoint)connection.ConnectionInfo.RemoteEndPoint);

                    Console.WriteLine("Client " + connection.ConnectionInfo + " disconnected.");
                };

                NetworkComms.PacketHandlerCallBackDelegate<int> ReplyDelegate = (packetHeader, connection, incomingObject) =>
                {
                    //Push the item into the swarm
                    lock (listLocker)
                        if (!connectedClients.Contains(connection.ConnectionInfo.RemoteEndPoint))
                            connectedClients.Add((IPEndPoint)connection.ConnectionInfo.RemoteEndPoint);

                    DFS.PushItemToPeer(connection, newItem, "BigDataRequestResponse");
                    Console.WriteLine("Pushing item to " + connection.ConnectionInfo + " (" + connection.ConnectionInfo.NetworkIdentifier + "). {0} in swarm. P#={1}, S#={2}.", connectedClients.Count, newItem.PushCount, newItem.TotalChunkSupplyCount);
                };

                NetworkComms.PacketHandlerCallBackDelegate<string> InfoDelegate = (packetHeader, connectionId, incomingString) =>
                {
                    Console.WriteLine(" ... " + connectionId + " - " + incomingString);
                };

                Console.WriteLine(" ... DFS has been initialised.");

                NetworkComms.AppendGlobalConnectionEstablishHandler(clientEstablishDelegate);
                NetworkComms.AppendGlobalConnectionCloseHandler(clientShutdownDelegate);
                NetworkComms.AppendGlobalIncomingPacketHandler("BigDataRequest", ReplyDelegate);
                NetworkComms.AppendGlobalIncomingPacketHandler("ClientInfo", InfoDelegate);

                Console.WriteLine("\nListening for incoming connections on:");
                foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP))
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                Console.WriteLine("\nIdentifier - {0}", NetworkComms.NetworkIdentifier);
                Console.WriteLine("\nPress 's' to write out stats, 'q' to close any connected peers, 'ctrl+q' to close this host.\n");

                while (true)
                {
                    ConsoleKeyInfo pressedKey = Console.ReadKey(true);
                    #region Host Shutdown
                    if (pressedKey.Modifiers != ConsoleModifiers.Control && pressedKey.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine("Sending shutdown to clients...");
                        lock (listLocker)
                        {
                            for (int i = 0; i < connectedClients.Count; i++)
                            {
                                try
                                {
                                    TCPConnection.GetConnection(new ConnectionInfo(connectedClients[i])).SendObject("ClientCommand", 0);
                                }
                                catch (Exception)
                                {
                                    Console.WriteLine("Exception telling client to shutdown. Probably already disconnected.");
                                }
                            }
                        }
                    }
                    else if (pressedKey.Modifiers == ConsoleModifiers.Control && pressedKey.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine("Sending shutdown to clients and closing local host...");
                        lock (listLocker)
                        {
                            for (int i = 0; i < connectedClients.Count; i++)
                            {
                                try
                                {
                                    TCPConnection.GetConnection(new ConnectionInfo(connectedClients[i])).SendObject("ClientCommand", 0);
                                }
                                catch (Exception)
                                {
                                    Console.WriteLine("Exception telling client to shutdown. Probably already disconnected.");
                                }
                            }
                        }

                        Console.WriteLine("Closing host.");
                        break;
                    }
                    else if (pressedKey.Key == ConsoleKey.S)
                    {
                        #region Stats
                        Console.WriteLine("\nCurrent Stats:");
                        Console.WriteLine("{0} comms connections.",NetworkComms.TotalNumConnections());

                        if (NetworkComms.TotalNumConnections() > 0)
                        {
                            Console.WriteLine("Connections with: ");
                            var connections = NetworkComms.GetExistingConnection();
                            foreach (var connection in connections)
                                Console.WriteLine("\t{0}", connection.ConnectionInfo);
                        }
                        #endregion
                    }
                    #endregion
                }
                #endregion
            }
            else if (!hostMode)
            {
                //Prepare DFS in peer mode
                #region PeerMode
                Console.WriteLine("\n ... peer mode selected.");

                ConnectionInfo serverConnectionInfo = ExampleHelper.GetServerDetails();

                DFS.Initialise(10000);
                Console.WriteLine(" ... DFS has been initialised.");

                bool shutDown = false;
                bool buildComplete = true;
                DateTime startTime = DateTime.Now;

                int buildCount = 0;

                NetworkComms.PacketHandlerCallBackDelegate<byte[]> ReplyDelegate = (packetHeader, connection, dataBytes) =>
                {
                    try
                    {
                        buildCount++;
                        DistributedItem item = DFS.MostRecentlyCompletedItem();
                        Console.WriteLine(" ... full item build " + buildCount + " took {0} secs ({1} MB/s), using {2} total peers and {4} build mode. {3} builds completed.", 
                            (DateTime.Now - startTime).TotalSeconds.ToString("0.00"), 
                            (((double)dataBytes.Length / 1048576.0) / (DateTime.Now - startTime).TotalSeconds).ToString("0.0"), 
                            item.SwarmChunkAvailability.NumPeersInSwarm(), 
                            buildCount, 
                            item.Data.DataBuildMode);

                        double speed = (((double)dataBytes.Length / 1048576.0) / (DateTime.Now - startTime).TotalSeconds);
                        connection.SendObject("ClientInfo", " ... build " + buildCount + " took " + (DateTime.Now - startTime).TotalSeconds.ToString("0.00") + " secs (" + speed.ToString("0.0") + " MB/s) using " + item.SwarmChunkAvailability.NumPeersInSwarm() + " peers. " + buildCount + " builds completed.");
                        buildComplete = true;
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Shutting down due to exception.");
                        shutDown = true;
                    }
                };

                NetworkComms.PacketHandlerCallBackDelegate<int> ShutdownDelegate = (packetHeader, connectionId, packetDataBytes) =>
                {
                    shutDown = true;
                };

                NetworkComms.AppendGlobalIncomingPacketHandler("BigDataRequestResponse", ReplyDelegate);
                NetworkComms.AppendGlobalIncomingPacketHandler("ClientCommand", ShutdownDelegate);

                Console.WriteLine("\nIdentifier - {0}", NetworkComms.NetworkIdentifier);
                Console.WriteLine("\nListening for incoming objects on:");
                foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.TCP))
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                startTime = DateTime.Now;

                while (true)
                {
                    if (!shutDown && buildComplete)
                    {
                        Console.WriteLine("\nPress 'r' to rebuild or any other key to shutdown.");
                        var shutdownKey = Console.ReadKey(true).Key;
                        if (shutdownKey != ConsoleKey.R) shutDown = true;

                        if (!shutDown)
                        {
                            DistributedItem item = DFS.MostRecentlyCompletedItem();
                            if (item != null)
                            {
                                DFS.RemoveItem(item.Data.CompleteDataCheckSum);
                                Console.WriteLine("\n ... item removed from local and rebuilding at {0}.", DateTime.Now.ToString("HH:mm:ss.fff"));
                                startTime = DateTime.Now;
                            }

                            buildComplete = false;

                            TCPConnection.GetConnection(serverConnectionInfo).SendObject("BigDataRequest");

                            Console.WriteLine(" ... initiating item build ...");
                        }
                    }
                    else if (shutDown)
                    {
                        shutDown = true;
                        DFS.Shutdown();
                        break;
                    }

                    Thread.Sleep(250);
                }

                try
                {
                    TCPConnection.GetConnection(serverConnectionInfo).SendObject("ClientInfo", "... shutting down, initiating DFS shutdown.");
                }
                catch (CommsException)
                {
                    Console.WriteLine("... unable to inform local of shutdown. Connection probably already closed.");
                }

                Console.WriteLine("Done. Completed {0} builds.", buildCount);
                #endregion
            }

            DFS.Shutdown();
            NetworkComms.Shutdown();
        }
    }
}
