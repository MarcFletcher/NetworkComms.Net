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
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NetworkCommsDotNet;
using DPSBase;
using NetworkCommsDotNet.PeerDiscovery;

namespace Examples.ExamplesConsole
{
    static class PeerDiscoveryExample
    {
        public static void RunExample()
        {
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
                    Console.WriteLine("3 - Close Client\n");

                    while (true)
                    {
                        bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedOption);
                        if (parseSucces && selectedOption <= 3 && selectedOption > 0) break;
                        Console.WriteLine("Invalid choice. Please try again.");
                    }

                    //Ensure a previous loop does not duplicate the asynchronous event delegate
                    PeerDiscovery.OnPeerDiscovered -= PeerDiscovered;

                    if (selectedOption == 1)
                    {
                        #region Discover Asynchronously
                        Console.WriteLine("\nDiscovering servers asynchronously ... ");

                        //Append the OnPeerDiscovered event (We only want to do this once so we set addedEvent boolean)
                        //If a peer responds to a discovery request we will just write to the console.
                        PeerDiscovery.OnPeerDiscovered += PeerDiscovered;

                        //Trigger the asynchronous discovery
                        PeerDiscovery.DiscoverPeersAsync(PeerDiscovery.DiscoveryMethod.UDPBroadcast);
                        #endregion
                    }
                    else if (selectedOption == 2)
                    {
                        #region Discover Synchronously
                        Console.WriteLine("\nDiscovering servers synchronously ... ");

                        //Discover peers asynchronously
                        //This method allows peers 2 seconds to respond after the request has been sent
                        List<EndPoint> discoveredPeerEndPoints = PeerDiscovery.DiscoverPeers(PeerDiscovery.DiscoveryMethod.UDPBroadcast);

                        //Write out a list of discovered peers
                        foreach (IPEndPoint ipEndPoint in discoveredPeerEndPoints)
                            Console.WriteLine("  ... Discovered server at {0}", ipEndPoint.ToString());
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
        /// Execute this method when a peer is discovered asynchronously 
        /// </summary>
        /// <param name="peerEndPoint"></param>
        /// <param name="connectionType"></param>
        private static void PeerDiscovered(EndPoint peerEndPoint, ConnectionType connectionType)
        {
            Console.WriteLine("\n  ... Discovered server at {0}", ((IPEndPoint)peerEndPoint).ToString());
        }
    }
}
