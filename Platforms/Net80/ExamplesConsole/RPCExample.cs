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
using System.Linq;
using System.Text;
using ProtoBuf;
using System.Collections.Specialized;
using System.Net;
using NetworkCommsDotNet;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.TCP;
using NetworkCommsDotNet.Connections.UDP;
using System.Threading.Tasks;
using System.Threading;

namespace Examples.ExamplesConsole
{
    /// <summary>
    /// An example which demonstrates <see href="http://en.wikipedia.org/wiki/Remote_procedure_call">Remote Procedure Calls</see> using NetworkComms.Net
    /// </summary>
    public static class RPCExample
    {
        /// <summary>
        /// Run the example
        /// </summary>
        public static void RunExample()
        {
            Console.WriteLine("Remote Procedure Call (RPC) Example ...\n");

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

            /// <summary>
            /// Multiply numbers together.
            /// </summary>
            /// <param name="a">Number a</param>
            /// <param name="b">Number b</param>
            /// <returns>Return a * b</returns>
            double Multiply(double a, double b);

            /// <summary>
            /// Add numbers together.
            /// </summary>
            /// <param name="a">Number a</param>
            /// <param name="b">Number b</param>
            /// <returns>Return a + b</returns>
            double Add(double a, double b);

            /// <summary>
            /// Subtract numbers.
            /// </summary>
            /// <param name="a">Number a</param>
            /// <param name="b">Number b</param>
            /// <returns>Return a - b</returns>
            double Subtract(double a, double b);

            /// <summary>
            /// Divide numbers.
            /// </summary>
            /// <param name="a">Number a</param>
            /// <param name="b">Number b</param>
            /// <returns>Return a / b</returns>
            double Divide(double a, double b);

            /// <summary>
            /// A non math method. Which just returns the provided input
            /// </summary>
            /// <param name="input">The input to return</param>
            /// <returns>Returns the input</returns>
            string Echo(string input);

            /// <summary>
            /// Perform an operation which throws an exception
            /// </summary>
            void ThrowTestException();

            /// <summary>
            /// Get a copy of the server IMath object
            /// </summary>
            /// <returns></returns>
            IMath GetServerObjectCopy();

            /// <summary>
            /// Access something using an <see href="http://msdn.microsoft.com/en-us/library/aa288465(v=vs.71).aspx">indexer</see>.
            /// </summary>
            /// <param name="index"></param>
            /// <returns></returns>
            double this[int index]
            {
                get;
                set;
            }

            /// <summary>
            /// Get the last result.
            /// </summary>
            double LastResult
            {
                get;
                set;
            }

            /// <summary>
            /// Event that pushes a message to clients
            /// </summary>
            event EventHandler<MathEventArgs> EchoEvent;

            /// <summary>
            /// Method echos a string after a given timeout, using RPC events
            /// </summary>
            /// <param name="timeout">The time to wait before sending back the echo in ms</param>
            /// <param name="toEcho">The string to echo</param>
            void TriggerEchoEventAfterDelay(int timeout, string toEcho);
        }

        /// <summary>
        /// Event args to demonstrate the use of events in RPC
        /// </summary>
        [ProtoContract]
        public class MathEventArgs : EventArgs
        {            
            /// <summary>
            /// A string representing the message to be sent
            /// </summary>
            [ProtoMember(1)]            
            public string EchoValue { get; private set; }

            private MathEventArgs() : base() { }

            /// <summary>
            /// Creates a new MathEventArgs object with a provided message
            /// </summary>
            /// <param name="toEcho">The message to echo</param>
            public MathEventArgs(string toEcho)
                : base()
            {
                EchoValue = toEcho;
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

                private EventHandler<MathEventArgs> echoEvent;
                private object locker = new object();
                public event EventHandler<MathEventArgs> EchoEvent
                {
                    add
                    {
                        lock (locker)
                        {
                            echoEvent += value;
                        }
                    }
                    remove
                    {
                        lock (locker)
                        {
                            echoEvent -= value;
                        }
                    }
                }

                public void TriggerEchoEventAfterDelay(int timeout, string toEcho)
                {
                    Task.Factory.StartNew(() =>
                        {
                            Thread.Sleep(timeout);
                            Console.WriteLine("Triggering echo after delay of {0}.", timeout);
                            echoEvent(this, new MathEventArgs(toEcho));
                        });
                }
            }

