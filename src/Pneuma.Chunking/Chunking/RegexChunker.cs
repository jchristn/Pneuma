namespace Pneuma.Chunking.Chunking
{
    using System.Text.RegularExpressions;
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Splits text at boundaries defined by a user-supplied regular expression.
    /// </summary>
    public static class RegexChunker
    {
        /// <summary>
        /// Chunk text by regex-defined boundaries.
        /// </summary>
        public static List<string> Chunk(
            string text,
            ChunkingConfiguration config,
            ITokenizerAdapter tokenizer,
            int tokenLimit)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            if (string.IsNullOrEmpty(config.RegexPattern))
                throw new ArgumentException("RegexPattern is required when using RegexBased strategy.");

            Regex regex = new Regex(
                config.RegexPattern,
                RegexOptions.Compiled | RegexOptions.Multiline,
                TimeSpan.FromSeconds(5));

            List<string> filtered = regex.Split(text)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (filtered.Count == 0) return ChunkingHelpers.ChunkByTokenSpans(text, config, tokenizer, tokenLimit);

            List<string> chunks = new List<string>();
            foreach (string segment in filtered)
            {
                if (tokenizer.CountTokens(segment) <= tokenLimit)
                {
                    chunks.Add(segment);
                }
                else
                {
                    chunks.AddRange(ChunkingHelpers.ChunkByTokenSpans(segment, config, tokenizer, tokenLimit));
                }
            }

            return chunks;
        }
    }
}
