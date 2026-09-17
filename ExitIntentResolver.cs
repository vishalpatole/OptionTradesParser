using System;
using System.Text.RegularExpressions;

namespace OptionTradesParser
{
    /// <param name="Fraction">Share of the held position to sell, 1.0 for a full close.</param>
    /// <param name="Ambiguous">True when the alert said to exit but never stated a size.</param>
    public sealed record ExitIntent(string Action, double Fraction, bool Ambiguous);

    /// Classifies how much of a position an exit alert refers to across the varied wording traders use.
    public static class ExitIntentResolver
    {
        private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.IgnoreCase;

        private const string SellVerbs = @"(?:sold|sell\w*|trim\w*|scal\w+|took|tak\w+|cut\w*|clos\w+|exit\w*|dump\w*)";

        // "sell 25%", "trimmed 30%". A leading + or - marks a P&L figure, never a size.
        private static readonly Regex PercentRegex = new($@"\b{SellVerbs}\b[^%\r\n]{{0,15}}?(?<![+\-\d.])(\d{{1,3}})\s*%", Opts);
        private static readonly Regex PercentOffRegex = new(@"(?<![+\-\d.])(\d{1,3})\s*%\s*(?:off|out)\b", Opts);

        // "Sold 12 of 25", "took 2/5"
        private static readonly Regex SoldOfRegex = new(@"\b(?:sold|sell\w*|trim\w*|took)\b\D{0,10}?(\d+)\s*(?:of|/)\s*(\d+)\b", Opts);

        private static readonly Regex FractionWordRegex = new(
            $@"\b{SellVerbs}\b[^\r\n]{{0,20}}?\b(half|a\s+third|two\s+thirds|a\s+quarter|three\s+quarters|1/2|1/3|2/3|1/4|3/4)\b", Opts);

        // Wording that proves something is being left on the table.
        private static readonly Regex PartialRegex = new(
            @"\b(?:trim\w*|scal\w+\s+out|partial\w*|still\s+(?:running|holding|open|in)|leaving\s+runners?|let\s+(?:the\s+)?rest\s+run|runners?\s+(?:on|left))\b", Opts);

        private static readonly Regex FullRegex = new(
            @"\b(?:sold\s+all|sell\s+all|closed?\s+all|clos\w+\s+(?:it|out|position)|all\s+out|full\s+exit|exited|exiting|flat|stopped\s+out|stop\s+out|out\s+completely|100\s*%)\b", Opts);

        // An "EXIT - SPY 757P" header, but not the bare "Exit" label used above a price in dashboard posts.
        private static readonly Regex ExitHeaderRegex = new(@"(?m)^\s*exit\b[ \t]*[^\s\r\n]", Opts);

        private static readonly Regex GenericExitRegex = new(@"\b(?:STC|sold|sell\w*|clos(?:e|ed|ing)|dump\w*)\b", Opts);

        // "Close or trim & set SL to breakeven" offers a choice, so it is treated as a trim and flagged for review.
        private static readonly Regex CloseOrTrimRegex = new(@"\bclos\w*\s+or\s+trim\w*|\btrim\w*\s+or\s+clos\w*", Opts);

        public static ExitIntent? Resolve(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            double? stated = ResolveStatedFraction(content);
            if (stated != null)
            {
                return stated.Value >= 1.0
                    ? new ExitIntent("SOLD_ALL", 1.0, false)
                    : new ExitIntent("TRIM", stated.Value, false);
            }

            if (CloseOrTrimRegex.IsMatch(content)) return new ExitIntent("TRIM", 0.5, true);

            // Partial wording is tested before full wording so a dashboard "Exit" label cannot override "still running".
            if (PartialRegex.IsMatch(content)) return new ExitIntent("TRIM", 0.5, true);

            if (FullRegex.IsMatch(content) || ExitHeaderRegex.IsMatch(content)) return new ExitIntent("SOLD_ALL", 1.0, false);

            if (GenericExitRegex.IsMatch(content)) return new ExitIntent("SELL", 1.0, true);

            return null;
        }

        private static double? ResolveStatedFraction(string content)
        {
            var soldOf = SoldOfRegex.Match(content);
            if (soldOf.Success
                && int.TryParse(soldOf.Groups[1].Value, out int sold)
                && int.TryParse(soldOf.Groups[2].Value, out int total)
                && sold > 0 && total > 0 && sold <= total)
            {
                return (double)sold / total;
            }

            foreach (var match in new[] { PercentRegex.Match(content), PercentOffRegex.Match(content) })
            {
                if (match.Success && int.TryParse(match.Groups[1].Value, out int percent) && percent > 0 && percent <= 100)
                {
                    return percent / 100.0;
                }
            }

            var word = FractionWordRegex.Match(content);
            return word.Success ? FractionFromWord(word.Groups[1].Value) : null;
        }

        private static double? FractionFromWord(string word) =>
            Regex.Replace(word.ToLowerInvariant(), @"\s+", " ") switch
            {
                "half" or "1/2" => 0.5,
                "a third" or "1/3" => 1.0 / 3.0,
                "two thirds" or "2/3" => 2.0 / 3.0,
                "a quarter" or "1/4" => 0.25,
                "three quarters" or "3/4" => 0.75,
                _ => null
            };
    }
}
