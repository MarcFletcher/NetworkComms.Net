using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ExamplesConsole
{
    /// <summary>
    /// A static class which provides implementation possibly shared across all examples
    /// </summary>
    public static class ExampleHelper
    {
        static string lastServerIP = "";
        static int lastServerPort = -1;

        public static void GetServerDetails(out string serverIP, out int serverPort)
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
                        serverIP = lastServerIP;
                        serverPort = lastServerPort;
                        break;
                    }
                    else
                    {
                        serverIP = userEnteredStr.Split(':')[0];
                        serverPort = int.Parse(userEnteredStr.Split(':')[1]);

                        lastServerIP = serverIP;
                        lastServerPort = serverPort;
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
