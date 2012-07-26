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
        public ISerialize Serializer { get; set; }
        public ICompress Compressor { get; set; }

        //The priority with which this send recieve is dealt with
        ThreadPriority priority { get; set; }

        //IEncrypt?
    }
}
