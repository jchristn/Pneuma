namespace Pneuma.Chunking.Enums
{
    /// <summary>
    /// Chunking strategy for splitting content.
    /// </summary>
    public enum ChunkStrategyEnum
    {
        /// <summary>Split into fixed token count chunks.</summary>
        FixedTokenCount,
        /// <summary>Split at sentence boundaries.</summary>
        SentenceBased,
        /// <summary>Split at paragraph boundaries.</summary>
        ParagraphBased,
        /// <summary>Treat entire list as a single chunk.</summary>
        WholeList,
        /// <summary>Each list entry becomes its own chunk.</summary>
        ListEntry,
        /// <summary>Each table data row as space-separated values (no headers).</summary>
        Row,
        /// <summary>Each table data row as a markdown table with headers prepended.</summary>
        RowWithHeaders,
        /// <summary>Groups of N table rows with headers prepended (configurable via RowGroupSize).</summary>
        RowGroupWithHeaders,
        /// <summary>Each table row as key-value pairs (e.g. "col1: val1, col2: val2").</summary>
        KeyValuePairs,
        /// <summary>Entire table as a single markdown table chunk.</summary>
        WholeTable,
        /// <summary>Split at boundaries defined by a user-supplied regular expression.</summary>
        RegexBased
    }
}
