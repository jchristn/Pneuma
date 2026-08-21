namespace Pneuma.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Structured, provider-agnostic per-stage performance telemetry for a single chat turn. Serialized to the
    /// turn's <see cref="ChatTurnRecord.PerformanceJson"/> (a schemaless payload column) so the history detail
    /// view can render a stage table and timing bars; the same stages are also written as individual
    /// <see cref="ChatTurnPerfEvent"/> rows for efficient analytics aggregation.
    /// </summary>
    public class TurnPerformance
    {
        #region Public-Members

        /// <summary>Schema version of this telemetry payload, so readers can evolve the shape safely.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Total wall-clock time across the measured stages, in milliseconds.</summary>
        public double WallTimeMs { get; set; } = 0;

        /// <summary>The ordered stages of the answer pipeline for this turn.</summary>
        public List<TurnPerformanceStage> Stages { get; set; } = new List<TurnPerformanceStage>();

        #endregion
    }
}
