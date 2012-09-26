using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;

namespace ExamplesConsole
{
    /// <summary>
    /// A static class which provides implementation possibly shared across all examples
    /// </summary>
    public static class ExampleHelper
    {
        static string lastServerIP = "";
        static int lastServerPort = -1;

        /// <summary>
        /// Request user to provide server details and returns the result as a <see cref="ConnectionInfo"/> object. Performs the necessary validation and prevents code duplication across examples.
        /// </summary>
        /// <param name="connectionInfo"></param>
        public static void GetServerDetails(out ConnectionInfo connectionInfo)
        {
            if (lastServerIP != "")
                Console.WriteLine("Please enter the destination IP and port. To reuse '{0}:{1}' use r:",lastServerIP,lastServerPort);
            else
                Console.WriteLine("Please enter the destination IP address and port, e.g. '192.168.0.1:4000':");

            while (true)
            {
                try
                {
                    //Parse the provided information
                    string userEnteredStr = Console.ReadLine();

                    if (userEnteredStr.Trim() == "r" && lastServerIP != "")
                    {
                        connectionInfo = new ConnectionInfo(lastServerIP, lastServerPort);
                        break;
                    }
                    else
                    {
                        string serverIP = userEnteredStr.Split(':')[0];
                        int serverPort = int.Parse(userEnteredStr.Split(':')[1]);

                        lastServerIP = serverIP;
                        lastServerPort = serverPort;

                        connectionInfo = new ConnectionInfo(serverIP, serverPort);
                        break;
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Unable to determine host IP address and port. Check format and try again:");
                }
            }
        }
    }
}
