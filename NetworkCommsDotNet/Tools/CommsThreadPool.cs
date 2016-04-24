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
using System.ComponentModel;
using System.Text;
using System.Threading;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
using System.Threading.Tasks;
#endif

namespace NetworkCommsDotNet.Tools
{
#if NETFX_CORE
    /// <summary>
    /// A compact thread pool used by NetworkComms.Net to run packet handlers
    /// </summary>
    public class CommsThreadPool
    {
        /// <summary>
        /// A sync object to make things thread safe
        /// </summary>
        object SyncRoot = new object();

        Dictionary<int, Task> scheduledTasks = new Dictionary<int, Task>();

        Dictionary<int, CancellationTokenSource> taskCancellationTokens = new Dictionary<int, CancellationTokenSource>();

        /// <summary>
        /// Priority queue used to order call backs 
        /// </summary>
        PriorityQueue<WaitCallBackWrapper> jobQueue = new PriorityQueue<WaitCallBackWrapper>();

        /// <summary>
        /// Set to true to ensure correct shutdown of worker threads.
        /// </summary>
        bool shutdown = false;
        
        /// <summary>
        /// The total number of items currently waiting to be collected by a thread
        /// </summary>
        public int QueueCount
        {
            get { return jobQueue.Count; }
        }

        /// <summary>
        /// Create a new NetworkComms.Net thread pool
        /// </summary>
        /// <param name="minThreadsCount">Minimum number of idle threads to maintain in the pool</param>
        /// <param name="maxActiveThreadsCount">The maximum number of active (i.e. not waiting for IO) threads</param>
        /// <param name="maxTotalThreadsCount">Maximum number of threads to create in the pool</param>
        /// <param name="threadIdleTimeoutClose">Timespan after which an idle thread will close</param>
        public CommsThreadPool()
        {            
        }

        /// <summary>
        /// Prevent any additional threads from starting. Returns immediately.
        /// </summary>
        public void BeginShutdown()
        {
            lock (SyncRoot)
                shutdown = true;
        }

        /// <summary>
        /// Prevent any additional threads from starting and return once all existing workers have completed.
        /// </summary>
        /// <param name="threadShutdownTimeoutMS"></param>
        public void EndShutdown(int threadShutdownTimeoutMS = 1000)
        {
#if NETFX_CORE     
            foreach (var pair in scheduledTasks)
            {
                if (!pair.Value.Wait(threadShutdownTimeoutMS))
                {
                    taskCancellationTokens[pair.Key].Cancel();
                    if (!pair.Value.Wait(threadShutdownTimeoutMS))
                        LogTools.LogException(new CommsSetupShutdownException("Managed threadpool shutdown error"), "ManagedThreadPoolShutdownError");
                }
            }
#else
            List<Thread> allWorkerThreads = new List<Thread>();
            lock (SyncRoot)
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
                    LogTools.LogException(ex, "ManagedThreadPoolShutdownError");
                }
            }
#endif

            lock (SyncRoot)
            {
                jobQueue.Clear();
                taskCancellationTokens.Clear();
                scheduledTasks.Clear();
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
        public void EnqueueItem(QueueItemPriority priority, WaitCallback callback, object state)
        {
            lock (SyncRoot)
                jobQueue.TryAdd(new KeyValuePair<QueueItemPriority, WaitCallBackWrapper>(priority, new WaitCallBackWrapper(callback, state)));
            
            Task t = null;
            CancellationTokenSource cSource = new CancellationTokenSource();     
       
            t = new Task(() =>
                {
                    KeyValuePair<QueueItemPriority, WaitCallBackWrapper> toRun;

                    lock (SyncRoot)
                    {
                        if (!jobQueue.TryTake(out toRun) || shutdown)
                            return;
                    }

                    toRun.Value.WaitCallBack(toRun.Value.State);

                    lock (SyncRoot)
                    {
                        scheduledTasks.Remove(t.Id);
                        taskCancellationTokens.Remove(t.Id);
                    }
                }, cSource.Token);

            lock (SyncRoot)
            {
                scheduledTasks.Add(t.Id, t);
                taskCancellationTokens.Add(t.Id, cSource);
                t.Start();
            }
        }
        
        /// <summary>
        /// Provides a brief string summarisation the state of the thread pool
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            lock (SyncRoot)
            {
                return "Queue Length:" + QueueCount.ToString();
            }
        }
    }
