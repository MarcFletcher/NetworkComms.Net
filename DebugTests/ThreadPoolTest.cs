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
