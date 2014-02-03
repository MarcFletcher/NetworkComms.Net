//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcomms.net/licensing/> for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// A class used for math operations in NetworkComms.Net. Primarily used for load analysis.
    /// </summary>
    public class CommsMath
    {
        private const double SMALLVALUE = 1E-10;

        private List<double> values;
        private List<double> weights;

        private object locker = new object();

        /// <summary>
        /// Create a new empty instance of CommsMath
        /// </summary>
        public CommsMath()
        {
            values = new List<double>();
            weights = new List<double>();
        }

        /// <summary>
        /// Create a new empty instance of CommsMath
        /// </summary>
        public CommsMath(List<double> initialValues, List<double> initialWeights)
        {
            values = initialValues;
            weights = initialWeights;
        }

        /// <summary>
        /// Returns the number of values in this object
        /// </summary>
        public int Count { get { lock (locker) return values.Count; } }

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
                weights.Add(1);
            }
        }

        /// <summary>
        /// Add a new value to the internal list
        /// </summary>
        /// <param name="value">The value to add</param>
        /// <param name="weight">The weight to apply to the provided value</param>
        public void AddValue(double value, double weight)
        {
            lock (locker)
            {
                if (double.IsNaN(value))
                    throw new Exception("Value is NaN");

                values.Add(value);
                weights.Add(weight);
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

                    List<double> tempValueList = new List<double>(values.Count - maxCount);
                    List<double> tempWeightList = new List<double>(values.Count - maxCount);

                    for (int i = itemsToSkip; i < values.Count; i++)
                    {
                        tempValueList.Add(values[i]);
                        tempWeightList.Add(weights[i]);
                    }

                    values = tempValueList;
                    weights = tempWeightList;
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
                weights = new List<double>();
            }
        }

        /// <summary>
        /// Return the mean of the current list.
        /// </summary>
        /// <returns>The mean of all values currently in the list.</returns>
        public double CalculateMean()
        {
            lock (locker)
                return CommsMath.CalculateMean(this.values, this.weights);
        }

        /// <summary>
        /// Return the standard deviation of the current list.
        /// </summary>
        /// <returns>The standard deviation of all values currently in the list.</returns>
        public double CalculateStdDeviation()
        {
            lock (locker)
                return CommsMath.CalculateStdDeviation(this.values, this.weights);
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
                List<double> itemWeights = new List<double>(lastNValues);

                for (int i = itemsToSkip; i < values.Count; ++i)
                {
                    itemsForMean.Add(values[i]);
                    itemWeights.Add(weights[i]);
                }

                return CommsMath.CalculateMean(itemsForMean, itemWeights);
            }
        }

        /// <summary>
        /// Return the standard deviation of the current list.
        /// </summary>
        /// <param name="lastNValues">If less than the number of items in the value list returns the mean of the lastNValues</param>
        /// <returns>The mean of relevant values</returns>
        public double CalculateStdDeviation(int lastNValues)
        {
            lock (locker)
            {
                int itemsToSkip = 0;

                if (lastNValues < values.Count)
                    itemsToSkip = values.Count - lastNValues;

                List<double> itemsForCalc = new List<double>(lastNValues);
                List<double> itemWeights = new List<double>(lastNValues);

                for (int i = itemsToSkip; i < values.Count; ++i)
                {
                    itemsForCalc.Add(values[i]);
                    itemWeights.Add(weights[i]);
                }

                return CommsMath.CalculateStdDeviation(itemsForCalc, itemWeights);
            }
        }

        /// <summary>
        /// Return the mean of the provided list of values
        /// </summary>
        /// <param name="localValues">Values for which a mean should be calculated</param>
        /// <returns>The mean of provided values</returns>
        public static double CalculateMean(List<double> localValues)
        {
            if (localValues == null) throw new ArgumentNullException("localValues", "Provided List<double> cannot be null.");

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

        /// <summary>
        /// Return the mean of the provided list of values
        /// </summary>
        /// <param name="localValues">Values for which a mean should be calculated</param>
        /// <param name="weights">The weights to apply to the corresponding values</param>
        /// <returns>The mean of provided values</returns>
        public static double CalculateMean(List<double> localValues, List<double> weights)
        {
            if (localValues == null) throw new ArgumentNullException("localValues", "Provided List<double> cannot be null.");
            if (weights == null) throw new ArgumentNullException("weights", "Provided List<double> cannot be null.");

            if (localValues.Count != weights.Count)
                throw new ArgumentException("Equal number of values and weights expected.", "localValues");

            if (localValues.Count == 0)
                return 0;

            double sum = 0;
            double result;

            double countedValues = 0;
            for (int i = 0; i < localValues.Count; i++)
            {
                if (!double.IsNaN(localValues[i]))
                {
                    sum += localValues[i] * weights[i];
                    countedValues += weights[i];
                }
            }

            if (Math.Abs(countedValues) < SMALLVALUE)
                result = 0;
            else
                result = sum / countedValues;

            if (double.IsNaN(result))
                throw new Exception("Result is NaN.");

            return result;
        }

        /// <summary>
        /// Return the standard deviation of the provided list of values
        /// </summary>
        /// <param name="localValues">Values for which a standard deviation should be calculated</param>
        /// <returns>The standard deviation of provided values</returns>
        public static double CalculateStdDeviation(List<double> localValues)
        {
            double s = 0;
            double result;

            double mean = CalculateMean(localValues);

            for (int i = 0; i <= localValues.Count - 1; i++)
                s += Math.Pow(localValues[i] - mean, 2);

            if (localValues.Count > 1)
                result = s / localValues.Count;
            else if (localValues.Count > 0)
                result = localValues[0];
            else
                throw new ArgumentException("Unable to calculate standard deviation if no values are provided.", "localValues");

            if (double.IsNaN(result))
                throw new Exception("Error");

            return Math.Sqrt(result);
        }

        /// <summary>
        /// Return the standard deviation of the provided list of values
        /// </summary>
        /// <param name="localValues">Values for which a standard deviation should be calculated</param>
        /// <param name="weights">The weights to apply to the corresponding values</param>
        /// <returns>The standard deviation of provided values</returns>
        public static double CalculateStdDeviation(List<double> localValues, List<double> weights)
        {
            if (localValues == null) throw new ArgumentNullException("localValues", "Provided List<double> cannot be null.");
            if (weights == null) throw new ArgumentNullException("weights", "Provided List<double> cannot be null.");

            if (localValues.Count != weights.Count)
                throw new Exception("Equal number of values and weights expected.");

            double s = 0, w =0;
            double result;

            double mean = CalculateMean(localValues, weights);

            for (int i = 0; i <= localValues.Count - 1; i++)
            {
                s += weights[i] * Math.Pow(localValues[i] - mean, 2);
                w += weights[i];
            }

            if (localValues.Count > 1)
                result = s / w;
            else if (localValues.Count > 0)
                result = localValues[0];
            else
                throw new ArgumentException("Unable to calculate standard deviation if no values are provided.", "localValues");

            if (double.IsNaN(result))
                throw new ArithmeticException("Error");

            return Math.Sqrt(result);
        }
    }
}
