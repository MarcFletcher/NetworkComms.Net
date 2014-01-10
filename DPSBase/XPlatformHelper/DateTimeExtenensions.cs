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

        /// <summary>
        /// Creates a DateTimeFormatter object that is initialized by a format template string.
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="formatTemplate">A format template string that specifies the requested components. The order of the components is irrelevant.  This can also be a format pattern.</param>
        /// <returns></returns>
        public static string Format(this DateTime dt, string formatTemplate)
        {
            var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter(formatTemplate);
            return formatter.Format(dt);
        }

        public static string Format(this DateTime dt, string formatTemplate, IEnumerable<string> languages)
        {
            var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter(formatTemplate, languages);
            return formatter.Format(dt);
        }

        /// <summary>
        /// Creates a DateTimeFormatter object that is initialized with hour, minute, and second formats.
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="hourFormat">The desired hour format to include in the template.</param>
        /// <param name="minuteFormat">The desired minute format to include in the template.</param>
        /// <param name="secondFormat">The desired second format to include in the template.</param>
        /// <returns></returns>
        public static string Format(this DateTime dt, HourFormat hourFormat, MinuteFormat minuteFormat, SecondFormat secondFormat)
        {
            var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter(hourFormat, minuteFormat, secondFormat);
            return formatter.Format(dt);
        }

        /// <summary>
        /// Creates a DateTimeFormatter object that is initialized with year, month, day, and day of week formats.
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="yearFormat">The desired year format to include in the template.</param>
        /// <param name="monthFormat">The desired month format to include in the template.</param>
        /// <param name="dayFormat">The desired day format to include in the template.</param>
        /// <param name="dayOfWeekFormat">The desired day of week format to include in the template.</param>
        /// <returns></returns>
        public static string Format(this DateTime dt, YearFormat yearFormat, MonthFormat monthFormat, DayFormat dayFormat, DayOfWeekFormat dayOfWeekFormat)
        {
            var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter(yearFormat, monthFormat, dayFormat, dayOfWeekFormat);
            return formatter.Format(dt);
        }

        public static string Format(this DateTime dt, string formatTemplate, IEnumerable<string> languages, string geographicRegion, string calendar, string clock)
        {
            var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter(formatTemplate, languages, geographicRegion, calendar, clock);
            return formatter.Format(dt);
        }

        public static string Format(this DateTime dt, YearFormat yearFormat, MonthFormat monthFormat, DayFormat dayFormat, DayOfWeekFormat dayOfWeekFormat, HourFormat hourFormat, MinuteFormat minuteFormat, SecondFormat secondFormat, IEnumerable<string> languages)
        {
            var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter(yearFormat, monthFormat, dayFormat, dayOfWeekFormat, hourFormat, minuteFormat, secondFormat, languages);
            return formatter.Format(dt);
        }

        public static string Format(this DateTime dt, YearFormat yearFormat, MonthFormat monthFormat, DayFormat dayFormat, DayOfWeekFormat dayOfWeekFormat, HourFormat hourFormat, MinuteFormat minuteFormat, SecondFormat secondFormat, IEnumerable<string> languages, string geographicRegion, string calendar, string clock)
        {
            var formatter = new Windows.Globalization.DateTimeFormatting.DateTimeFormatter(yearFormat, monthFormat, dayFormat, dayOfWeekFormat, hourFormat, minuteFormat, secondFormat, languages, geographicRegion, calendar, clock);
            return formatter.Format(dt);
        }
    }
}
#endif