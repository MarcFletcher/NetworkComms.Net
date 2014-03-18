//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using NetworkCommsDotNet.DPSBase;

namespace RemoteProcedureCalls
{
    /// <summary>
    /// An error occured during an RPC (Remote Procedure Call) exchange.
    /// </summary>
    public class RPCException : CommsException
    {
        /// <summary>
        /// Create a new instance of RPCException
        /// </summary>
        /// <param name="msg">A string containing useful information regarding the error</param>
        public RPCException(string msg)
            : base(msg)
        {

        }
    }
}
