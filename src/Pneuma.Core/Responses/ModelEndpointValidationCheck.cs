namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// The outcome of a single probe performed while validating a model endpoint (for example a basic
    /// completion, a tool-calling round-trip, or an embedding request). A validation groups one or more of
    /// these; the endpoint is considered healthy only when every check reports <see cref="Ok"/>.
    /// </summary>
    public class ModelEndpointValidationCheck
    {
        #region Public-Members

        /// <summary>Human-readable name of the probe (e.g. "Completion", "Tool calling", "Embedding").</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Whether the probe succeeded.</summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// Whether this check is informational rather than pass/fail — a capability the endpoint may or may not
        /// have (e.g. tool calling). A warning does not fail the overall validation; it just reports the finding.
        /// </summary>
        public bool Warning { get; set; } = false;

        /// <summary>A short human-readable description of what the probe observed on success (e.g. the reply text or vector dimensionality). Null when the probe failed.</summary>
        public string? Detail { get; set; } = null;

        /// <summary>Round-trip duration of the probe in milliseconds.</summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>Error message when the probe failed, or null.</summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
