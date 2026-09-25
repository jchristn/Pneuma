namespace Pneuma.Server.Services
{
    using System;
    using Pneuma.Core.Requests;
    using Pneuma.Server.Settings;

    /// <summary>
    /// The retrieval settings in effect for one request: the configured <see cref="RetrievalSettings"/> with any
    /// per-request <see cref="RetrievalOverrides"/> applied and clamped to safe ranges.
    /// </summary>
    public class RetrievalKnobs
    {
        #region Public-Members

        /// <summary>RRF constant.</summary>
        public int RrfK { get; }

        /// <summary>Full-text channel fusion weight.</summary>
        public double LexicalWeight { get; }

        /// <summary>Vector channel fusion weight.</summary>
        public double SemanticWeight { get; }

        /// <summary>MMR on or off.</summary>
        public bool DiversityEnabled { get; }

        /// <summary>MMR lambda.</summary>
        public double DiversityLambda { get; }

        /// <summary>Grounded candidate pool multiplier.</summary>
        public int PoolMultiplier { get; }

        /// <summary>Neighbor expansion on or off.</summary>
        public bool NeighborExpansionEnabled { get; }

        /// <summary>Neighbor expansion depth.</summary>
        public int NeighborExpansionMaxHops { get; }

        /// <summary>Neighbor expansion node budget.</summary>
        public int NeighborExpansionMaxNodes { get; }

        /// <summary>True when any override was applied.</summary>
        public bool Overridden { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Resolve the effective settings.
        /// </summary>
        /// <param name="settings">Configured retrieval settings.</param>
        /// <param name="overrides">Per-request overrides, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when the settings are null.</exception>
        public RetrievalKnobs(RetrievalSettings settings, RetrievalOverrides? overrides)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            RrfK = Math.Clamp(overrides?.RrfK ?? settings.RrfK, 1, 1000);
            LexicalWeight = Math.Clamp(overrides?.LexicalWeight ?? settings.LexicalWeight, 0.0, 10.0);
            SemanticWeight = Math.Clamp(overrides?.SemanticWeight ?? settings.SemanticWeight, 0.0, 10.0);
            DiversityEnabled = overrides?.DiversityEnabled ?? settings.DiversityEnabled;
            DiversityLambda = Math.Clamp(overrides?.DiversityLambda ?? settings.DiversityLambda, 0.0, 1.0);
            PoolMultiplier = Math.Clamp(overrides?.PoolMultiplier ?? 4, 1, 25);
            NeighborExpansionEnabled = overrides?.NeighborExpansionEnabled ?? settings.NeighborExpansionEnabled;
            NeighborExpansionMaxHops = Math.Clamp(overrides?.NeighborExpansionMaxHops ?? settings.NeighborExpansionMaxHops, 1, 5);
            NeighborExpansionMaxNodes = Math.Clamp(overrides?.NeighborExpansionMaxNodes ?? settings.NeighborExpansionMaxNodes, 0, 200);
            Overridden = overrides != null && !overrides.IsEmpty();
        }

        #endregion
    }
}
