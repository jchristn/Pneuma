namespace Pneuma.Core.Ingestion.Refresh
{
    using System;

    /// <summary>The result of one refresh check.</summary>
    public class LinkRefreshResult
    {
        #region Public-Members

        /// <summary>The link.</summary>
        public string LinkId { get; set; } = String.Empty;

        /// <summary>What the check did.</summary>
        public LinkRefreshOutcomeEnum Outcome { get; set; } = LinkRefreshOutcomeEnum.Skipped;

        /// <summary>The queued job, when the outcome is Queued.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Why the check failed or was skipped, or null.</summary>
        public string? Message { get; set; } = null;

        /// <summary>When the link is checked next, or null when it is not refreshed.</summary>
        public DateTime? NextRefreshUtc { get; set; } = null;

        #endregion
    }
}
