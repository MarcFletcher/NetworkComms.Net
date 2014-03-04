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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ILMerging;

namespace MergedDllBuild
{
    class Program
    {
        static void Main(string[] args)
        {
#if !DEBUG
            Version networkCommsVersion =
                new Version(Assembly.ReflectionOnlyLoad("NetworkCommsDotNet").FullName.
                    Split(',').
                    Where(s => s.Split('=').Length == 2).
                    ToDictionary(s => s.Split('=')[0].Trim(), s => s.Split('=')[1].Trim())["Version"]);

            string targetPlatform = "v4";
            string msCoreLibDirectory = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319";

            bool coreBuildEnabled = false;
            bool completeBuildEnabled = true;

            #region Merge Core
            if (coreBuildEnabled)
            {
                Directory.CreateDirectory("MergedCore");
                ILMerge coreMerge = new ILMerge();

                List<string> coreAssembles = new List<string>();
                coreAssembles.Add("NetworkCommsDotNet.dll");
                coreAssembles.Add("protobuf-net.dll");
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
            }
            #endregion

            #region Merge Complete
            if (completeBuildEnabled)
            {
                Directory.CreateDirectory("MergedComplete");
                ILMerge completeMerge = new ILMerge();

                List<string> completeAssembles = new List<string>();
                completeAssembles.Add("NetworkCommsDotNet.dll");
                completeAssembles.Add("protobuf-net.dll");
                completeAssembles.Add("ProtobufSerializer.dll");
                completeAssembles.Add("NLog.dll");
                completeAssembles.Add("ICSharpCode.SharpZipLib.dll");
                completeAssembles.Add("SharpZipLibCompressor.dll");
                completeAssembles.Add("QuickLZCompressor.dll");
                completeAssembles.Add("InTheHand.Net.Personal.dll");

                completeAssembles.Add("DistributedFileSystem.dll");
                completeAssembles.Add("RemoteProcedureCalls.dll");

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
            }
            #endregion
#endif
        }
    }
}
