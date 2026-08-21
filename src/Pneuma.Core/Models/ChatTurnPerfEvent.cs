namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A single persisted performance-event row: one stage of one chat turn's answer pipeline. Written
    /// alongside the turn's serialized <see cref="TurnPerformance"/> so per-stage/per-endpoint latency can be
    /// aggregated (percentiles, averages) without parsing JSON across every turn.
    /// </summary>
    public class ChatTurnPerfEvent
    {
        #region Public-Members

        /// <summary>Performance-event identifier (prefix "perf_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>The chat turn this stage belongs to.</summary>
        public string TurnId
        {
            get { return _TurnId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TurnId)); _TurnId = value; }
        }

        /// <summary>Subject the turn was scoped to, or null for a whole-tenant chat.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Stage name (for example "final_inference").</summary>
        public string Stage { get; set; } = String.Empty;

        /// <summary>Stage kind (for example "inference", "retrieval", "tool").</summary>
        public string Kind { get; set; } = String.Empty;

        /// <summary>Provider that served the stage, when applicable.</summary>
        public string? Provider { get; set; } = null;

        /// <summary>Model that served the stage, when applicable.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Elapsed wall-clock time for the stage, in milliseconds.</summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>Time to first streamed token for an inference stage, in milliseconds (0 when not applicable).</summary>
        public double TimeToFirstTokenMs { get; set; } = 0;

        /// <summary>Prompt tokens consumed by the stage.</summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>Completion tokens produced by the stage.</summary>
        public int CompletionTokens { get; set; } = 0;

        /// <summary>Whether the stage completed successfully.</summary>
        public bool Success { get; set; } = true;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GeneratePerfEventId();
        private string _TenantId = String.Empty;
        private string _TurnId = String.Empty;

        #endregion
    }
}
