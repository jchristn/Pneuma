namespace Test.Benchmark.Reference
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// An English analyzer in the spirit of Lucene's: lower-case alphanumeric tokens, possessive "'s" dropped,
    /// Lucene's default English stopwords removed, Porter-stemmed. Also strips HTML to text.
    /// </summary>
    public static class TextAnalyzer
    {
        #region Private-Members

        private static readonly HashSet<string> _Stopwords = new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "if", "in", "into", "is", "it", "no", "not", "of",
            "on", "or", "such", "that", "the", "their", "then", "there", "these", "they", "this", "to", "was", "will", "with"
        };

        private static readonly Regex _Tags = new Regex("<(script|style)[^>]*>.*?</\\1>|<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Analyze text into index terms.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Terms in order.</returns>
        public static List<string> Analyze(string text)
        {
            List<string> terms = new List<string>();
            if (string.IsNullOrEmpty(text)) return terms;
            StringBuilder token = new StringBuilder();
            string lower = text.ToLowerInvariant();
            for (int i = 0; i <= lower.Length; i++)
            {
                char c = i < lower.Length ? lower[i] : ' ';
                if (char.IsLetterOrDigit(c))
                {
                    token.Append(c);
                    continue;
                }

                // Possessive: "atlas's" -> "atlas" (the apostrophe ends the token; skip a following lone "s").
                if ((c == '\'' || c == '’') && i + 1 < lower.Length && lower[i + 1] == 's' && (i + 2 >= lower.Length || !char.IsLetterOrDigit(lower[i + 2])))
                {
                    i++;
                }

                Flush(token, terms);
            }

            return terms;
        }

        /// <summary>
        /// Crude HTML-to-text: drops scripts, styles, and tags, and decodes the common entities.
        /// </summary>
        /// <param name="html">HTML.</param>
        /// <returns>Text.</returns>
        public static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            string text = _Tags.Replace(html, " ");
            return System.Net.WebUtility.HtmlDecode(text);
        }

        /// <summary>
        /// Split text into word windows (a stand-in for token-based chunking in the reference arm).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="wordsPerChunk">Words per chunk.</param>
        /// <returns>Chunks.</returns>
        public static List<string> WordChunks(string text, int wordsPerChunk)
        {
            List<string> chunks = new List<string>();
            string[] words = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return chunks;
            for (int start = 0; start < words.Length; start += wordsPerChunk)
            {
                int count = Math.Min(wordsPerChunk, words.Length - start);
                chunks.Add(string.Join(' ', words, start, count));
            }

            return chunks;
        }

        #endregion

        #region Private-Methods

        private static void Flush(StringBuilder token, List<string> terms)
        {
            if (token.Length == 0) return;
            string word = token.ToString();
            token.Clear();
            if (_Stopwords.Contains(word)) return;
            terms.Add(PorterStemmer.Stem(word));
        }

        #endregion
    }
}
