using System.Text.RegularExpressions;

namespace OptionTradesParser
{
    /// Strips Discord markdown, links and emoji images so the matchers see plain text rather than formatting.
    public static class MessageNormalizer
    {
        private const RegexOptions Opts = RegexOptions.Compiled;

        private static readonly Regex ImageMarkdown = new(@"!\[[^\]]*\]\([^)]*\)", Opts);
        private static readonly Regex LinkMarkdown = new(@"\[([^\]]*)\]\([^)]*\)", Opts);
        private static readonly Regex BareUrl = new(@"https?://\S+", Opts);
        private static readonly Regex Emphasis = new(@"```|~~|\*\*|__|[`*]", Opts);
        private static readonly Regex Arrows = new(@"[\u2192\u279C\u21D2\u27A1]|=>", Opts);
        private static readonly Regex Separators = new(@"[\u00B7\u2022|]", Opts);
        private static readonly Regex HorizontalSpace = new(@"[ \t\u00A0]+", Opts);

        public static string Normalize(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

            // Images must go before links: ![alt](url) also matches the plain link pattern.
            string text = ImageMarkdown.Replace(content, " ");
            text = LinkMarkdown.Replace(text, "$1");
            text = BareUrl.Replace(text, " ");
            text = Emphasis.Replace(text, string.Empty);
            text = Arrows.Replace(text, "->");
            text = Separators.Replace(text, " ");
            text = text.Replace('\u2014', '-').Replace('\u2013', '-').Replace('\u00D7', 'x');

            return HorizontalSpace.Replace(text, " ");
        }
    }
}
