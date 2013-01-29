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
using System.Text;
using System.Threading;
using System.Collections;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Custom queue which contains features to add and remove items using a basic priority model.
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    public class PriorityQueue<TValue>
    {
        //Each internal queue in the array represents a priority level.  
        //We keep the priority associated with each item so that when eventually returned the priority can be easily included
        private Queue<KeyValuePair<int, TValue>>[] internalQueues = null;

        //The number of queues we store internally. 
        private int numDistinctPriorities = 0;

        //The total number of items currently in all queues
        private int totalNumberQueuedItems = 0;

        /// <summary>
        /// Create a new instance of the priority queue.
        /// </summary>
        /// <param name="numDistinctPriorities">The total number of priority levels to allow.</param>
        public PriorityQueue(int numDistinctPriorities)
        {
            this.numDistinctPriorities = numDistinctPriorities;
            internalQueues = new Queue<KeyValuePair<int, TValue>>[numDistinctPriorities];
            for (int i = 0; i < numDistinctPriorities; i++)
                internalQueues[i] = new Queue<KeyValuePair<int, TValue>>();
        }

        /// <summary>
        /// Try adding an item to the priority queue.
        /// </summary>
        /// <param name="item">Key is priority, lower number is lower priority, and value is TValue</param>
        /// <returns>True if an item was succesfully added to the queue</returns>
        public bool TryAdd(KeyValuePair<int, TValue> item)
        {
            if (item.Key < 0 || item.Key > numDistinctPriorities - 1)
                throw new ArgumentOutOfRangeException("The provided item contains a priority value which is outside the allowed range.");

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
        /// <returns>True if an item was succesfully removed from the queue</returns>
        public bool TryTake(out KeyValuePair<int, TValue> item)
        {
            // Loop through the queues in priority order. Higher priority first
            for (int i = numDistinctPriorities - 1; i >= 0; i--)
            {
                // Lock the internal data so that the Dequeue 
                // operation and the updating of m_count are atomic. 
                lock (internalQueues)
                {
                    if (internalQueues[i].Count > 0)
                    {
                        item = internalQueues[i].Dequeue();
                        Interlocked.Decrement(ref totalNumberQueuedItems);
                        return true;
                    }
                    else
                        continue;
                }
            }

            // If we get here, we found nothing, return defaults
            item = new KeyValuePair<int, TValue>(0, default(TValue));
            return false;
        }

        /// <summary>
        /// Try removing an item from the priority queue which has a priority of atleast that provided.
        /// </summary>
        /// <param name="minimumPriority">The miniumum priority to consider</param>
        /// <param name="item">Key is priority, lower number is lower priority, and value is TValue</param>
        /// <returns>True if an item was succesfully removed from the queue</returns>
        public bool TryTake(int minimumPriority, out KeyValuePair<int, TValue> item)
        {
            // Loop through the queues in priority order. Higher priority first
            for (int i = numDistinctPriorities - 1; i >= minimumPriority; i--)
            {
                // Lock the internal data so that the Dequeue 
                // operation and the updating of m_count are atomic.                 
                lock (internalQueues)
                {
                    if (internalQueues[i].Count > 0)
                    {
                        item = internalQueues[i].Dequeue();
                        Interlocked.Decrement(ref totalNumberQueuedItems);
                        return true;
                    }
                    else
                        continue;
                }
            }

            // If we get here, we found nothing, return defaults
            item = new KeyValuePair<int, TValue>(0, default(TValue));
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
        /// Copies queued items into the provided destination array. Highest priority items first descending until destination is full or there are no remaining items.
        /// </summary>
        /// <param name="destination">The destination array</param>
        /// <param name="destStartingIndex">The position within destination to start copying to</param>
        public void CopyTo(KeyValuePair<int, TValue>[] destination, int destStartingIndex)
        {
            if (destination == null) throw new ArgumentNullException();
            if (destStartingIndex < 0) throw new ArgumentOutOfRangeException();

            int remaining = destination.Length;
            KeyValuePair<int, TValue>[] temp = this.ToArray();
            for (int i = destStartingIndex; i < destination.Length && i < temp.Length; i++)
                destination[i] = temp[i];
        }

        /// <summary>
        /// Returns all queued items as a 1D array. Highest priority items first descending.
        /// </summary>
        /// <returns></returns>
        public KeyValuePair<int, TValue>[] ToArray()
        {
            KeyValuePair<int, TValue>[] result;

            lock (internalQueues)
            {
                result = new KeyValuePair<int, TValue>[this.Count];
                int index = 0;
                for (int i = numDistinctPriorities - 1; i >= 0; i--)
                {
                    if (internalQueues[i].Count > 0)
                    {
                        internalQueues[i].CopyTo(result, index);
                        index += internalQueues[i].Count;
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Returns an Enumerator for all items, highest priority first descending
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<int, TValue>> GetEnumerator()
        {
            lock (internalQueues)
            {
                for (int i = numDistinctPriorities - 1; i >= 0; i--)
                {
                    foreach (var item in internalQueues[i])
                        yield return item;
                }
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
            get { throw new Exception("All access to PriorityQueue is thread safe so calling SyncRoot() is unncessary."); }
        }

    }
}
