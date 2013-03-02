using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DistributedFileSystem;
using DPSBase;
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
            CommsThreadPool threadPool = new CommsThreadPool(1, Environment.ProcessorCount * 2, new TimeSpan(0, 0, 10));
            Random rand = new Random();

            while (true)
            {
                int jobsToAdd = (int)(40 * rand.NextDouble());
                for (int i = 0; i < jobsToAdd; i++)
                {
                    threadPool.EnqueueItem(QueueItemPriority.Normal, Worker, rand.NextDouble());
                    Thread.Sleep((int)(15 * rand.NextDouble()));
                }

                Console.Clear();
                Console.WriteLine("Queue Count:" + threadPool.QueueCount + ", Thread Count:" + threadPool.CurrentNumTotalThreads + ", Idle Count:" + threadPool.CurrentNumIdleThreads);

                Thread.Sleep((int)(500 * rand.NextDouble()));
            }
            
            Console.WriteLine("\nTest completed. Press any key to continue.");
            threadPool.BeginShutdown();
            Console.ReadKey(true);
            threadPool.EndShutdown();
        }

        static void Worker(object state)
        {
            double randomNumber = (double)state;
            Thread.SpinWait(1000 + (int)(randomNumber * 10000000));
        }
    }
}
