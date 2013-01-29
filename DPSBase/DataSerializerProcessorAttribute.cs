using System;
using System.Collections.Generic;
using System.Text;

namespace DPSBase
{
    [System.AttributeUsage(AttributeTargets.Class)]
    public class DataSerializerProcessorAttribute : System.Attribute
    {
        public byte Identifier { get; private set; }

        public DataSerializerProcessorAttribute(byte identifier)
        {
            this.Identifier = identifier;
        }
    }
}
