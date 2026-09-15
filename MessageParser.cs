using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OptionTradesParser
{
    public record ParsedTrade(string TraderName, string ActionType, string Ticker, string OptionType, double Strike, string Expiration, double PricePaid, string RiskCategory);

    public class MessageParser
    {
        // Flexible Option Contract Matcher (Handles strings with or without $ signs, spaces, or uppercase/lowercase)
        private static readonly Regex UniversalOptionRegex = new Regex(@"\$?([A-Z]{1,5})\s*(\d+(?:\.\d+)?)\s*([CPcp])\b", RegexOptions.Compiled);

        // Core Layout Target Signatures
        private static readonly Regex SwiftHeaderRegex = new Regex(@"SWIFT TRADES|WIFT TRADES", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NamroodHeaderRegex = new Regex(@"Namrood", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HandleTraderRegex = new Regex(@"@([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

        // Pricing Target Evaluators
        private static readonly Regex ArrowPriceRegex = new Regex(@"\$?(\d+(?:\.\d+)?)\s*→\s*([$]?\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex SwiftEntryLabelRegex = new Regex(@"(?:Entry|Added\s+\d+\s*@)\s*\r?\n?\$?(\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ShorthandAtPriceRegex = new Regex(@"@\s*\$?(\d+(?:\.\d+)?)\b", RegexOptions.Compiled);
        
        // 🔥 FIXED: Handles tightly grouped contracts (e.g., INTC 97C 0DTE 0.9 or INTC 97C 0.9)
        private static readonly Regex NamroodFlatPriceRegex = new Regex(@"\b([A-Z]{1,5})\s*(\d+(?:\.\d+)?)\s*([CPcp])(?:\s+\d+DTE)?\s+(\d+(?:\.\d+)?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Expiration Target Date Filters
        private static readonly Regex SlashDateRegex = new Regex(@"\b(\d{1,2})\/(\d{1,2})(?:\/\d{2,4})?\b", RegexOptions.Compiled);
        private static readonly Regex DetailedDateRegex = new Regex(@"\b([A-Za-z]{3})\s*(\d{1,2})\s*'\s*(\d{2})\b", RegexOptions.Compiled);
        private static readonly Regex ShorthandDateRegex = new Regex(@"\b([A-Za-z]{3})\s*(\d{1,2})\b", RegexOptions.Compiled);

        public ParsedTrade? TryParse(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            try
            {
                // 1. Core Structural Contract Check
                var contractMatch = UniversalOptionRegex.Match(content);
                if (!contractMatch.Success)
                {
                    Console.WriteLine("💡 [PARSER LOG] Skipped: Message does not contain a decipherable option contract structure.");
                    return null;
                }

                string ticker = contractMatch.Groups[1].Value.ToUpper();
                double strike = double.Parse(contractMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                string optionType = contractMatch.Groups[3].Value.ToUpper() == "C" ? "CALL" : "PUT";

                // 2. Adaptive Trader Name Discovery
                string traderName = "UNKNOWN";
                var handleMatch = HandleTraderRegex.Match(content);

                if (SwiftHeaderRegex.IsMatch(content) || content.Contains("Live Dashboard") || content.Contains("Trim Targets"))
                    traderName = "SWIFT TRADES";
                else if (NamroodHeaderRegex.IsMatch(content))
                    traderName = "NAMROOD";
                else if (handleMatch.Success)
                    traderName = handleMatch.Groups[1].Value.ToUpper(); 
                else if (content.Contains("BTO") || content.Contains("STC"))
                    traderName = "COMMUNITY_TRADER"; 

                // 3. Classify Risk Profile
                string riskCategory = "STANDARD";
                if (content.Contains("Super Lotto", StringComparison.OrdinalIgnoreCase)) riskCategory = "SUPER_LOTTO";
                else if (content.Contains("Lotto", StringComparison.OrdinalIgnoreCase)) riskCategory = "LOTTO";

                // 4. Discover Trade Action & Execution Price
                string actionType = "BUY";
                double executionPrice = 0.0;

                var arrowMatch = ArrowPriceRegex.Match(content);
                var entryLabelMatch = SwiftEntryLabelRegex.Match(content);
                var shorthandAtMatch = ShorthandAtPriceRegex.Match(content);
                var inlineMatch = NamroodFlatPriceRegex.Match(content);

                // Check Action Keywords
                if (content.Contains("BTO", StringComparison.OrdinalIgnoreCase) || content.Contains("BUY", StringComparison.OrdinalIgnoreCase) || content.Contains("Entered", StringComparison.OrdinalIgnoreCase))
                    actionType = "BUY";
                else if (content.Contains("STC", StringComparison.OrdinalIgnoreCase) || content.Contains("Trim", StringComparison.OrdinalIgnoreCase) || content.Contains("Close", StringComparison.OrdinalIgnoreCase) || content.Contains("SOLD ALL", StringComparison.OrdinalIgnoreCase))
                    actionType = "SELL";
                else if (content.Contains("AVERAGE", StringComparison.OrdinalIgnoreCase) || content.Contains("AVERAGING", StringComparison.OrdinalIgnoreCase))
                    actionType = "AVERAGE_DOWN";

                // Isolate target pricing value
                if (arrowMatch.Success)
                {
                    actionType = (actionType == "BUY") ? "TRIM" : actionType; 
                    string cleanRight = arrowMatch.Groups[2].Value.Replace("$", "").Trim();
                    executionPrice = double.Parse(cleanRight, CultureInfo.InvariantCulture);
                }
                else if (entryLabelMatch.Success)
                {
                    string cleanPrice = entryLabelMatch.Groups[1].Value.Replace("$", "").Trim();
                    executionPrice = double.Parse(cleanPrice, CultureInfo.InvariantCulture);
                }
                else if (shorthandAtMatch.Success)
                {
                    executionPrice = double.Parse(shorthandAtMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                else if (inlineMatch.Success)
                {
                    // For inline configurations, the 4th capture group handles the trailing premium value
                    string cleanPrice = inlineMatch.Groups[4].Value.Replace("$", "").Trim();
                    executionPrice = double.Parse(cleanPrice, CultureInfo.InvariantCulture);
                }

                if (executionPrice == 0.0)
                {
                    Console.WriteLine("💡 [PARSER LOG] Skipped: Identified contract target but failed to resolve valid execution price.");
                    return null;
                }

                // 5. Expiration Code Normalizer (Target: YYYYMMDD)
                string formattedExpiry = DateTime.UtcNow.ToString("yyyyMMdd");

                if (!content.Contains("0DTE", StringComparison.OrdinalIgnoreCase))
                {
                    var slashDateMatch = SlashDateRegex.Match(content);
                    var detailedMatch = DetailedDateRegex.Match(content);
                    var shorthandMatch = ShorthandDateRegex.Match(content);

                    if (slashDateMatch.Success)
                    {
                        int month = int.Parse(slashDateMatch.Groups[1].Value);
                        int day = int.Parse(slashDateMatch.Groups[2].Value);
                        formattedExpiry = new DateTime(DateTime.UtcNow.Year, month, day).ToString("yyyyMMdd");
                    }
                    else if (detailedMatch.Success)
                    {
                        string rawDate = $"{detailedMatch.Groups[1].Value} {detailedMatch.Groups[2].Value} 20{detailedMatch.Groups[3].Value}";
                        if (DateTime.TryParseExact(rawDate, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime pDate))
                        {
                            formattedExpiry = pDate.ToString("yyyyMMdd");
                        }
                    }
                    else if (shorthandMatch.Success && !shorthandMatch.Value.Equals(ticker, StringComparison.OrdinalIgnoreCase))
                    {
                        string rawDate = $"{shorthandMatch.Groups[1].Value} {shorthandMatch.Groups[2].Value} {DateTime.UtcNow.Year}";
                        if (DateTime.TryParseExact(rawDate, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime pDate))
                        {
                            formattedExpiry = pDate.ToString("yyyyMMdd");
                        }
                    }
                }

                return new ParsedTrade(traderName, actionType, ticker, optionType, strike, formattedExpiry, executionPrice, riskCategory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ [PARSER EXCEPTION]: Structural validation step crash: {ex.Message}");
                return null;
            }
        }
    }
}
