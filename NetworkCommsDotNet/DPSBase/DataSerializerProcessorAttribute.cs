//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

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

    /// <summary>
    /// Custom attribute used to label data processors as security critical or not
    /// </summary>
    [System.AttributeUsage(AttributeTargets.Class)]
    public class SecurityCriticalDataProcessorAttribute : System.Attribute
    {
        /// <summary>
        /// A booling defining if this data processor is security critical
        /// </summary>
        public bool IsSecurityCritical { get; private set; }

        /// <summary>
        /// Create a new instance of this attribute
        /// </summary>
        /// <param name="isSecurityCritical"></param>
        public SecurityCriticalDataProcessorAttribute(bool isSecurityCritical)
        {
            this.IsSecurityCritical = isSecurityCritical;
        }
    }
}
