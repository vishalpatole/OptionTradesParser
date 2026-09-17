using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OptionTradesParser
{
    /// Derives the IBKR YYYYMMDD expiry from the alert text. Undated alerts default to 0DTE.
    public static class ExpirationResolver
    {
        private static readonly Regex ZeroDteRegex = new(@"\b0\s*DTE\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IsoDateRegex = new(@"\b(?<yr>20\d{2})-(?<mon>\d{1,2})-(?<day>\d{1,2})\b", RegexOptions.Compiled);

        // The month token is case-insensitive but its suffix is not, so an uppercase ticker such as MARA cannot pose as "Mar".
        // "Sep15" is accepted through the digit lookahead; "MARA" is not, since A is neither a lowercase suffix nor a digit.
        private static readonly Regex MonthNameRegex = new(
            @"\b(?<mon>(?i:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec))(?:[a-z]*\b|(?=\d))\.?\s*(?<day>\d{1,2})(?!\d)(?:\s*'\s*(?<yr>\d{2})|\s*,?\s*(?<yr>20\d{2}))?",
            RegexOptions.Compiled);

        private static readonly Regex SlashDateRegex = new(
            @"(?<![\d.])(?<mon>\d{1,2})/(?<day>\d{1,2})(?:/(?<yr>\d{2,4}))?(?![\d.])",
            RegexOptions.Compiled);

        private static readonly string[] MonthAbbreviations =
            { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        public static string? Resolve(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            DateTime today = EasternToday();
            var candidates = CollectCandidates(content);

            // A fully dated contract line outranks a "0DTE" tag, which in turn outranks a bare month/day.
            foreach (var candidate in candidates)
            {
                if (candidate.Year == null) continue;
                string? resolved = Build(candidate.Month, candidate.Day, candidate.Year, today);
                if (resolved != null) return resolved;
            }

            if (ZeroDteRegex.IsMatch(content)) return today.ToString("yyyyMMdd");

            foreach (var candidate in candidates)
            {
                string? resolved = Build(candidate.Month, candidate.Day, candidate.Year, today);
                if (resolved != null) return resolved;
            }

            return today.ToString("yyyyMMdd");
        }

        private static List<(int Month, int Day, int? Year)> CollectCandidates(string content)
        {
            var candidates = new List<(int, int, int?)>();

            foreach (Match match in IsoDateRegex.Matches(content))
                candidates.Add((ParseInt(match.Groups["mon"]), ParseInt(match.Groups["day"]), ParseYear(match.Groups["yr"])));

            foreach (Match match in MonthNameRegex.Matches(content))
            {
                int month = Array.IndexOf(MonthAbbreviations, match.Groups["mon"].Value[..3].ToLowerInvariant()) + 1;
                candidates.Add((month, ParseInt(match.Groups["day"]), ParseYear(match.Groups["yr"])));
            }

            foreach (Match match in SlashDateRegex.Matches(content))
                candidates.Add((ParseInt(match.Groups["mon"]), ParseInt(match.Groups["day"]), ParseYear(match.Groups["yr"])));

            return candidates;
        }

        private static string? Build(int month, int day, int? explicitYear, DateTime today)
        {
            if (month < 1 || month > 12 || day < 1) return null;

            int year = explicitYear ?? today.Year;
            if (day > DateTime.DaysInMonth(year, month)) return null;

            var expiry = new DateTime(year, month, day);

            // An alert quoting only a month/day near a year boundary refers to the upcoming occurrence.
            if (explicitYear == null && expiry < today) expiry = expiry.AddYears(1);

            return expiry.ToString("yyyyMMdd");
        }

        private static int ParseInt(Group group) => int.TryParse(group.Value, out int value) ? value : -1;

        private static int? ParseYear(Group group)
        {
            if (!group.Success || !int.TryParse(group.Value, out int year)) return null;
            return year < 100 ? 2000 + year : year;
        }

        internal static DateTime EasternToday()
        {
            try
            {
                var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eastern).Date;
            }
            catch (TimeZoneNotFoundException)
            {
                return DateTime.UtcNow.Date;
            }
        }
    }
}
