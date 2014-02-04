using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

#if NETFX_CORE
using System.Threading.Tasks;
using Windows.Storage;
#endif

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// Quickly log exceptions and information to a file.
    /// </summary>
    public class LogTools
    {
        /// <summary>
        /// Locker for log methods which ensures threadSafe save outs.
        /// </summary>
        static object errorLocker = new object();

        /// <summary>
        /// Appends the provided logString to end of fileName.txt. If the file does not exist it will be created.
        /// </summary>
        /// <param name="fileName">The filename to use. The extension .txt will be appended automatically</param>
        /// <param name="logString">The string to append.</param>
        public static void AppendStringToLogFile(string fileName, string logString)
        {
            try
            {
                lock (errorLocker)
                {
#if NETFX_CORE
                    Func<Task> writeTask = new Func<Task>(async () =>
                        {
                            StorageFolder folder = ApplicationData.Current.LocalFolder;
                            StorageFile file = await folder.CreateFileAsync(fileName + ".txt", CreationCollisionOption.OpenIfExists);
                            await FileIO.AppendTextAsync(file, logString);
                        });

                    writeTask().Wait();
#else
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fileName + ".txt", true))
                        sw.WriteLine(logString);
#endif
                }
            }
            catch (Exception)
            {
                //If an error happens here, such as if the file is locked then we lucked out.
            }
        }

        /// <summary>
        /// Logs the provided exception to a file to assist troubleshooting.
        /// </summary>
        /// <param name="ex">The exception to be logged</param>
        /// <param name="fileName">The filename to use. A time stamp and extension .txt will be appended automatically</param>
        /// <param name="optionalCommentStr">An optional string which will appear at the top of the error file</param>
        /// <returns>The entire fileName used.</returns>
        public static string LogException(Exception ex, string fileName, string optionalCommentStr = "")
        {
            string entireFileName;

            lock (errorLocker)
            {

#if iOS
                //We need to ensure we add the correct document path for iOS
                entireFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "]"));
#elif ANDROID
                entireFileName = Path.Combine(global::Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "]"));
#elif WINDOWS_PHONE
                entireFileName = fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Thread.CurrentThread.ManagedThreadId.ToString() + "]");
#elif NETFX_CORE
                entireFileName = fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + Environment.CurrentManagedThreadId.ToString() + "]");
#else
                using (Process currentProcess = System.Diagnostics.Process.GetCurrentProcess())
                    entireFileName = fileName + " " + DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " " + DateTime.Now.ToString("dd-MM-yyyy" + " [" + currentProcess.Id.ToString() + "-" + Thread.CurrentContext.ContextID.ToString() + "]");
#endif

                if (NetworkComms.LoggingEnabled) NetworkComms.Logger.Fatal(entireFileName, ex);

                try
                {
#if NETFX_CORE
                    Func<Task> writeTask = new Func<Task>(async () =>
                        {
                            List<string> lines = new List<string>();

                            if (optionalCommentStr != "")
                            {
                                lines.Add("Comment: " + optionalCommentStr);
                                lines.Add("");
                            }

                            if (ex.GetBaseException() != null)
                                lines.Add("Base Exception Type: " + ex.GetBaseException().ToString());

                            if (ex.InnerException != null)
                                lines.Add("Inner Exception Type: " + ex.InnerException.ToString());

                            if (ex.StackTrace != null)
                            {
                                lines.Add("");
                                lines.Add("Stack Trace: " + ex.StackTrace.ToString());
                            }

                            StorageFolder folder = ApplicationData.Current.LocalFolder;
                            StorageFile file = await folder.CreateFileAsync(fileName + ".txt", CreationCollisionOption.OpenIfExists);
                            await FileIO.WriteLinesAsync(file, lines);
                        });

                    writeTask().Wait();
#else
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(entireFileName + ".txt", false))
                    {
                        if (optionalCommentStr != "")
                        {
                            sw.WriteLine("Comment: " + optionalCommentStr);
                            sw.WriteLine("");
                        }

                        if (ex.GetBaseException() != null)
                            sw.WriteLine("Base Exception Type: " + ex.GetBaseException().ToString());

                        if (ex.InnerException != null)
                            sw.WriteLine("Inner Exception Type: " + ex.InnerException.ToString());

                        if (ex.StackTrace != null)
                        {
                            sw.WriteLine("");
                            sw.WriteLine("Stack Trace: " + ex.StackTrace.ToString());
                        }
                    }
#endif
                }
                catch (Exception)
                {
                    //This should never really happen, but just incase.
                }
            }

            return entireFileName;
        }
    }
}
