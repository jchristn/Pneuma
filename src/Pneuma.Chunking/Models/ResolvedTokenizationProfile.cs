namespace Pneuma.Chunking.Models
{
    using Pneuma.Chunking.Enums;

    /// <summary>
    /// Runtime tokenization contract used for chunking, budgeting, batching, and diagnostics.
    /// </summary>
    public class ResolvedTokenizationProfile
    {
        private int _MaxInputTokens = 1;
        private int _ReservedInputTokens = 0;
        private int _EffectiveInputBudget = 1;

        /// <summary>
        /// Active tokenizer family.
        /// </summary>
        public TokenizerKindEnum TokenizerKind { get; set; } = TokenizerKindEnum.Cl100kBase;

        /// <summary>
        /// Active tokenizer model or vocabulary identifier.
        /// </summary>
        public string TokenizerModel { get; set; } = "cl100k_base";

        /// <summary>
        /// Upstream maximum accepted input tokens.
        /// </summary>
        public int MaxInputTokens
        {
            get => _MaxInputTokens;
            set => _MaxInputTokens = value >= 1
                ? value
                : throw new ArgumentOutOfRangeException(nameof(MaxInputTokens), "MaxInputTokens must be at least 1.");
        }

        /// <summary>
        /// Tokens reserved before chunking begins.
        /// </summary>
        public int ReservedInputTokens
        {
            get => _ReservedInputTokens;
            set => _ReservedInputTokens = value >= 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(ReservedInputTokens), "ReservedInputTokens must be at least 0.");
        }

        /// <summary>
        /// Effective per-input chunking budget after reserved tokens are removed.
        /// </summary>
        public int EffectiveInputBudget
        {
            get => _EffectiveInputBudget;
            set => _EffectiveInputBudget = value >= 1
                ? value
                : throw new ArgumentOutOfRangeException(nameof(EffectiveInputBudget), "EffectiveInputBudget must be at least 1.");
        }

        /// <summary>
        /// Batch limit behavior for the active endpoint.
        /// </summary>
        public BatchLimitModeEnum BatchLimitMode { get; set; } = BatchLimitModeEnum.PerInput;

        /// <summary>
        /// Source of the resolved profile.
        /// </summary>
        public TokenizationProfileSourceEnum ProfileSource { get; set; } = TokenizationProfileSourceEnum.GlobalFallback;

        /// <summary>
        /// True when resolution descended past endpoint override or provider probe.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// Provider-specific metadata captured during capability resolution.
        /// </summary>
        public Dictionary<string, string> ProviderMetadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
