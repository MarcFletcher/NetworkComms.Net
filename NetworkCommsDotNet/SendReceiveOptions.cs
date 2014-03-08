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
//  Non-GPL versions of this software can also be purchased. 
//  Please see <http://www.networkcomms.net> for details.

using System;
using System.Collections.Generic;
using System.Text;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
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

        /// <summary>
        /// If true any packets sent with this <see cref="SendReceiveOptions"/> will be nested which can be used to obscure the actual
        /// packet type.
        /// </summary>
        public bool UseNestedPacket
        {
            get { return Options.ContainsKey("UseNestedPacketType"); }
            set
            {
                if (value) Options["UseNestedPacketType"] = "";
                else Options.Remove("UseNestedPacketType");
            }
        }

        /// <summary>
        ///  Incoming packets are handled using a flexible QueueItemPriority (Default - QueueItemPriority.Normal). Reserved internal 
        ///  packet types and packets marked with QueueItemPriority.Highest are not enqueued but handled in real time by the thread 
        ///  handling the incoming data. You are free to specify the queue item priority for packet handlers using this 
        ///  SendReceiveOptions by setting this value as desired. CAUTION: Only use QueueItemPriority.Highest sparingly.
        /// </summary>
        public QueueItemPriority ReceiveHandlePriority
        {
            get
            {
                if (Options.ContainsKey("ReceiveHandlePriority"))
                    return (QueueItemPriority)Enum.Parse(typeof(QueueItemPriority), "ReceiveHandlePriority");
                else
                    return QueueItemPriority.Normal;
            }
            set 
            { 
                Options["ReceiveHandlePriority"] = Enum.GetName(typeof(QueueItemPriority), value); 
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
                throw new ArgumentNullException("serializer", "The serializer argument when creating a sendReceiveOptions object should never be null.");

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
            DataSerializer = null; //This will set the NullSerialiser as a default
            DataProcessors = null; //This will set an empty options dictionary

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
            if (options == null) throw new ArgumentNullException("options", "Provided SendReceiveOptions cannot be null.");

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
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    public class SendReceiveOptions<T_DS> : SendReceiveOptions 
        where T_DS : DataSerializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> is passed as a generic parameter and no <see cref="DPSBase.DataProcessor"/>s are used. To provide additional options see other overrides. 
        /// </summary>
        public SendReceiveOptions()
            : base(null)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = null; //Note that this will cause data processors to be set to an empty list

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> is passed as a generic parameter and no <see cref="DPSBase.DataProcessor"/>s are used.  
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/></param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = null; //Note that this will cause data processors to be set to an empty list

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="T_DP1">The type of <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<T_DS, T_DP1> : SendReceiveOptions 
        where T_DS : DataSerializer 
        where T_DP1 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and a single <see cref="DPSBase.DataProcessor"/> while will be used are passed as generic parameters. To provide additional options see other overrides. 
        /// </summary>
        public SendReceiveOptions()
            : base(null)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() { DPSManager.GetDataProcessor<T_DP1>() };

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and a single <see cref="DPSBase.DataProcessor"/> while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/> as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/></param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() { DPSManager.GetDataProcessor<T_DP1>() };

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="T_DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<T_DS, T_DP1, T_DP2> : SendReceiveOptions
        where T_DS : DataSerializer
        where T_DP1 : DataProcessor
        where T_DP2 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 2 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<T_DP1>(),
                DPSManager.GetDataProcessor<T_DP2>() };

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="T_DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<T_DS, T_DP1, T_DP2, T_DP3> : SendReceiveOptions
        where T_DS : DataSerializer
        where T_DP1 : DataProcessor
        where T_DP2 : DataProcessor
        where T_DP3 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 3 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<T_DP1>(), 
                DPSManager.GetDataProcessor<T_DP2>(), 
                DPSManager.GetDataProcessor<T_DP3>() };

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="T_DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<T_DS, T_DP1, T_DP2, T_DP3, T_DP4> : SendReceiveOptions
        where T_DS : DataSerializer
        where T_DP1 : DataProcessor
        where T_DP2 : DataProcessor
        where T_DP3 : DataProcessor
        where T_DP4 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 4 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<T_DP1>(),
                DPSManager.GetDataProcessor<T_DP2>(),
                DPSManager.GetDataProcessor<T_DP3>(),
                DPSManager.GetDataProcessor<T_DP4>() };

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="T_DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP5">The type of the fifth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<T_DS, T_DP1, T_DP2, T_DP3, T_DP4, T_DP5> : SendReceiveOptions
        where T_DS : DataSerializer
        where T_DP1 : DataProcessor
        where T_DP2 : DataProcessor
        where T_DP3 : DataProcessor
        where T_DP4 : DataProcessor
        where T_DP5 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 5 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<T_DP1>(),
                DPSManager.GetDataProcessor<T_DP2>(),
                DPSManager.GetDataProcessor<T_DP3>(),
                DPSManager.GetDataProcessor<T_DP4>(),
                DPSManager.GetDataProcessor<T_DP5>() };

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="T_DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP5">The type of the fifth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP6">The type of the sixth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<T_DS, T_DP1, T_DP2, T_DP3, T_DP4, T_DP5, T_DP6> : SendReceiveOptions
        where T_DS : DataSerializer
        where T_DP1 : DataProcessor
        where T_DP2 : DataProcessor
        where T_DP3 : DataProcessor
        where T_DP4 : DataProcessor
        where T_DP5 : DataProcessor
        where T_DP6 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 6 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<T_DP1>(),
                DPSManager.GetDataProcessor<T_DP2>(),
                DPSManager.GetDataProcessor<T_DP3>(),
                DPSManager.GetDataProcessor<T_DP4>(),
                DPSManager.GetDataProcessor<T_DP5>(),
                DPSManager.GetDataProcessor<T_DP6>() };

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }

    /// <inheritdoc />
    /// <typeparam name="T_DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    /// <typeparam name="T_DP1">The type of the first <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP2">The type of the second <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP3">The type of the third <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP4">The type of the fourth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP5">The type of the fifth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP6">The type of the sixth <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    /// <typeparam name="T_DP7">The type of the seventh <see cref="DPSBase.DataProcessor"/> to use</typeparam>
    public class SendReceiveOptions<T_DS, T_DP1, T_DP2, T_DP3, T_DP4, T_DP5, T_DP6, T_DP7> : SendReceiveOptions
        where T_DS : DataSerializer
        where T_DP1 : DataProcessor
        where T_DP2 : DataProcessor
        where T_DP3 : DataProcessor
        where T_DP4 : DataProcessor
        where T_DP5 : DataProcessor
        where T_DP6 : DataProcessor
        where T_DP7 : DataProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> and 7 <see cref="DPSBase.DataProcessor"/>s while will be used are passed as generic parameters
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s as an argument which may be null
        /// </summary>
        /// <param name="options">Additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s</param>
        public SendReceiveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<T_DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<T_DP1>(),
                DPSManager.GetDataProcessor<T_DP2>(),
                DPSManager.GetDataProcessor<T_DP3>(),
                DPSManager.GetDataProcessor<T_DP4>(),
                DPSManager.GetDataProcessor<T_DP5>(),
                DPSManager.GetDataProcessor<T_DP6>(),
                DPSManager.GetDataProcessor<T_DP7>()};

            if (DataSerializer == null)
                throw new InvalidOperationException("Attempted to use null DataSerializer. If this exception is thrown DPSManager.GetDataSerializer<DS>() has failed.");
        }
    }
}
