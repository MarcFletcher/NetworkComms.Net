// 
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
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
                threadId = System.Threading.Thread.CurrentThread.ManagedThreadId.ToString();
            }
            catch (Exception) { }

            string logStringToWrite;
            if (threadId != null)
                logStringToWrite = DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + threadId + " - " + level + "] - " + message;
            else
                logStringToWrite = DateTime.Now.Hour.ToString() + "." + DateTime.Now.Minute.ToString() + "." + DateTime.Now.Second.ToString() + "." + DateTime.Now.Millisecond.ToString() + " [" + level + "] - " + message;

            if (_currentLogMode == LogMode.ConsoleAndLogFile || _currentLogMode == LogMode.ConsoleOnly)
                Console.WriteLine(logStringToWrite);

            if ((_currentLogMode == LogMode.ConsoleAndLogFile || _currentLogMode == LogMode.LogFileOnly) && LogFileLocationName != null)
            {
                try
                {
                    lock (_locker)
                    {
                        using (var sw = new System.IO.StreamWriter(LogFileLocationName, true))
                            sw.WriteLine(logStringToWrite);                      
                    }
                }
                catch (Exception) { }
            }
        }

        /// <inheritdoc />
        public void Shutdown() { }
    }
}
