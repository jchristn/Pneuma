namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A content link submitted by an subject for ingestion.
    /// </summary>
    public class SubjectLink
    {
        #region Public-Members

        /// <summary>Link identifier (prefix "lnk_").</summary>
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

        /// <summary>Subject identifier this link belongs to.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>The submitted URL.</summary>
        public string Url
        {
            get { return _Url; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Url)); _Url = value; }
        }

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

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateLinkId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;
        private string _Url = String.Empty;

        #endregion
    }
}
