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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DPSBase;
using NetworkCommsDotNet;

namespace DebugTests
{
    [Flags]
    enum TestMode
    {
        TCP_Managed = 1,
        TCP_Unmanaged = 2,
        UDP_Managed = 4,
        UDP_Unmanaged = 8,
        TCPSSL_Managed = 16,
    }

    static class NumConnectionLoadTest
    {
        static bool serverMode;

        static Dictionary<IPEndPoint, ApplicationLayerProtocolStatus> TCPServerEndPoints;
        static Dictionary<IPEndPoint, ApplicationLayerProtocolStatus> UDPServerEndPoints;
        static List<IPEndPoint> TCPServerEndPointsKeys;
        static List<IPEndPoint> UDPServerEndPointsKeys;

        static int connectionHammerExecCount = 50;
        static int connectionsPerHammer = 100;
        static byte[] clientHammerData;

        static int testDataSize = 1024;
        static bool closeConnectionAfterSend = true;

        //No connection close
        //Debug mode - MF laptop - Single run 50K connections
        //static TestMode mode = TestMode.TCP_Managed; // (0.18ms / connection) (Using RijndaelPSKEncrypter data processor 7.46ms / connection)
        //static TestMode mode = TestMode.TCP_Unmanaged; // (0.05ms / connection)
        //static TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged; // (0.11ms / connection)
        //static TestMode mode = TestMode.UDP_Managed; // (0.03ms / connection) (wHandshake - 0.07ms / connection) (slow receive & missing packets)
        //static TestMode mode = TestMode.UDP_Unmanaged; // (0.03ms / connection) (slow receive & missing packets)
        //static TestMode mode = TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (0.03ms / connection) (missing packets)
        //static TestMode mode = TestMode.TCP_Managed ^ TestMode.UDP_Managed; // (0.12ms / connection) (wHandshake - 0.12ms / connection) (missing packets)
        //static TestMode mode = TestMode.TCP_Unmanaged ^ TestMode.UDP_Unmanaged; // (0.05ms / connection) (missing packets)
        //static TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged ^ TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (0.11ms / connection) (missing packets)
        //static TestMode mode = TestMode.TCPSSL_Managed; //(0.15ms / connection)
        
        //With connection close
        //static Debug mode - MF laptop - Single run 5K connections
        static TestMode mode = TestMode.TCP_Managed; // (9.87ms / connection) (missing packets)
        //static TestMode mode = TestMode.TCP_Unmanaged; // (0.47ms / connection) (missing packets)
        //static TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged; // (5.66ms / connection) (missing packets)
        //static TestMode mode = TestMode.UDP_Managed; // (0.29ms / connection) (wHandshake - 0.64ms / connection)
        //static TestMode mode = TestMode.UDP_Unmanaged; // (0.25ms / connection)
        //static TestMode mode = TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (0.30ms / connection)
        //static TestMode mode = TestMode.TCP_Managed ^ TestMode.UDP_Managed; // (6.72ms / connection) (wHandshake - 7.16ms / connection) (missing packets)
        //static TestMode mode = TestMode.TCP_Unmanaged ^ TestMode.UDP_Unmanaged; // (0.43ms / connection)
        //static TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged ^ TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (3.44ms / connection)

        static SSLOptions sslOptions;
        static SendReceiveOptions sendReceiveOptions;

        static string packetTypeStr;

        public static void RunExample()
        {
            Console.WriteLine("Please select mode:");
            Console.WriteLine("1 - Server (Listens for connections)");
            Console.WriteLine("2 - Client (Creates connections to server)");

            sendReceiveOptions = new SendReceiveOptions<NullSerializer>();
            //RijndaelPSKEncrypter.AddPasswordToOptions(sendReceiveOptions.Options, "test");
            //sendReceiveOptions.DataProcessors.Add(DPSManager.GetDataProcessor<RijndaelPSKEncrypter>());

            //NetworkComms.ConnectionEstablishTimeoutMS = 600000;
            NetworkComms.ConnectionListenModeUseSync = true;

            X509Certificate cert = new X509Certificate2("testCertificate.pfx");
            sslOptions = new SSLOptions(cert, true);

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) serverMode = true;
            else serverMode = false;

