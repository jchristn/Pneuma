namespace Pneuma.Core.Responses
{
    using System;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// One entry in the live ingestion snapshot: a job that is currently running a stage, waiting for a
    /// concurrency slot at a stage, or waiting in the job pool to start. Carries the document it relates to
    /// (<see cref="SourceUrl"/>), the current step (<see cref="Stage"/>), and when it entered its current state
    /// (<see cref="StateSinceUtc"/>) so the caller can render a live-ticking "time in this state".
    /// </summary>
    public class IngestionLiveJob
    {
        #region Public-Members

        /// <summary>The ingestion job id.</summary>
        public string JobId { get; set; } = String.Empty;

        /// <summary>The owning subject id.</summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>The document (source URL) the job relates to.</summary>
        public string SourceUrl { get; set; } = String.Empty;

        /// <summary>The current pipeline step, or null for a job still waiting in the pool to start.</summary>
        public IngestionStageEnum? Stage { get; set; } = null;

        /// <summary>UTC timestamp at which the job entered its current state (running / waiting for a slot / queued).</summary>
        public DateTime StateSinceUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
