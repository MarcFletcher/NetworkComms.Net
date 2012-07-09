using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Collections.Specialized;
using Common.Logging.Simple;
using Common.Logging;
using Common.Logging.Log4Net;
using log4net.Repository;
using log4net.Layout;
using log4net.Appender;
using System.Threading;
using System.IO;
using DistributedFileSystem;

namespace DebugTests
{
    class Program
    {
        static void Main(string[] args)
        {
            //int a = 100;

            //var bytes = SerializerBase.Protobuf.ProtobufSerializer.Instance.SerialiseDataObject(a, SerializerBase.NullCompressor.Instance);
            //int b = SerializerBase.Protobuf.ProtobufSerializer.Instance.DeserialiseDataObject<int>(bytes, SerializerBase.NullCompressor.Instance);

            //return;

            try
            {
                Console.SetBufferSize(130, 500);
                Console.SetWindowSize(130, 25);
            }
            catch (Exception) { }

            Thread.CurrentThread.Name = "MainThread";

            //Configure the logger here
            NameValueCollection properties = new NameValueCollection();
            properties["configType"] = "FILE";
            properties["configFile"] = "log4net.config";
            NetworkComms.EnableLogging(new Log4NetLoggerFactoryAdapter(properties));

            NetworkComms.PreferredIPPrefix = new string[] { "172", "131" };
            NetworkComms.CommsPort = int.Parse(args[0]);

            //No arguments we are running in NIES

            string serverIP = "172.24.226.32";
            if (NetworkComms.LocalIP != serverIP)
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        NetworkComms.SendObject("Message", serverIP, NetworkComms.CommsPort, true, "Calling home from " + NetworkComms.HostName);
                        break;
                    }
                    catch(Exception)
                    {

                    }

                    Thread.Sleep(60000);
                }
            }
            else
                BasicSend.RunExample();

            //AliveTest.RunExample();
            //DebugTest.Go();
            //DFSTest.RunExample();

            NetworkComms.ShutdownComms();
        }
    }
}
