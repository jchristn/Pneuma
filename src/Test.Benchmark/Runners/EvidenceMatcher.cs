namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Test.Benchmark.Reference;

    /// <summary>
    /// Decides whether gold evidence spans appear in retrieved text: a normalized (lower-case, whitespace-collapsed,
    /// HTML-stripped) substring match, falling back to 80% token coverage within a single passage for spans that a
    /// chunk boundary split.
    /// </summary>
    public static class EvidenceMatcher
    {
        #region Public-Methods

        /// <summary>
        /// Share of evidence spans found in any of the passages.
        /// </summary>
        /// <param name="evidence">Gold spans.</param>
        /// <param name="passages">Retrieved passages.</param>
        /// <returns>0..1, or null when there is no evidence.</returns>
        public static double? Coverage(IReadOnlyList<string>? evidence, IEnumerable<string?> passages)
        {
            if (evidence == null || evidence.Count == 0) return null;
            List<string> normalized = passages.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Normalize(p!)).ToList();
            if (normalized.Count == 0) return 0.0;
            int found = 0;
            foreach (string span in evidence)
            {
                if (Found(Normalize(span), normalized)) found++;
            }

            return (double)found / evidence.Count;
        }

        /// <summary>
        /// Normalize text for matching.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Normalized text.</returns>
        public static string Normalize(string text)
        {
            string stripped = text.IndexOf('<') >= 0 ? TextAnalyzer.StripHtml(text) : text;
            StringBuilder sb = new StringBuilder(stripped.Length);
            bool space = false;
            foreach (char raw in stripped)
            {
                char c = char.ToLowerInvariant(raw);
                if (c == '*' || c == '`' || c == '_') continue;
                if (char.IsWhiteSpace(c))
                {
                    if (!space && sb.Length > 0) sb.Append(' ');
                    space = true;
                    continue;
                }

                sb.Append(c);
                space = false;
            }

            return sb.ToString().Trim();
        }

        #endregion

        #region Private-Methods

        private static bool Found(string span, List<string> passages)
        {
            if (span.Length == 0) return true;
            foreach (string passage in passages)
            {
                if (passage.Contains(span, StringComparison.Ordinal)) return true;
            }

            HashSet<string> spanTokens = Tokens(span);
            if (spanTokens.Count == 0) return false;
            foreach (string passage in passages)
            {
                HashSet<string> passageTokens = Tokens(passage);
                int covered = spanTokens.Count(t => passageTokens.Contains(t));
                if (covered >= 0.8 * spanTokens.Count) return true;
            }

            return false;
        }

        private static HashSet<string> Tokens(string text)
        {
            HashSet<string> tokens = new HashSet<string>(StringComparer.Ordinal);
            StringBuilder token = new StringBuilder();
            foreach (char c in text + " ")
            {
                if (char.IsLetterOrDigit(c))
                {
                    token.Append(c);
                }
                else if (token.Length > 0)
                {
                    if (token.Length > 1) tokens.Add(token.ToString());
                    token.Clear();
                }
            }

            return tokens;
        }

        #endregion
    }
}
