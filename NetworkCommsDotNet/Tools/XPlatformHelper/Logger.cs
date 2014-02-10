#if NO_LOGGING

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet.Tools.XPlatformHelper
{
    /// <summary>
    /// On some platforms NLog has issues so this class provides the most basic logging features.
    /// </summary>
    public class Logger
    {
        internal object locker = new object();
        public string LogFileLocation { get; set; }

        public void Trace(string message) { log("Trace", message); }
        public void Debug(string message) { log("Debug", message); }
        public void Fatal(string message, Exception e = null) { log("Fatal", message); }
        public void Info(string message) { log("Info", message); }
        public void Warn(string message) { log("Warn", message); }
        public void Error(string message) { log("Error", message); }

        private void log(string level, string message)
        {
            if (LogFileLocation != null)
            {
                //Try to get the threadId which is very usefull when debugging
                string threadId = null;
                try
                {
#if NETFX_CORE
                    threadId = Environment.CurrentManagedThreadId.ToString();
#else
                    threadId = Thread.CurrentThread.ManagedThreadId.ToString();
#endif
                }
                catch (Exception) { }

                try
                {
                    lock (locker)
                    {
                        string toWrite;
                        if (threadId != null)
                                toWrite = DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + threadId + " - " + level + "] - " + message;
                            else
                                toWrite = DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + level + "] - " + message;
#if NETFX_CORE
                        Func<System.Threading.Tasks.Task> writeFunc = new Func<System.Threading.Tasks.Task>(async () =>
                            {
                                Windows.Storage.StorageFolder folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                                Windows.Storage.StorageFile file = await folder.CreateFileAsync(LogFileLocation, Windows.Storage.CreationCollisionOption.OpenIfExists);
                                await Windows.Storage.FileIO.AppendTextAsync(file, toWrite);
                            });

                        writeFunc().Wait();
#else
                        using (var sw = new StreamWriter(LogFileLocation, true))
                                sw.WriteLine(toWrite);
#endif                        
                    }
                }
                catch (Exception) { }
            }
        }

        public Logger() { }

        public void Shutdown() { }
    }
}
#endif
