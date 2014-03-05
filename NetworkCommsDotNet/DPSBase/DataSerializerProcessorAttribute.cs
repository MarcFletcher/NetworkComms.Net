//  Copyright 2009-2014 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  A commercial license of this software can also be purchased. 
//  Please see <http://www.networkcomms.net/licensing/> for details.

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
        /// <param name="identifier"></param>
        public SecurityCriticalDataProcessorAttribute(bool isSecurityCritical)
        {
            this.IsSecurityCritical = isSecurityCritical;
        }
    }
}
