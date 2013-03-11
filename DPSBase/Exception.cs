//  Copyright 2011-2012 Marc Fletcher, Matthew Dean
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
//  Please see <http://www.networkcommsdotnet.com/licenses> for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace DPSBase
{
    /// <summary>
    /// Base comms exception. All NetworkCommsDotNet exceptions can be caught in a single catch block by using catch(<see cref="CommsException"/>)
    /// </summary>
    public abstract class CommsException : Exception
    {
        /// <summary>
        /// Create a new instance of CommsException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CommsException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A checksum error has occured. NetworkComms.EnablePacketCheckSumValidation must be set to true for this exception to be thrown.
    /// </summary>
    public class CheckSumException : CommsException
    {
        /// <summary>
        /// Create a new instance of CheckSumException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CheckSumException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A timeout has occured while waiting for a confirmation packet to be received. Check for errors and or consider increasing NetworkComms.PacketConfirmationTimeoutMS
    /// </summary>
    public class ConfirmationTimeoutException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConfirmationTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConfirmationTimeoutException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A timeout has occured while waiting for an expected return object. Check for errors and or consider increasing the provided return timeout value.
    /// </summary>
    public class ExpectedReturnTimeoutException : CommsException
    {
        /// <summary>
        /// Create a new instance of ExpectedReturnTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ExpectedReturnTimeoutException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured while trying to serialise/compress or deserialise/uncompress an object.
    /// </summary>
    public class SerialisationException : CommsException
    {
        /// <summary>
        /// Create a new instance of SerialisationException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public SerialisationException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured while trying to establish a Connection
    /// </summary>
    public class ConnectionSetupException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConnectionSetupException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConnectionSetupException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured while trying to establish a Connection
    /// </summary>
    public class ConnectionShutdownException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConnectionShutdownException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConnectionShutdownException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured while trying to setup or shutdown NetworkCommsDotNet
    /// </summary>
    public class CommsSetupShutdownException : CommsException
    {
        /// <summary>
        /// Create a new instance of CommsSetupShutdownException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CommsSetupShutdownException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured while during communication which does not fall under other exception cases.
    /// </summary>
    public class CommunicationException : CommsException
    {
        /// <summary>
        /// Create a new instance of CommunicationException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public CommunicationException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An unexpected incoming packetType has been received. Consider setting NetworkComms.IgnoreUnknownPacketTypes to true to prevent this exception.
    /// </summary>
    public class UnexpectedPacketTypeException : CommsException
    {
        /// <summary>
        /// Create a new instance of UnexpectedPacketTypeException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public UnexpectedPacketTypeException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An invalid network identifier has been provided.
    /// </summary>
    public class InvalidNetworkIdentifierException : CommsException
    {
        /// <summary>
        /// Create a new instance of InvalidNetworkIdentifierException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public InvalidNetworkIdentifierException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A possible duplicate connection has been detected.
    /// </summary>
    public class DuplicateConnectionException : CommsException
    {
        /// <summary>
        /// Create a new instance of DuplicateConnectionException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public DuplicateConnectionException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A connection send has timed out.
    /// </summary>
    public class ConnectionSendTimeoutException : CommsException
    {
        /// <summary>
        /// Create a new instance of ConnectionSendTimeoutException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public ConnectionSendTimeoutException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured during a packetType data handler execution.
    /// </summary>
    public class PacketHandlerException : CommsException
    {
        /// <summary>
        /// Create a new instance of PacketHandlerException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public PacketHandlerException(string msg)
            : base(msg)
        {

        }
    }

}
