//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DistributedFileSystem;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using NetworkCommsDotNet;

namespace DebugTests
{
    /// <summary>
    /// Used for testing the issues with writing to fileStreams in a high performance way in MONO
    /// </summary>
    public static class ThreadPoolTest
    {
        /// <summary>
        /// Run the example
        /// </summary>
        public static void RunExample()
        {
            CommsThreadPool threadPool = new CommsThreadPool(1, Environment.ProcessorCount, Environment.ProcessorCount * 30, new TimeSpan(0, 0, 10));
            Random rand = new Random();

            bool generate = true;

            while (true)
            {
                if (generate)
                {
                    int jobsToAdd = (int)(600 * rand.NextDouble());
                    for (int i = 0; i < jobsToAdd; i++)
                    {
                        threadPool.EnqueueItem(QueueItemPriority.Normal, Worker, rand.NextDouble());
                        Thread.Sleep((int)(15 * rand.NextDouble()));
                    }
                }

                for (int i = 0; i < 40; i++)
                {
                    Console.Clear();
                    Console.WriteLine(threadPool.ToString());
                    Thread.Sleep((int)(500 * rand.NextDouble()));
                }
            }
            
            Console.WriteLine("\nTest completed. Press any key to continue.");
            threadPool.BeginShutdown();
            Console.ReadKey(true);
            threadPool.EndShutdown();
        }

        static void Worker(object state)
        {
            double randomNumber = (double)state;
            if (randomNumber < 0.2)
            {
                Thread.SpinWait(1000 + (int)(randomNumber * 1000000000));
            }
            else
            {
                AutoResetEvent testWait = new AutoResetEvent(false);
                testWait.WaitOne(10 + (int)((randomNumber/0.2) * 1000));
            }
        }
    }
}
