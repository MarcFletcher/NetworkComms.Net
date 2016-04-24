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
            string timeStr = line.Substring(0, line.IndexOf(" ["));

            char timeSep = timeStr.Contains('.') ? '.' : ':';
            string[] splitElements = timeStr.Split(timeSep);

            //Determine match string
            #region Build Expected Time Match String
            string timeMatchString = "";
            for (int i = 0; i < splitElements.Length; i++)
            {
                switch (i)
                {
                    case 0:
                        timeMatchString += (splitElements[i].Length == 1 ? "H" : "HH") + timeSep;
                        break;
                    case 1:
                        timeMatchString += (splitElements[i].Length == 1 ? "m" : "mm") + timeSep;
                        break;
                    case 2:
                        timeMatchString += (splitElements[i].Length == 1 ? "s" : "ss") + timeSep;
                        break;
                    case 3:
                        {
                            switch (splitElements[i].Length)
                            {
                                case 3:
                                    timeMatchString += "fff" + timeSep;
                                    break;
                                case 2:
                                    timeMatchString += "ff" + timeSep;
                                    break;
                                case 1:
                                    timeMatchString += "f" + timeSep;
                                    break;
                                default:
                                    break;
                            }

                            break;
                        }
                    default:
                        throw new Exception("Unexpected time format.");
                }
            }

            //Remove the final timeSep
            timeMatchString = timeMatchString.Substring(0, timeMatchString.Length - 1);
            #endregion

            try
            {
                result = DateTime.ParseExact(timeStr, timeMatchString, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                result = DateTime.Now;
                return false;
            }

            return true;
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
