using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DPSBase;
using System.Threading;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Describes options for sending and receiving data such as serialisation method, compression, encryption etc
    /// </summary>
    public class SendReceiveOptions : ICloneable
    {
        private DataSerializer _dataSerializer;
        private List<DataProcessor> _dataProcessors;

        /// <summary>
        /// Gets the <see cref="DPSBase.DataSerializer"/> that should be used when sending information
        /// </summary>
        public DataSerializer DataSerializer { get { return _dataSerializer; } protected set { _dataSerializer = value; } }

        /// <summary>
        /// Gets the <see cref="DPSBase.DataProcessor"/>s that should be used when sending information. <see cref="DPSBase.DataProcessor"/>s are applied in index order
        /// </summary>
        public List<DataProcessor> DataProcessors 
        {
            get { return _dataProcessors; }
            protected set
            {
                if (value.Count > 7)
                    throw new ArgumentException("Only 7 data Processors are supported");

                //validate the list to make sure all the data processors are the same
                if (value.Distinct().SequenceEqual(value))
                    _dataProcessors = value;
                else
                    throw new ArgumentException("Same data processor cannot be applied twice");
            }
        }

        private Dictionary<string, string> options;

        /// <summary>
        /// Gets the options that should be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DataProcessor"/>s on object serialization and deserialization
        /// </summary>
        public Dictionary<string, string> Options
        {
            get
            {
                return options;
            }
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class with a specified <see cref="DPSBase.DataSerializer"/>, set of <see cref="DPSBase.DataProcessor"/>s and and other options
        /// </summary>
        /// <param name="serializer">The <see cref="DPSBase.DataSerializer"/> to use</param>
        /// <param name="dataProcessors">The set of <see cref="DPSBase.DataProcessor"/>s to use.  The order in the list determines the order the <see cref="DPSBase.DataProcessor"/>s will be applied</param>
        /// <param name="options">Allows additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DATAProcessor"/>s</param>
        public SendReceiveOptions(DataSerializer serializer, List<DataProcessor> dataProcessors, Dictionary<string, string> options)
        {            
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
        /// <param name="options">Allows additional options to be passed to the <see cref="DPSBase.DataSerializer"/> and <see cref="DPSBase.DATAProcessor"/>s</param>
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
        /// <remarks>Two <see cref="SendReceiveOptions"/> instances will be compatible if they use the same <see cref="DPSBase.DataSerializer"/> and the same set of <see cref="DPSBase.DATAProcessor"/>s</remarks>
        public bool OptionsCompatable(SendReceiveOptions options)
        {
            return options.DataProcessors.SequenceEqual(DataProcessors) && options.DataSerializer == DataSerializer;                    
        }

        #region ICloneable Members

        /// <inheritdoc />
        public object Clone()
        {
            return new SendReceiveOptions(DataSerializer, DataProcessors, Options);
        }

        #endregion
    }

    /// <inheritdoc />
    /// <typeparam name="DS">The type of <see cref="DPSBase.DataSerializer"/> to use</typeparam>
    public class SendRecieveOptions<DS> : SendReceiveOptions 
        where DS : DataSerializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SendReceiveOptions"/> class. The <see cref="DPSBase.DataSerializer"/> is passed as a generic parameter and no <see cref="DPSBase.DataProcessor"/>s are used.  
        /// Further options can be passed to the <see cref="DPSBase.DataSerializer"/> as an argument which may be null
        /// </summary>
        /// <param name="options"></param>
        public SendRecieveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
        }
    }

    public class SendRecieveOptions<DS, DP1> : SendReceiveOptions 
        where DS : DataSerializer 
        where DP1 : DataProcessor
    {
        public SendRecieveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() { DPSManager.GetDataProcessor<DP1>() };
        }
    }

    public class SendRecieveOptions<DS, DP1, DP2> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
    {
        public SendRecieveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>() };
        }
    }

    public class SendRecieveOptions<DS, DP1, DP2, DP3> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
    {
        public SendRecieveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(), 
                DPSManager.GetDataProcessor<DP2>(), 
                DPSManager.GetDataProcessor<DP3>() };
        }
    }

    public class SendRecieveOptions<DS, DP1, DP2, DP3, DP4> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
    {
        public SendRecieveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>(),
                DPSManager.GetDataProcessor<DP3>(),
                DPSManager.GetDataProcessor<DP4>() };
        }
    }

    public class SendRecieveOptions<DS, DP1, DP2, DP3, DP4, DP5> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
        where DP5 : DataProcessor
    {
        public SendRecieveOptions(Dictionary<string, string> options)
            : base(options)
        {
            DataSerializer = DPSManager.GetDataSerializer<DS>();
            DataProcessors = new List<DataProcessor>() {
                DPSManager.GetDataProcessor<DP1>(),
                DPSManager.GetDataProcessor<DP2>(),
                DPSManager.GetDataProcessor<DP3>(),
                DPSManager.GetDataProcessor<DP4>(),
                DPSManager.GetDataProcessor<DP5>() };
        }
    }

    public class SendRecieveOptions<DS, DP1, DP2, DP3, DP4, DP5, DP6> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
        where DP5 : DataProcessor
        where DP6 : DataProcessor
    {
        public SendRecieveOptions(Dictionary<string, string> options)
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
        }
    }

    public class SendRecieveOptions<DS, DP1, DP2, DP3, DP4, DP5, DP6, DP7> : SendReceiveOptions
        where DS : DataSerializer
        where DP1 : DataProcessor
        where DP2 : DataProcessor
        where DP3 : DataProcessor
        where DP4 : DataProcessor
        where DP5 : DataProcessor
        where DP6 : DataProcessor
        where DP7 : DataProcessor
    {
        public SendRecieveOptions(Dictionary<string, string> options)
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
        }
    }
}
