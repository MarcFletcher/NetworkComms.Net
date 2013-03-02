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
        /// A sync object to make things thread safe
        /// </summary>
        object SyncRoot = new object();

        /// <summary>
        /// Dictionary of threads, index is ThreadId
        /// </summary>
        Dictionary<int, Thread> threadDict = new Dictionary<int,Thread>();

        /// <summary>
        /// Dictionary of thread worker info, index is ThreadId
        /// </summary>
        Dictionary<int, WorkerInfo> workerInfoDict = new Dictionary<int, WorkerInfo>();

        /// <summary>
        /// A quick lookup of the number of current idle threads
        /// </summary>
        int idleThreads = 0;

        /// <summary>
        /// Priority queue used to order call backs 
        /// </summary>
        PriorityQueue<WaitCallBackWrapper> jobQueue = new PriorityQueue<WaitCallBackWrapper>();

        /// <summary>
        /// Set to true to ensure correct shutdown of worker threads.
        /// </summary>
        bool shutdown = false;

        /// <summary>
        /// The timespan after which an idle thread will close
        /// </summary>
        TimeSpan ThreadIdleTimeoutClose { get; set; }

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
        public int CurrentNumTotalThreads
        {
            get { lock(SyncRoot) return threadDict.Count; }
        }

        /// <summary>
        /// The total number of idle threads currently in the thread pool
        /// </summary>
        public int CurrentNumIdleThreads
        {
            get { lock (SyncRoot) return idleThreads; }
        }
        
        /// <summary>
        /// The total number of items currently waiting to be collected by a thread
        /// </summary>
        public int QueueCount
        {
            get { return jobQueue.Count; }
        }

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
                foreach (var thread in threadDict)
                {
                    workerInfoDict[thread.Key].ThreadSignal.Set();
                    allWorkerThreads.Add(thread.Value);
                }
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
        /// <returns>Returns the managed threadId running the callback if one was available, otherwise -1</returns>
        public int EnqueueItem(QueueItemPriority priority, WaitCallback callback, object state)
        {
            int chosenThreadId = -1;

            lock (SyncRoot)
            {
                if (!shutdown && idleThreads == 0 && threadDict.Count < MaxThreadsCount)
                {
                    //Launch a new thread
                    Thread newThread = new Thread(ThreadWorker);
                    newThread.Name = "ManagedThreadPool_" + newThread.ManagedThreadId;

                    WorkerInfo info = new WorkerInfo(newThread.ManagedThreadId, new WaitCallBackWrapper(callback, state));

                    chosenThreadId = newThread.ManagedThreadId;
                    threadDict.Add(newThread.ManagedThreadId, newThread);
                    workerInfoDict.Add(newThread.ManagedThreadId, info);

                    newThread.Start(info);
                }
                else if (!shutdown && idleThreads > 0)
                {
                    jobQueue.TryAdd(new KeyValuePair<QueueItemPriority, WaitCallBackWrapper>(priority, new WaitCallBackWrapper(callback, state)));

                    int checkCount = 0;

                    foreach (var info in workerInfoDict)
                    {
                        //Trigger the first idle thread
                        checkCount++;
                        if (info.Value.ThreadIdle)
                        {
                            info.Value.ClearThreadIdle();
                            idleThreads--;

                            info.Value.ThreadSignal.Set();
                            chosenThreadId = info.Value.ThreadId;

                            break;
                        }

                        if (checkCount == workerInfoDict.Count)
                            throw new Exception("IdleThreads count is " + idleThreads + " but unable to locate thread marked as idle.");
                    }
                }
                else if (!shutdown)
                    //If there are no idle threads and we can't start any new ones we just have to enqueue the item
                    jobQueue.TryAdd(new KeyValuePair<QueueItemPriority, WaitCallBackWrapper>(priority, new WaitCallBackWrapper(callback, state)));
            }

            return chosenThreadId;
        }

        /// <summary>
        /// The worker object for the thread pool
        /// </summary>
        /// <param name="state"></param>
        private void ThreadWorker(object state)
        {
            WorkerInfo threadInfo = (WorkerInfo)state;

            do
            {
                //While there are jobs in the queue process the jobs
                while (true)
                {
                    if (threadInfo.CurrentCallBackWrapper == null)
                    {
                        KeyValuePair<QueueItemPriority, WaitCallBackWrapper> packetQueueItem;
                        lock (SyncRoot)
                        {
                            if (shutdown || !jobQueue.TryTake(out packetQueueItem))
                            {
                                //If we failed to get a job we switch to idle and wait to be triggered
                                if (!threadInfo.ThreadIdle)
                                {
                                    threadInfo.SetThreadIdle();
                                    idleThreads++;
                                }

                                break;
                            }
                            else
                            {
                                if (threadInfo.ThreadIdle && idleThreads > 0)
                                    idleThreads--;

                                threadInfo.UpdateCurrentCallBackWrapper(packetQueueItem.Value);
                                threadInfo.ClearThreadIdle();
                            }
                        }
                    }

                    //Perform the waitcallBack
                    try
                    {
                        threadInfo.CurrentCallBackWrapper.WaitCallBack(threadInfo.CurrentCallBackWrapper.State);
                    }
                    catch (Exception ex)
                    {
                        NetworkComms.LogError(ex, "ManagedThreadPoolCallBackError", "An unhandled exception was caught while processing a callback. Make sure to catch errors in callbacks to prevent this error file being produced.");
                    }

                    threadInfo.UpdateLastActiveTime();
                    threadInfo.ClearCallBackWrapper();
                }

                if (shutdown) break;

                //As soon as the queue is empty we wait until perhaps close time
                if (!threadInfo.ThreadSignal.WaitOne(250))
                {
                    //While we are waiting we check to see if we need to close
                    if (DateTime.Now - threadInfo.LastActiveTime > ThreadIdleTimeoutClose)
                    {
                        lock (SyncRoot)
                        {
                            if (threadDict.Count > MinThreadsCount)
                            {
                                if (threadInfo.ThreadIdle && idleThreads > 0)
                                    idleThreads--;

                                threadInfo.ClearThreadIdle();
                                break;
                            }
                        }
                    }
                }
            } while (!shutdown);

            if (!shutdown && DateTime.Now - threadInfo.LastActiveTime < ThreadIdleTimeoutClose)
                NetworkComms.LogError(new TimeoutException("Thread closing before correct timeout. This should be impossible."), "ManagedThreadPoolError");

            //Last thing we do is to remove the thread entry
            lock (SyncRoot)
            {
                threadDict.Remove(threadInfo.ThreadId);
                workerInfoDict.Remove(threadInfo.ThreadId);
            }
        }
    }

    class WorkerInfo
    {
        public int ThreadId { get; private set; }
        public AutoResetEvent ThreadSignal { get; private set; }
        public bool ThreadIdle { get; private set; }
        public DateTime LastActiveTime {get; private set;}
        public WaitCallBackWrapper CurrentCallBackWrapper { get; private set; }

        public WorkerInfo(int threadId, WaitCallBackWrapper initialisationCallBackWrapper)
        {
            ThreadSignal = new AutoResetEvent(false);
            ThreadIdle = false;
            ThreadId = threadId;
            LastActiveTime = DateTime.Now;
            this.CurrentCallBackWrapper = initialisationCallBackWrapper;
        }

        public void UpdateCurrentCallBackWrapper(WaitCallBackWrapper waitCallBackWrapper)
        {
            CurrentCallBackWrapper = waitCallBackWrapper;
        }

        public void UpdateLastActiveTime()
        {
            LastActiveTime = DateTime.Now;
        }

        public void ClearCallBackWrapper()
        {
            CurrentCallBackWrapper = null;
        }

        public void SetThreadIdle()
        {
            this.ThreadIdle = true;
        }

        public void ClearThreadIdle()
        {
            this.ThreadIdle = false;
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
