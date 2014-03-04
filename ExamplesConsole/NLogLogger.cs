using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NetworkCommsDotNet;
using NetworkCommsDotNet.Tools;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Examples.ExamplesConsole
{
    /// <summary>
    /// An example implementation of an external logging framework, in this case NLog.
    /// Uses a simple configuration that logs to console (DEBUG log level and above) 
    /// and to a file (All log Levels).
    /// </summary>
    public class NLogLogger : ILogger
    {
        private Logger logger;

        /// <summary>
        /// Initialise a new instance of the NLogLogger using a default configuration.
        /// </summary>
        public NLogLogger()
        {
            LoggingConfiguration logConfig = new LoggingConfiguration();
            FileTarget fileTarget = new FileTarget();
            fileTarget.FileName = "${basedir}/ExamplesConsoleLog_" + NetworkComms.NetworkIdentifier + ".txt";
            fileTarget.Layout = "${date:format=HH\\:mm\\:ss\\:fff} [${threadid} - ${level}] - ${message}";
            ConsoleTarget consoleTarget = new ConsoleTarget();
            consoleTarget.Layout = "${date:format=HH\\:mm\\:ss} - ${message}";

            logConfig.AddTarget("file", fileTarget);
            logConfig.AddTarget("console", consoleTarget);

            logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, fileTarget));
            logConfig.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, consoleTarget));

            LogManager.Configuration = logConfig;
            logger = LogManager.GetCurrentClassLogger();
            LogManager.EnableLogging();
        }

        /// <inheritdoc />
        public void Trace(string message)
        {
            logger.Trace(message);
        }

        /// <inheritdoc />
        public void Debug(string message)
        {
            logger.Debug(message);
        }

        /// <inheritdoc />
        public void Fatal(string message)
        {
            logger.Fatal(message);
        }

        /// <inheritdoc />
        public void Fatal(string message, Exception ex)
        {
            logger.Fatal(message);
        }

        /// <inheritdoc />
        public void Info(string message)
        {
            logger.Info(message);
        }

        /// <inheritdoc />
        public void Warn(string message)
        {
            logger.Warn(message);
        }

        /// <inheritdoc />
        public void Error(string message)
        {
            logger.Error(message);
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            LogManager.DisableLogging();
        }
    }
}
