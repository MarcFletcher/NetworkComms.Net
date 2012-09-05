//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;
using NetworkCommsDotNet;
using System.Collections.Specialized;
using Common.Logging.Log4Net;

namespace ExamplesConsole
{
    public static class RPCExample
    {
        public static void RunExample()
        {
            Console.WriteLine("Please select run mode:\nServer - 1\nClient - 2\n");
            IRPCExampleInstance exampleToRun;

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
            {
                Console.WriteLine("Server mode selected.\n");
                exampleToRun = new ServerExampleInstance();
            }
            else if (selectedMode == 2)
            {
                Console.WriteLine("Client mode selected.\n");
                exampleToRun = new ClientExampleInstance();
            }
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

            double LastResult
            {
                get;
                set;
            }
        }

        /// <summary>
        /// An interface which can be used for the different server and client side implementations of the example
        /// </summary>
        public interface IRPCExampleInstance
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
        public class ServerExampleInstance : IRPCExampleInstance
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

                    Console.WriteLine("Client requested RPC of Multiply. {0}*{1}={2}.", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
                    return lastResult;
                }

                public double Add(double a, double b)
                {
                    lastResult = a + b;

                    Console.WriteLine("Client requested RPC of Add. {0}+{1}={2}.", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
                    return lastResult;
                }

                public double Subtract(double a, double b)
                {
                    lastResult = a - b;

                    Console.WriteLine("Client requested RPC of Subtract. {0}-{1}={2}.", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
                    return lastResult;
                }

                public double Divide(double a, double b)
                {
                    lastResult = a / b;

                    Console.WriteLine("Client requested RPC of Divide. {0}/{1}={2}.", a.ToString("0.###"), b.ToString("0.###"), lastResult.ToString("0.###"));
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

                public double LastResult
                {
                    get 
                    {
                        Console.WriteLine("Client requested last result. Last result was {0}.", lastResult);
                        return lastResult; 
                    }
                    set 
                    { 
                        lastResult = value; 
                    }
                }
            }

            public ServerExampleInstance()
            {
            }

            public void Run()
            {
                //Setup the RPC server side
                SetupRPCUsage();

                //Print out something at the server end to show that we are listening
                Console.WriteLine("Listening for connections on:");
                foreach (System.Net.IPEndPoint localEndPoint in TCPConnection.CurrentLocalEndPoints()) Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                Console.WriteLine("\nPress 'any' key to quit.");
                Console.ReadKey(true);

                //When we are done we must close down comms
                //This will clear all server side RPC object, delegates, handlers etc
                //As an alternative we could also use
                //RemoteProcedureCalls.Server.RemovePublicRPCObject(object instanceName);
                //RemoteProcedureCalls.Server.RemovePrivateRPCObjectType<T, I>();

                NetworkComms.Shutdown();
            }

            /// <summary>
            /// Configures the server side RPC features depending on desired usage mode
            /// </summary>
            private void SetupRPCUsage()
            {
                Console.WriteLine("What access method would you like to allow clients to use?\nPrivate client object instances - 1\nA single named, public, object instance - 2");
                int selectedUsageMode;

                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedUsageMode);
                    if (parseSucces && selectedUsageMode <= 2 && selectedUsageMode > 0) break;
                    Console.WriteLine("Invalid operation choice. Please try again.");
                }

                string instanceName;
                switch (selectedUsageMode)
                {
                    case 1:
                        Console.WriteLine("\nYou selected private client object instances.");
                        RemoteProcedureCalls.Server.RegisterTypeForPrivateRemoteCall<MathClass, IMath>();
                        Console.WriteLine("\nA type of {0} has been succesfully registered for RPC usage.\n",typeof(IMath));
                        break;
                    case 2:
                        Console.WriteLine("\nYou selected a single named, public, object instance.");
                        Console.Write("Please enter an identifying name for the public instance: ");
                        instanceName = Console.ReadLine();
                        RemoteProcedureCalls.Server.RegisterInstanceForPublicRemoteCall<MathClass, IMath>(new MathClass(), instanceName);
                        Console.WriteLine("\nServer object instance has been succesfully created.\n");
                        break;
                }
            }
        }

        /// <summary>
        ///  We are going to isolate the server and client example to demonstrate that the client never has to see 
        ///  the implementation used for IMath
        /// </summary>
        public class ClientExampleInstance : IRPCExampleInstance
        {
            public ClientExampleInstance()
            {

            }

