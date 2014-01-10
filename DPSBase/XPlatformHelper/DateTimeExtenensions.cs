#if NETFX_CORE

using System;
using System.Collections.Generic;
using System.Text;
using Windows.Globalization.DateTimeFormatting;

namespace NetworkCommsDotNet.XPlatformHelper
{
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Converts the value of the current DateTime object to its equivalent short date string representation.
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static string ToShortDateString(this DateTime dt)
        {
            return dt.ToString(System.Globalization.DateTimeFormatInfo.CurrentInfo.ShortDatePattern);
        }

        /// <summary>
        /// Converts the value of the current DateTime object to its equivalent short time string representation.
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static string ToShortTimeString(this DateTime dt)
        {
            return dt.ToString(System.Globalization.DateTimeFormatInfo.CurrentInfo.ShortTimePattern);
        }

        /// <summary>
        /// Converts the value of the current DateTime object to its equivalent long date string representation.
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static string ToLongDateString(this DateTime dt)
        {
            return dt.ToString(System.Globalization.DateTimeFormatInfo.CurrentInfo.LongDatePattern);
        }

        /// <summary>
        /// Converts the value of the current DateTime object to its equivalent long time string representation.
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static string ToLongTimeString(this DateTime dt)
        {
            return dt.ToString(System.Globalization.DateTimeFormatInfo.CurrentInfo.LongTimePattern);
        }        
    }
}
#endif