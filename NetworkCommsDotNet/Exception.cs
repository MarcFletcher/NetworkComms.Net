//  Copyright 2011 Marc Fletcher, Matthew Dean
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkCommsDotNet
{
    /// <summary>
    /// Base comms exception. Should you choose to catch all comms exceptions in a single catch block just catch(CommsException)
    /// </summary>
    public abstract class CommsException : Exception
    {
        public CommsException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A checksum error has occured. Will only be thrown if NetworkComms.EnablePacketCheckSumValidation is true.
    /// </summary>
    public class CheckSumException : CommsException
    {
        public CheckSumException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A timeout has occured while waiting for a confirmation packet to be received.
    /// </summary>
    public class ConfirmationTimeoutException : CommsException
    {
        public ConfirmationTimeoutException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// A timout has occured while waiting for the expected return type. Thrown by SendReceiveObject.
    /// </summary>
    public class ExpectedReturnTimeoutException : CommsException
    {
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
        public SerialisationException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured while trying to establish a remote connection
    /// </summary>
    public class ConnectionSetupException : CommsException
    {
        public ConnectionSetupException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured while trying to setup networkComms.net
    /// </summary>
    public class CommsSetupException : CommsException
    {
        public CommsSetupException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error has occured while during communication.
    /// </summary>
    public class CommunicationException : CommsException
    {
        public CommunicationException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An unexpected incoming packetTypeStr has been received. Consider setting IgnoreUnknownPacketTypes to prevent this exception.
    /// </summary>
    public class UnexpectedPacketTypeException : CommsException
    {
        public UnexpectedPacketTypeException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An invalid connectionId has been provided. Ensure the connection still exists.
    /// </summary>
    public class InvalidConnectionIdException : CommsException
    {
        public InvalidConnectionIdException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured involving a packetTypeStr data handler.
    /// </summary>
    public class PacketHandlerException : CommsException
    {
        public PacketHandlerException(string msg)
            : base(msg)
        {

        }
    }

    /// <summary>
    /// An error occured involving an RPC method.
    /// </summary>
    public class RPCException : CommsException
    {
        public RPCException(string msg)
            : base(msg)
        {

        }
    }
}
