namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to set the scheduled refresh of one link, or of several links (set <see cref="Ids"/>).
    /// Set <see cref="RefreshIntervalMinutes"/> or <see cref="UseSubjectDefault"/>.
    /// </summary>
    public class LinkRefreshRequest
    {
        /// <summary>Link ids, for the bulk call only.</summary>
        public List<string>? Ids { get; set; } = null;

        /// <summary>Minutes between checks: 0 (off) or 60 to 525600.</summary>
        public int? RefreshIntervalMinutes { get; set; } = null;

        /// <summary>Follow the subject's default interval instead of a per-link one.</summary>
        public bool UseSubjectDefault { get; set; } = false;
    }
}
