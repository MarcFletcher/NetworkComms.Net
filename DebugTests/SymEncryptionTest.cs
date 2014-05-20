using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace DebugTests
{
    class SymEncryptionTest
    {
        /// <summary>
        /// Run example
        /// </summary>
        public static void RunExample()
        {
            //NetworkComms.PacketConfirmationTimeoutMS = 1000000;

            //var processors = new List<DataProcessor> { DPSManager.GetDataProcessor<SharpZipLibCompressor.SharpZipLibGzipCompressor>(), DPSManager.GetDataProcessor<RijndaelPSKEncrypter>() };
            var processors = new List<DataProcessor> { DPSManager.GetDataProcessor<RijndaelPSKEncrypter>() };
            var dataOptions = new Dictionary<string, string>();
            RijndaelPSKEncrypter.AddPasswordToOptions(dataOptions, "oj1N0bcfsjtQxfgRKT7B");
            var options = new SendReceiveOptions(DPSManager.GetDataSerializer<ProtobufSerializer>(), processors, dataOptions) { UseNestedPacket = false };

            //Uncomment to make it work
            //RijndaelPSKEncrypter.AddPasswordToOptions(NetworkComms.DefaultSendReceiveOptions.Options, "oj1N0bcfsjtQxfgRKT7B");

            var points = new List<IPEndPoint>();
            var localIPs = HostInfo.IP.FilteredLocalAddresses();
            foreach (var ip in localIPs)
            {
                var listener = new TCPConnectionListener(options, ApplicationLayerProtocolStatus.Enabled);
                listener.AppendIncomingPacketHandler<string>("Kill all humans",
                                (header, con, customObject) =>
                                {
                                    Console.WriteLine("\nReceived custom protobuf object from " + con);
                                }, options);
                Connection.StartListening(listener, new IPEndPoint(ip, 0));
                points.Add(listener.LocalListenEndPoint as IPEndPoint);
            }

            Console.WriteLine("Listening on:");
            foreach (var endpoint in points)
                Console.WriteLine("{0}:{1}", endpoint.Address.ToString(), endpoint.Port.ToString());

            var point = points.First();
            //IPEndPoint point = IPTools.ParseEndPointFromString("::1:46112");

            var connectionInfo = new ConnectionInfo(point);
            var connection = TCPConnection.GetConnection(connectionInfo);

            options.ReceiveConfirmationRequired = true;
            connection.SendObject("Kill all humans", "Bite my shiny metal ass", options);

            Console.ReadLine();
        }
    }
}
