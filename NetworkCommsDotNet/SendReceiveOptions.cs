using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DPSBase;
using System.Threading;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Provides flags for send and receive options such as serialisation, compression, encryption etc
    /// </summary>
    public class SendReceiveOptions : ICloneable
    {
        private DataSerializer _dataSerializer;
        private List<DataProcessor> _dataProcessors;

        public DataSerializer DataSerializer { get { return _dataSerializer; } protected set { _dataSerializer = value; } }
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
        public Dictionary<string, string> Options
        {
            get
            {
                return options.ToDictionary(pair => String.Copy(pair.Key), pair => String.Copy(pair.Value));
            }
        }

        public string this[string key]
        {
            get
            {
                if (options.ContainsKey(key))
                    return options[key];
                else
                    return null;
            }
            set
            {
                if (options.ContainsKey(key))
                    options[key] = value;
                else
                    options.Add(key, value);
            }
        }

        public SendReceiveOptions(DataSerializer serializer, List<DataProcessor> dataProcessors, Dictionary<string, string> options)
        {            
            this.DataSerializer = serializer;
            this.DataProcessors = dataProcessors;            
            this.options = options;
        }
        
        public SendReceiveOptions(Dictionary<string, string> options)
        {
            this.options = options;
        }

        public bool OptionsCompatable(SendReceiveOptions options)
        {
            return options.DataProcessors.SequenceEqual(DataProcessors) && options.DataSerializer == DataSerializer;                    
        }

        public override bool Equals(object obj)
        {
            SendReceiveOptions sendRecieveOptions = obj as SendReceiveOptions;
            if (sendRecieveOptions == null) return false;
            else
            {                
                return sendRecieveOptions.DataProcessors == DataProcessors && sendRecieveOptions.DataSerializer == DataSerializer &&
                    sendRecieveOptions.Options.Keys.OrderBy(s => s).SequenceEqual(Options.Keys.OrderBy(s => s)) &&
                    sendRecieveOptions.Options.Values.OrderBy(s => s).SequenceEqual(Options.Values.OrderBy(s => s));
            }
        }

        #region ICloneable Members

        public object Clone()
        {
            return new SendReceiveOptions(DataSerializer, DataProcessors, Options);
        }

        #endregion
    }

    public class SendRecieveOptions<DS> : SendReceiveOptions 
        where DS : DataSerializer
    {
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
