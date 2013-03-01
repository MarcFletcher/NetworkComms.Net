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
    public static class MonoFileStreamTest
    {
        static int MBPerBlock = 2;
        static int NumBlocks = 100;
        static int numberOfStreams = 3;
        static int numberOfThreads = 40;

        static ThreadSafeStream[] threadSafeStreams;
        static Queue<int>[] blocksWritesOrder;
        static object queueLocker = new object();

        static ManualResetEvent finishEvent = new ManualResetEvent(false);
        static int completedCount = 0;

        public static void RunExample()
        {
            Console.WriteLine("Please enter the number of blocks:");
            NumBlocks = int.Parse(Console.ReadLine());
            Console.WriteLine(" ... entered {0} blocks.\n",NumBlocks);

            Console.WriteLine("Please enter the MB per block");
            MBPerBlock = int.Parse(Console.ReadLine());
            Console.WriteLine(" ... entered {0} MB.\n", MBPerBlock);

            blocksWritesOrder = new Queue<int>[numberOfStreams];
            threadSafeStreams = new ThreadSafeStream[numberOfStreams];

            List<int> blockWriteOrderTemp = new List<int>();
            for (int j = 0; j < NumBlocks; j++)
                blockWriteOrderTemp.Add(j);

            for (int i = 0; i < numberOfStreams; i++)
            {
                FileStream newStream = new FileStream("testFile_" + i + ".DFSItemData", FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                newStream.SetLength(MBPerBlock * 1024 * 1024 * NumBlocks);
                newStream.Flush();

                threadSafeStreams[i] = new ThreadSafeStream(newStream);

                //Each stream gets a random queue which is the chunk write order
                List<int> streamBlockWriteOrder = ShuffleList.Shuffle<int>(blockWriteOrderTemp).ToList();
                for (int j = 0; j < streamBlockWriteOrder.Count; j++)
                {
                    if (blocksWritesOrder[i] == null)
                        blocksWritesOrder[i] = new Queue<int>();

                    blocksWritesOrder[i].Enqueue(streamBlockWriteOrder[j]);
                }
            }

            CommsThreadPool threadpool = new CommsThreadPool(1, 40, new TimeSpan(0, 0, 30));

            //Start N threads
            for (int i = 0; i < numberOfThreads; i++)
            {
                int innerIndex = i;
                Console.WriteLine("Added i={3} | Q:{0}, T:{1}, I:{2}", threadpool.QueueCount, threadpool.CurrentNumTotalThreads, threadpool.CurrentNumIdleThreads, i);
                threadpool.EnqueueItem(QueueItemPriority.Normal, new WaitCallback(Worker), innerIndex);
            }

            Thread.Sleep(5000);

            for (int i = numberOfThreads; i < numberOfThreads + numberOfThreads; i++)
            {
                int innerIndex = i;
                Console.WriteLine("Added i={3} | Q:{0}, T:{1}, I:{2}", threadpool.QueueCount, threadpool.CurrentNumTotalThreads, threadpool.CurrentNumIdleThreads, i);
                threadpool.EnqueueItem(QueueItemPriority.Normal, new WaitCallback(Worker), innerIndex);
            }

            Console.WriteLine("Started threads. Waiting for completion.");
            finishEvent.WaitOne();
            Console.WriteLine("\nTest completed. Press any key to continue.");
            threadpool.BeginShutdown();
            Console.ReadKey(true);
            threadpool.EndShutdown();
        }

        static void Worker(object state)
        {
            int index = (int)state;
            Random rand = new Random((int)(DateTime.Now.Ticks * index));
            List<int> streamAccessOrder = new List<int>();
            for (int i = 0; i < numberOfStreams; i++)
                streamAccessOrder.Add(i);

            try
            {
                byte[] buffer = new byte[MBPerBlock * 1024 * 1024];

                while(true)
                {
                    int streamIndex = -1, selectedBlock =-1;
                    rand.NextBytes(buffer);

                    streamAccessOrder = ShuffleList.Shuffle<int>(streamAccessOrder).ToList();

                    lock (queueLocker)
                    {
                        for (int i = 0; i < numberOfStreams; i++)
                        {
                            if (blocksWritesOrder[streamAccessOrder[i]].Count > 0)
                            {
                                streamIndex = streamAccessOrder[i];
                                selectedBlock = blocksWritesOrder[streamAccessOrder[i]].Dequeue();
                                break;
                            }

                            if (i == numberOfStreams - 1)
                                selectedBlock = -1;
                        }
                    }

                    if (selectedBlock < 0)
                        break;

                    threadSafeStreams[streamIndex].Write(buffer, selectedBlock * MBPerBlock * 1024 * 1024);
                }
            }
            catch (Exception ex)
            {
                NetworkComms.LogError(ex, "ErrorFek");
                Console.WriteLine("Fek!");
            }

            Console.WriteLine("Completed worker {0}.", index);

            if (Interlocked.Increment(ref completedCount) == numberOfThreads)
                finishEvent.Set();
        }
    }
}
