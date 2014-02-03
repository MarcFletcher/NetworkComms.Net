using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet.DPSBase
{
    /// <summary>
    /// Custom attribute used to keep track of serializers and processors
    /// </summary>
    [System.AttributeUsage(AttributeTargets.Class)]
    public class DataSerializerProcessorAttribute : System.Attribute
    {
        /// <summary>
        /// A byte identifier, unique amongst all serialisers and data processors.
        /// </summary>
        public byte Identifier { get; private set; }

        /// <summary>
        /// Create a new instance of this attribute
        /// </summary>
        /// <param name="identifier"></param>
        public DataSerializerProcessorAttribute(byte identifier)
        {
            this.Identifier = identifier;
        }
    }
}