#else    
    /// <summary>
    /// A compact priority based thread pool used by NetworkComms.Net to run packet handlers
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
        Dictionary<int, Thread> threadDict = new Dictionary<int, Thread>();

        /// <summary>
        /// Dictionary of thread worker info, index is ThreadId
        /// </summary>
        Dictionary<int, WorkerInfo> workerInfoDict = new Dictionary<int, WorkerInfo>();

        /// <summary>
        /// The minimum timespan between thread wait sleep join updates
        /// </summary>
        TimeSpan ThreadWaitSleepJoinCountUpdateInterval = new TimeSpan(0, 0, 0, 0, 250);

        /// <summary>
        /// A quick lookup of the number of current threads which are idle and require jobs
        /// </summary>
        int requireJobThreadsCount = 0;

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
        public int MaxTotalThreadsCount { get; private set; }

        /// <summary>
        /// The maximum number of active threads in the pool. This can be less than MaxTotalThreadsCount, taking account of waiting threads.
        /// </summary>
        public int MaxActiveThreadsCount { get; private set; }

        /// <summary>
        /// The minimum number of idle threads to maintain in the pool
        /// </summary>
        public int MinThreadsCount { get; private set; }

        /// <summary>
        /// The most recent count of pool threads which are waiting for IO
        /// </summary>
        public int CurrentNumWaitSleepJoinThreadsCache { get; private set; }

        /// <summary>
        /// The dateTime associated with the most recent count of pool threads which are waiting for IO
        /// </summary>
        public DateTime LastThreadWaitSleepJoinCountCacheUpdate { get; private set; }

        /// <summary>
        /// The total number of threads currently in the thread pool
        /// </summary>
        public int CurrentNumTotalThreads
        {
            get { lock (SyncRoot) return threadDict.Count; }
        }

        /// <summary>
        /// The total number of idle threads currently in the thread pool
        /// </summary>
        public int CurrentNumIdleThreads
        {
            get { lock (SyncRoot) return requireJobThreadsCount; }
        }

        /// <summary>
        /// The total number of items currently waiting to be collected by a thread
        /// </summary>
        public int QueueCount
        {
            get { return jobQueue.Count; }
        }

        /// <summary>
        /// Create a new NetworkComms.Net thread pool
        /// </summary>
        /// <param name="minThreadsCount">Minimum number of idle threads to maintain in the pool</param>
        /// <param name="maxActiveThreadsCount">The maximum number of active (i.e. not waiting for IO) threads</param>
        /// <param name="maxTotalThreadsCount">Maximum number of threads to create in the pool</param>
        /// <param name="threadIdleTimeoutClose">Timespan after which an idle thread will close</param>
        public CommsThreadPool(int minThreadsCount, int maxActiveThreadsCount, int maxTotalThreadsCount, TimeSpan threadIdleTimeoutClose)
        {
            MinThreadsCount = minThreadsCount;
            MaxTotalThreadsCount = maxTotalThreadsCount;
            MaxActiveThreadsCount = maxActiveThreadsCount;
            ThreadIdleTimeoutClose = threadIdleTimeoutClose;
        }

        /// <summary>
        /// Prevent any additional threads from starting. Returns immediately.
        /// </summary>
        public void BeginShutdown()
        {
            lock (SyncRoot)
                shutdown = true;
        }

        /// <summary>
        /// Prevent any additional threads from starting and return once all existing workers have completed.
        /// </summary>
        /// <param name="threadShutdownTimeoutMS"></param>
        public void EndShutdown(int threadShutdownTimeoutMS = 1000)
        {
            List<Thread> allWorkerThreads = new List<Thread>();
            lock (SyncRoot)
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
                    LogTools.LogException(ex, "ManagedThreadPoolShutdownError");
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
                UpdateThreadWaitSleepJoinCountCache();

                int numInJobActiveThreadsCount = Math.Max(0, threadDict.Count - CurrentNumWaitSleepJoinThreadsCache - requireJobThreadsCount);

                //int numActiveThreads = Math.Max(0,threadDict.Count - CurrentNumWaitSleepJoinThreadsCache);
                if (!shutdown && requireJobThreadsCount == 0 && numInJobActiveThreadsCount < MaxActiveThreadsCount && threadDict.Count < MaxTotalThreadsCount)
                {
                    //Launch a new thread
                    Thread newThread = new Thread(ThreadWorker);
                    newThread.Name = "ManagedThreadPool_" + newThread.ManagedThreadId.ToString();
                    newThread.IsBackground = true;

                    WorkerInfo info = new WorkerInfo(newThread.ManagedThreadId, new WaitCallBackWrapper(callback, state));

                    chosenThreadId = newThread.ManagedThreadId;
                    threadDict.Add(newThread.ManagedThreadId, newThread);
                    workerInfoDict.Add(newThread.ManagedThreadId, info);

                    newThread.Start(info);
                }
                else if (!shutdown && requireJobThreadsCount > 0 && numInJobActiveThreadsCount < MaxActiveThreadsCount)
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
                            requireJobThreadsCount--;

                            info.Value.ThreadSignal.Set();
                            chosenThreadId = info.Value.ThreadId;

                            break;
                        }

                        if (checkCount == workerInfoDict.Count)
                            throw new Exception("IdleThreads count is " + requireJobThreadsCount.ToString() + " but unable to locate thread marked as idle.");
                    }
                }
                else if (!shutdown)
                {
                    //If there are no idle threads and we can't start any new ones we just have to enqueue the item
                    jobQueue.TryAdd(new KeyValuePair<QueueItemPriority, WaitCallBackWrapper>(priority, new WaitCallBackWrapper(callback, state)));
                }
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
                            UpdateThreadWaitSleepJoinCountCache();
                            int numInJobActiveThreadsCount = Math.Max(0, threadDict.Count - CurrentNumWaitSleepJoinThreadsCache - requireJobThreadsCount);

                            if (shutdown || threadDict.Count > MaxTotalThreadsCount) //If we have too many active threads
                            {
                                //If shutdown was true then we may need to set thread to idle
                                if (threadInfo.ThreadIdle && requireJobThreadsCount > 0)
                                    requireJobThreadsCount--;

                                threadInfo.ClearThreadIdle();

                                threadDict.Remove(threadInfo.ThreadId);
                                workerInfoDict.Remove(threadInfo.ThreadId);

                                UpdateThreadWaitSleepJoinCountCache();
                                return;
                            }
                            else if (numInJobActiveThreadsCount > MaxActiveThreadsCount) //If we have too many active threads
                            {
                                //We wont close here to prevent thread creation/destruction thrashing.
                                //We will instead act as if there is no work and wait to potentially be timed out
                                if (!threadInfo.ThreadIdle)
                                {
                                    threadInfo.SetThreadIdle();
                                    requireJobThreadsCount++;
                                }

                                break;
                            }
                            else
                            {
                                //Try to get a job
                                if (!jobQueue.TryTake(out packetQueueItem)) //We fail to get a new job
                                {
                                    //If we failed to get a job we switch to idle and wait to be triggered
                                    if (!threadInfo.ThreadIdle)
                                    {
                                        threadInfo.SetThreadIdle();
                                        requireJobThreadsCount++;
                                    }

                                    break;
                                }
                                else
                                {
                                    if (threadInfo.ThreadIdle && requireJobThreadsCount > 0)
                                        requireJobThreadsCount--;

                                    threadInfo.UpdateCurrentCallBackWrapper(packetQueueItem.Value);
                                    threadInfo.ClearThreadIdle();
                                }
                            }
                        }
                    }

                    //Perform the waitcallBack
                    try
                    {
                        threadInfo.SetInsideCallBack();
                        threadInfo.CurrentCallBackWrapper.WaitCallBack(threadInfo.CurrentCallBackWrapper.State);
                    }
                    catch (Exception ex)
                    {
                        LogTools.LogException(ex, "ManagedThreadPoolCallBackError", "An unhandled exception was caught while processing a callback. Make sure to catch errors in callbacks to prevent this error file being produced.");
                    }
                    finally
                    {
                        threadInfo.ClearInsideCallBack();
                    }

                    threadInfo.UpdateLastActiveTime();
                    threadInfo.ClearCallBackWrapper();
                }

                //As soon as the queue is empty we wait until perhaps close time
