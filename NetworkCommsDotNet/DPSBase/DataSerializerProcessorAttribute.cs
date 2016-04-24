// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
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
