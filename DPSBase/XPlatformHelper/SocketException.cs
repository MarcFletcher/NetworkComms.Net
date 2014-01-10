#if NETFX_CORE

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace NetworkCommsDotNet.XPlatformHelper
{
    public class SocketException : Exception
    {
        int errorCode;

        public SocketException()
            : base()
        {
        }

        public SocketException(int error)
            : base()
        {
            errorCode = error;
        }
        
        internal SocketException(int error, string message)
            : base(message)
        {
            errorCode = error;
        }

        public int ErrorCode
        {
            get
            {
                return errorCode;
            }
        }

        public SocketError SocketErrorCode
        {
            get
            {
                return (SocketError)errorCode;
            }
        }

        public override string Message
        {
            get
            {
                return base.Message;
            }
        }
    }
}
#endif