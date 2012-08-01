//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// A simple class to do math operations in networkComms.net
    /// </summary>
    class CommsMath
    {
        private List<double> values;
        private object locker = new object();

        public CommsMath()
        {
            values = new List<double>();
        }

        /// <summary>
        /// Add a new value to the list
        /// </summary>
        /// <param name="value"></param>
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
        /// Trims the list to the provided maxCount. The most recent added items are preserved.
        /// </summary>
        /// <param name="maxCount"></param>
        public void TrimList(int maxCount)
        {
            lock (locker)
            {
                if (values.Count > maxCount)
                    values = values.Skip(values.Count - maxCount).ToList();
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
        /// <returns></returns>
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
        /// <returns></returns>
        public double CalculateMean(int lastNValues)
        {
            lock (locker)
            {
                int itemsToSkip = 0;

                if (lastNValues < this.values.Count)
                    itemsToSkip = this.values.Count - lastNValues;

                return CommsMath.CalculateMean(this.values.Skip(itemsToSkip).ToList());
            }
        }

        /// <summary>
        /// Return the mean of the provided list of values
        /// </summary>
        /// <param name="localValues"></param>
        /// <returns></returns>
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
