using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DebugTests
{
    class LogAnalyser
    {
        string[] logLines;

        /// <summary>
        /// Initialise a new log analyser using the provided logfile
        /// </summary>
        /// <param name="logfileName"></param>
        public LogAnalyser(string logfileName)
        {
            logLines = File.ReadAllLines(logfileName);
        }

        /// <summary>
        /// Parses the time at which the current logline occured and returns an associated dateTime
        /// </summary>
        /// <param name="line"></param>
        /// <returns>True if the parse is succesfull</returns>
        private static bool ParseLineTime(string line, out DateTime result)
        {
            try
            {
                string timeStr = line.Substring(0, line.IndexOf(" ["));

                if (timeStr.Count(f => f == ':') == 3)
                    result = DateTime.ParseExact(timeStr, "HH:mm:ss:fff", CultureInfo.InvariantCulture);
                else
                    result = DateTime.ParseExact(timeStr, "HH:mm:ss", CultureInfo.InvariantCulture);

                return true;
            }
            catch (Exception)
            {
                result = DateTime.Now;
                return false;
            }
        }

        /// <summary>
        /// Write all lines that contain the provided matchString to a new resultFile
        /// </summary>
        /// <param name="matchString"></param>
        /// <param name="resultFile"></param>
        public void LinesWithMatch(string[] matchStrings, string resultFile)
        {
            using(StreamWriter result = new StreamWriter(resultFile, false))
            {
                for (int i = 0; i < logLines.Length; i++)
                {
                    if ((from current in matchStrings where logLines[i].Contains(current) select current).Count() > 0)
                        result.WriteLine(logLines[i]);
                }
            }
        }

        /// <summary>
        /// Parse the thread pool stats to a new resultFile
        /// </summary>
        /// <param name="resultFile"></param>
        public void ThreadPoolInfo(string resultFile)
        {
            using (StreamWriter result = new StreamWriter(resultFile, false))
            {
                result.WriteLine("time,timeDiff_ms,queue,threads,idle,selectedThread");

                bool startTimeSet = false;
                DateTime startTime = DateTime.Now;
                DateTime time;

                for (int i = 0; i < logLines.Length; i++)
                {
                    if (logLines[i].Contains("thread pool (Q") && ParseLineTime(logLines[i], out time))
                    {
                        if (!startTimeSet)
                        {
                            startTimeSet = true;
                            startTime = time;
                        }

                        bool selectedThread = logLines[i].Contains("Selected threadId");

                        int infoStart = logLines[i].IndexOf(" (Q") + " (".Length;
                        int infoEnd = logLines[i].IndexOf(") with priority ");
                        string threadInfo = logLines[i].Substring(infoStart, infoEnd - infoStart);

                        string[] splitInfo = threadInfo.Split(new char[] { ':', ',' });

                        result.WriteLine(time + ",{0},{1},{2},{3},{4}", (time - startTime).TotalMilliseconds, splitInfo[1], splitInfo[3], splitInfo[5], (selectedThread ? "1" : "0"));
                    }
                }
            }
        }

        /// <summary>
        /// Determines the KB/sec and saves it out versus time
        /// </summary>
        /// <param name="resultFile"></param>
        public void DataSendReceive(int statIntervalSecs, string resultFile)
        {
            using (StreamWriter result = new StreamWriter(resultFile, false))
            {
                result.WriteLine("time,timeDiff_ms,KB Send,KB Receive,KB Total");

                bool startTimeSet = false;
                DateTime startTime = DateTime.Now;
                DateTime currentSecond = DateTime.Now;
                DateTime time;

                double KBSent = 0, KBReceived = 0;

                for (int i = 0; i < logLines.Length; i++)
                {
                    if (ParseLineTime(logLines[i], out time))
                    {
                        if (!startTimeSet)
                        {
                            startTimeSet = true;
                            startTime = currentSecond = time;
                        }

                        double timeDiff = (time - startTime).TotalMilliseconds;

                        //If we have moved along in time we need to write out the previous second
                        if ((time - currentSecond).TotalSeconds >= statIntervalSecs)
                        {
                            currentSecond = time;
                            result.WriteLine(time + ",{0},{1},{2},{3}", timeDiff, (KBSent / statIntervalSecs).ToString("0.0"), (KBReceived / statIntervalSecs).ToString("0.0"), ((KBSent + KBReceived)/ statIntervalSecs).ToString("0.0"));
                            KBSent = KBReceived = 0;
                        }

                        if (logLines[i].Contains("Sending a packet of type"))
                        {
                            int startIndex = logLines[i].IndexOf(" containing ") + " containing ".Length;
                            int endIndex = logLines[i].IndexOf(" payload bytes");
                            string[] subString = logLines[i].Substring(startIndex, endIndex - startIndex).Split(' ').ToArray();

                            long totalBytes = long.Parse(subString[0]) + long.Parse(subString[4]);
                            KBSent += totalBytes / 1024.0;
                        }
                        else if (logLines[i].Contains("Received packet of type"))
                        {
                            int startIndex = logLines[i].IndexOf(" containing ") + " containing ".Length;
                            int endIndex = logLines[i].IndexOf(" payload bytes");
                            string[] subString = logLines[i].Substring(startIndex, endIndex - startIndex).Split(' ').ToArray();

                            long totalBytes = long.Parse(subString[0]) + long.Parse(subString[4]);
                            KBReceived += totalBytes / 1024.0;
                        }
                    }
                }
            }
        }
    }
}