            private ConnectionType connectionTypeToUse;

            /// <summary>
            /// Create an instance of ServerExampleInstance
            /// </summary>
            public ServerExampleInstance()
            {
            }

            /// <summary>
            /// Run the example
            /// </summary>
            public void Run()
            {
                //Select whether to use TCP or UDP
                SelectConnectionType();

                //Setup the RPC server side
                SetupRPCUsage();

                //Print out something at the server end to show that we are listening
                Console.WriteLine("Listening for connections on:");
                foreach (System.Net.IPEndPoint localEndPoint in Connection.ExistingLocalListenEndPoints(connectionTypeToUse)) 
                    Console.WriteLine("{0}:{1}", localEndPoint.Address, localEndPoint.Port);

                Console.WriteLine("\nPress 'any' key to quit.");
                Console.ReadKey(true);

                //When we are done we must close down comms
                //This will clear all server side RPC object, delegates, handlers etc
                //As an alternative we could also use
                //RemoteProcedureCalls.Server.RemovePublicRPCObject(object instanceName);
                //RemoteProcedureCalls.Server.RemovePrivateRPCObjectType<T, I>();
                RemoteProcedureCalls.Server.ShutdownAllRPC();
                NetworkComms.Shutdown();
            }

            private void SelectConnectionType()
            {
                Console.WriteLine("Please select a connection type:\n1 - TCP\n2 - UDP\n");

                int selectedType;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedType);
                    if (parseSucces && selectedType <= 2) break;
                    Console.WriteLine("Invalid connection type choice. Please try again.");
                }

                if (selectedType == 1)
                {
                    Console.WriteLine(" ... selected TCP.\n");
                    connectionTypeToUse = ConnectionType.TCP;
                }
                else if (selectedType == 2)
                {
                    Console.WriteLine(" ... selected UDP.\n");
                    connectionTypeToUse = ConnectionType.UDP;
                }
                else
                    throw new Exception("Unable to determine selected connection type.");
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
                        RemoteProcedureCalls.Server.RegisterTypeForPrivateRemoteCall<MathClass, IMath>(10000);
                        Console.WriteLine("\nA type of {0} has been successfully registered for RPC usage.\n",typeof(IMath));
                        break;
                    case 2:
                        Console.WriteLine("\nYou selected a single named, public, object instance.");
                        Console.Write("Please enter an identifying name for the public instance: ");
                        instanceName = Console.ReadLine();
                        RemoteProcedureCalls.Server.RegisterInstanceForPublicRemoteCall<MathClass, IMath>(new MathClass(), instanceName);
                        Console.WriteLine("\nServer object instance has been successfully created.\n");
                        break;
                }

