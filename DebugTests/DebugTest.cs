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
            DataSerializer dataSerializer = DPSManager.GetDataSerializer<ProtobufSerializer>();
            List<DataProcessor> dataProcessors = new List<DataProcessor>() { DPSManager.GetDataProcessor<SharpZipLibCompressor.SharpZipLibGzipCompressor>() };
            Dictionary<string, string> processorOptions = new Dictionary<string,string>();

            //Recreate the issue when sending a null object
            //We initialise an empty stream to pass through the serialisation stages
            MemoryStream emptyStream = new MemoryStream(new byte[0], 0, 0, false, true);
            object objectToSerialise = new StreamTools.StreamSendWrapper(new StreamTools.ThreadSafeStream(emptyStream, true));

            //Serializer as we do we a real send
            StreamTools.StreamSendWrapper result = dataSerializer.SerialiseDataObject(objectToSerialise, dataProcessors, processorOptions);

            //Get the bytes and deserialize
            byte[] bytes = result.ThreadSafeStream.ToArray();

            //If this works we expect the original string to be default(string)
            string originalString = dataSerializer.DeserialiseDataObject<string>(bytes, dataProcessors, processorOptions);
        }
    }
}