#if NET2
                if (!threadInfo.ThreadSignal.WaitOne(250, false))
#else
                if (!threadInfo.ThreadSignal.WaitOne(250))
#endif
                {
                    //While we are waiting we check to see if we need to close
                    if (DateTime.Now - threadInfo.LastActiveTime > ThreadIdleTimeoutClose)
                    {
                        lock (SyncRoot)
                        {
                            //We have timed out but we don't go below the minimum
                            if (threadDict.Count > MinThreadsCount)
                            {
                                if (threadInfo.ThreadIdle && requireJobThreadsCount > 0)
                                    requireJobThreadsCount--;

                                threadInfo.ClearThreadIdle();

                                threadDict.Remove(threadInfo.ThreadId);
                                workerInfoDict.Remove(threadInfo.ThreadId);

                                UpdateThreadWaitSleepJoinCountCache();
                                return;
                            }
                        }
                    }
                }

                //We only leave via one of our possible breaks
            } while (true);
        }

        /// <summary>
        /// Returns the total number of threads in the pool which are waiting for IO
        /// </summary>
        private void UpdateThreadWaitSleepJoinCountCache()
        {
            lock (SyncRoot)
            {
                if (DateTime.Now - LastThreadWaitSleepJoinCountCacheUpdate > ThreadWaitSleepJoinCountUpdateInterval)
                {
                    int returnValue = 0;

                    foreach (var thread in threadDict)
                    {
                        if (workerInfoDict[thread.Key].InsideCallBack && thread.Value.ThreadState == ThreadState.WaitSleepJoin)
                            returnValue++;
                    }

                    CurrentNumWaitSleepJoinThreadsCache = returnValue;
                    LastThreadWaitSleepJoinCountCacheUpdate = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// Provides a brief string summarisation the state of the thread pool
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            lock (SyncRoot)
            {
                UpdateThreadWaitSleepJoinCountCache();
                return "TotalTs:" + CurrentNumTotalThreads.ToString() + ", IdleTs:" + CurrentNumIdleThreads.ToString() + ", SleepTs:" + CurrentNumWaitSleepJoinThreadsCache.ToString() + ", Q:" + QueueCount.ToString();
            }
        }
    }

    /// <summary>
    /// A private wrapper used by CommsThreadPool
    /// </summary>
    class WorkerInfo
    {
        public int ThreadId { get; private set; }
        public AutoResetEvent ThreadSignal { get; private set; }
        public bool ThreadIdle { get; private set; }
        public DateTime LastActiveTime { get; private set; }
        public WaitCallBackWrapper CurrentCallBackWrapper { get; private set; }
        public bool InsideCallBack { get; private set; }

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

        /// <summary>
        /// Set InsideCallBack to true
        /// </summary>
        public void SetInsideCallBack()
        {
            InsideCallBack = true;
        }

        /// <summary>
        /// Set InsideCallBack to false
        /// </summary>
        public void ClearInsideCallBack()
        {
            InsideCallBack = false;
        }

        /// <summary>
        /// Set threadIdle to true
        /// </summary>
        public void SetThreadIdle()
        {
            this.ThreadIdle = true;
        }

        /// <summary>
        /// Set threadIdle to false
        /// </summary>
        public void ClearThreadIdle()
        {
            this.ThreadIdle = false;
        }
    }
#endif
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
