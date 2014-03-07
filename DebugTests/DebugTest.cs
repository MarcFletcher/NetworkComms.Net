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
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using System.Threading;
using System.Net;
using NetworkCommsDotNet.DPSBase;
using NetworkCommsDotNet.Tools;
using System.IO;
using InTheHand.Net;
using NetworkCommsDotNet.Connections;
using NetworkCommsDotNet.Connections.Bluetooth;
using NetworkCommsDotNet.Connections.TCP;

namespace DebugTests
{
    /// <summary>
    /// A scrap board class for solving whatever bug is currently causing issues
    /// </summary>
    static class DebugTest
    {
        public static void RunExample()
        {
            //Get the serializer and data processors
            NetworkComms.AppendGlobalIncomingPacketHandler<string>("Data", (header, connection, message) =>
                {
                    Console.WriteLine("Server received - " + message);
                    connection.SendObject(header.RequestedReturnPacketType, "replyMessage");
                });

            Connection.StartListening(ConnectionType.TCP, new IPEndPoint(IPAddress.Any, 10000));

            Connection conn = TCPConnection.GetConnection(new ConnectionInfo(IPTools.ParseEndPointFromString("::1:10000")));
            var reply = conn.SendReceiveObject<string, string>("Data", "Data-Response", 1000, "hello server");

            Console.WriteLine("Client done!");
            Console.ReadKey();
        }
    }
}
