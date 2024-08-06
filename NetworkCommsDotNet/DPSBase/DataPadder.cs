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
using System.Text;
using NetworkCommsDotNet.Tools;

namespace NetworkCommsDotNet.DPSBase
{
    /// <summary>
    /// <see cref="DataProcessor"/> which pads data section of a packet using to a fixed size
    /// </summary>
    [DataSerializerProcessor(5)]
    public class DataPadder : DataProcessor
    {
        /// <summary>
        /// The type of data padding to use
        /// </summary>
        public enum DataPaddingType
        {
            /// <summary>
            /// Pads with all zeros (fastest)
            /// </summary>
            Zero,
            /// <summary>
            /// Pads with cryptographically secure random numbers (slower but potentially slightly more secure)
            /// </summary>
            Random,
        }

        private const string paddedSizeOptionName = "DataPadder_PADDEDSIZE";
        private const string padTypeOptionName = "DataPadder_PADTYPE";
        private const string padExceptionOptionName = "DataPadder_PADEXCEPTION";

        private DataPadder() { }

        System.Security.Cryptography.RandomNumberGenerator rand = new System.Security.Cryptography.RNGCryptoServiceProvider();

        /// <inheritdoc />
        public override void ForwardProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            if (options == null)
                throw new ArgumentException("Options dictionary was null", "options");
            else if (!options.ContainsKey(paddedSizeOptionName) || !options.ContainsKey(padTypeOptionName) || !options.ContainsKey(padExceptionOptionName))
                throw new ArgumentException("Options dictionary did not contain the necessary padding options", "options");

            int paddingSize;
            DataPaddingType padType;
            bool padException;
                        
            if (!int.TryParse(options[paddedSizeOptionName], out paddingSize) || !bool.TryParse(options[padExceptionOptionName], out padException))
                throw new ArgumentException("Options dictionary contained invalid options for DataPadder", "options");

            try { padType = (DataPaddingType)Enum.Parse(typeof(DataPaddingType), options[padTypeOptionName]); }
            catch (ArgumentException) { throw new ArgumentException("Options dictionary contained invalid options for DataPadder", "options"); }

            if (padException && inStream.Length > paddingSize - 4)
                throw new ArgumentException("Options dictionary contained invalid options for DataPadder. Not enough data padding was allowed", "options");

            paddingSize = paddingSize - 4 - (int)inStream.Length;
            if (paddingSize < 0)
                paddingSize = 0;

            byte[] padData;

            if (padType == DataPaddingType.Random)
            {
                padData = new byte[paddingSize];
                rand.GetBytes(padData);
            }
            else
                padData = new byte[paddingSize];

            StreamTools.Write(inStream, 0, inStream.Length, outStream, 8192, double.MaxValue, int.MaxValue);

            if (paddingSize != 0)
                outStream.Write(padData, 0, padData.Length);

            outStream.Write(BitConverter.GetBytes(paddingSize + 4), 0, 4);
            writtenBytes = outStream.Position;
        }

        /// <inheritdoc />
        public override void ReverseProcessDataStream(Stream inStream, Stream outStream, Dictionary<string, string> options, out long writtenBytes)
        {
            inStream.Seek(inStream.Length - 4, SeekOrigin.Begin);
            byte[] buffer = new byte[4];
            inStream.Read(buffer, 0, 4);
            inStream.Seek(0, SeekOrigin.Begin);

            //read the last 4 bytes from the input stream
            int padSize = BitConverter.ToInt32(buffer, 0);
            StreamTools.Write(inStream, 0, inStream.Length - padSize, outStream, 8192, double.MaxValue, int.MaxValue);
            writtenBytes = outStream.Length;
        }

        /// <summary>
        /// Adds the necessary options for padding 
        /// </summary>
        /// <param name="options">The Dictionary to add the options to</param>
        /// <param name="paddedSize">The size that the data section of the packet should be padded to. If throwExceptionOnNotEnoughPadding is true this must be at least the original data packet size plus four bytes</param>        
        /// <param name="paddingType">Determines whether the data is padded with zeros or random data</param>
        /// <param name="throwExceptionOnNotEnoughPadding">If true an <see cref="ArgumentException"/> is thrown if paddingSize is smaller than the original data packet size plus four bytes</param>
        public static void AddPaddingOptions(Dictionary<string, string> options, int paddedSize, DataPaddingType paddingType = DataPaddingType.Zero, bool throwExceptionOnNotEnoughPadding = true)
        {
            if (options == null) throw new ArgumentNullException("options");

            options[paddedSizeOptionName] = paddedSize.ToString();
            options[padTypeOptionName] = paddingType.ToString();
            options[padExceptionOptionName] = throwExceptionOnNotEnoughPadding.ToString();
        }
    }
}
