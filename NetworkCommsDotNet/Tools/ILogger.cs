//
//  Copyright 2009-2014 NetworkComms.Net Ltd.
//
//  This source code is made available for reference purposes only.
//  It may not be distributed and it may not be made publicly available.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// The logging interface using by NetworkComms.Net. Implement an instance of this interface to enable your own
    /// customised logging.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Log a trace message
        /// </summary>
        /// <param name="message">The message to log</param>
        void Trace(string message);

        /// <summary>
        /// Log a debug message
        /// </summary>
        /// <param name="message">The message to log</param>
        void Debug(string message);

        /// <summary>
        /// Log a fatal message
        /// </summary>
        /// <param name="message">The message to log</param>
        void Fatal(string message);

        /// <summary>
        /// Log a fatal message including an exception
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="ex">The exception to log</param>
        void Fatal(string message, Exception ex);

        /// <summary>
        /// Log a info message
        /// </summary>
        /// <param name="message">The message to log</param>
        void Info(string message);

        /// <summary>
        /// Log a warn message
        /// </summary>
        /// <param name="message">The message to log</param>
        void Warn(string message);

        /// <summary>
        /// Log a error message
        /// </summary>
        /// <param name="message">The message to log</param>
        void Error(string message);

        /// <summary>
        /// Shutdown the logger and release all resources.
        /// </summary>
        void Shutdown();
    }

    /// <summary>
    /// A core logger that can be used to write log messages to the console and or a log file.
    /// </summary>
    public class LiteLogger : ILogger
    {
        /// <summary>
        /// The different log modes available in the lite logger
        /// </summary>
        public enum LogMode
        {
            /// <summary>
            /// Only log to the current console.
            /// </summary>
            ConsoleOnly,

            /// <summary>
            /// Only log to an output file.
            /// </summary>
            LogFileOnly,

            /// <summary>
            /// Log to both the console and output file.
            /// </summary>
            ConsoleAndLogFile,
        }

        internal object _locker = new object();

        internal LogMode _currentLogMode;

        /// <summary>
        /// The location and filename of the log file. Must be set to successfully log to a file
        /// </summary>
        public string LogFileLocationName { get; set; }

        /// <summary>
        /// Initialise an instance of the core logger. If logging to a file also set LogFileLocationName.
        /// </summary>
        /// <param name="logMode">The log mode to use</param>
        public LiteLogger(LogMode logMode) 
        {
            _currentLogMode = logMode;
        }

        /// <summary>
        /// Initialise an instance of the core logger
        /// </summary>
        /// <param name="logMode">The log mode to use</param>
        /// <param name="logFileLocationName">The log file location and name, i.e. logs/logFile.txt</param>
        public LiteLogger(LogMode logMode, string logFileLocationName)
        {
            _currentLogMode = logMode;
            LogFileLocationName = logFileLocationName;
        }

        /// <inheritdoc />
        public void Trace(string message) { log("Trace", message); }

        /// <inheritdoc />
        public void Debug(string message) { log("Debug", message); }

        /// <inheritdoc />
        public void Fatal(string message) { log("Fatal", message); }

        /// <inheritdoc />
        public void Fatal(string message, Exception ex) { log("Fatal", message); }

        /// <inheritdoc />
        public void Info(string message) { log("Info", message); }

        /// <inheritdoc />
        public void Warn(string message) { log("Warn", message); }

        /// <inheritdoc />
        public void Error(string message) { log("Error", message); }

        private void log(string level, string message)
        {
            //Try to get the threadId which is very useful when debugging
            string threadId = null;
            try
            {
#if NETFX_CORE
                threadId = Environment.CurrentManagedThreadId.ToString();
#else
                threadId = System.Threading.Thread.CurrentThread.ManagedThreadId.ToString();
#endif
            }
            catch (Exception) { }

            string logStringToWrite;
            if (threadId != null)
                logStringToWrite = DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + threadId + " - " + level + "] - " + message;
            else
                logStringToWrite = DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + level + "] - " + message;

#if !NETFX_CORE
            if (_currentLogMode == LogMode.ConsoleAndLogFile || _currentLogMode == LogMode.ConsoleOnly)
                Console.WriteLine(logStringToWrite);
#endif

            if ((_currentLogMode == LogMode.ConsoleAndLogFile || _currentLogMode == LogMode.LogFileOnly) && LogFileLocationName != null)
            {
                try
                {
                    lock (_locker)
                    {

#if NETFX_CORE
                        System.Threading.Tasks.Task writeTask = new System.Threading.Tasks.Task(async () =>
                            {
                                while (true)
                                {
                                    try
                                    {
                                        Windows.Storage.StorageFolder folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                                        Windows.Storage.StorageFile file = await folder.CreateFileAsync(LogFileLocationName, Windows.Storage.CreationCollisionOption.OpenIfExists);
                                        await Windows.Storage.FileIO.AppendTextAsync(file, logStringToWrite + "\n");
                                        break;
                                    }
                                    catch (Exception) { }
                                }
                            });

                        writeTask.ConfigureAwait(false);
                        writeTask.Start();
                        writeTask.Wait(); 
#else
                        using (var sw = new System.IO.StreamWriter(LogFileLocationName, true))
                            sw.WriteLine(logStringToWrite);
#endif                        
                    }
                }
                catch (Exception) { }
            }
        }

        /// <inheritdoc />
        public void Shutdown() { }
    }
}
