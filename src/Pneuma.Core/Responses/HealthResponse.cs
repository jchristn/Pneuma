namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// Health check response.
    /// </summary>
    public class HealthResponse
    {
        /// <summary>Overall status string.</summary>
        public string Status { get; set; } = "healthy";

        /// <summary>Service name.</summary>
        public string ServiceName { get; set; } = "pneuma-server";

        /// <summary>Service version.</summary>
        public string Version { get; set; } = "0.1.0";

        /// <summary>Current UTC time.</summary>
        public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    }
}
