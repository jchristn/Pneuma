namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Responses;

    /// <summary>
    /// In-memory, mutable health state accumulated for a single base URL by the model health monitor.
    /// One instance is shared by every model endpoint that resolves to the same base URL (deduplication),
    /// so a host is probed once and all its endpoints report the same status. Not persisted.
    /// </summary>
    internal class BaseUrlHealthState
    {
        #region Public-Members

        /// <summary>Base URL this state tracks.</summary>
        public string BaseUrl { get; set; } = String.Empty;

        /// <summary>Whether the base URL is currently considered healthy (starts false until proven).</summary>
        public bool IsHealthy { get; set; } = false;

        /// <summary>UTC time of the first probe.</summary>
        public DateTime? FirstCheckUtc { get; set; } = null;

        /// <summary>UTC time of the most recent probe.</summary>
        public DateTime? LastCheckUtc { get; set; } = null;

        /// <summary>UTC time last observed healthy.</summary>
        public DateTime? LastHealthyUtc { get; set; } = null;

        /// <summary>UTC time last observed unhealthy.</summary>
        public DateTime? LastUnhealthyUtc { get; set; } = null;

        /// <summary>UTC time of the last state transition (or first probe).</summary>
        public DateTime? LastStateChangeUtc { get; set; } = null;

        /// <summary>Accumulated healthy time (milliseconds), excluding the current in-progress period.</summary>
        public double TotalUptimeMs { get; set; } = 0;

        /// <summary>Accumulated unhealthy time (milliseconds), excluding the current in-progress period.</summary>
        public double TotalDowntimeMs { get; set; } = 0;

        /// <summary>Consecutive successful probes.</summary>
        public int ConsecutiveSuccesses { get; set; } = 0;

        /// <summary>Consecutive failed probes.</summary>
        public int ConsecutiveFailures { get; set; } = 0;

        /// <summary>Error message from the most recent failed probe, or null.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>HTTP status code from the most recent probe that received a response.</summary>
        public int? LastStatusCode { get; set; } = null;

        /// <summary>Round-trip latency of the most recent probe (milliseconds).</summary>
        public double? LastLatencyMs { get; set; } = null;

        /// <summary>Rolling probe history.</summary>
        public List<EndpointHealthRecord> History { get; } = new List<EndpointHealthRecord>();

        /// <summary>Synchronization object guarding mutations and reads of this state.</summary>
        public object Sync { get; } = new object();

        #endregion
    }
}
