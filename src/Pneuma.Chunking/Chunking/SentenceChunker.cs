namespace Pneuma.Chunking.Chunking
{
    using System.Text.RegularExpressions;
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Splits text at sentence boundaries, grouping sentences to fill a token budget.
    /// </summary>
    public static class SentenceChunker
    {
        internal static readonly Regex SentencePattern = new Regex(
            @"(?<=[.!?])\s+",
            RegexOptions.Compiled);

        /// <summary>
        /// Chunk text by sentence boundaries.
        /// </summary>
        /// <param name="text">Input text to chunk.</param>
        /// <param name="config">Chunking configuration.</param>
        /// <param name="tokenizer">Tokenizer adapter.</param>
        /// <param name="tokenLimit">Effective token budget.</param>
        /// <returns>List of chunk text strings.</returns>
        public static List<string> Chunk(string text, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            List<string> sentences = ChunkingHelpers.SplitSentences(text);
            if (sentences.Count == 0) return ChunkingHelpers.ChunkByTokenSpans(text, config, tokenizer, tokenLimit);

            return ChunkingHelpers.ChunkUnits(
                sentences,
                " ",
                tokenLimit,
                tokenizer,
                ChunkingHelpers.GetUnitOverlapCount(config),
                sentence => ChunkingHelpers.ChunkByTokenSpans(sentence, config, tokenizer, tokenLimit));
        }
    }
}
