namespace Pneuma.Core.Ingestion.Models
{
    using System;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// The result of classifying an ingestion failure: its category, whether retrying could succeed, and the
    /// remediation shown to operators.
    /// </summary>
    public class IngestionFailureClassification
    {
        #region Public-Members

        /// <summary>The failure category.</summary>
        public IngestionFailureCategoryEnum Category { get; set; } = IngestionFailureCategoryEnum.Internal;

        /// <summary>True when a retry could succeed; false for deterministic failures.</summary>
        public bool Retryable { get; set; } = true;

        /// <summary>
        /// Multiplier applied to the retry backoff. 1 is the normal backoff; categories that wait on an external
        /// service to recover (for example a rate-limited model endpoint) use a larger value. Minimum 1, maximum 10.
        /// </summary>
        public int BackoffMultiplier
        {
            get { return _BackoffMultiplier; }
            set { _BackoffMultiplier = Math.Clamp(value, 1, 10); }
        }

        /// <summary>Operator-facing remediation text for the category.</summary>
        public string Remediation { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private int _BackoffMultiplier = 1;

        #endregion
    }
}
