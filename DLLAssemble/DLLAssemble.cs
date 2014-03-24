using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DLLAssemble
{
    /// <summary>
    /// Class used to assemble all DLL builds into a single folder. 
    /// Greatly speeds up the collation for releasing updates
    /// </summary>
    static class DLLAssemble
    {
        public static void Assemble()
        {
            try
            {
                int minutesAllowedBeforeExceptionForOldDLLs = 5;
                string assembleMode = "Release";
                string sourceDir;
                string destDir;

                #region Create Directories
                if (Directory.Exists("DLLs"))
                    Directory.Delete("DLLs", true);

                string[] newDirectories = new string[] 
            {
                @"DLLs\Net20\Individual",
                @"DLLs\Net20\Merged",
                @"DLLs\Net35\Individual",
                @"DLLs\Net35\Merged",
                @"DLLs\Net40\Individual",
                @"DLLs\Net40\Merged",
                @"DLLs\WinRT\Individual",
                @"DLLs\WP8\Individual",
                @"DLLs\Xamarin.Android\Individual",
                @"DLLs\Xamarin.iOS\Individual",
            };

                foreach (string directory in newDirectories)
                    Directory.CreateDirectory(directory);
                #endregion

                #region Add Readme
                string[] readmeLines = new string[]
            {
                @"Folders in this directory contain the NetworkComms.Net DLLs for supported platforms.",
                @"",
                @"For some platforms we have included merged DLLs which can be used in isolation,", 
                @"i.e. all dependencies are included, significantly reducing the number of assembly", 
                @"references required in your own projects when using NetworkComms.Net",
                @"",
                @"DLLs that are found in the folders named 'Individual' are the equivalent unmerged", 
                @"DLLs and may have further dependencies which have not been included to reduce the", 
                @"total bundle size. If you choose to use these unmerged DLLs these dependencies can", 
                @"be easily downloaded from www.NuGet.org and may include the following:",
                @"  \ 32Feet.NET - InTheHand.Net.Personal.dll",
                @"  \ Json.NET - Newtonsoft.Json.dll",
                @"  \ protobuf-net - protobuf-net.dll"
            };

                using (StreamWriter sw = new StreamWriter(@"DLLs\Important DLLs Readme.txt", false))
                {
                    foreach (string line in readmeLines)
                        sw.WriteLine(line);
                }
                #endregion

                #region Net20
                //Copy individual DLLs
                sourceDir = Path.Combine(@"..\..\..\Platforms\Net20\MergedDllBuild\bin", assembleMode);
                destDir = Path.Combine(@"DLLs\Net20");

                string[] net20IndividualFiles = new string[] 
                {
                    "NetworkCommsDotNet",
                    "ICSharpCode.SharpZipLib",
                    "ProtobufSerializer",
                    "SharpZipLibCompressor"
                };

                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, "NetworkCommsDotNet.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                //Copy the files over
                foreach (string file in net20IndividualFiles)
                {
                    //Check filesize
                    if (new FileInfo(Path.Combine(sourceDir, file + ".dll")).Length == 0)
                        throw new InvalidDataException("Size of dll should be greater than 0.");

                    File.Copy(Path.Combine(sourceDir, file + ".dll"), Path.Combine(destDir, "Individual", file + ".dll"));

                    if (File.Exists(Path.Combine(sourceDir, file + ".pdb"))) File.Copy(Path.Combine(sourceDir, file + ".pdb"), Path.Combine(destDir, "Individual", file + ".pdb"));
                    if (File.Exists(Path.Combine(sourceDir, file + ".xml"))) File.Copy(Path.Combine(sourceDir, file + ".xml"), Path.Combine(destDir, "Individual", file + ".xml"));
                }

                //Copy merged DLLs
                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.dll"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.dll"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.pdb"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.pdb"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.xml"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.xml"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\MergeLog.txt"), Path.Combine(destDir, "Merged", "MergeLog.txt"));
                #endregion

                #region Net35
                //Copy individual DLLs
                sourceDir = Path.Combine(@"..\..\..\Platforms\Net35\MergedDllBuild\bin", assembleMode);
                destDir = Path.Combine(@"DLLs\Net35");

                string[] net35IndividualFiles = new string[] 
                {
                    "NetworkCommsDotNet",
                    "ICSharpCode.SharpZipLib",
                    "ProtobufSerializer",
                    "SharpZipLibCompressor"
                };

                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, "NetworkCommsDotNet.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                //Copy the files over
                foreach (string file in net35IndividualFiles)
                {
                    //Check filesize
                    if (new FileInfo(Path.Combine(sourceDir, file + ".dll")).Length == 0)
                        throw new InvalidDataException("Size of dll should be greater than 0.");

                    File.Copy(Path.Combine(sourceDir, file + ".dll"), Path.Combine(destDir, "Individual", file + ".dll"));

                    if (File.Exists(Path.Combine(sourceDir, file + ".pdb"))) File.Copy(Path.Combine(sourceDir, file + ".pdb"), Path.Combine(destDir, "Individual", file + ".pdb"));
                    if (File.Exists(Path.Combine(sourceDir, file + ".xml"))) File.Copy(Path.Combine(sourceDir, file + ".xml"), Path.Combine(destDir, "Individual", file + ".xml"));
                }

                //Copy merged DLLs
                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.dll"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.dll"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.pdb"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.pdb"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.xml"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.xml"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\MergeLog.txt"), Path.Combine(destDir, "Merged", "MergeLog.txt"));
                #endregion

                #region Net40
                //Copy individual DLLs
                sourceDir = Path.Combine(@"..\..\..\Platforms\Net40\MergedDllBuild\bin", assembleMode);
                destDir = Path.Combine(@"DLLs\Net40");

                string[] net40IndividualFiles = new string[] 
                {
                    "NetworkCommsDotNet",
                    "ICSharpCode.SharpZipLib",
                    "ProtobufSerializer",
                    "SharpZipLibCompressor",
                    "DistributedFileSystem",
                    "JSONSerializer",
                    "RemoteProcedureCalls",
                };

                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, "NetworkCommsDotNet.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                //Copy the files over
                foreach (string file in net40IndividualFiles)
                {
                    //Check filesize
                    if (new FileInfo(Path.Combine(sourceDir, file + ".dll")).Length == 0)
                        throw new InvalidDataException("Size of dll should be greater than 0.");

                    File.Copy(Path.Combine(sourceDir, file + ".dll"), Path.Combine(destDir, "Individual", file + ".dll"));

                    if (File.Exists(Path.Combine(sourceDir, file + ".pdb"))) File.Copy(Path.Combine(sourceDir, file + ".pdb"), Path.Combine(destDir, "Individual", file + ".pdb"));
                    if (File.Exists(Path.Combine(sourceDir, file + ".xml"))) File.Copy(Path.Combine(sourceDir, file + ".xml"), Path.Combine(destDir, "Individual", file + ".xml"));
                }

                //Copy merged DLLs
                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.dll"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.dll"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.pdb"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.pdb"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\NetworkCommsDotNetComplete.xml"), Path.Combine(destDir, "Merged", "NetworkCommsDotNetComplete.xml"));
                File.Copy(Path.Combine(sourceDir, @"MergedComplete\MergeLog.txt"), Path.Combine(destDir, "Merged", "MergeLog.txt"));
                #endregion

                #region WinRT
                //Copy individual DLLs
                sourceDir = Path.Combine(@"..\..\..\Platforms\WinRT\ExamplesChat.WinRT\bin", assembleMode);
                destDir = Path.Combine(@"DLLs\WinRT");

                string[] winRTIndividualFiles = new string[] 
                {
                    "NetworkCommsDotNet",
                    "ProtobufSerializer",
                    "JSONSerializer",
                };

                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, "NetworkCommsDotNet.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                //Copy the files over
                foreach (string file in winRTIndividualFiles)
                {
                    //Check filesize
                    if (new FileInfo(Path.Combine(sourceDir, file + ".dll")).Length == 0)
                        throw new InvalidDataException("Size of dll should be greater than 0.");

                    File.Copy(Path.Combine(sourceDir, file + ".dll"), Path.Combine(destDir, "Individual", file + ".dll"));

                    if (File.Exists(Path.Combine(sourceDir, file + ".pdb"))) File.Copy(Path.Combine(sourceDir, file + ".pdb"), Path.Combine(destDir, "Individual", file + ".pdb"));
                    if (File.Exists(Path.Combine(sourceDir, file + ".xml"))) File.Copy(Path.Combine(sourceDir, file + ".xml"), Path.Combine(destDir, "Individual", file + ".xml"));
                }
                #endregion

                #region WP8
                //Copy individual DLLs
                sourceDir = Path.Combine(@"..\..\..\Platforms\WP8\ExamplesChat.WP8\bin", assembleMode);
                destDir = Path.Combine(@"DLLs\WP8");

                string[] winP8IndividualFiles = new string[] 
                {
                    "NetworkCommsDotNet",
                    "ProtobufSerializer",
                    "JSONSerializer",
                };

                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, "NetworkCommsDotNet.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                //Copy the files over
                foreach (string file in winP8IndividualFiles)
                {
                    //Check filesize
                    if (new FileInfo(Path.Combine(sourceDir, file + ".dll")).Length == 0)
                        throw new InvalidDataException("Size of dll should be greater than 0.");

                    File.Copy(Path.Combine(sourceDir, file + ".dll"), Path.Combine(destDir, "Individual", file + ".dll"));

                    if (File.Exists(Path.Combine(sourceDir, file + ".pdb"))) File.Copy(Path.Combine(sourceDir, file + ".pdb"), Path.Combine(destDir, "Individual", file + ".pdb"));
                    if (File.Exists(Path.Combine(sourceDir, file + ".xml"))) File.Copy(Path.Combine(sourceDir, file + ".xml"), Path.Combine(destDir, "Individual", file + ".xml"));
                }
                #endregion

                #region Xamarin.Android
                //Copy individual DLLs
                sourceDir = Path.Combine(@"..\..\..\Platforms\Xamarin.Android\MergedDllBuild\bin", assembleMode);
                destDir = Path.Combine(@"DLLs\Xamarin.Android");

                string[] androidIndividualFiles = new string[] 
                {
                    "NetworkCommsDotNet",
                    "ICSharpCode.SharpZipLib",
                    "ProtobufSerializer",
                    "SharpZipLibCompressor",
                };

                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, "NetworkCommsDotNet.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                //Copy the files over
                foreach (string file in androidIndividualFiles)
                {
                    //Check filesize
                    if (new FileInfo(Path.Combine(sourceDir, file + ".dll")).Length == 0)
                        throw new InvalidDataException("Size of dll should be greater than 0.");

                    File.Copy(Path.Combine(sourceDir, file + ".dll"), Path.Combine(destDir, "Individual", file + ".dll"));

                    if (File.Exists(Path.Combine(sourceDir, file + ".pdb"))) File.Copy(Path.Combine(sourceDir, file + ".pdb"), Path.Combine(destDir, "Individual", file + ".pdb"));
                    if (File.Exists(Path.Combine(sourceDir, file + ".xml"))) File.Copy(Path.Combine(sourceDir, file + ".xml"), Path.Combine(destDir, "Individual", file + ".xml"));
                }
                #endregion

                #region Xamarin.iOS
                //Copy individual DLLs
                sourceDir = Path.Combine(@"..\..\..\Platforms\Xamarin.iOS\MergedDllBuild\bin", assembleMode);
                destDir = Path.Combine(@"DLLs\Xamarin.iOS");

                string[] iOSIndividualFiles = new string[] 
                {
                    "NetworkCommsDotNet",
                    "ProtobufSerializer",
                };

                //Check Dll build time was in the last 5 minutes
                if ((DateTime.Now - File.GetLastWriteTime(Path.Combine(sourceDir, "NetworkCommsDotNet.dll"))).TotalMinutes > minutesAllowedBeforeExceptionForOldDLLs)
                    throw new InvalidDataException("dlls must have been created within the last " + minutesAllowedBeforeExceptionForOldDLLs + " minutes. This check ensures we are not using old dlls has been recently");

                //Copy the files over
                foreach (string file in iOSIndividualFiles)
                {
                    //Check filesize
                    if (new FileInfo(Path.Combine(sourceDir, file + ".dll")).Length == 0)
                        throw new InvalidDataException("Size of dll should be greater than 0.");

                    File.Copy(Path.Combine(sourceDir, file + ".dll"), Path.Combine(destDir, "Individual", file + ".dll"));

                    if (File.Exists(Path.Combine(sourceDir, file + ".pdb"))) File.Copy(Path.Combine(sourceDir, file + ".pdb"), Path.Combine(destDir, "Individual", file + ".pdb"));
                    if (File.Exists(Path.Combine(sourceDir, file + ".xml"))) File.Copy(Path.Combine(sourceDir, file + ".xml"), Path.Combine(destDir, "Individual", file + ".xml"));
                }
                #endregion

                using (StreamWriter sw = new StreamWriter("assembleSucceeded.txt", false))
                    sw.WriteLine("Success!");
            }
            catch (Exception ex)
            {
                using (StreamWriter sw = new StreamWriter("assembleFailed.txt", false))
                    sw.WriteLine(ex.ToString());
            }
        }
    }
}
