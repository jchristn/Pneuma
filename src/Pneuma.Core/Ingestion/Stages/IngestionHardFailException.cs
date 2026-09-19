namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// Thrown by a stage for a <b>deterministic</b> failure that must not be retried — for example an unknown
    /// document type, no extracted cells, no configured completion endpoint, or no target collection. Re-running
    /// the same job would deterministically fail again, so the orchestrator fails it immediately rather than
    /// exhausting the retry budget. Transient failures (timeouts, subordinate-service errors) are represented by
    /// ordinary exceptions and are retried.
    /// </summary>
    public class IngestionHardFailException : Exception
    {
        #region Public-Members

        /// <summary>The stage at which the deterministic failure occurred.</summary>
        public IngestionStageEnum Stage { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate a hard-fail for the given stage.</summary>
        /// <param name="stage">The stage at which the failure occurred.</param>
        /// <param name="message">The failure message shown to the operator.</param>
        public IngestionHardFailException(IngestionStageEnum stage, string message) : base(message)
        {
            Stage = stage;
        }

        #endregion
    }
}
