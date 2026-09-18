namespace Pneuma.Chunking.Chunking
{
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Splits text into chunks of a fixed token count.
    /// </summary>
    public static class FixedTokenChunker
    {
        /// <summary>
        /// Chunk text into fixed-token-count segments with optional overlap.
        /// </summary>
        /// <param name="text">Input text to chunk.</param>
        /// <param name="config">Chunking configuration.</param>
        /// <param name="tokenizer">Tokenizer adapter.</param>
        /// <param name="tokenLimit">Effective token budget.</param>
        /// <returns>List of chunk text strings.</returns>
        public static List<string> Chunk(string text, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            return ChunkingHelpers.ChunkByTokenSpans(text, config, tokenizer, tokenLimit);
        }
    }
}
