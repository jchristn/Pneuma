namespace Pneuma.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// How a cell of text should be chunked. Resolved per subject at ingestion time and passed to the
    /// semantic processor, so different subjects (short FAQ entries vs. long technical documents) can be
    /// chunked appropriately instead of sharing one fixed strategy. When null is passed to the processor the
    /// backend defaults are used.
    /// </summary>
    public class ChunkingOptions
    {
        #region Public-Members

        /// <summary>
        /// The chunking strategies supported here — the subset of the chunker's <c>Strategy</c> enum that applies to
        /// the free-text cells reaching the chunker (table/list/regex strategies need structure or parameters we
        /// do not supply at this stage). The first entry is the default. Exposed so dashboards can offer exactly
        /// the supported values.
        /// </summary>
        public static readonly IReadOnlyList<string> SupportedStrategies = new List<string> { "FixedTokenCount", "SentenceBased", "ParagraphBased", "Recursive" };

        /// <summary>
        /// Chunking strategy passed to the chunker's <c>ChunkingConfiguration.Strategy</c>. Coerced to one of
        /// <see cref="SupportedStrategies"/> (case-insensitively); an unrecognized value falls back to the
        /// default "FixedTokenCount" so an invalid strategy is never used.
        /// </summary>
        public string Strategy
        {
            get { return _Strategy; }
            set { _Strategy = Canonicalize(value); }
        }

        /// <summary>
        /// Target chunk size in tokens, sent as the chunker's <c>ChunkingConfiguration.FixedTokenCount</c> (used by
        /// the FixedTokenCount strategy). Default 256; clamped to [16, 8192].
        /// </summary>
        public int MaxTokens
        {
            get { return _MaxTokens; }
            set { _MaxTokens = Math.Clamp(value, 16, 8192); }
        }

        /// <summary>
        /// The embedding model the chunks are sized for (for example "nomic-embed-text"). The chunker resolves the
        /// model's tokenizer family and input budget from it, so chunk sizes are counted in the tokens that model
        /// actually uses. Null counts in cl100k_base.
        /// </summary>
        public string? ModelId { get; set; } = null;

        /// <summary>Overlap between adjacent chunks in tokens, sent as the chunker's <c>OverlapCount</c>. Default 32; clamped to [0, 4096].</summary>
        /// <summary>
        /// Context header embedded in front of each chunk (the document title and section headings), or null for none.
        /// Its token cost is taken out of the chunk budget so header plus chunk still fits the model. The stored chunk
        /// text never includes it.
        /// </summary>
        public string? ContextHeader { get; set; } = null;

        /// <summary>
        /// Override of the embedding model's input limit in tokens, or null to use the known limit for the model
        /// family. Chunks are sized to fit it less the safety margin. Minimum 1 when set.
        /// </summary>
        public int? EffectiveInputBudget { get; set; } = null;

        /// <summary>
        /// Fraction of the model's input limit held back, for runtimes whose tokenizer counts slightly more than the
        /// local one. Default 0.01; clamped to [0, 0.5].
        /// </summary>
        public double SafetyMarginPercentage
        {
            get { return _SafetyMarginPercentage; }
            set { _SafetyMarginPercentage = Math.Clamp(value, 0.0, 0.5); }
        }

        /// <summary>Tokens held back from the model's input limit in addition to the percentage. Default 2; clamped to [0, 1024].</summary>
        public int SafetyMarginTokens
        {
            get { return _SafetyMarginTokens; }
            set { _SafetyMarginTokens = Math.Clamp(value, 0, 1024); }
        }

        /// <summary>Copy these options.</summary>
        /// <returns>A copy.</returns>
        public ChunkingOptions Clone()
        {
            return new ChunkingOptions
            {
                Strategy = Strategy,
                MaxTokens = MaxTokens,
                ModelId = ModelId,
                OverlapCount = OverlapCount,
                ContextHeader = ContextHeader,
                EffectiveInputBudget = EffectiveInputBudget,
                SafetyMarginPercentage = SafetyMarginPercentage,
                SafetyMarginTokens = SafetyMarginTokens
            };
        }

        public int OverlapCount
        {
            get { return _OverlapCount; }
            set { _OverlapCount = Math.Clamp(value, 0, 4096); }
        }

        #endregion

        #region Private-Members

        private string _Strategy = "FixedTokenCount";
        private double _SafetyMarginPercentage = 0.01;
        private int _SafetyMarginTokens = 2;
        private int _MaxTokens = 256;
        private int _OverlapCount = 32;

        #endregion

        #region Private-Methods

        private static string Canonicalize(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "FixedTokenCount";
            foreach (string supported in SupportedStrategies)
            {
                if (String.Equals(supported, value.Trim(), StringComparison.OrdinalIgnoreCase)) return supported;
            }
            return "FixedTokenCount";
        }

        #endregion
    }
}
