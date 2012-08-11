using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SerializerBase;
using System.Threading;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Provides flags for send and receive options such as serialisation, compression, encryption etc
    /// </summary>
    public class SendReceiveOptions
    {
        public ISerialize Serializer { get; protected set; }
        public ICompress Compressor { get; protected set; }

        public bool ReceiveConfirmationRequired { get; protected set; }

        //The priority with which this send recieve is dealt with
        public ThreadPriority ReceiveHandlePriority { get; protected set; }

        //IEncrypt?

        public SendReceiveOptions(bool receiveConfirmationRequired, ISerialize serializer, ICompress compressor, ThreadPriority receiveHandlePriority)
        {
            this.ReceiveConfirmationRequired = receiveConfirmationRequired;
            this.Serializer = serializer;
            this.Compressor = compressor;
            this.ReceiveHandlePriority = receiveHandlePriority;
        }

        public override bool Equals(object obj)
        {
            SendReceiveOptions options = obj as SendReceiveOptions;
            if (options == null) return false;
            else
                //We don't compare receive handle priority here as that can be different for the same packet types
                return options.Compressor == Compressor && options.Serializer == Serializer && options.ReceiveConfirmationRequired==ReceiveConfirmationRequired;
        }
    }
}
