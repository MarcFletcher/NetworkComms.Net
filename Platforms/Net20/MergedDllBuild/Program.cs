using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ILMerging;
using System.IO;

namespace MergedDllBuild
{
    class Program
    {
        static void Main(string[] args)
        {
            Directory.CreateDirectory("MergedCore");
            Directory.CreateDirectory("MergedComplete");

            #region Merge Core
            ILMerge coreMerge = new ILMerge();

            List<string> coreAssembles = new List<string>();
            coreAssembles.Add("NetworkCommsDotNet.dll");
            coreAssembles.Add("protobuf-net.dll");
            coreAssembles.Add("DPSBase.dll");
            coreAssembles.Add("SevenZipLZMACompressor.dll");
            coreAssembles.Add("NLog.dll");

            coreMerge.SetInputAssemblies(coreAssembles.ToArray());

            coreMerge.SetTargetPlatform("v2", @"C:\Windows\Microsoft.NET\Framework\v2.0.50727");
            coreMerge.XmlDocumentation = true;

            coreMerge.OutputFile = @"MergedCore\NetworkCommsDotNetCore.dll";
            coreMerge.Merge();
            #endregion

            #region Merge Complete
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

            completeMerge.SetTargetPlatform("v2", @"C:\Windows\Microsoft.NET\Framework\v2.0.50727");
            completeMerge.XmlDocumentation = true;

            completeMerge.OutputFile = @"MergedComplete\NetworkCommsDotNetComplete.dll";
            completeMerge.Merge();
            #endregion
        }
    }
}
