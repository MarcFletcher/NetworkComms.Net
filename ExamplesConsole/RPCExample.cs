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
        public interface ITest
        {
            double Mult(double a, double b);
            double Add(double a, double b);
            double Subtract(double a, double b);
            double Divide(double a, double b);

            string Echo(string input);
            void ThrowException();

            Test GetServerCopy();

            double this[int index]
            {
                get;
                set;
            }
        }        

        [ProtoContract]
        public class Test : ITest
        {
            public Test() { }

            [ProtoMember(1)]
            double lastResult;
            
            public double Mult(double a, double b)
            {
                lastResult = a * b;
                return lastResult;
            }

            public double Add(double a, double b)
            {
                lastResult = a + b;
                return lastResult;
            }

            public double Subtract(double a, double b)
            {
                lastResult = a - b;
                return lastResult;
            }

            public double Divide(double a, double b)
            {
                lastResult = a / b;
                return lastResult;
            }

            public string Echo(string input)
            {
                return input;
            }

            public Test GetServerCopy()
            {
                return this;
            }

            public void ThrowException()
            {
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
                    return lastResult * index;
                }

                set
                {
                    lastResult = value * index;
                }
            }
        }


        public static void RunExample()
        {
            Console.WriteLine("Please select run mode\nServer - 1\nClient - 2");
            switch (Console.ReadLine())
            {
                case "1":
                    Server();
                    return;
                case "2":
                    Client();
                    return;
                default:
                    RunExample();
                    return;
            }
        }

        static void Client()
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
                var test = RemoteProcedureCalls.ProxyClassGenerator.Create<ITest>(serverIP, serverPort, "TestInstance1");
                bool exit = false;

                while (!exit)
                {
                    Console.WriteLine("Would you like to do something (Y/N)?");

                    switch (Console.ReadLine().ToUpper())
                    {
                        case "Y":
                            Console.WriteLine(DoMath(test).ToString());
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

        static object DoMath(ITest proxy)
        {
            Console.WriteLine("What operation would you like to do?\nMultiply - 1\nAdd - 2\nSubtract - 3\nDivide - 4\nEcho - 5\nGet Server Instance of Test - 6\nThrow Exception remotely - 7");

            double a, b;

            switch (Console.ReadLine())
            {
                case "1":                    
                    Console.WriteLine("Please enter first number");
                    if (!double.TryParse(Console.ReadLine(), out a)) break;
                    Console.WriteLine("Please enter second number");
                    if (!double.TryParse(Console.ReadLine(), out b)) break;                                        
                    return proxy.Mult(a, b);
                    break;                    
                case "2":
                    Console.WriteLine("Please enter first number");
                    if (!double.TryParse(Console.ReadLine(), out a)) break;
                    Console.WriteLine("Please enter second number");
                    if (!double.TryParse(Console.ReadLine(), out b)) break;
                    return proxy.Add(a, b);
                case "3":
                    Console.WriteLine("Please enter first number");
                    if (!double.TryParse(Console.ReadLine(), out a)) break;
                    Console.WriteLine("Please enter second number");
                    if (!double.TryParse(Console.ReadLine(), out b)) break;
                    return proxy.Subtract(a, b);
                case "4":
                    Console.WriteLine("Please enter first number");
                    if (!double.TryParse(Console.ReadLine(), out a)) break;
                    Console.WriteLine("Please enter second number");
                    if (!double.TryParse(Console.ReadLine(), out b)) break;
                    return proxy.Divide(a, b);
                case "5":
                    Console.WriteLine("Please enter string");
                    return proxy.Echo(Console.ReadLine());
                case "6":
                    return proxy.GetServerCopy();                    
                case "7":
                    try
                    {
                        proxy.ThrowException();
                    }
                    catch (Exception e)
                    {
                        return e.ToString();
                    }
                    return null;
            }

            Console.WriteLine("Input Error please try again");
            return DoMath(proxy);
        }

        static void Server()
        {
            RemoteProcedureCalls.RegisterTypeForRemoteCall<Test, ITest>();

            Console.WriteLine("Listening for connections on {0}:{1}", NetworkComms.LocalIP, NetworkComms.CommsPort.ToString());
            Console.WriteLine("Press any key then enter to exit");
            Console.ReadLine();

            NetworkComms.ShutdownComms();
        }
    }
}