            public void Run()
            {
                //Expecting user to enter ip address as 192.168.0.1:4000
                string serverIP; int serverPort;
                ExampleHelper.GetServerDetails(out serverIP, out serverPort);

                try
                {
                    //We would really like to create a local instance of MathClass, but the client does not have a 
                    //reference to MathClass (defined server side only):
                    //
                    //IMath remoteObject = new MathClass();
                    //
                    //This example is all about RPC, so we create the instance remotely instead, as follows ...

                    //We need to select our remote object using one of the available access methods
                    string instanceId = ""; 
                    IMath remoteObject = SelectRemoteObject(serverIP, serverPort, out instanceId);
                    Console.WriteLine("\nRemote object has been selected. RPC object instanceId: {0}", instanceId);

                    while (true)
                    {
                        //We can now perform RPC operations on our remote object
                        Console.WriteLine("\nPlease press 'y' key to perform some remote math. Any other key will quit the example.");
                        string message = Console.ReadKey(true).KeyChar.ToString().ToLower();

                        //If the user has typed exit then we leave our loop and end the example
                        if (message == "y")
                        {
                            //We pass our remoteObject to our local method DoMath to keep this area of code clean                         
                            Console.WriteLine("Result: " + DoMath(remoteObject).ToString());
                        }
                        else
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                finally
                {
                    NetworkComms.Shutdown();
                }
            }

            /// <summary>
            /// Allows the user to select a remote object based on the different available access methods
            /// </summary>
            /// <param name="serverIP">The RPC server ip address</param>
            /// <param name="serverPort">The RPC server port number</param>
            /// <param name="instanceId">The instanceId of the linked object</param>
            /// <returns>The remote RPC object</returns>
            private IMath SelectRemoteObject(string serverIP, int serverPort, out string instanceId)
            {
                //We have three main different usage cases for the RPC functionality provided by networkComms.net
                //Before we can demonstrate RPC features we need to select the remote object
                //We select a remote object using one of three different access methods
                //1. Create a private client specific instance
                //2. Access a named server instance
                //3. Access a private client specific instance via instanceId
                Console.WriteLine("\nWhat access method would you like to use to a remote object?\nNew private client object instance - 1\nExisting named, public, server object instance - 2\nExisting private client object instance, using instanceId - 3");
                int selectedUsageMode;

                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedUsageMode);
                    if (parseSucces && selectedUsageMode <= 3 && selectedUsageMode > 0) break;
                    Console.WriteLine("Invalid operation choice. Please try again.");
                }

                string instanceName;
                switch (selectedUsageMode)
                {
                    case 1:
                        Console.WriteLine("\nYou selected to create a new private client object instance.");
                        Console.Write("Please enter a name for your private object: ");
                        instanceName = Console.ReadLine();
                        return RemoteProcedureCalls.Client.CreateProxyToPrivateInstance<IMath>(serverIP, serverPort, instanceName, out instanceId);
                    case 2:
                        Console.WriteLine("\nYou selected to access an existing, named, public server object instance.");
                        Console.Write("Please enter the name of the server object: ");
                        instanceName = Console.ReadLine();
                        return RemoteProcedureCalls.Client.CreateProxyToPublicNamedInstance<IMath>(serverIP, serverPort, instanceName, out instanceId);
                    case 3:
                        Console.WriteLine("\nYou selected to access an existing, private, client object instance using an instanceId.");
                        Console.Write("Please enter the object instanceId: ");
                        instanceId = Console.ReadLine();
                        return RemoteProcedureCalls.Client.CreateProxyToIdInstance<IMath>(serverIP, serverPort, instanceId);
                }

                throw new Exception("It should be impossible to get here.");
            }

            private static object DoMath(IMath remoteObject)
            {
                Console.WriteLine("\nWhat operation would you like to perform?\nMultiply - 1\nAdd - 2\nSubtract - 3\nDivide - 4\nEcho - 5\nGet Server Instance of MathClass - 6\nThrow Exception Remotely - 7\n" + 
                    "Get last result using property - 8\nGet last result multiplied by an int using an indexer - 9\n");

                int selectedOperation;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedOperation);
                    if (parseSucces && selectedOperation <= 9 && selectedOperation > 0) break;
                    Console.WriteLine("Invalid operation choice. Please try again.");
                }

                double a, b;
                switch (selectedOperation)
                {
                    case 1:
                        Console.WriteLine("You selected to Multiply two numbers.");
                        Console.Write("Please enter first number: ");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.Write("Please enter second number: ");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Multiply(a, b);
                    case 2:
                        Console.WriteLine("You selected to Add two numbers.");
                        Console.Write("Please enter first number: ");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.Write("Please enter second number: ");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Add(a, b);
                    case 3:
                        Console.WriteLine("You selected to Subtract two numbers.");
                        Console.Write("Please enter first number: ");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.Write("Please enter second number: ");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Subtract(a, b);
                    case 4:
                        Console.WriteLine("You selected to Divide two numbers.");
                        Console.Write("Please enter first number: ");
                        if (!double.TryParse(Console.ReadLine(), out a)) break;
                        Console.Write("Please enter second number: ");
                        if (!double.TryParse(Console.ReadLine(), out b)) break;
                        return remoteObject.Divide(a, b);
                    case 5:
                        Console.WriteLine("Please enter string. This will be sent to the server and 'echoed' back:");
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
                    case 8:
                        Console.WriteLine("Last result was.");
                        return remoteObject.LastResult;
                    case 9:
                        int temp;
                        Console.WriteLine("You selected to get the last result multiplied by an int via an indexer");
                        Console.WriteLine("Please enter an integer to multiply by");
                        if (!int.TryParse(Console.ReadLine(), out temp)) break;
                        return remoteObject[temp];                        
                }

                throw new Exception("How have you managed to get execution to this point? Maybe you entered something that was BAD");
            }
        }
    }
}
