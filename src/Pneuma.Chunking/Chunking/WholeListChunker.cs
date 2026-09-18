namespace Pneuma.Chunking.Chunking
{
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Treats the entire list as a single chunk.
    /// </summary>
    public static class WholeListChunker
    {
        /// <summary>
        /// Serialize an entire list into a single chunk.
        /// </summary>
        /// <param name="items">List items.</param>
        /// <param name="config">Chunking configuration.</param>
        /// <param name="ordered">Whether the list is ordered (numbered) or unordered (bulleted).</param>
        /// <param name="tokenizer">Tokenizer adapter.</param>
        /// <param name="tokenLimit">Effective token budget.</param>
        /// <returns>List containing a single chunk text string.</returns>
        public static List<string> Chunk(List<string> items, ChunkingConfiguration config, bool ordered, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (items == null || items.Count == 0) return new List<string>();

            List<string> lines = SerializeItems(items, ordered);
            string wholeList = string.Join("\n", lines);
            if (tokenizer.CountTokens(wholeList) <= tokenLimit)
                return new List<string> { wholeList };

            return ChunkingHelpers.ChunkUnits(
                lines,
                "\n",
                tokenLimit,
                tokenizer,
                0,
                item => ChunkingHelpers.ChunkByTokenSpans(item, config, tokenizer, tokenLimit));
        }

        internal static List<string> SerializeItems(List<string> items, bool ordered)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                if (ordered)
                    lines.Add($"{i + 1}. {items[i]}");
                else
                    lines.Add($"- {items[i]}");
            }

            return lines;
        }
    }
}
