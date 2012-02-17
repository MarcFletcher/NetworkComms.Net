using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;
using NetworkCommsDotNet;

namespace ExamplesConsole
{   
    public static class RPCExample
    {
        public static void RunExample()
        {
            Console.WriteLine("Please select run mode\nServer - 1\nClient - 2\n");
            IExampleInstance exampleToRun;

            //Select the desired mode, client or server.
            int selectedMode;
            while (true)
            {
                bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedMode);
                if (parseSucces && selectedMode <= 2 && selectedMode > 0) break;
                Console.WriteLine("Invalid choice. Please try again.");
            }

            //Prepare the necessary class
            if (selectedMode == 1)
                exampleToRun = new ServerExampleInstance();
            else if (selectedMode == 2)
                exampleToRun = new ClientExampleInstance();
            else
                throw new Exception("Unable to determine correct mode. Please try again.");

            //Run the example
            exampleToRun.Run();
        }

        /// <summary>
        /// This is an interface known to both client and server
        /// </summary>
        public interface IMath
        {
            //We define some basic math operations here
            double Multiply(double a, double b);
            double Add(double a, double b);
            double Subtract(double a, double b);
            double Divide(double a, double b);

            //Define a non math operator
            string Echo(string input);

            //Do something that throws an exception
            void ThrowTestException();

            //Get a copy of the server object
            IMath GetServerObjectCopy();

            //Access something using an 'indexer'
            //See http://msdn.microsoft.com/en-us/library/aa288465(v=vs.71).aspx for more information on indexers
            double this[int index]
            {
                get;
                set;
            }
        }

        public interface IExampleInstance
        {
            /// <summary>
            /// Run the example
            /// </summary>
            void Run();
        }

        /// <summary>
        /// We are going to isolate the server and client example to demonstrate that the client never has to see 
        /// the implementation used for IMath
        /// </summary>
        public class ServerExampleInstance : IExampleInstance
        {
            [ProtoContract]
            private class MathClass : IMath
            {
                public MathClass() { }

                [ProtoMember(1)]
                double lastResult;

                public double Multiply(double a, double b)
                {
                    lastResult = a * b;

                    Console.WriteLine("Client requested RPC of Multiply. {0}*{1}={3}", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
                    return lastResult;
                }

                public double Add(double a, double b)
                {
                    lastResult = a + b;

                    Console.WriteLine("Client requested RPC of Add. {0}+{1}={3}", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
                    return lastResult;
                }

                public double Subtract(double a, double b)
                {
                    lastResult = a - b;

                    Console.WriteLine("Client requested RPC of Subtract. {0}-{1}={3}", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
                    return lastResult;
                }

                public double Divide(double a, double b)
                {
                    lastResult = a / b;

                    Console.WriteLine("Client requested RPC of Divide. {0}/{1}={3}", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
                    return lastResult;
                }

                public string Echo(string input)
                {
                    Console.WriteLine("Client requested RPC an Echo of '{0}'.", input);
                    return input;
                }

                public IMath GetServerObjectCopy()
                {
                    Console.WriteLine("Client requested a copy of this object.");
                    return this;
                }

                public void ThrowTestException()
                {
                    Console.WriteLine("Client requested an exception test.");
                    throw new Exception("Test Exception");
                }

                public override string ToString()
                {
                    return lastResult.ToString();
                }

                public double this[int index]
                {
                    get
                    {
                        Console.WriteLine("Client tested a get indexer.");
                        return lastResult * index;
                    }

                    set
                    {
                        Console.WriteLine("Client tested a set indexer.");
                        lastResult = value * index;
                    }
                }
            }

            public ServerExampleInstance()
            {
            }

            public void Run()
            {
                //We register the implemention MathClass with the corresponding interface
                //This method automatically enables networkComms to start acception connections, set enableAutoListen = false to prevent that
                RemoteProcedureCalls.RegisterTypeForRemoteCall<MathClass, IMath>();

                //Print out something at the server end to show that we are listening
                Console.WriteLine("Listening for connections on {0}:{1}", NetworkComms.LocalIP, NetworkComms.CommsPort.ToString());
                Console.WriteLine("Press any key then enter to quit");
                Console.ReadLine();

                //When we are done we must close down comms
                NetworkComms.ShutdownComms();
            }
        }

        /// <summary>
        ///  We are going to isolate the server and client example to demonstrate that the client never has to see 
        ///  the implementation used for IMath
        /// </summary>
        public class ClientExampleInstance : IExampleInstance
        {
            public ClientExampleInstance()
            {

            }

            public void Run()
            {
                //Expecting user to enter ip address as 192.168.0.1:4000
                string serverIP; int serverPort;
                #region Parse Destination
                Console.WriteLine("\nPlease enter the destination IP address and port, e.g. '192.168.0.1:4000':");
                while (true)
                {
                    try
                    {
                        //Parse the provided information
                        string userEnteredStr = Console.ReadLine();
                        serverIP = userEnteredStr.Split(':')[0];
                        serverPort = int.Parse(userEnteredStr.Split(':')[1]);
                        break;
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Unable to determine host IP address and port. Check format and try again:");
                    }
                }
                #endregion

                try
                {
                    //Setup the remote object at the server end with an object name of "TestInstance1"
                    var remoteObject = RemoteProcedureCalls.ProxyClassGenerator.Create<IMath>(serverIP, serverPort, "TestInstance1");
                    
                    bool exit = false;
                    while (!exit)
                    {
                        Console.WriteLine("Would you like to perform some maths? (Y/N)?");

                        switch (Console.ReadLine().ToUpper())
                        {
                            case "Y":
                                Console.WriteLine(DoMath(remoteObject).ToString());
                                break;
                            case "N":
                                exit = true;
                                break;
                            default:
                                Console.WriteLine("Input error please try again");
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                finally
                {
                    NetworkComms.ShutdownComms();
                }
            }

            private static object DoMath(IMath remoteObject)
            {
                Console.WriteLine("\nWhat operation would you like to perform?\nMultiply - 1\nAdd - 2\nSubtract - 3\nDivide - 4\nEcho - 5\nGet Server Instance of MathClass - 6\nThrow Exception Remotely - 7\n");

                int selectedOperation;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedOperation);
                    if (parseSucces && selectedOperation <= 7 && selectedOperation > 0) break;
                    Console.WriteLine("Invalid operation choice. Please try again.");
                }

                double a, b;
                switch (selectedOperation)
                {
                    case 1:
                        Console.WriteLine("Please enter first number");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.WriteLine("Please enter second number");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Multiply(a, b);
                    case 2:
                        Console.WriteLine("Please enter first number");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.WriteLine("Please enter second number");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Add(a, b);
                    case 3:
                        Console.WriteLine("Please enter first number");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.WriteLine("Please enter second number");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Subtract(a, b);
                    case 4:
                        Console.WriteLine("Please enter first number");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.WriteLine("Please enter second number");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Divide(a, b);
                    case 5:
                        Console.WriteLine("Please enter string");
                        return remoteObject.Echo(Console.ReadLine());
                    case 6:
                        return remoteObject.GetServerObjectCopy();
                    case 7:
                        try
                        {
                            remoteObject.ThrowTestException();
                        }
                        catch (Exception e)
                        {
                            return e.ToString();
                        }
                        return null;
                }

                Console.WriteLine("Input error please try again.\n");
                return DoMath(remoteObject);
            }
        }
    }
}
