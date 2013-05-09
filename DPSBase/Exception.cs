//  Copyright 2011-2013 Marc Fletcher, Matthew Dean
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
using System.Runtime.Serialization;
using System.Text;

namespace DPSBase
{
    /// <summary>
    /// Base comms exception. All NetworkCommsDotNet exceptions can be caught in a single catch block by using catch(<see cref="CommsException"/>)
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public abstract class CommsException : Exception
    {
        /// <summary>
        /// Create a new instance of CommsException
        /// </summary>
        public CommsException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of CommsException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CommsException(string msg)
            : base(msg)
        {

        }

        /// <summary>
        /// Create a new instance of CommsException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public CommsException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected CommsException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// A checksum error has occured. NetworkComms.EnablePacketCheckSumValidation must be set to true for this exception to be thrown.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class CheckSumException : CommsException
    {
        /// <summary>
        /// Create a new instance of CheckSumException
        /// </summary>
        public CheckSumException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of CheckSumException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CheckSumException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of CheckSumException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public CheckSumException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected CheckSumException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// A timeout has occured while waiting for a confirmation packet to be received. Check for errors and or consider increasing NetworkComms.PacketConfirmationTimeoutMS
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class ConfirmationTimeoutException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConfirmationTimeoutException
        /// </summary>
        public ConfirmationTimeoutException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of ConfirmationTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConfirmationTimeoutException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of ConfirmationTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public ConfirmationTimeoutException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected ConfirmationTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// A timeout has occured while waiting for an expected return object. Check for errors and or consider increasing the provided return timeout value.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class ExpectedReturnTimeoutException : CommsException
    {
        /// <summary>
        /// Create a new instance of ExpectedReturnTimeoutException
        /// </summary>
        public ExpectedReturnTimeoutException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of ExpectedReturnTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ExpectedReturnTimeoutException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of ExpectedReturnTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public ExpectedReturnTimeoutException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected ExpectedReturnTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An error occured while trying to serialise/compress or deserialise/uncompress an object.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class SerialisationException : CommsException
    {
        /// <summary>
        /// Create a new instance of SerialisationException
        /// </summary>
        public SerialisationException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of SerialisationException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public SerialisationException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of SerialisationException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public SerialisationException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected SerialisationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An error occured while trying to establish a Connection
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class ConnectionSetupException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConnectionSetupException
        /// </summary>
        public ConnectionSetupException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of ConnectionSetupException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConnectionSetupException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of ConnectionSetupException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public ConnectionSetupException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected ConnectionSetupException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An error occured while trying to establish a Connection
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class ConnectionShutdownException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConnectionShutdownException
        /// </summary>
        public ConnectionShutdownException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of ConnectionShutdownException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConnectionShutdownException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of ConnectionShutdownException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public ConnectionShutdownException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected ConnectionShutdownException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An error occured while trying to setup or shutdown NetworkCommsDotNet
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class CommsSetupShutdownException : CommsException
    {
        /// <summary>
        /// Create a new instance of CommsSetupShutdownException
        /// </summary>
        public CommsSetupShutdownException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of CommsSetupShutdownException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CommsSetupShutdownException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of CommsSetupShutdownException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public CommsSetupShutdownException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected CommsSetupShutdownException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An error occured while during communication which does not fall under other exception cases.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class CommunicationException : CommsException
    {
        /// <summary>
        /// Create a new instance of CommunicationException
        /// </summary>
        public CommunicationException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of CommunicationException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CommunicationException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of CommunicationException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public CommunicationException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected CommunicationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An unexpected incoming packetType has been received. Consider setting NetworkComms.IgnoreUnknownPacketTypes to true to prevent this exception.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class UnexpectedPacketTypeException : CommsException
    {
        /// <summary>
        /// Create a new instance of UnexpectedPacketTypeException
        /// </summary>
        public UnexpectedPacketTypeException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of UnexpectedPacketTypeException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public UnexpectedPacketTypeException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of UnexpectedPacketTypeException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public UnexpectedPacketTypeException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected UnexpectedPacketTypeException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An invalid network identifier has been provided.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class InvalidNetworkIdentifierException : CommsException
    {
        /// <summary>
        /// Create a new instance of InvalidNetworkIdentifierException
        /// </summary>
        public InvalidNetworkIdentifierException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of InvalidNetworkIdentifierException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public InvalidNetworkIdentifierException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of InvalidNetworkIdentifierException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public InvalidNetworkIdentifierException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected InvalidNetworkIdentifierException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// A possible duplicate connection has been detected.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class DuplicateConnectionException : CommsException
    {
        /// <summary>
        /// Create a new instance of DuplicateConnectionException
        /// </summary>
        public DuplicateConnectionException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of DuplicateConnectionException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public DuplicateConnectionException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of DuplicateConnectionException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public DuplicateConnectionException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected DuplicateConnectionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// A connection send has timed out.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class ConnectionSendTimeoutException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConnectionSendTimeoutException
        /// </summary>
        public ConnectionSendTimeoutException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of ConnectionSendTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConnectionSendTimeoutException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of ConnectionSendTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public ConnectionSendTimeoutException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected ConnectionSendTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// An error occured during a packetType data handler execution.
    /// </summary>
#if !WINDOWS_PHONE
    [Serializable]
#endif
    public class PacketHandlerException : CommsException
    {
        /// <summary>
        /// Create a new instance of PacketHandlerException
        /// </summary>
        public PacketHandlerException()
            : base()
        {
        }

        /// <summary>
        /// Create a new instance of PacketHandlerException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public PacketHandlerException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Create a new instance of PacketHandlerException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        /// <param name="innerException">An associated inner exception</param>
        public PacketHandlerException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

#if !WINDOWS_PHONE
        /// <summary>
        /// Constructor required by the runtime and by .NET programming conventions
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected PacketHandlerException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}
