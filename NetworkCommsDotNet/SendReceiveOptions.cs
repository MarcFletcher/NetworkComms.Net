using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SerializerBase;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Provides flags for send and receive options such as serialisation, compression, encryption etc
    /// </summary>
    public class SendReceiveOptions
    {
        ISerialize serializer;
        ICompress compressor;

        //IEncrypt?
    }
}
