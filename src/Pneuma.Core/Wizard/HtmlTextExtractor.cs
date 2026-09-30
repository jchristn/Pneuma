namespace Pneuma.Core.Wizard
{
    using System;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Turns a web page into plain text for the wizard's grounding: drops scripts, styles, and markup, decodes entities,
    /// and collapses whitespace. It is deliberately simple; ingestion uses DocumentAtom for real extraction.
    /// </summary>
    public static class HtmlTextExtractor
    {
        #region Private-Members

        private static readonly Regex _Blocks = new Regex(@"<(script|style|noscript|svg|head|nav|footer)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex _Breaks = new Regex(@"<(br|/p|/div|/li|/h[1-6]|/tr|/section|/article)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex _Tags = new Regex(@"<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex _Spaces = new Regex(@"[ \t\f\v]+", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex _Lines = new Regex(@"\s*\n\s*(\n\s*)+", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        #endregion

        #region Public-Methods

        /// <summary>Extract readable text from a response body.</summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="contentType">The response content type, or null.</param>
        /// <param name="maxCharacters">Most characters returned.</param>
        /// <returns>The text (possibly empty), or null when the content type is not text.</returns>
        public static string? Extract(byte[] body, string? contentType, int maxCharacters)
        {
            if (body == null || body.Length == 0) return String.Empty;
            string type = (contentType ?? String.Empty).ToLowerInvariant();
            bool html = type.Contains("html") || type.Length == 0;
            if (!html && !type.StartsWith("text/", StringComparison.Ordinal) && !type.Contains("json") && !type.Contains("xml")) return null;

            string text = Encoding.UTF8.GetString(body);
            if (html)
            {
                text = _Blocks.Replace(text, " ");
                text = _Breaks.Replace(text, "\n");
                text = _Tags.Replace(text, " ");
                text = WebUtility.HtmlDecode(text);
            }
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            text = _Spaces.Replace(text, " ");
            text = _Lines.Replace(text, "\n\n").Trim();
            int limit = Math.Max(0, maxCharacters);
            return text.Length <= limit ? text : text.Substring(0, limit);
        }

        #endregion
    }
}
