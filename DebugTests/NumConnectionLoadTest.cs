using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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
    }

    static class NumConnectionLoadTest
    {
        static bool serverMode;

        static Dictionary<IPEndPoint, ApplicationLayerProtocolStatus> TCPServerEndPoints;
        static Dictionary<IPEndPoint, ApplicationLayerProtocolStatus> UDPServerEndPoints;
        static List<IPEndPoint> TCPServerEndPointsKeys;
        static List<IPEndPoint> UDPServerEndPointsKeys;

        static int connectionsPerHammer = 100;
        static byte[] clientHammerData;

        static int testDataSize = 1024;

        public static void RunExample()
        {
            Console.WriteLine("Please select mode:");
            Console.WriteLine("1 - Server (Listens for connections)");
            Console.WriteLine("2 - Client (Creates connections to server)");

            NetworkComms.DefaultSendReceiveOptions = new SendReceiveOptions<ProtobufSerializer>();

            //Read in user choice
            if (Console.ReadKey(true).Key == ConsoleKey.D1) serverMode = true;
            else serverMode = false;

            UDPConnection.DefaultUDPOptions = UDPOptions.None;

            if (serverMode)
            {
                //No connection close
                //Debug mode - MF laptop - Single run 50K connections
                //TestMode mode = TestMode.TCP_Managed; // (0.10ms / connection)
                //TestMode mode = TestMode.TCP_Unmanaged; // (0.05ms / connection)
                //TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged; // (0.09ms / connection)
                TestMode mode = TestMode.UDP_Managed; // (0.03ms / connection) (wHandshake - 0.04ms / connection)
                //TestMode mode = TestMode.UDP_Unmanaged; // (0.03ms / connection)
                //TestMode mode = TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (0.03ms / connection)
                //TestMode mode = TestMode.TCP_Managed ^ TestMode.UDP_Managed; // (0.10ms / connecition) (wHandshake - 0.11ms / connection)
                //TestMode mode = TestMode.TCP_Unmanaged ^ TestMode.UDP_Unmanaged; // (0.04ms / connection)
                //TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged ^ TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (0.09ms / connection)

                //With connection close
                //Debug mode - MF laptop - Single run 5K connections
                //TestMode mode = TestMode.TCP_Managed; // (6.73ms / connection)
                //TestMode mode = TestMode.TCP_Unmanaged; // (0.40ms / connection)
                //TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged; // (3.00ms / connection)
                //TestMode mode = TestMode.UDP_Managed; // (0.64ms / connection) (wHandshake - 1.05ms / connection)
                //TestMode mode = TestMode.UDP_Unmanaged; // (0.61ms / connection)
                //TestMode mode = TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (0.64ms / connection)
                //TestMode mode = TestMode.TCP_Managed ^ TestMode.UDP_Managed; // (6.31ms / connecition) (wHandshake - 6.74ms / connection)
                //TestMode mode = TestMode.TCP_Unmanaged ^ TestMode.UDP_Unmanaged; // (0.58ms / connection)
                //TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged ^ TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged; // (3.03ms / connection)

                //Listen for connections
                int totalNumberOfListenPorts = 500;
                IPAddress localIPAddress = IPAddress.Parse("::1");

                int portDivisor = 0;
                if ((mode & TestMode.TCP_Managed) == TestMode.TCP_Managed) portDivisor++;
                if ((mode & TestMode.TCP_Unmanaged) == TestMode.TCP_Unmanaged) portDivisor++;
                if ((mode & TestMode.UDP_Managed) == TestMode.UDP_Managed) portDivisor++;
                if ((mode & TestMode.UDP_Unmanaged) == TestMode.UDP_Unmanaged) portDivisor++;

                for (int i = 0; i < totalNumberOfListenPorts/portDivisor; i++)
                {
                    if ((mode & TestMode.TCP_Managed) == TestMode.TCP_Managed)
                        TCPConnection.StartListening(new IPEndPoint(localIPAddress, 10000 + i), ApplicationLayerProtocolStatus.Enabled, true);

                    if ((mode & TestMode.TCP_Unmanaged) == TestMode.TCP_Unmanaged)
                        TCPConnection.StartListening(new IPEndPoint(localIPAddress, 20000 + i), ApplicationLayerProtocolStatus.Disabled, true);
                   
                    if ((mode & TestMode.UDP_Managed) == TestMode.UDP_Managed)
                        UDPConnection.StartListening(new IPEndPoint(localIPAddress, 30000 + i), ApplicationLayerProtocolStatus.Enabled, true);

                    if ((mode & TestMode.UDP_Unmanaged) == TestMode.UDP_Unmanaged)
                        UDPConnection.StartListening(new IPEndPoint(localIPAddress, 40000 + i), ApplicationLayerProtocolStatus.Disabled, true);
                }

                object locker = new object();
                int messageCount = 0;
                long totalBytesReceived = 0;
                int tcpFragmentationConcatCount = 0;
                int connectionEstablishCount = 0;
                int connectionCloseCount = 0;

                List<string> packetSequenceNumbers = new List<string>();

                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("Unmanaged", (header, connection, data) =>
                    {
                        lock (locker)
                        {
                            long seqNumber = header.GetOption(PacketHeaderLongItems.PacketSequenceNumber);
                            packetSequenceNumbers.Add(connection.ToString() + "," + seqNumber);

                            //Increment a global counter
                            messageCount++;

                            if (data.Length != testDataSize)
                                tcpFragmentationConcatCount++;

                            totalBytesReceived += data.Length;
                        }
                    });

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
                    List<IPEndPoint> localListenEndPoints = TCPConnection.ExistingLocalListenEndPoints();
                    foreach (IPEndPoint endPoint in localListenEndPoints)
                    {
                        if (TCPConnection.ExistingLocalListenEndPointApplicationLayerProtocolStatus(endPoint) == ApplicationLayerProtocolStatus.Enabled)
                            sw.WriteLine("T-"+endPoint.Address.ToString() + "-" + endPoint.Port);
                        else
                            sw.WriteLine("F-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                    }
                }

                //Save the ports list out to disk
                using (StreamWriter sw = new StreamWriter("UDPServerPorts.txt", false))
                {
                    List<IPEndPoint> localListenEndPoints = UDPConnection.ExistingLocalListenEndPoints();
                    foreach (IPEndPoint endPoint in localListenEndPoints)
                    {
                        if (UDPConnection.ExistingLocalListenEndPointApplicationLayerProtocolStatus(endPoint) == ApplicationLayerProtocolStatus.Enabled)
                            sw.WriteLine("T-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                        else
                            sw.WriteLine("F-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                    }
                }

                Console.WriteLine("\nSelected mode = {0}, UDPOptions = {1}", mode, UDPConnection.DefaultUDPOptions);
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
                //The client side never wants logging
                NetworkComms.DisableLogging();

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

                UDPServerEndPointsKeys = UDPServerEndPoints.Keys.ToList();

                Console.WriteLine("\nLoaded {0} TCP & {1} UDP server ports. Press any key to start the hammer!", TCPServerEndPoints.Count, UDPServerEndPointsKeys.Count);
                Console.ReadKey(true);
                Console.WriteLine("It's hammer time ...");

                clientHammerData = new byte[testDataSize];

                //Lets start by making as many connections as possible, go to absolute maximum performance
                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = 8;

                int connectionHammerExecCount = 500;

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

                SendReceiveOptions options = (SendReceiveOptions)NetworkComms.DefaultSendReceiveOptions.Clone();
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
                        conn = TCPConnection.GetConnection(connInfo, options);
                        conn.SendObject("Unmanaged", clientHammerData);
                        //conn.CloseConnection(false);
                    }
                    else
                    {
                        conn = UDPConnection.GetConnection(connInfo, options, UDPConnection.DefaultUDPOptions);
                        conn.SendObject("Unmanaged", clientHammerData);
                        //conn.CloseConnection(false);
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
