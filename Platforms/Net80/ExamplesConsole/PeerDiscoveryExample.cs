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
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet.Connections;

namespace Examples.ExamplesConsole
{
    static class PeerDiscoveryExample
    {
        public static void RunExample()
        {
            PeerDiscovery.ListenMode = PeerDiscovery.LocalListenMode.Both;

            Console.WriteLine("Peer Discovery Example ...\n");

            Console.WriteLine("Please select mode:");
            Console.WriteLine("1 - Server (Discoverable)");
            Console.WriteLine("2 - Client (Discovers servers)");

            //Read in user choice
            bool serverMode;
            if (Console.ReadKey(true).Key == ConsoleKey.D1) serverMode = true;
            else serverMode = false;

            //Both server and client must be discoverable
            PeerDiscovery.EnableDiscoverable(PeerDiscovery.DiscoveryMethod.UDPBroadcast);

            //Write out the network adaptors that are discoverable
            Console.WriteLine("\nPeer Identifier: " + NetworkComms.NetworkIdentifier);
            Console.WriteLine("\nDiscoverable on:");
            foreach (IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.UDP))
                Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

            if (serverMode)
            {
                //The server does nothing else now but wait to be discovered.
                Console.WriteLine("\nPress any key to quit.");
                ConsoleKeyInfo key = Console.ReadKey(true);
            }
            else
            {
                while (true)
                {
                    int selectedOption = 0;
                    Console.WriteLine("\nPlease select the desired option:");
                    Console.WriteLine("1 - Discover servers asynchronously");
                    Console.WriteLine("2 - Discover servers synchronously");
                    Console.WriteLine("3 - Close Client");

                    while (true)
                    {
                        bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedOption);
                        if (parseSucces && selectedOption <= 3 && selectedOption > 0) break;
                        Console.WriteLine("Invalid choice. Please try again.");
                    }

                    //Ensure a previous example loop does not duplicate the asynchronous event delegate
                    PeerDiscovery.OnPeerDiscovered -= PeerDiscovered;

                    if (selectedOption == 1)
                    {
                        #region Discover Asynchronously
                        Console.WriteLine("\nDiscovering servers asynchronously ... ");

                        //Append the OnPeerDiscovered event
                        //The PeerDiscovered delegate will just write to the console.
                        PeerDiscovery.OnPeerDiscovered += PeerDiscovered;

                        //Trigger the asynchronous discovery
                        PeerDiscovery.DiscoverPeersAsync(PeerDiscovery.DiscoveryMethod.UDPBroadcast);
                        #endregion
                    }
                    else if (selectedOption == 2)
                    {
                        #region Discover Synchronously
                        Console.WriteLine("\nDiscovering servers synchronously ... ");

                        //Discover peers synchronously
                        //This method allows peers 2 seconds to respond after the request has been sent
                        Dictionary<ShortGuid, Dictionary<ConnectionType, List<EndPoint>>> discoveredPeerEndPoints = PeerDiscovery.DiscoverPeers(PeerDiscovery.DiscoveryMethod.UDPBroadcast);

                        //Write out a list of discovered peers
                        foreach (ShortGuid networkIdentifier in discoveredPeerEndPoints.Keys)
                            PeerDiscovered(networkIdentifier, discoveredPeerEndPoints[networkIdentifier]);
                        #endregion
                    }
                    else if (selectedOption == 3)
                        break;
                    else
                        throw new Exception("Unable to determine selected option.");
                }
            }

            //We should always call shutdown when our application closes.
            NetworkComms.Shutdown();
        }

        /// <summary>
        /// Static locker used to ensure we only write information to the console in a clear fashion
        /// </summary>
        static object locker = new object();

        /// <summary>
        /// Execute this method when a peer is discovered 
        /// </summary>
        /// <param name="peerIdentifier">The network identifier of the discovered peer</param>
        /// <param name="discoveredPeerEndPoints">The discoverable endpoints found for the provided peer</param>
        private static void PeerDiscovered(ShortGuid peerIdentifier, Dictionary<ConnectionType, List<EndPoint>> discoveredPeerEndPoints)
        {
            //Lock to ensure we do not write to the console in parallel.
            lock (locker)
            {
                Console.WriteLine("\nEndpoints discovered for peer with networkIdentifier {0} ...", peerIdentifier);
                foreach (ConnectionType connectionType in discoveredPeerEndPoints.Keys)
                {
                    Console.WriteLine("  ... endPoints of type {0}:", connectionType);
                    foreach (EndPoint endPoint in discoveredPeerEndPoints[connectionType])
                        Console.WriteLine("    -> {0}", endPoint.ToString());
                }
            }                        
        }
    }
}
