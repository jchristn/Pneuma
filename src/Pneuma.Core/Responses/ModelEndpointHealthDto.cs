namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Live health status for a model endpoint. Health is probed per unique base URL (endpoints sharing a
    /// base URL are checked once and share the result), so every endpoint on a given host reports the same
    /// status. Uptime, history, and consecutive-check counts are accumulated by the health monitor.
    /// </summary>
    public class ModelEndpointHealthDto
    {
        /// <summary>Partio endpoint identifier this status applies to.</summary>
        public string EndpointId { get; set; } = String.Empty;

        /// <summary>Human-readable endpoint name.</summary>
        public string? EndpointName { get; set; } = null;

        /// <summary>Endpoint type ("Embedding" or "Completion").</summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>Base URL that was probed for this endpoint's health.</summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>Whether the base URL is currently healthy.</summary>
        public bool IsHealthy { get; set; } = false;

        /// <summary>Whether health checking is being performed for this endpoint's base URL.</summary>
        public bool HealthCheckEnabled { get; set; } = true;

        /// <summary>HTTP status code observed on the most recent probe, when a response was received.</summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>Round-trip latency of the most recent probe in milliseconds.</summary>
        public double? LatencyMs { get; set; } = null;

        /// <summary>UTC time of the first health check.</summary>
        public DateTime? FirstCheckUtc { get; set; } = null;

        /// <summary>UTC time of the most recent health check.</summary>
        public DateTime? LastCheckUtc { get; set; } = null;

        /// <summary>UTC time the base URL was last observed healthy.</summary>
        public DateTime? LastHealthyUtc { get; set; } = null;

        /// <summary>UTC time the base URL was last observed unhealthy.</summary>
        public DateTime? LastUnhealthyUtc { get; set; } = null;

        /// <summary>UTC time of the last healthy/unhealthy state transition.</summary>
        public DateTime? LastStateChangeUtc { get; set; } = null;

        /// <summary>Total accumulated healthy time in milliseconds, including the current in-progress period.</summary>
        public double TotalUptimeMs { get; set; } = 0;

        /// <summary>Total accumulated unhealthy time in milliseconds, including the current in-progress period.</summary>
        public double TotalDowntimeMs { get; set; } = 0;

        /// <summary>Uptime percentage (0-100) since monitoring began.</summary>
        public double UptimePercentage { get; set; } = 0;

        /// <summary>Consecutive successful probes.</summary>
        public int ConsecutiveSuccesses { get; set; } = 0;

        /// <summary>Consecutive failed probes.</summary>
        public int ConsecutiveFailures { get; set; } = 0;

        /// <summary>Error message from the most recent failed probe, or null.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>Rolling history of recent probes (last 24 hours).</summary>
        public List<EndpointHealthRecord> History { get; set; } = new List<EndpointHealthRecord>();
    }
}
