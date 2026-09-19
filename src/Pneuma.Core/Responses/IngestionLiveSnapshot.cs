namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A point-in-time snapshot of ingestion work for the live "Ingestion Jobs" view: the jobs actively running
    /// a stage, the jobs whose current stage is waiting for a concurrency slot, and the jobs still waiting in the
    /// pool to start. Each list is ordered longest-in-state first. Timestamps are absolute so the caller can tick
    /// the elapsed time client-side rather than the value going stale between polls.
    /// </summary>
    public class IngestionLiveSnapshot
    {
        #region Public-Members

        /// <summary>UTC time this snapshot was generated.</summary>
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Jobs actively executing a stage right now.</summary>
        public List<IngestionLiveJob> Running { get; set; } = new List<IngestionLiveJob>();

        /// <summary>Jobs whose current stage is waiting for a free concurrency slot (per-stage gate contention).</summary>
        public List<IngestionLiveJob> WaitingForSlot { get; set; } = new List<IngestionLiveJob>();

        /// <summary>Jobs waiting in the pool to be claimed and started.</summary>
        public List<IngestionLiveJob> Queued { get; set; } = new List<IngestionLiveJob>();

        #endregion
    }
}
