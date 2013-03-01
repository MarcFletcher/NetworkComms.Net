//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A compact thread pool used by NetworkComms.Net to run packet handlers
    /// </summary>
    public class CommsThreadPool
    {
        /// <summary>
        /// Create a new comms thread pool
        /// </summary>
        /// <param name="minThreadsCount">Minimum number of idle threads to maintain in the pool</param>
        /// <param name="maxThreadsCount">Maximum number of threads to create in the pool</param>
        /// <param name="threadIdleTimeoutClose">Timespan after which an idle thread will close</param>
        public CommsThreadPool(int minThreadsCount, int maxThreadsCount, TimeSpan threadIdleTimeoutClose)
        {
            MinThreadsCount = minThreadsCount;
            MaxThreadsCount = maxThreadsCount;
            ThreadIdleTimeoutClose = threadIdleTimeoutClose;
        }

        /// <summary>
        /// A sync object to make things thread safe
        /// </summary>
        object SyncRoot = new object();

        /// <summary>
        /// Dictionary of threads, index is ThreadId
        /// </summary>
        Dictionary<int, Thread> threadDict = new Dictionary<int,Thread>();

        /// <summary>
        /// Dictionary of thread last active times, index is ThreadId
        /// </summary>
        Dictionary<int, DateTime> threadLastActiveDict = new Dictionary<int, DateTime>();

        /// <summary>
        /// Priority queue used to order call backs 
        /// </summary>
        PriorityQueue<WaitCallBackWrapper> jobQueue = new PriorityQueue<WaitCallBackWrapper>();

        /// <summary>
        /// Set to true to ensure correct shutdown of worker threads.
        /// </summary>
        bool shutdown = false;

        /// <summary>
        /// A quick lookup of the number of current idle threads
        /// </summary>
        int idleThreads = 0;
        
        /// <summary>
        /// The timespan after which an idle thread will close
        /// </summary>
        TimeSpan ThreadIdleTimeoutClose { get; set; }
        
        /// <summary>
        /// A thread signal used to trigger an idle thread
        /// </summary>
        AutoResetEvent queueItemAddedSignal = new AutoResetEvent(false);

        /// <summary>
        /// The maximum number of threads to create in the pool
        /// </summary>
        public int MaxThreadsCount {get; private set;}

        /// <summary>
        /// The minimum number of idle threads to maintain in the pool
        /// </summary>
        public int MinThreadsCount {get; private set;}

        /// <summary>
        /// The total number of threads currently in the thread pool
        /// </summary>
        public int CurrentNumThreads
        {
            get { lock(SyncRoot) return threadDict.Count; }
        }
        
        /// <summary>
        /// The total number of items currently waiting to be collected by a thread
        /// </summary>
        public int QueueCount
        {
            get { return jobQueue.Count; }
        }

        /// <summary>
        /// Prevent any additional threads from starting. Returns immediately.
        /// </summary>
        public void BeginShutdown()
        {
            lock(SyncRoot)
                shutdown = true;
        }

        /// <summary>
        /// Prevent any additional threads from starting and return once all existing workers have completed.
        /// </summary>
        /// <param name="threadShutdownTimeoutMS"></param>
        public void EndShutdown(int threadShutdownTimeoutMS = 1000)
        {
            List<Thread> allWorkerThreads = new List<Thread>();
            lock(SyncRoot)
            {
                foreach(Thread thread in threadDict.Values)
                    allWorkerThreads.Add(thread);
            }

            //Wait for all threads to finish
            foreach (Thread thread in allWorkerThreads)
            {
                try
                {
                    if (!thread.Join(threadShutdownTimeoutMS))
                        thread.Abort();
                }
                catch (Exception ex)
                {
                    NetworkComms.LogError(ex, "ManagedThreadPoolShutdownError");
                }
            }

            lock (SyncRoot)
            {
                jobQueue.Clear();
                shutdown = false;
            }
        }

        /// <summary>
        ///  Enqueue a callback to the thread pool.
        /// </summary>
        /// <param name="priority">The priority with which to enqueue the provided callback</param>
        /// <param name="callback">The callback to execute</param>
        /// <param name="state">The state parameter to pass to the callback when executed</param>
        public void EnqueueItem(QueueItemPriority priority, WaitCallback callback, object state)
        {
            lock (SyncRoot)
            {
                jobQueue.TryAdd(new KeyValuePair<QueueItemPriority, WaitCallBackWrapper>(priority, new  WaitCallBackWrapper(callback,state)));

                if (!shutdown && idleThreads == 0 && threadDict.Count < MaxThreadsCount)
                {
                    //Launch a new thread
                    Thread newThread = new Thread(ThreadWorker);
                    newThread.Name = "ManagedThreadPool_" + newThread.ManagedThreadId;

                    threadDict.Add(newThread.ManagedThreadId, newThread);
                    threadLastActiveDict.Add(newThread.ManagedThreadId, DateTime.Now);
                    newThread.Start(newThread.ManagedThreadId);
                }

                queueItemAddedSignal.Set();
            }
        }

        /// <summary>
        /// The worker object for the thread pool
        /// </summary>
        /// <param name="state"></param>
        private void ThreadWorker(object state)
        {
            int threadId = (int)state;
            bool treadIdle = false;

            do
            {
                //While there are jobs in the queue process the jobs
                while (true)
                {
                    KeyValuePair<QueueItemPriority, WaitCallBackWrapper> packetQueueItem;
                    lock (SyncRoot)
                    {
                        if (shutdown || !jobQueue.TryTake(out packetQueueItem))
                        {
                            if (!treadIdle)
                            {
                                treadIdle = true;
                                idleThreads++;
                            }
                            break;
                        }
                        else
                        {
                            if (treadIdle && idleThreads > 0)
                                idleThreads--;
                        }
                    }

                    //Perform the waitcallBack
                    try
                    {
                        packetQueueItem.Value.WaitCallBack(packetQueueItem.Value.State);
                    }
                    catch (Exception ex)
                    {
                        NetworkComms.LogError(ex, "ManagedThreadPoolCallBackError", "An unhandled exception was caught while processing a callback. Make sure to catch errors in callbacks to prevent this error file being produced.");
                    }

                    threadLastActiveDict[threadId] = DateTime.Now;
                }

                if (shutdown)
                    break;

                //As soon as the queue is empty we wait until perhaps close time
                if (!queueItemAddedSignal.WaitOne(100))
                {
                    //While we are waiting we check to see if we need to close
                    if (DateTime.Now - threadLastActiveDict[threadId] > ThreadIdleTimeoutClose)
                    {
                        lock (SyncRoot)
                        {
                            if (threadDict.Count > MinThreadsCount)
                            {
                                if (treadIdle && idleThreads > 0)
                                    idleThreads--;

                                threadLastActiveDict.Remove(threadId);
                                threadDict.Remove(threadId);
                                return;
                            }
                        }
                    }
                }
            } while (!shutdown);
        }
    }

    /// <summary>
    /// A private wrapper used by CommsThreadPool
    /// </summary>
    class WaitCallBackWrapper
    {
        public WaitCallback WaitCallBack { get; private set; }
        public object State { get; private set; }

        public WaitCallBackWrapper(WaitCallback waitCallBack, object state)
        {
            this.WaitCallBack = waitCallBack;
            this.State = state;
        }
    }
}
