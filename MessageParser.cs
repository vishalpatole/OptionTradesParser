using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OptionTradesParser
{
    public record TradeContractContext(string Ticker, string OptionType, double Strike, string Expiration);
    public record ParsedTrade(string TraderName, string ActionType, string Ticker, string OptionType, double Strike, string Expiration, double PricePaid, string RiskCategory, double TrimFraction, bool SizingAmbiguous, bool ContractInferred);

    public class MessageParser
    {
        private const RegexOptions Opts = RegexOptions.Compiled;
        private const RegexOptions OptsIc = RegexOptions.Compiled | RegexOptions.IgnoreCase;

        // Matches "288", "288.5", and ".32" alike, since traders often drop the leading zero on a premium.
        private const string Num = @"(?:\d+\.?\d*|\.\d+)";

        // TICKER + STRIKE + C/P or CALL/PUT, e.g. "SPY 757P", "IWM 288 CALL". Ticker stays case-sensitive to avoid matching prose.
        private static readonly Regex ContractRegex = new($@"\b([A-Z]{{1,5}})\s*({Num})\s*(?i:(CALL|PUT|C|P))\b", Opts);
        private static readonly Regex TickerHereRegex = new(@"\b([A-Z]{1,5})\s+here\b", OptsIc);
        private static readonly Regex DetachedContractRegex = new(
            $@"(?m)^\s*\d{{1,2}}/\d{{1,2}}(?:/\d{{2,4}})?\s+({Num})\s*([CPcp])\b", Opts);

        // TICKER + STRIKE with no C/P/CALL/PUT stated at all, e.g. "SPY 500 @ 1.20". Anchored to the start of its
        // own line (unlike ContractRegex) since without a right-hand marker this is far more likely to false-match prose.
        private static readonly Regex BareContractRegex = new($@"(?m)^\s*\$?([A-Z]{{1,5}})\s+({Num})\b(?!\s*%)", Opts);

        private static readonly Regex NamroodHeaderRegex = new(@"Namrood", OptsIc);
        private static readonly Regex SwiftHeaderRegex = new(@"SWIFT TRADES|WIFT TRADES|LIVE DESK|Trim Targets|Locked In|Open Live Dashboard", OptsIc);
        private static readonly Regex HandleTraderRegex = new(@"@((?=[A-Za-z0-9_.\-]*[A-Za-z_])[A-Za-z0-9_.\-]+)", Opts);

        // Explicit entry wording outranks everything, so a "Trim Targets" table in a BUY post cannot flip it to an exit.
        private static readonly Regex EntrySignalRegex = new(@"\bBTO\b|\bbuy\s+to\s+open\b|\bentered\b|\bentering\b|\bBUY\b\s*[-:]|\bBUY\b(?=\s+\$?[A-Z]{1,5}\s*\d)", OptsIc);
        private static readonly Regex AverageDownRegex = new(@"\baverag(?:e|ed|ing)\b|\badding\s+to\b", OptsIc);

        private static readonly Regex ArrowPriceRegex = new($@"\$?({Num})\s*(?:->|\s-\s)\s*\$?({Num})", Opts);
        private static readonly Regex EntryLabelRegex = new($@"\b(?:Entry|Added\s+\d+\s*@)\b\s*[:\-]?\s*\r?\n?\s*\$?({Num})", OptsIc);
        private static readonly Regex AveragePriceRegex = new($@"\bAvg\.?\s*[:\-]?\s*\$?({Num})", OptsIc);
        private static readonly Regex ExitLabelRegex = new($@"\b(?:Exit|avg)\b\.?\s*[:\-]?\s*\r?\n?\s*\$?({Num})", OptsIc);
        private static readonly Regex ShorthandAtPriceRegex = new($@"@\s*\$?({Num})\b", Opts);
        private static readonly Regex SignedContractPriceRegex = new(
            $@"(?m)^\s*[A-Z]{{1,5}}\s*{Num}\s*[CPcp]\b\s+\$?-({Num})(?:\s+@[A-Za-z0-9_\-]+)?\s*$", Opts);

        // Block form such as "INTC 97C 0DTE 0.9" or "SPCX 148C 9/18/2026 $2.88": premium is the last value on the line.
        private static readonly Regex LineTailPriceRegex = new(
            $@"(?m)^[^\r\n]*?\b[A-Z]{{1,5}}\s*{Num}\s*[CPcp]\b[^\r\n]*?\$?({Num})\s*$", Opts);

        public ParsedTrade? TryParse(string rawContent, Func<string, string, TradeContractContext?>? contextResolver = null)
        {
            if (string.IsNullOrWhiteSpace(rawContent)) return null;

            try
            {
                string content = MessageNormalizer.Normalize(rawContent);
                string trader = ResolveTrader(content);
                (string actionType, double trimFraction, bool sizingAmbiguous) = ResolveAction(content);

                var contractMatch = ContractRegex.Match(content);
                string ticker;
                double strike;
                string optionType;
                bool contractInferred = false;
                string? inferredExpiry = null;

                if (contractMatch.Success)
                {
                    ticker = contractMatch.Groups[1].Value.ToUpperInvariant();
                    strike = double.Parse(contractMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                    optionType = contractMatch.Groups[3].Value.ToUpperInvariant().StartsWith("C") ? "CALL" : "PUT";
                }
                else
                {
                    var tickerMatch = TickerHereRegex.Match(content);
                    var detachedContract = DetachedContractRegex.Match(content);

                    if (tickerMatch.Success && detachedContract.Success)
                    {
                        ticker = tickerMatch.Groups[1].Value.ToUpperInvariant();
                        strike = double.Parse(detachedContract.Groups[1].Value, CultureInfo.InvariantCulture);
                        optionType = detachedContract.Groups[2].Value.ToUpperInvariant() == "C" ? "CALL" : "PUT";
                    }
                    else if (tickerMatch.Success
                        && actionType is "SELL" or "TRIM" or "SOLD_ALL"
                        && contextResolver?.Invoke(trader, tickerMatch.Groups[1].Value.ToUpperInvariant()) is TradeContractContext context)
                    {
                        ticker = context.Ticker;
                        strike = context.Strike;
                        optionType = context.OptionType;
                        inferredExpiry = context.Expiration;
                        contractInferred = true;
                    }
                    else if (BareContractRegex.Match(content) is { Success: true } bareContract)
                    {
                        // No C/P/CALL/PUT stated anywhere: the trader's own convention is that this means a CALL.
                        ticker = bareContract.Groups[1].Value.ToUpperInvariant();
                        strike = double.Parse(bareContract.Groups[2].Value, CultureInfo.InvariantCulture);
                        optionType = "CALL";
                    }
                    else
                    {
                        Console.WriteLine("💡 [PARSER LOG] Skipped: Message does not contain a decipherable option contract structure.");
                        return null;
                    }
                }

                bool isExit = actionType is "SELL" or "TRIM" or "SOLD_ALL";
                double executionPrice = ResolvePrice(content, isExit);

                if (executionPrice == 0.0)
                {
                    Console.WriteLine("💡 [PARSER LOG] Skipped: Identified contract target but failed to resolve valid execution price.");
                    return null;
                }

                string? expiry = contractInferred
                    ? inferredExpiry
                    : ExpirationResolver.Resolve(content);
                if (expiry == null)
                {
                    Console.WriteLine("💡 [PARSER LOG] Skipped: No expiration date could be determined from the alert text.");
                    return null;
                }

                return new ParsedTrade(trader, actionType, ticker, optionType, strike, expiry,
                    executionPrice, ResolveRisk(content), trimFraction, sizingAmbiguous, contractInferred);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ [PARSER EXCEPTION]: Structural validation step crash: {ex.Message}");
                return null;
            }
        }

        private static (string Action, double Fraction, bool Ambiguous) ResolveAction(string content)
        {
            if (AverageDownRegex.IsMatch(content)) return ("AVERAGE_DOWN", 0.5, false);
            if (EntrySignalRegex.IsMatch(content)) return ("BUY", 0.5, false);

            var exit = ExitIntentResolver.Resolve(content);
            if (exit != null) return (exit.Action, exit.Fraction, exit.Ambiguous);

            // A bare "0.83 -> 0.35" progress update is a position update, never a fresh entry.
            if (ArrowPriceRegex.IsMatch(content)) return ("TRIM", 0.5, true);

            return ("BUY", 0.5, false);
        }

        private static double ResolvePrice(string content, bool isExit)
        {
            if (isExit)
            {
                if (TryPrice(ExitLabelRegex.Match(content), 1, out double exitPrice)) return exitPrice;
                if (TryPrice(ArrowPriceRegex.Match(content), 2, out double arrowPrice)) return arrowPrice;
            }
            else if (TryPrice(EntryLabelRegex.Match(content), 1, out double entryPrice))
            {
                return entryPrice;
            }

            if (!isExit && TryPrice(AveragePriceRegex.Match(content), 1, out double averagePrice)) return averagePrice;

            if (TryPrice(ShorthandAtPriceRegex.Match(content), 1, out double atPrice)) return atPrice;
            if (TryPrice(SignedContractPriceRegex.Match(content), 1, out double signedPrice)) return signedPrice;
            if (TryPrice(LineTailPriceRegex.Match(content), 1, out double tailPrice)) return tailPrice;

            return 0.0;
        }

        private static bool TryPrice(Match match, int group, out double price)
        {
            price = 0.0;
            if (!match.Success) return false;

            string raw = match.Groups[group].Value.Replace("$", string.Empty).Trim();
            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out price) && price > 0;
        }

        private static string ResolveTrader(string content)
        {
            // Namrood is checked first: those posts also carry a "LIVE DASHBOARD" link that resembles the Swift footer.
            if (NamroodHeaderRegex.IsMatch(content)) return "NAMROOD";
            if (SwiftHeaderRegex.IsMatch(content)) return "SWIFT TRADES";

            var handle = HandleTraderRegex.Match(content);
            if (handle.Success) return handle.Groups[1].Value.ToUpperInvariant();

            return "UNKNOWN";
        }

        private static string ResolveRisk(string content)
        {
            if (content.Contains("Super Lotto", StringComparison.OrdinalIgnoreCase)) return "SUPER_LOTTO";
            if (content.Contains("Lotto", StringComparison.OrdinalIgnoreCase)) return "LOTTO";
            return "STANDARD";
        }

        /// Best-effort read of whatever signals exist in the text, for diagnostics when TryParse yields nothing.
        /// Never throws and never fails the pipeline; this is console output only, not a trading decision.
        public static string Diagnose(string rawContent)
        {
            string content = MessageNormalizer.Normalize(rawContent);
            var lines = new System.Collections.Generic.List<string>();

            var contract = ContractRegex.Match(content);
            lines.Add(contract.Success
                ? $"Contract match : {contract.Groups[1].Value.ToUpperInvariant()} {contract.Groups[2].Value}{contract.Groups[3].Value.ToUpperInvariant()[0]}"
                : "Contract match : none (no TICKER + STRIKE + C/P or CALL/PUT pattern found)");

            var tickerHere = TickerHereRegex.Match(content);
            lines.Add(tickerHere.Success ? $"'... here' ticker: {tickerHere.Groups[1].Value.ToUpperInvariant()}" : "'... here' ticker: none");

            (string action, double fraction, bool ambiguous) = ResolveAction(content);
            lines.Add($"Action signal  : {action} (fraction {fraction:F2}, ambiguous={ambiguous})");

            bool isExit = action is "SELL" or "TRIM" or "SOLD_ALL";
            double price = ResolvePrice(content, isExit);
            lines.Add(price > 0 ? $"Price match    : {price}" : "Price match    : none");

            string? expiry = ExpirationResolver.Resolve(content);
            lines.Add(expiry != null ? $"Expiry match   : {expiry}" : "Expiry match   : none");

            lines.Add($"Trader guess   : {ResolveTrader(content)}");
            lines.Add($"Risk category  : {ResolveRisk(content)}");

            return string.Join("\n", lines);
        }
    }
}
