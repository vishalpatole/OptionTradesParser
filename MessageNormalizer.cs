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

        // Discord's "ansi" code blocks wrap highlighted tokens in real ANSI color-escape control characters
        // (ESC=\x1B), e.g. "BAC" becomes "\x1B[0;33mBAC\x1B[0m". Left in place these sit between a ticker and its
        // strike and break every regex that expects only whitespace there, so they must be stripped first.
        private static readonly Regex AnsiEscape = new("\u001B\\[[0-9;]*[A-Za-z]", Opts);

        // A fence's language tag (```ansi, ```diff, ...) only counts as a tag when it sits alone on its own line;
        // an inline block like "```INTC 97C 0DTE 0.9```" has no tag at all, and INTC must not be swallowed with it.
        private static readonly Regex Emphasis = new(@"```[a-zA-Z]+\r?\n|```|~~|\*\*|__|[`*]", Opts);
        private static readonly Regex Arrows = new(@"[\u2192\u279C\u21D2\u27A1]|=>", Opts);
        private static readonly Regex Separators = new(@"[\u00B7\u2022|]", Opts);
        private static readonly Regex HorizontalSpace = new(@"[ \t\u00A0]+", Opts);

        public static string Normalize(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

            // Must run first: a stray '[' from an ANSI code can otherwise be greedily captured by LinkMarkdown
            // as a fake link stretching all the way to the next real "](url)", corrupting everything between them.
            string text = AnsiEscape.Replace(content, string.Empty);

            // Images must go before links: ![alt](url) also matches the plain link pattern.
            text = ImageMarkdown.Replace(text, " ");
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
