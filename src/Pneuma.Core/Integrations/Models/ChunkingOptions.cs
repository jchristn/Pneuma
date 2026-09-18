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
        public static readonly IReadOnlyList<string> SupportedStrategies = new List<string> { "FixedTokenCount", "SentenceBased", "ParagraphBased" };

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

        /// <summary>Overlap between adjacent chunks in tokens, sent as the chunker's <c>OverlapCount</c>. Default 32; clamped to [0, 4096].</summary>
        public int OverlapCount
        {
            get { return _OverlapCount; }
            set { _OverlapCount = Math.Clamp(value, 0, 4096); }
        }

        #endregion

        #region Private-Members

        private string _Strategy = "FixedTokenCount";
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
