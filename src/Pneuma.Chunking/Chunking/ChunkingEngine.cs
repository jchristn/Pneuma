namespace Pneuma.Chunking.Chunking
{
    using Pneuma.Chunking.Enums;
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;
    using SyslogLogging;

    /// <summary>
    /// Factory/dispatcher for chunking operations across different strategies and atom types.
    /// </summary>
    public class ChunkingEngine
    {
        /// <summary>
        /// Initialize a new ChunkingEngine.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public ChunkingEngine(LoggingModule logging)
        {
        }

        /// <summary>
        /// Chunk a semantic cell request into text chunks (before embedding).
        /// </summary>
        /// <param name="request">Semantic cell request.</param>
        /// <param name="tokenizer">Tokenizer adapter for the active embedding profile.</param>
        /// <param name="tokenBudget">Effective token budget to enforce while chunking.</param>
        /// <returns>List of chunk results with text but no embeddings yet.</returns>
        public List<ChunkResult> Chunk(SemanticCellRequest request, ITokenizerAdapter tokenizer, int tokenBudget)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (tokenizer == null) throw new ArgumentNullException(nameof(tokenizer));
            if (tokenBudget < 1) throw new ArgumentOutOfRangeException(nameof(tokenBudget));

            List<string> rawChunks = GetRawChunks(request, tokenizer, tokenBudget);

            List<ChunkResult> results = new List<ChunkResult>();

            foreach (string chunk in rawChunks)
            {
                ChunkResult result = new ChunkResult();
                result.Text = chunk;
                results.Add(result);
            }

            return results;
        }

        private List<string> GetRawChunks(SemanticCellRequest request, ITokenizerAdapter tokenizer, int tokenBudget)
        {
            ChunkingConfiguration config = request.ChunkingConfiguration;

            switch (request.Type)
            {
                case AtomTypeEnum.Text:
                case AtomTypeEnum.Code:
                case AtomTypeEnum.Hyperlink:
                case AtomTypeEnum.Meta:
                    return ChunkText(request.Text ?? string.Empty, config, tokenizer, tokenBudget);

                case AtomTypeEnum.List:
                    return ChunkList(request, config, tokenizer, tokenBudget);

                case AtomTypeEnum.Table:
                    return ChunkTable(request, config, tokenizer, tokenBudget);

                case AtomTypeEnum.Binary:
                case AtomTypeEnum.Image:
                    if (!string.IsNullOrEmpty(request.Text))
                        return ChunkText(request.Text, config, tokenizer, tokenBudget);
                    return new List<string>();

                case AtomTypeEnum.Unknown:
                default:
                    if (!string.IsNullOrEmpty(request.Text))
                        return ChunkText(request.Text, config, tokenizer, tokenBudget);
                    return new List<string>();
            }
        }

        private List<string> ChunkText(string text, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenBudget)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            switch (config.Strategy)
            {
                case ChunkStrategyEnum.FixedTokenCount:
                    return FixedTokenChunker.Chunk(text, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.SentenceBased:
                    return SentenceChunker.Chunk(text, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.ParagraphBased:
                    return ParagraphChunker.Chunk(text, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.RegexBased:
                    return RegexChunker.Chunk(text, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.WholeList:
                    if (tokenizer.CountTokens(text) <= tokenBudget)
                        return new List<string> { text };
                    return ChunkingHelpers.ChunkByTokenSpans(text, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.ListEntry:
                    return ChunkingHelpers.ChunkUnits(
                        text.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToList(),
                        "\n",
                        tokenBudget,
                        tokenizer,
                        0,
                        line => ChunkingHelpers.ChunkByTokenSpans(line, config, tokenizer, tokenBudget));

                default:
                    return FixedTokenChunker.Chunk(text, config, tokenizer, tokenBudget);
            }
        }

        private List<string> ChunkList(SemanticCellRequest request, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenBudget)
        {
            List<string>? items = request.OrderedList ?? request.UnorderedList;
            if (items == null || items.Count == 0) return new List<string>();

            bool ordered = request.OrderedList != null;

            switch (config.Strategy)
            {
                case ChunkStrategyEnum.WholeList:
                    return WholeListChunker.Chunk(items, config, ordered, tokenizer, tokenBudget);

                case ChunkStrategyEnum.ListEntry:
                    return ListEntryChunker.Chunk(items, config, tokenizer, tokenBudget);

                default:
                    string serialized = SerializeList(items, ordered);
                    return ChunkText(serialized, config, tokenizer, tokenBudget);
            }
        }

        private List<string> ChunkTable(SemanticCellRequest request, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenBudget)
        {
            if (request.Table == null || request.Table.Count == 0) return new List<string>();

            switch (config.Strategy)
            {
                case ChunkStrategyEnum.Row:
                    return TableChunker.ChunkByRow(request.Table, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.RowWithHeaders:
                    return TableChunker.ChunkByRowWithHeaders(request.Table, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.RowGroupWithHeaders:
                    return TableChunker.ChunkByRowGroupWithHeaders(request.Table, config.RowGroupSize, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.KeyValuePairs:
                    return TableChunker.ChunkByKeyValuePairs(request.Table, config, tokenizer, tokenBudget);

                case ChunkStrategyEnum.WholeTable:
                    return TableChunker.ChunkWholeTable(request.Table, config.RowGroupSize, config, tokenizer, tokenBudget);

                default:
                    string serialized = SerializeTable(request.Table);
                    return ChunkText(serialized, config, tokenizer, tokenBudget);
            }
        }

        private string SerializeList(List<string> items, bool ordered)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                if (ordered)
                    lines.Add($"{i + 1}. {items[i]}");
                else
                    lines.Add($"- {items[i]}");
            }
            return string.Join("\n", lines);
        }

        private string SerializeTable(List<List<string>> table)
        {
            List<string> lines = new List<string>();
            foreach (List<string> row in table)
            {
                lines.Add(string.Join(" | ", row));
            }
            return string.Join("\n", lines);
        }
    }
}