            UDPConnection.DefaultUDPOptions = UDPOptions.None;

            IPAddress localIPAddress = IPAddress.Parse("::1");
            packetTypeStr = "Unmanaged";

            if (serverMode)
            {
                //NetworkComms.DOSProtection.Enabled = true;

                //NetworkComms.DisableLogging();

                //Listen for connections
                int totalNumberOfListenPorts = 500;

                int portDivisor = 0;
                if ((mode & TestMode.TCP_Managed) == TestMode.TCP_Managed) portDivisor++;
                if ((mode & TestMode.TCP_Unmanaged) == TestMode.TCP_Unmanaged) portDivisor++;
                if ((mode & TestMode.UDP_Managed) == TestMode.UDP_Managed) portDivisor++;
                if ((mode & TestMode.UDP_Unmanaged) == TestMode.UDP_Unmanaged) portDivisor++;
                if ((mode & TestMode.TCPSSL_Managed) == TestMode.TCPSSL_Managed) portDivisor++;

                List<EndPoint> localIPEndPoints = new List<EndPoint>();
                List<ConnectionListenerBase> listeners = new List<ConnectionListenerBase>();
                for (int i = 0; i < totalNumberOfListenPorts/portDivisor; i++)
                {
                    if ((mode & TestMode.TCP_Managed) == TestMode.TCP_Managed)
                    {
                        localIPEndPoints.Add(new IPEndPoint(localIPAddress, 10000 + i));
                        listeners.Add(new TCPConnectionListener(sendReceiveOptions, ApplicationLayerProtocolStatus.Enabled));
                    }

                    if ((mode & TestMode.TCP_Unmanaged) == TestMode.TCP_Unmanaged)
                    {
                        localIPEndPoints.Add(new IPEndPoint(localIPAddress, 20000 + i));
                        listeners.Add(new TCPConnectionListener(sendReceiveOptions, ApplicationLayerProtocolStatus.Disabled));
                    }

                    if ((mode & TestMode.UDP_Managed) == TestMode.UDP_Managed)
                    {
                        localIPEndPoints.Add(new IPEndPoint(localIPAddress, 30000 + i));
                        listeners.Add(new UDPConnectionListener(sendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, UDPConnection.DefaultUDPOptions));
                    }

                    if ((mode & TestMode.UDP_Unmanaged) == TestMode.UDP_Unmanaged)
                    {
                        localIPEndPoints.Add(new IPEndPoint(localIPAddress, 40000 + i));
                        listeners.Add(new UDPConnectionListener(sendReceiveOptions, ApplicationLayerProtocolStatus.Disabled, UDPConnection.DefaultUDPOptions));
                    }

                    if ((mode & TestMode.TCPSSL_Managed) == TestMode.TCPSSL_Managed)
                    {
                        localIPEndPoints.Add(new IPEndPoint(localIPAddress, 50000 + i));
                        listeners.Add(new TCPConnectionListener(sendReceiveOptions, ApplicationLayerProtocolStatus.Enabled, sslOptions));
                    }
                }

                Connection.StartListening(listeners, localIPEndPoints, true);

                object locker = new object();
                int messageCount = 0;
                long totalBytesReceived = 0;
                int tcpFragmentationConcatCount = 0;
                int connectionEstablishCount = 0;
                int connectionCloseCount = 0;

                //List<string> packetSequenceNumbers = new List<string>();

                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>(packetTypeStr, (header, connection, data) =>
                    {
                        lock (locker)
                        {
                            //long seqNumber = header.GetOption(PacketHeaderLongItems.PacketSequenceNumber);
                            //packetSequenceNumbers.Add(connection.ToString() + "," + seqNumber);

                            //Increment a global counter
                            messageCount++;

                            if (data.Length != testDataSize)
                                tcpFragmentationConcatCount++;

                            totalBytesReceived += data.Length;
                        }
                    }, sendReceiveOptions);

                //Establish handler
                NetworkComms.AppendGlobalConnectionEstablishHandler((connection) =>
                    { lock (locker)
                        connectionEstablishCount++;
                    });

                //Close handler
                NetworkComms.AppendGlobalConnectionCloseHandler((connection) =>
                {
                    lock (locker)
                        connectionCloseCount++;
                });

                //Save the ports list out to disk
                using (StreamWriter sw = new StreamWriter("TCPServerPorts.txt", false))
                {
                    List<EndPoint> localListenEndPoints = Connection.ExistingLocalListenEndPoints(ConnectionType.TCP);
                    foreach (IPEndPoint endPoint in localListenEndPoints)
                    {
                        if (Connection.ExistingLocalListeners<TCPConnectionListener>(endPoint)[0].ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
                            sw.WriteLine("T-"+endPoint.Address.ToString() + "-" + endPoint.Port);
                        else
                            sw.WriteLine("F-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                    }
                }

                //Save the ports list out to disk
                using (StreamWriter sw = new StreamWriter("UDPServerPorts.txt", false))
                {
                    List<EndPoint> localListenEndPoints = Connection.ExistingLocalListenEndPoints(ConnectionType.UDP);
                    foreach (IPEndPoint endPoint in localListenEndPoints)
                    {
                        if (Connection.ExistingLocalListeners<UDPConnectionListener>(endPoint)[0].ApplicationLayerProtocol == ApplicationLayerProtocolStatus.Enabled)
                            sw.WriteLine("T-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                        else
                            sw.WriteLine("F-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                    }
                }

                Console.WriteLine("\nSelected mode = {0}, UDPOptions = {1}", mode, UDPConnection.DefaultUDPOptions);
                Console.WriteLine("Connection close after send = {0}", (closeConnectionAfterSend? "TRUE" : "FALSE"));
                Console.WriteLine("\nListening for incoming connections on {0} ports. Press 'c' key to see message count.", totalNumberOfListenPorts);

                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.KeyChar == 'c')
                    {
                        Console.WriteLine("#Handlers={0}, #Data={2}, #TCPFragConcat={1}, #Establish={3}, #Close={4}. Press 'c' to refresh message count, any other key to quit.", messageCount, tcpFragmentationConcatCount, totalBytesReceived / (double)testDataSize, connectionEstablishCount, connectionCloseCount);
                        //using (StreamWriter sw = new StreamWriter("seqNumbers.txt", false))
                        //{
                        //    List<string> copy = packetSequenceNumbers.ToList();
                        //    foreach (string line in copy)
                        //        sw.WriteLine(line);
                        //}
                    }
                    else
                        break;
                }
            }
            else
            {
                //NetworkComms.DisableLogging();

                //Load server port list
                string[] tcpServerPortList = File.ReadAllLines("TCPServerPorts.txt");
                TCPServerEndPoints = new Dictionary<IPEndPoint, ApplicationLayerProtocolStatus>();
                foreach (string current in tcpServerPortList)
                    TCPServerEndPoints.Add(new IPEndPoint(IPAddress.Parse(current.Split('-')[1]), int.Parse(current.Split('-')[2])), (current.Substring(0, 1) == "T" ? ApplicationLayerProtocolStatus.Enabled : ApplicationLayerProtocolStatus.Disabled));

                TCPServerEndPointsKeys = TCPServerEndPoints.Keys.ToList();

                string[] udpServerPortList = File.ReadAllLines("UDPServerPorts.txt");
                UDPServerEndPoints = new Dictionary<IPEndPoint, ApplicationLayerProtocolStatus>();
                foreach (string current in udpServerPortList)
                    UDPServerEndPoints.Add(new IPEndPoint(IPAddress.Parse(current.Split('-')[1]), int.Parse(current.Split('-')[2])), (current.Substring(0, 1) == "T" ? ApplicationLayerProtocolStatus.Enabled : ApplicationLayerProtocolStatus.Disabled));

                //UDPConnectionListener udpListener = new UDPConnectionListener(sendReceiveOptions,
                //    ((UDPConnection.DefaultUDPOptions & UDPOptions.Handshake) != UDPOptions.Handshake ? ApplicationLayerProtocolStatus.Disabled : ApplicationLayerProtocolStatus.Enabled), 
                //    UDPConnection.DefaultUDPOptions);
                //Connection.StartListening(udpListener, new IPEndPoint(localIPAddress, 10010), true);

                Console.WriteLine("Listening for connections on:");
                foreach (System.Net.IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(ConnectionType.UDP)) 
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                UDPServerEndPointsKeys = UDPServerEndPoints.Keys.ToList();

                Console.WriteLine("\nLoaded {0} TCP & {1} UDP server ports. Press any key to start the hammer!", TCPServerEndPoints.Count, UDPServerEndPointsKeys.Count);
                Console.ReadKey(true);
                Console.WriteLine("It's hammer time ...");

                clientHammerData = new byte[testDataSize];

                //Lets start by making as many connections as possible, go to absolute maximum performance
                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = 8;

                Stopwatch timer = new Stopwatch();
                timer.Start();
                Parallel.For(0, connectionHammerExecCount, options, ConnectionHammer);
                timer.Stop();

                Console.WriteLine("\nCompleted {0} connections in {1} secs. {2}ms per connection. {3} exceptions. Press any key to quit.", connectionHammerExecCount * connectionsPerHammer, (timer.ElapsedMilliseconds / 1000.0).ToString("0.00"), ((double)timer.ElapsedMilliseconds / (connectionHammerExecCount * connectionsPerHammer)).ToString("0.00"), exceptionCount);
                Console.ReadKey(true);
            }

            NetworkComms.Shutdown();
        }

        static int exceptionCount = 0;

        /// <summary>
        /// Hammers the server with connections.
        /// </summary>
        private static void ConnectionHammer(int index)
        {
            Random rand = new Random(index);

            for (int i = 0; i < connectionsPerHammer; i++)
            {
                bool tcpSelected = false;
                if (TCPServerEndPointsKeys.Count > 0 && UDPServerEndPointsKeys.Count > 0)
                    tcpSelected = (rand.NextDouble() > 0.5);
                else if (TCPServerEndPointsKeys.Count > 0)
                    tcpSelected = true;

                //options.Options.Add("ReceiveConfirmationRequired", "");

                IPEndPoint selectedEndPoint;
                ConnectionInfo connInfo;
                if (tcpSelected)
                {
                    selectedEndPoint = TCPServerEndPointsKeys[(int)(TCPServerEndPoints.Count * rand.NextDouble())];
                    connInfo = new ConnectionInfo(selectedEndPoint, TCPServerEndPoints[selectedEndPoint]);
                }
                else
                {
                    selectedEndPoint = UDPServerEndPointsKeys[(int)(UDPServerEndPoints.Count * rand.NextDouble())];
                    connInfo = new ConnectionInfo(selectedEndPoint, UDPServerEndPoints[selectedEndPoint]);
                }

                try
                {
                    Connection conn;

                    if (tcpSelected)
                    {
                        conn = TCPConnection.GetConnection(connInfo, sendReceiveOptions);
                        conn.SendObject(packetTypeStr, clientHammerData);

                        if(closeConnectionAfterSend)
                            conn.CloseConnection(false);
                    }
                    else
                    {
                        conn = UDPConnection.GetConnection(connInfo, sendReceiveOptions, UDPConnection.DefaultUDPOptions);
                        conn.SendObject(packetTypeStr, clientHammerData);

                        if (closeConnectionAfterSend)
                            conn.CloseConnection(false);

                        //SendReceiveOptions unmanagedOptions = new SendReceiveOptions<NullSerializer>();
                        //UDPConnection.SendObject("Unmanaged", clientHammerData, connInfo.RemoteEndPoint, unmanagedOptions, connInfo.ApplicationLayerProtocol);
                    }
                }
                catch (CommsException ex)
                {
                    Interlocked.Increment(ref exceptionCount);
                    NetworkComms.AppendStringToLogFile("ClientExceptions", ex.ToString());
                }
            }
        }
    }
}
