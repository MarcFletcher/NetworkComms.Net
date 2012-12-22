//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DistributedFileSystem;
using NetworkCommsDotNet;
using System.Threading;
using DPSBase;
using System.Net;
using System.IO;

namespace ExamplesConsole
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
            //Select launch mode
            Console.WriteLine("Launching DFS system ...\n");
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

                Console.WriteLine("\n ... succesfully created a {0}MB test packet.", ((double)someRandomData.Length / (1024.0 * 1024.0)).ToString("0.###"));

                object listLocker = new object();
                List<IPEndPoint> connectedClients = new List<IPEndPoint>();

                //Create the item to be distributed
                ConnectionInfo seedConnectionInfo = new ConnectionInfo(ConnectionType.TCP, NetworkComms.NetworkIdentifier, new IPEndPoint(NetworkComms.AllAllowedIPs()[0], NetworkComms.DefaultListenPort), true);
                seedConnectionInfo.ResetNetworkIdentifer(NetworkComms.NetworkIdentifier);

                DistributedItem newItem = new DistributedItem("exampleItem", "example1", new MemoryStream(someRandomData), seedConnectionInfo, ItemBuildTarget.Disk);

                NetworkComms.ConnectionEstablishShutdownDelegate clientEstablishDelegate = (connection) =>
                {
                    lock (listLocker)
                        connectedClients.Remove(connection.ConnectionInfo.RemoteEndPoint);

                    Console.WriteLine("Client " + connection.ConnectionInfo + " connected.");
                };

                NetworkComms.ConnectionEstablishShutdownDelegate clientShutdownDelegate = (connection) =>
                {
                    lock (listLocker)
                        connectedClients.Remove(connection.ConnectionInfo.RemoteEndPoint);

                    Console.WriteLine("Client " + connection.ConnectionInfo + " disconnected.");
                };

                NetworkComms.PacketHandlerCallBackDelegate<int> ReplyDelegate = (packetHeader, connection, incomingObject) =>
                {
                    //Push the item into the swarm
                    lock (listLocker)
                        if (!connectedClients.Contains(connection.ConnectionInfo.RemoteEndPoint))
                            connectedClients.Add(connection.ConnectionInfo.RemoteEndPoint);

                    DFS.PushItemToPeer(connection, newItem, "BigDataRequestResponse");
                    Console.WriteLine("Pushing item to " + connection.ConnectionInfo + " (" + connection.ConnectionInfo.NetworkIdentifier + "). {0} in swarm. P#={1}, S#={2}.", connectedClients.Count, newItem.PushCount, newItem.TotalChunkSupplyCount);
                };

                NetworkComms.PacketHandlerCallBackDelegate<string> InfoDelegate = (packetHeader, connectionId, incomingString) =>
                {
                    Console.WriteLine(" ... " + connectionId + " - " + incomingString);
                };

                DFS.InitialiseDFS(NetworkComms.DefaultListenPort);
                Console.WriteLine(" ... DFS has been initialised.");

                NetworkComms.AppendGlobalConnectionEstablishHandler(clientEstablishDelegate);
                NetworkComms.AppendGlobalConnectionCloseHandler(clientShutdownDelegate);
                NetworkComms.AppendGlobalIncomingPacketHandler("BigDataRequest", ReplyDelegate);
                NetworkComms.AppendGlobalIncomingPacketHandler("ClientInfo", InfoDelegate);

                Console.WriteLine("\nListening for incoming connections on:");
                foreach (IPEndPoint localEndPoint in TCPConnection.ExistingLocalListenEndPoints())
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

                ConnectionInfo serverConnectionInfo;
                ExampleHelper.GetServerDetails(out serverConnectionInfo);

                DFS.InitialiseDFS(NetworkComms.DefaultListenPort);
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
                        Console.WriteLine(" ... full item build " + buildCount + " took {0} secs ({1} MB/s) using {2} total peers. {3} builds completed.", (DateTime.Now - startTime).TotalSeconds.ToString("0.00"), (((double)dataBytes.Length / 1048576.0) / (DateTime.Now - startTime).TotalSeconds).ToString("0.0"), item.SwarmChunkAvailability.NumPeersInSwarm(), buildCount);

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
                foreach (IPEndPoint localEndPoint in TCPConnection.ExistingLocalListenEndPoints())
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
                                DFS.RemoveItem(item.ItemCheckSum);
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
                        DFS.ShutdownDFS();
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

            DFS.ShutdownDFS();
            NetworkComms.Shutdown();
        }
    }
}
