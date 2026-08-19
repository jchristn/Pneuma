namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// A single point in an endpoint's health-check history: whether the probe at a given time succeeded.
    /// </summary>
    public class EndpointHealthRecord
    {
        /// <summary>UTC timestamp of the health-check probe.</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Whether the probe succeeded.</summary>
        public bool Success { get; set; } = false;
    }
}
