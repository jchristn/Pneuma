namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// A content link submitted by an subject for ingestion.
    /// </summary>
    public class SubjectLink
    {
        /// <summary>Link identifier (prefix "lnk_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Subject identifier this link belongs to.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>The submitted URL.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Optional operator-facing title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Identifier of the user who submitted the link.</summary>
        public string? SubmittedByUserId { get; set; } = null;

        /// <summary>Current processing status.</summary>
        public SubjectLinkStatusEnum Status { get; set; } = SubjectLinkStatusEnum.Submitted;

        /// <summary>UTC timestamp of the last successful ingestion, if any.</summary>
        public DateTime? LastIngestedUtc { get; set; } = null;

        /// <summary>Last error message, if the most recent ingestion failed.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>Whether the link is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the link is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
