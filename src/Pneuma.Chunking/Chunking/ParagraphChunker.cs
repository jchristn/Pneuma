namespace Pneuma.Chunking.Chunking
{
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Splits text at paragraph boundaries (double newline).
    /// </summary>
    public static class ParagraphChunker
    {
        /// <summary>
        /// Chunk text by paragraph boundaries, grouping paragraphs to fill a token budget.
        /// </summary>
        /// <param name="text">Input text to chunk.</param>
        /// <param name="config">Chunking configuration.</param>
        /// <param name="tokenizer">Tokenizer adapter.</param>
        /// <param name="tokenLimit">Effective token budget.</param>
        /// <returns>List of chunk text strings.</returns>
        public static List<string> Chunk(string text, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            List<string> paragraphs = ChunkingHelpers.SplitParagraphs(text);
            if (paragraphs.Count == 0) return ChunkingHelpers.ChunkByTokenSpans(text, config, tokenizer, tokenLimit);

            return ChunkingHelpers.ChunkUnits(
                paragraphs,
                "\n\n",
                tokenLimit,
                tokenizer,
                ChunkingHelpers.GetUnitOverlapCount(config),
                paragraph => SentenceChunker.Chunk(paragraph, config, tokenizer, tokenLimit));
        }
    }
}
