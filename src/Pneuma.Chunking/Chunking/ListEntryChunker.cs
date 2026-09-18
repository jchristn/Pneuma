namespace Pneuma.Chunking.Chunking
{
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Each list item becomes its own chunk.
    /// </summary>
    public static class ListEntryChunker
    {
        /// <summary>
        /// Create one chunk per list item.
        /// </summary>
        /// <param name="items">List items.</param>
        /// <param name="config">Chunking configuration.</param>
        /// <param name="tokenizer">Tokenizer adapter.</param>
        /// <param name="tokenLimit">Effective token budget.</param>
        /// <returns>List of chunk text strings, one per item or token-span fallback.</returns>
        public static List<string> Chunk(List<string> items, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (items == null || items.Count == 0) return new List<string>();

            List<string> chunks = new List<string>();
            foreach (string item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (tokenizer.CountTokens(item) <= tokenLimit)
                    chunks.Add(item);
                else
                    chunks.AddRange(ChunkingHelpers.ChunkByTokenSpans(item, config, tokenizer, tokenLimit));
            }

            return chunks;
        }
    }
}
