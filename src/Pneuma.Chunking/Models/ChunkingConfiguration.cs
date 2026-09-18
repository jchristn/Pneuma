namespace Pneuma.Chunking.Models
{
    using Pneuma.Chunking.Enums;

    /// <summary>
    /// Configuration for how content is chunked.
    /// </summary>
    public class ChunkingConfiguration
    {
        private ChunkStrategyEnum _Strategy = ChunkStrategyEnum.FixedTokenCount;
        private int _FixedTokenCount = 256;
        private int _OverlapCount = 0;
        private double? _OverlapPercentage = null;
        private OverlapStrategyEnum _OverlapStrategy = OverlapStrategyEnum.SlidingWindow;
        private int _RowGroupSize = 5;
        private string? _ContextPrefix = null;
        private string? _RegexPattern = null;

        /// <summary>
        /// Chunking strategy to use.
        /// </summary>
        /// <remarks>Default: FixedTokenCount.</remarks>
        public ChunkStrategyEnum Strategy
        {
            get => _Strategy;
            set => _Strategy = value;
        }

        /// <summary>
        /// Number of tokens per chunk (for FixedTokenCount strategy).
        /// </summary>
        /// <remarks>Default: 256. Minimum: 1.</remarks>
        public int FixedTokenCount
        {
            get => _FixedTokenCount;
            set => _FixedTokenCount = (value >= 1)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(FixedTokenCount), "FixedTokenCount must be at least 1.");
        }

        /// <summary>
        /// Number of tokens/characters to overlap between chunks.
        /// </summary>
        /// <remarks>Default: 0. Minimum: 0.</remarks>
        public int OverlapCount
        {
            get => _OverlapCount;
            set => _OverlapCount = (value >= 0)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(OverlapCount), "OverlapCount must be at least 0.");
        }

        /// <summary>
        /// Alternative overlap as a percentage of chunk size (0.0–1.0).
        /// </summary>
        public double? OverlapPercentage
        {
            get => _OverlapPercentage;
            set
            {
                if (value.HasValue && (value.Value < 0.0 || value.Value > 1.0))
                    throw new ArgumentOutOfRangeException(nameof(OverlapPercentage), "OverlapPercentage must be between 0.0 and 1.0.");
                _OverlapPercentage = value;
            }
        }

        /// <summary>
        /// Strategy for handling overlap boundaries.
        /// </summary>
        /// <remarks>Default: SlidingWindow.</remarks>
        public OverlapStrategyEnum OverlapStrategy
        {
            get => _OverlapStrategy;
            set => _OverlapStrategy = value;
        }

        /// <summary>
        /// Number of rows per group (for RowGroupWithHeaders strategy).
        /// </summary>
        /// <remarks>Default: 5. Minimum: 1.</remarks>
        public int RowGroupSize
        {
            get => _RowGroupSize;
            set => _RowGroupSize = (value >= 1)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(RowGroupSize), "RowGroupSize must be at least 1.");
        }

        /// <summary>
        /// Text prefix prepended to each chunk before embedding.
        /// </summary>
        public string? ContextPrefix
        {
            get => _ContextPrefix;
            set => _ContextPrefix = value;
        }

        /// <summary>
        /// Regular expression pattern used as a split delimiter (for RegexBased strategy).
        /// The text is split at every match of this pattern.
        /// </summary>
        public string? RegexPattern
        {
            get => _RegexPattern;
            set => _RegexPattern = value;
        }
    }
}
