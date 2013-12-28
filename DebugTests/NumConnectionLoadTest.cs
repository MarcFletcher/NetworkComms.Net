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

        static Dictionary<IPEndPoint,bool> TCPServerEndPoints;
        static Dictionary<IPEndPoint, bool> UDPServerEndPoints;
        static List<IPEndPoint> TCPServerEndPointsKeys;
        static List<IPEndPoint> UDPServerEndPointsKeys;

        static int connectionsPerHammer = 25;
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

            if (serverMode)
            {
                //TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged;
                //TestMode mode = TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged;
                TestMode mode = TestMode.TCP_Managed ^ TestMode.TCP_Unmanaged ^ TestMode.UDP_Managed ^ TestMode.UDP_Unmanaged;

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
                        TCPConnection.StartListening(new IPEndPoint(localIPAddress, 10000 + i), true, true);

                    if ((mode & TestMode.TCP_Unmanaged) == TestMode.TCP_Unmanaged)
                        TCPConnection.StartListening(new IPEndPoint(localIPAddress, 20000 + i), false, true);
                   
                    if ((mode & TestMode.UDP_Managed) == TestMode.UDP_Managed)
                        UDPConnection.StartListening(new IPEndPoint(localIPAddress, 30000 + i), true, true);

                    if ((mode & TestMode.UDP_Unmanaged) == TestMode.UDP_Unmanaged)
                        UDPConnection.StartListening(new IPEndPoint(localIPAddress, 40000 + i), false, true);
                }

                int messageCount = 0;
                int tcpFragmentationConcatCount = 0;
                NetworkComms.AppendGlobalIncomingPacketHandler<byte[]>("Unmanaged", (header, connection, data) =>
                    {
                        if (data.Length != testDataSize)
                            Interlocked.Increment(ref tcpFragmentationConcatCount);

                        //Increment a global counter
                        Interlocked.Increment(ref messageCount);
                    });

                //Save the ports list out to disk
                using (StreamWriter sw = new StreamWriter("TCPServerPorts.txt", false))
                {
                    List<IPEndPoint> localListenEndPoints = TCPConnection.ExistingLocalListenEndPoints();
                    foreach (IPEndPoint endPoint in localListenEndPoints)
                    {
                        if ((bool)TCPConnection.ExistingLocalListenEndPointApplicationLayerProtocolEnabled(endPoint))
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
                        if ((bool)UDPConnection.ExistingLocalListenEndPointApplicationLayerProtocolEnabled(endPoint))
                            sw.WriteLine("T-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                        else
                            sw.WriteLine("F-" + endPoint.Address.ToString() + "-" + endPoint.Port);
                    }
                }

                Console.WriteLine("\nSelected mode = {0}", mode);
                Console.WriteLine("\nListening for incoming connections on {0} ports. Press any key to see message count.", totalNumberOfListenPorts);
                Console.ReadKey(true);
                Console.WriteLine("Message count = {0}. TCPFragConcat count = {1}. Press any key to quit.", messageCount, tcpFragmentationConcatCount);
                Console.ReadKey(true);
            }
            else
            {
                //Load server port list
                string[] tcpServerPortList = File.ReadAllLines("TCPServerPorts.txt");
                TCPServerEndPoints = new Dictionary<IPEndPoint, bool>();
                foreach (string current in tcpServerPortList)
                    TCPServerEndPoints.Add(new IPEndPoint(IPAddress.Parse(current.Split('-')[1]), int.Parse(current.Split('-')[2])), (current.Substring(0, 1) == "T" ? true : false));

                TCPServerEndPointsKeys = TCPServerEndPoints.Keys.ToList();

                string[] udpServerPortList = File.ReadAllLines("UDPServerPorts.txt");
                UDPServerEndPoints = new Dictionary<IPEndPoint, bool>();
                foreach (string current in udpServerPortList)
                    UDPServerEndPoints.Add(new IPEndPoint(IPAddress.Parse(current.Split('-')[1]), int.Parse(current.Split('-')[2])), (current.Substring(0, 1) == "T" ? true : false));

                UDPServerEndPointsKeys = UDPServerEndPoints.Keys.ToList();

                Console.WriteLine("\nLoaded {0} TCP & {1} UDP server ports. Press any key to start the hammer!", TCPServerEndPoints.Count, UDPServerEndPointsKeys.Count);
                Console.ReadKey(true);
                Console.WriteLine("It's hammer time ...");

                clientHammerData = new byte[testDataSize];

                //Lets start by making as many connections as possible, go to absolute maximum performance
                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = 8;

                int connectionHammerExecCount = 1000;

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
                        conn = TCPConnection.GetConnection(connInfo);
                    else
                        conn = UDPConnection.GetConnection(connInfo, UDPOptions.None);

                    conn.SendObject("Unmanaged", clientHammerData);

                    //conn.CloseConnection(false);
                }
                catch (CommsException ex)
                {
                    Interlocked.Increment(ref exceptionCount);
                    NetworkComms.AppendStringToLogFile("ClientExceptions.txt", ex.ToString());
                }
            }
        }
    }
}
