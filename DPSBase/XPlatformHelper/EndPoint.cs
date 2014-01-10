#if NETFX_CORE

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet.XPlatformHelper
{
    public abstract class EndPoint
    {
        // NB: These methods really do nothing but throw
        // NotImplementedException

        public virtual AddressFamily AddressFamily
        {
            get { throw NotImplemented(); }
        }

        public virtual EndPoint Create(SocketAddress socketAddress)
        {
            throw NotImplemented();
        }

        public virtual SocketAddress Serialize()
        {
            throw NotImplemented();
        }

        protected EndPoint()
        {
        }

        static Exception NotImplemented()
        {
            // hide the "normal" NotImplementedException from corcompare-like tools
            return new NotImplementedException();
        }
    }
}

#endif