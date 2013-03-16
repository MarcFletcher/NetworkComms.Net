//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
using DPSBase;
using System.Threading;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Contains options and flags for sending and receiving data such as serialisation method, data processors, encryption etc.
    /// Several static constructors are provided to help create SendReceiveOptions in the most common formats.
    /// </summary>
    public class SendReceiveOptions
    {
        /// <summary>
        /// If true any packets sent with this <see cref="SendReceiveOptions"/> will be forced to trigger a receive confirmation.
        /// </summary>
        public bool ReceiveConfirmationRequired
        {
            get { return Options.ContainsKey("ReceiveConfirmationRequired"); }
            set {
                if (value) Options["ReceiveConfirmationRequired"] = "";
                else Options.Remove("ReceiveConfirmationRequired");
            }
        }

        /// <summary>
        /// If true any packets sent with this <see cref="SendReceiveOptions"/> will include the packet creation time in the header.
        /// </summary>
        public bool IncludePacketConstructionTime
        {
            get { return Options.ContainsKey("IncludePacketConstructionTime"); }
            set
            {
                if (value) Options["IncludePacketConstructionTime"] = "";
                else Options.Remove("IncludePacketConstructionTime");
            }
        }

        private DataSerializer _dataSerializer;
        private List<DataProcessor> _dataProcessors;

        /// <summary>
        /// Gets the <see cref="DPSBase.DataSerializer"/> that should be used when sending information
        /// </summary>
        public DataSerializer DataSerializer
        {
            get { return _dataSerializer; }
            protected set
            {
                if (value == null)
                    _dataSerializer = DPSManager.GetDataSerializer<NullSerializer>();
                else
                    _dataSerializer = value;
            }
        }

        /// <summary>
        /// Gets the <see cref="DPSBase.DataProcessor"/>s that should be used when sending information. <see cref="DPSBase.DataProcessor"/>s are applied in index order
        /// </summary>
        public List<DataProcessor> DataProcessors 
        {
            get { return _dataProcessors; }
            protected set
            {
                if (value == null)
                {
                    _dataProcessors = new List<DataProcessor>();
                    return;
                }

                if (value.Count > 7)
                    throw new ArgumentException("Only 7 data Processors are supported");

                //validate the list to make sure all the data processors are the same
                List<DataProcessor> distinctProcessors = new List<DataProcessor>();
                foreach (var processor in value)
                {
                    if(distinctProcessors.Contains(processor))
                        throw new ArgumentException("Same data processor cannot be applied twice");
                    
                    distinctProcessors.Add(processor);
                }

                _dataProcessors = value;
            }
        }

        private Dictionary<string, string> options;

        /// <summary>
        /// Gets the options that should be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s on object serialization and deserialization
        /// </summary>
        public Dictionary<string, string> Options
        {
            get { return options; }
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class with a specified <see cref="DPSBase.DataSerializer"/>, set of <see cref="DPSBase.DataProcessor"/>s and and other options
        /// </summary>
        /// <param name="serializer">The <see cref="DPSBase.DataSerializer"/> to use</param>
        /// <param name="dataProcessors">The set of <see cref="DPSBase.DataProcessor"/>s to use.  The order in the list determines the order the <see cref="DPSBase.DataProcessor"/>s will be applied</param>
        /// <param name="options">Allows additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(DataSerializer serializer, List<DataProcessor> dataProcessors, Dictionary<string, string> options)
        {
            if (serializer == null)
                throw new ArgumentNullException("The serializer argument when creating a sendReceiveOptions object should never be null.");

            this.DataSerializer = serializer;
            this.DataProcessors = dataProcessors;

            if (options != null)
                this.options = options;
            else
                this.options = new Dictionary<string, string>();
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class providing only options for the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s.  This constructor should only be used when adding packet handlers for incoming connections
        /// </summary>
        /// <param name="options">Allows additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
        {
            if (options != null)
                this.options = options;
            else
                this.options = new Dictionary<string, string>();
        }

        /// <summary>
        /// Determines whether the supplied <see cref="SendReceiveOptions"/> is compatible, from a serialization point of view, with this instance
        /// </summary>
        /// <param name="options">The <see cref="SendReceiveOptions"/> to compare against</param>
        /// <returns>True if the options are compatible, false otherwise</returns>
        /// <remarks>Two <see cref="SendReceiveOptions"/> instances will be compatible if they use the same <see cref="DPSBase.DataSerializer"/> and the same set of <see cref="DPSBase.DataProcessor"/>s</remarks>
        public bool OptionsCompatible(SendReceiveOptions options)
        {
            bool equal = options.DataSerializer == DataSerializer;

            for (int i = 0; i < options.DataProcessors.Count; i++)
                equal &= options.DataProcessors[i] == DataProcessors[i];

            return equal;
        }

        /// <summary>
        /// Create a deep clone of this <see cref="SendReceiveOptions"/> object. 
        /// </summary>
        /// <returns>The cloned object</returns>
        public object Clone()
        {
            return new SendReceiveOptions(DataSerializer, new List<DataProcessor>(DataProcessors), new Dictionary<string,string>(Options));
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    public class SendReceiveOptions<DS> : SendReceiveOptions 
        where DS : DataSerializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> is passed as a generic parameter and no <see cref="DPSBase.DataProcessor"/>s are used. To provide additional options see other overrides. 
        /// </summary>
        public SendReceiveOptions()
            : base(null)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = null; //Note that this will cause data processors to be set to an empty list

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> is passed as a generic parameter and no <see cref="DPSBase.DataProcessor"/>s are used.  
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/></param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = null; //Note that this will cause data processors to be set to an empty list

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="DP1">The type of <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<DS, DP1> : SendReceiveOptions 
        where DS : DataSerializer 
        where DP1 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and a single <see cref="DPSBase.DataProcessor"/> while will be used are passed as generic parameters. To provide additional options see other overrides. 
        /// </summary>
        public SendReceiveOptions()
            : base(null)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() { DPSManager.GetDataProcessor<DP1>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and a single <see cref="DPSBase.DataProcessor"/> while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/> as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/></param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() { DPSManager.GetDataProcessor<DP1>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<DS, DP1, DP2> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 2 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<DS, DP1, DP2, DP3> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 3 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(), 
                DPSManager.GetDataProcessor<DP2>(), 
                DPSManager.GetDataProcessor<DP3>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<DS, DP1, DP2, DP3, DP4> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 4 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>(),
                DPSManager.GetDataProcessor<DP3>(),
                DPSManager.GetDataProcessor<DP4>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP5">The type of the fifth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<DS, DP1, DP2, DP3, DP4, DP5> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
        where DP5 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 5 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>(),
                DPSManager.GetDataProcessor<DP3>(),
                DPSManager.GetDataProcessor<DP4>(),
                DPSManager.GetDataProcessor<DP5>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP5">The type of the fifth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP6">The type of the sixth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<DS, DP1, DP2, DP3, DP4, DP5, DP6> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
        where DP5 : DataProcessor
        where DP6 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 6 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>(),
                DPSManager.GetDataProcessor<DP3>(),
                DPSManager.GetDataProcessor<DP4>(),
                DPSManager.GetDataProcessor<DP5>(),
                DPSManager.GetDataProcessor<DP6>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP5">The type of the fifth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP6">The type of the sixth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="DP7">The type of the seventh <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<DS, DP1, DP2, DP3, DP4, DP5, DP6, DP7> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
        where DP5 : DataProcessor
        where DP6 : DataProcessor
        where DP7 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 7 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>(),
                DPSManager.GetDataProcessor<DP3>(),
                DPSManager.GetDataProcessor<DP4>(),
                DPSManager.GetDataProcessor<DP5>(),
                DPSManager.GetDataProcessor<DP6>(),
                DPSManager.GetDataProcessor<DP7>() };

            if (DataSerializer == null)
                throw new ArgumentNullException("Attempted to set DataSerializer to null. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }
}
