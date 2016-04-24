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
using System.Text;
using System.Threading;
using System.Collections;

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Queue which contains features to add and remove items using a simple priority model.
    /// </summary>
    /// <typeparam name="TValue">The type of this queue</typeparam>
    public class PriorityQueue<TValue>
    {
        /// <summary>
        /// Each internal queue in the array represents a priority level.  
        /// We keep the priority associated with each item so that when eventually returned the 
        /// priority can be easily included.
        /// </summary>
        private Dictionary<QueueItemPriority,Queue<KeyValuePair<QueueItemPriority, TValue>>> internalQueues = null;

        /// <summary>
        /// The list of priorities used to handle incoming packets.
        /// </summary>
        private QueueItemPriority[] QueueItemPriorityVals;

        /// <summary>
        /// The number of queues we store internally. 
        /// </summary>
        private int numDistinctPriorities = 0;

        /// <summary>
        /// The total number of items currently in all queues
        /// </summary>
        private int totalNumberQueuedItems = 0;

        /// <summary>
        /// Create a new instance of the priority queue.
        /// </summary>
        public PriorityQueue()
        {
            var vals = Enum.GetValues(typeof(QueueItemPriority)) as int[];
            Array.Sort(vals);

            this.numDistinctPriorities = vals.Length;

            QueueItemPriorityVals = new QueueItemPriority[numDistinctPriorities];
            
            internalQueues = new Dictionary<QueueItemPriority,Queue<KeyValuePair<QueueItemPriority,TValue>>>(numDistinctPriorities);
            for (int i = 0; i < numDistinctPriorities; i++)
            {
                internalQueues[(QueueItemPriority)vals[i]] = new Queue<KeyValuePair<QueueItemPriority, TValue>>();
                QueueItemPriorityVals[i] = (QueueItemPriority)vals[i];
            }
        }

        /// <summary>
        /// Try adding an item to the priority queue.
        /// </summary>
        /// <param name="item">Key is priority, lower number is lower priority, and value is TValue</param>
        /// <returns>True if an item was successfully added to the queue</returns>
        public bool TryAdd(KeyValuePair<QueueItemPriority, TValue> item)
        {            
            lock (internalQueues)
            {
                internalQueues[item.Key].Enqueue(item);
                Interlocked.Increment(ref totalNumberQueuedItems);
            }

            return true;
        }

        /// <summary>
        /// Try removing an item from the priority queue
        /// </summary>
        /// <param name="item">Key is priority, lower number is lower priority, and value is TValue</param>
        /// <returns>True if an item was successfully removed from the queue</returns>
        public bool TryTake(out KeyValuePair<QueueItemPriority, TValue> item)
        {
            // Loop through the queues in priority order. Higher priority first
            for (int i = numDistinctPriorities - 1; i >= 0; i--)
            {
                // Lock the internal data so that the Dequeue 
                // operation and the updating of m_count are atomic. 
                lock (internalQueues)
                {
                    if (internalQueues[QueueItemPriorityVals[i]].Count > 0)
                    {
                        item = internalQueues[QueueItemPriorityVals[i]].Dequeue();
                        Interlocked.Decrement(ref totalNumberQueuedItems);
                        return true;
                    }
                    else
                        continue;
                }
            }

            // If we get here, we found nothing, return defaults
            item = new KeyValuePair<QueueItemPriority, TValue>((QueueItemPriority)0, default(TValue));
            return false;
        }

        /// <summary>
        /// Try removing an item from the priority queue which has a priority of at least that provided.
        /// </summary>
        /// <param name="minimumPriority">The minimum priority to consider</param>
        /// <param name="item">Key is priority, lower number is lower priority, and value is TValue</param>
        /// <returns>True if an item was successfully removed from the queue</returns>
        public bool TryTake(QueueItemPriority minimumPriority, out KeyValuePair<QueueItemPriority, TValue> item)
        {
            // Loop through the queues in priority order. Higher priority first
            for (int i = numDistinctPriorities - 1; i >= (int)minimumPriority; i--)
            {
                // Lock the internal data so that the Dequeue 
                // operation and the updating of m_count are atomic.                 
                lock (internalQueues)
                {
                    if (internalQueues[QueueItemPriorityVals[i]].Count > 0)
                    {
                        item = internalQueues[QueueItemPriorityVals[i]].Dequeue();
                        Interlocked.Decrement(ref totalNumberQueuedItems);
                        return true;
                    }
                    else
                        continue;
                }
            }

            // If we get here, we found nothing, return defaults
            item = new KeyValuePair<QueueItemPriority, TValue>((QueueItemPriority)0, default(TValue));
            return false;
        }

        /// <summary>
        /// The total number of items currently queued.
        /// </summary>
        public int Count
        {
            get { return totalNumberQueuedItems; }
        }

        /// <summary>
        /// Copies queued items into the provided destination array. Highest priority items first descending until 
        /// destination is full or there are no remaining items.
        /// </summary>
        /// <param name="destination">The destination array</param>
        /// <param name="destStartingIndex">The position within destination to start copying to</param>
        public void CopyTo(KeyValuePair<QueueItemPriority, TValue>[] destination, int destStartingIndex)
        {
            if (destination == null) throw new ArgumentNullException("destination", "Provided KeyValuePair<QueueItemPriority, TValue>[] cannot be null.");
            if (destStartingIndex < 0) throw new ArgumentOutOfRangeException("destStartingIndex", "Provided int must be positive.");

            int remaining = destination.Length;
            KeyValuePair<QueueItemPriority, TValue>[] temp = this.ToArray();
            for (int i = destStartingIndex; i < destination.Length && i < temp.Length; i++)
                destination[i] = temp[i];
        }

        /// <summary>
        /// Returns all queued items as a 1D array. Highest priority items first descending.
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<QueueItemPriority, TValue>[] ToArray()
        {
            KeyValuePair<QueueItemPriority, TValue>[] result;

            lock (internalQueues)
            {
                result = new KeyValuePair<QueueItemPriority, TValue>[this.Count];
                int index = 0;
                for (int i = numDistinctPriorities - 1; i >= 0; i--)
                {
                    if (internalQueues[QueueItemPriorityVals[i]].Count > 0)
                    {
                        internalQueues[QueueItemPriorityVals[i]].CopyTo(result, index);
                        index += internalQueues[QueueItemPriorityVals[i]].Count;
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Gets a value indicating whether access to the PriorityQueue is synchronized (thread safe). Always returns true.
        /// </summary>
        public bool IsSynchronized
        {
            get { return true; }
        }

        /// <summary>
        /// Gets an object that can be used to synchronize access to the PriorityQueue. Throws an exception as all access is explicitly thread safe.
        /// </summary>
        public object SyncRoot
        {
            get { throw new Exception("All access to PriorityQueue is thread safe so calling SyncRoot() is unnecessary."); }
        }

        /// <summary>
        /// Clear the content of all queues
        /// </summary>
        public void Clear()
        {
            lock (internalQueues)
            {
                for (int i = 0; i < numDistinctPriorities; i++)
                    internalQueues[QueueItemPriorityVals[i]].Clear();
            }
        }
    }
}
