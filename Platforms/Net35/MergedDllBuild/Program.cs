using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ILMerging;

namespace MergedDllBuild
{
    class Program
    {
        static void Main(string[] args)
        {
            Version networkCommsVersion = new Version(3, 0, 0);
            string targetPlatform = "v2";
            string msCoreLibDirectory = @"C:\Windows\Microsoft.NET\Framework\v2.0.50727";

            #region Merge Core
            Directory.CreateDirectory("MergedCore");
            ILMerge coreMerge = new ILMerge();

            List<string> coreAssembles = new List<string>();
            coreAssembles.Add("NetworkCommsDotNet.dll");
            coreAssembles.Add("protobuf-net.dll");
            coreAssembles.Add("DPSBase.dll");
            coreAssembles.Add("SevenZipLZMACompressor.dll");
            coreAssembles.Add("NLog.dll");

            coreMerge.SetInputAssemblies(coreAssembles.ToArray());
            coreMerge.Version = networkCommsVersion;

            coreMerge.TargetKind = ILMerge.Kind.Dll;
            coreMerge.SetTargetPlatform(targetPlatform, msCoreLibDirectory);
            coreMerge.XmlDocumentation = true;

            coreMerge.KeyFile = "networkcomms.net.snk";

            coreMerge.OutputFile = @"MergedCore\NetworkCommsDotNetCore.dll";

            coreMerge.Log = true;
            coreMerge.LogFile = @"MergedCore\MergeLog.txt";

            coreMerge.Merge();
            #endregion

            #region Merge Complete
            Directory.CreateDirectory("MergedComplete");
            ILMerge completeMerge = new ILMerge();

            List<string> completeAssembles = new List<string>();
            completeAssembles.Add("NetworkCommsDotNet.dll");
            completeAssembles.Add("protobuf-net.dll");
            completeAssembles.Add("DPSBase.dll");
            completeAssembles.Add("SevenZipLZMACompressor.dll");
            completeAssembles.Add("NLog.dll");
            completeAssembles.Add("ICSharpCode.SharpZipLib.dll");
            completeAssembles.Add("SharpZipLibCompressor.dll");
            completeAssembles.Add("QuickLZCompressor.dll");

            completeMerge.SetInputAssemblies(completeAssembles.ToArray());
            completeMerge.Version = networkCommsVersion;

            completeMerge.TargetKind = ILMerge.Kind.Dll;
            completeMerge.SetTargetPlatform(targetPlatform, msCoreLibDirectory);
            completeMerge.XmlDocumentation = true;

            completeMerge.KeyFile = "networkcomms.net.snk";

            completeMerge.OutputFile = @"MergedComplete\NetworkCommsDotNetComplete.dll";

            completeMerge.Log = true;
            completeMerge.LogFile = @"MergedComplete\MergeLog.txt";

            completeMerge.Merge();
            #endregion
        }
    }
}
