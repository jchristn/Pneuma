namespace Pneuma.Sdk.Responses
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// Result of checking a link for changes.
    /// </summary>
    public class LinkRefreshResult
    {
        /// <summary>Link id.</summary>
        public string LinkId { get; set; } = string.Empty;

        /// <summary>What the check found.</summary>
        public LinkRefreshOutcomeEnum Outcome { get; set; } = LinkRefreshOutcomeEnum.Skipped;

        /// <summary>The queued re-ingest job, when the outcome is Queued.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Detail, such as the failure reason.</summary>
        public string? Message { get; set; } = null;

        /// <summary>When the link is next checked, or null when its refresh is off.</summary>
        public DateTime? NextRefreshUtc { get; set; } = null;
    }
}
