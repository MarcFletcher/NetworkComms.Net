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

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A simple class to do math operations in NetworkCommsDotNet. Primarly used for load detection.
    /// </summary>
    public class CommsMath
    {
        private List<double> values;
        private object locker = new object();

        /// <summary>
        /// Create a new empty instance of CommsMath
        /// </summary>
        public CommsMath()
        {
            values = new List<double>();
        }

        /// <summary>
        /// Add a new value to the internal list
        /// </summary>
        /// <param name="value">The value to add</param>
        public void AddValue(double value)
        {
            lock (locker)
            {
                if (double.IsNaN(value))
                    throw new Exception("Value is NaN");

                values.Add(value);
            }
        }

        /// <summary>
        /// Trims the list to the provided maxCount. The most recently added items are preserved.
        /// </summary>
        /// <param name="maxCount">The maximum size of the list after being trimmed</param>
        public void TrimList(int maxCount)
        {
            lock (locker)
            {
                if (values.Count > maxCount)
                {
                    int itemsToSkip = values.Count - maxCount;

                    List<double> tempList = new List<double>(values.Count - maxCount);

                    for (int i = itemsToSkip; i < values.Count; i++)
                        tempList.Add(values[i]);

                    values = tempList;
                }
            }
        }

        /// <summary>
        /// Reset the value list
        /// </summary>
        public void ClearList()
        {
            lock (locker)
            {
                values = new List<double>();
            }
        }

        /// <summary>
        /// Return the mean of the current list.
        /// </summary>
        /// <returns>The mean of all values currently in the list.</returns>
        public double CalculateMean()
        {
            lock (locker)
            {
                return CommsMath.CalculateMean(this.values);
            }
        }

        /// <summary>
        /// Return the mean of the current list.
        /// </summary>
        /// <param name="lastNValues">If less than the number of items in the value list returns the mean of the lastNValues</param>
        /// <returns>The mean of relevant values</returns>
        public double CalculateMean(int lastNValues)
        {
            lock (locker)
            {
                int itemsToSkip = 0;

                if (lastNValues < values.Count)
                    itemsToSkip = values.Count - lastNValues;

                List<double> itemsForMean = new List<double>(lastNValues);

                for (int i = itemsToSkip; i < values.Count; ++i)
                    itemsForMean.Add(values[i]);

                return CommsMath.CalculateMean(itemsForMean);
            }
        }

        /// <summary>
        /// Return the mean of the provided list of values
        /// </summary>
        /// <param name="localValues">Values for which a mean should be calculated</param>
        /// <returns>The mean of provided values</returns>
        public static double CalculateMean(List<double> localValues)
        {
            if (localValues.Count == 0)
                return 0;

            double sum = 0;
            double result;

            int countedValues = 0;
            for (int i = 0; i < localValues.Count; i++)
            {
                if (!double.IsNaN(localValues[i]))
                {
                    sum += localValues[i];
                    countedValues++;
                }
            }

            if (countedValues == 0)
                result = 0;
            else
                result = sum / countedValues;

            if (double.IsNaN(result))
                throw new Exception("Result is NaN.");

            return result;
        }
    }
}
