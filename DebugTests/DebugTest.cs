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
            Random randGen = new Random();
            int[] someRandomData = new int[29 * 1024 * 1024 / 4];
            for(int i=0; i<someRandomData.Length; i++)
                someRandomData[i] = randGen.Next(int.MaxValue);

            byte[] compressedHandRankData = DPSManager.GetDataSerializer<NullSerializer>().SerialiseDataObject<int[]>(someRandomData, new List<DataProcessor>() { DPSManager.GetDataProcessor<SharpZipLibCompressor.SharpZipLibGzipCompressor>() }, new Dictionary<string, string>()).ThreadSafeStream.ToArray();

            int[] someRadomData2 = DPSManager.GetDataSerializer<NullSerializer>().DeserialiseDataObject<int[]>(compressedHandRankData, new List<DataProcessor>() { DPSManager.GetDataProcessor<SharpZipLibCompressor.SharpZipLibGzipCompressor>() }, new Dictionary<string, string>());

            Console.WriteLine("Client done!");
            Console.ReadKey();
        }
    }
}
