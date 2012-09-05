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
        public DataSerializer Serializer { get; protected set; }
        public List<DataProcessor> DataProcessors { get; protected set; }

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
            this.Serializer = serializer;
            this.DataProcessors = dataProcessors;            
            this.options = options;
        }

        public SendReceiveOptions(Dictionary<string, string> options)
        {
            this.options = options;
        }

        public bool OptionsCompatable(SendReceiveOptions options)
        {
            return options.DataProcessors.SequenceEqual(DataProcessors) && options.Serializer == Serializer;                    
        }

        public override bool Equals(object obj)
        {
            SendReceiveOptions sendRecieveOptions = obj as SendReceiveOptions;
            if (sendRecieveOptions == null) return false;
            else
            {                
                return sendRecieveOptions.DataProcessors == DataProcessors && sendRecieveOptions.Serializer == Serializer &&
                    sendRecieveOptions.Options.Keys.OrderBy(s => s).SequenceEqual(Options.Keys.OrderBy(s => s)) &&
                    sendRecieveOptions.Options.Values.OrderBy(s => s).SequenceEqual(Options.Values.OrderBy(s => s));
            }
        }

        #region ICloneable Members

        public object Clone()
        {
            return new SendReceiveOptions(Serializer, DataProcessors, Options);
        }

        #endregion
    }
}