                //Start listening for incoming connections
                //We want to select a random port on all available adaptors so provide 
                //an IPEndPoint using IPAddress.Any and port 0.
                Connection.StartListening(connectionTypeToUse, new IPEndPoint(IPAddress.Any, 0));
            }
        }

        /// <summary>
        ///  We are going to isolate the server and client example to demonstrate that the client never has to see 
        ///  the implementation used for IMath
        /// </summary>
        public class ClientExampleInstance : IRPCExampleInstance
        {
            private ConnectionType connectionTypeToUse;

            /// <summary>
            /// Create an instance of ClientExampleInstance
            /// </summary>
            public ClientExampleInstance()
            {

            }

            /// <summary>
            /// Run the example
            /// </summary>
            public void Run()
            {
                //Expecting user to enter ip address as 192.168.0.1:4000
                ConnectionInfo connectionInfo = ExampleHelper.GetServerDetails();

                SelectConnectionType();

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
                    Connection connection = null;

                    if (connectionTypeToUse == ConnectionType.TCP)
                        connection = TCPConnection.GetConnection(connectionInfo);
                    else
                        connection = UDPConnection.GetConnection(connectionInfo, UDPOptions.None);

                    //Get the remote object
                    IMath remoteObject = SelectRemoteObject(connection, out instanceId);
                    //Add a handler to the object's event to demonstrate remote triggering of events
                    remoteObject.EchoEvent += (sender, args) =>
                        {
                            Console.WriteLine("Echo event received saying {0}", args.EchoValue);
                        };

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

            private void SelectConnectionType()
            {
                Console.WriteLine("\nPlease select a connection type:\n1 - TCP\n2 - UDP\n");

                int selectedType;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedType);
                    if (parseSucces && selectedType <= 2) break;
                    Console.WriteLine("Invalid connection type choice. Please try again.");
                }

                if (selectedType == 1)
                {
                    Console.WriteLine(" ... selected TCP.\n");
                    connectionTypeToUse = ConnectionType.TCP;
                }
                else if (selectedType == 2)
                {
                    Console.WriteLine(" ... selected UDP.\n");
                    connectionTypeToUse = ConnectionType.UDP;
                }
                else
                    throw new Exception("Unable to determine selected connection type.");
            }

            /// <summary>
            /// Allows the user to select a remote object based on the different available access methods
            /// </summary>
            ///<param name="connection">The connection over which to perform remote procedure calls</param>
            /// <param name="instanceId">The instanceId of the linked object</param>
            /// <returns>The remote RPC object</returns>
            private IMath SelectRemoteObject(Connection connection, out string instanceId)
            {
                //We have three main different usage cases for the RPC functionality provided by NetworkCommsDotNet
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
                        return RemoteProcedureCalls.Client.CreateProxyToPrivateInstance<IMath>(connection, instanceName, out instanceId);
                    case 2:
                        Console.WriteLine("\nYou selected to access an existing, named, public server object instance.");
                        Console.Write("Please enter the name of the server object: ");
                        instanceName = Console.ReadLine();
                        return RemoteProcedureCalls.Client.CreateProxyToPublicNamedInstance<IMath>(connection, instanceName, out instanceId);
                    case 3:
                        Console.WriteLine("\nYou selected to access an existing, private, client object instance using an instanceId.");
                        Console.Write("Please enter the object instanceId: ");
                        instanceId = Console.ReadLine();
                        return RemoteProcedureCalls.Client.CreateProxyToIdInstance<IMath>(connection, instanceId);
                }

                throw new Exception("It should be impossible to get here.");
            }

            private static object DoMath(IMath remoteObject)
            {
                Console.WriteLine("\nWhat operation would you like to perform?\nMultiply - 1\nAdd - 2\nSubtract - 3\nDivide - 4\nEcho - 5\nGet Server Instance of MathClass - 6\nThrow Exception Remotely - 7\n" + 
                    "Get last result using property - 8\nGet last result multiplied by an int using an indexer - 9\nTrigger an echo event after a delay - 0\n");

                int selectedOperation;
                while (true)
                {
                    bool parseSucces = int.TryParse(Console.ReadKey(true).KeyChar.ToString(), out selectedOperation);
                    if (parseSucces && selectedOperation <= 9 && selectedOperation >= 0) break;
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
                    case 0:
                        int delay;
                        string echoVal;
                        Console.WriteLine("You selected to trigger an echo event after a delay");
                        Console.WriteLine("Please enter the number of milliseconds to delay");
                        if (!int.TryParse(Console.ReadLine(), out delay)) break;
                        Console.WriteLine("Please enter string to echo");
                        echoVal = Console.ReadLine();
                        remoteObject.TriggerEchoEventAfterDelay(delay, echoVal);
                        return "Will echo " + echoVal + " in " + delay + "ms\n";                    
                }

                throw new Exception("How have you managed to get execution to this point? Maybe you entered something that was BAD");
            }
        }
    }
}
