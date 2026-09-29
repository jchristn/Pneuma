namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thrown when a job finished with dropped work (failed batches, summaries, or cell nodes) and the partial-loss
    /// policy is <c>Fail</c>. The job is retried, because the dropped work usually failed for a transient reason.
    /// </summary>
    public class PartialLossException : Exception
    {
        #region Public-Members

        /// <summary>The warnings that describe what was dropped. Never null.</summary>
        public IReadOnlyList<string> Warnings { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the exception.</summary>
        /// <param name="warnings">The warnings describing the lost work; null is treated as empty.</param>
        public PartialLossException(IReadOnlyList<string>? warnings)
            : base("Ingestion dropped work and the partial-loss policy is Fail: " + String.Join("; ", warnings ?? new List<string>()))
        {
            Warnings = warnings ?? new List<string>();
        }

        #endregion
    }
}
